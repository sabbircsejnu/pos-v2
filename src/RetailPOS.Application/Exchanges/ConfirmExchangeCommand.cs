using System.Globalization;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Exchanges;

public sealed record ConfirmExchangeCommand(
    ExchangeRequest Request,
    ExchangeActor Actor,
    string PreviewHash,
    string IdempotencyKey,
    IReadOnlyList<ExchangeSettlementInput> Settlements,
    IReadOnlyList<string> ApprovalTokens) : IRequest<ExchangeResultReadModel>;

public sealed record ExchangeResultReadModel(long ExchangeId, string ExchangeNumber, bool Replayed);

/// <summary>
/// The atomic exchange (C-04): every authoritative write — header and lines, shared returned quantity, stock in and out,
/// pack state and packaging, cash movements, settlements, the balanced journal, salesperson facts, approval consumption,
/// idempotency and the outbox event — commits in one PostgreSQL transaction or not at all. Lock order follows the
/// Stage 0 audit (§7.2): sales → replacement sources → variants (UOM read) → session → inventories (one sorted statement)
/// → pack state → approvals → accounting period → number counter → journal → drawer and cash movements last.
/// </summary>
public sealed class ConfirmExchangeCommandHandler(
    IBusinessScope businessScope,
    IExchangeStore store,
    ExchangeEvaluator evaluator,
    ISaleCashSessionValidator sessionValidator,
    ICashSessionStore cashSessions,
    ICashSessionMovementRecorder cashMovements,
    IStockLedgerWriter stockLedger,
    IExchangeJournalPoster journal,
    IExchangeAuditWriter audit,
    IExchangeEventWriter events,
    IAuthenticationStore users,
    IUserOutletAccess outletAccess,
    IVariantUomReader? uomReader = null,
    IPackStateLedger? packState = null) : IRequestHandler<ConfirmExchangeCommand, ExchangeResultReadModel>
{
    private const int MaxIdempotencyKeyLength = 120;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExchangeResultReadModel> Handle(ConfirmExchangeCommand command, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var key = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxIdempotencyKeyLength)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "A confirmation needs an idempotency key of at most 120 characters.");
        var fingerprint = Fingerprint(command);

        var earlier = await store.FindByIdempotencyKeyAsync(businessId, key, cancellationToken);
        if (earlier is not null)
            return Replay(earlier, fingerprint);

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await ConfirmAsync(businessId, key, fingerprint, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return result;
        }
        catch (Exception exception) when (!committed && store.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            // The same key committed concurrently: answer with it. Anything else unique is a retryable conflict.
            var winner = await store.FindByIdempotencyKeyAsync(businessId, key, cancellationToken);
            if (winner is not null)
                return Replay(winner, fingerprint);
            throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "Another transaction changed the same stock. Try again.");
        }
        catch (Exception exception) when (!committed && store.IsConcurrencyFailure(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "Another transaction changed the same items or stock. Try again.");
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static ExchangeResultReadModel Replay(Exchange earlier, string fingerprint) =>
        string.Equals(earlier.RequestFingerprint, fingerprint, StringComparison.Ordinal)
            ? new ExchangeResultReadModel(earlier.Id, earlier.ExchangeNumber, true)
            : throw ExchangeException.Conflict(ExchangeErrorCodes.IdempotencyMismatch, "This idempotency key was already used for a different exchange.");

    private async Task<ExchangeResultReadModel> ConfirmAsync(
        long businessId, string key, string fingerprint, ConfirmExchangeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var actor = command.Actor;
        var nowUtc = DateTime.UtcNow;

        // 1–3: lock the sources (sales ascending, then replacement lines), then evaluate on the locked state.
        var sales = await store.LoadSourceSalesAsync(businessId, request.SourceSaleIds.Distinct().ToList(), forUpdate: true, cancellationToken);
        // A retry that waited on these locks must replay the committed result, not be re-evaluated against it.
        if (await store.FindByIdempotencyKeyAsync(businessId, key, cancellationToken) is { } committedMeanwhile)
            return Replay(committedMeanwhile, fingerprint);
        var replacementSourceIds = request.Returns.Where(line => line.SourceKind == ExchangeSourceKinds.ReplacementLine)
            .Select(line => line.SourceLineId).Distinct().ToList();
        var replacementSources = replacementSourceIds.Count == 0
            ? []
            : await store.LoadReplacementLinesAsync(businessId, replacementSourceIds, forUpdate: true, cancellationToken);
        var evaluated = await evaluator.EvaluateAsync(businessId, request, actor, sales, replacementSources, nowUtc, cancellationToken);
        if (!string.Equals(evaluated.PreviewHash, command.PreviewHash, StringComparison.Ordinal))
            throw ExchangeException.Conflict(ExchangeErrorCodes.PreviewStale,
                "Prices, availability, the date or the rules changed since the exchange was reviewed. Review it again.");

        // UOM configs of returned variants (read FOR SHARE, before any inventory row).
        var returnVariantIds = evaluated.Returns.Select(line => line.VariantId).Distinct().ToList();
        var returnConfigs = uomReader is null
            ? new Dictionary<long, ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(businessId, returnVariantIds, cancellationToken);

        // 4: the terminal (FOR SHARE: still active at the receiving outlet, L6), then the receiving session (FOR SHARE,
        // 20-hour guard) — EX-15 applies even to an even exchange.
        if (!await store.LockActiveTerminalAsync(businessId, request.ReceivingOutletId, evaluated.TerminalId, cancellationToken))
            throw ExchangeTerminals.Unavailable(evaluated.TerminalId);
        CashSession session;
        try
        {
            session = await sessionValidator.ValidateActiveForSaleAsync(request.ReceivingOutletId, evaluated.TerminalId, actor.UserId, cancellationToken);
        }
        catch (CashSessionException exception)
        {
            throw ExchangeException.Conflict(ExchangeErrorCodes.SessionRequired, exception.Message);
        }

        var businessDate = session.BusinessDate.Date;

        // 5: every receiving-outlet stock row this exchange touches, locked in one sorted statement.
        var packagingNeeds = evaluated.Replacements.SelectMany(line => line.Uom.Packaging).ToList();
        var componentReturns = evaluated.Returns
            .Where(line => line.SaleItem is not null && line.Input.PackagingReusable)
            .SelectMany(line => line.SaleItem!.Components.Select(component => component.ComponentVariantId))
            .ToList();
        var stockVariantIds = returnVariantIds
            .Concat(evaluated.Replacements.Select(line => line.Input.VariantId))
            .Concat(packagingNeeds.Select(need => need.VariantId))
            .Concat(componentReturns)
            .Distinct()
            .ToList();
        var inventories = await store.LockOutletInventoriesAsync(request.ReceivingOutletId, stockVariantIds, cancellationToken);
        async Task<RetailPOS.Core.Entities.Inventory> StockInAsync(long variantId)
        {
            if (!inventories.TryGetValue(variantId, out var row))
                inventories[variantId] = row = await store.CreateOutletInventoryAsync(variantId, request.ReceivingOutletId, cancellationToken);
            return row;
        }

        foreach (var group in evaluated.Replacements.Select(line => (line.Input.VariantId, line.Uom.BaseQuantity))
                     .Concat(packagingNeeds.Select(need => (need.VariantId, BaseQuantity: need.Quantity)))
                     .GroupBy(entry => entry.VariantId))
        {
            var needed = group.Sum(entry => entry.BaseQuantity);
            var available = inventories.TryGetValue(group.Key, out var row) ? row.Quantity : 0m;
            // A returned unit of the same variant is accepted before the replacement leaves, so it may serve it.
            var incoming = evaluated.Returns.Where(line => line.VariantId == group.Key).Sum(line => line.Input.Quantity);
            if (available + incoming < needed)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.StockInsufficient,
                    $"Not enough stock of variant ID {group.Key} at this outlet. Available: {ExchangeCalculator.Quantity(available)}, needed: {ExchangeCalculator.Quantity(needed)}.");
        }

        // 6: approvals — consumed exactly once and re-authorised now (C-02, OC-01, OC-02, OC-03).
        var settlements = ExchangeSettlementPlanner.Plan(evaluated.Totals, command.Settlements);
        var approvals = await ConsumeApprovalsAsync(businessId, command, evaluated, settlements, nowUtc, cancellationToken);

        // 7: accounts and accounting period, before anything moves (C-06, D-15, VI-18).
        await journal.EnsureAccountsAsync(
            request.ReceivingOutletId,
            // Only the slots this exchange's journal actually uses (an unused, unmapped slot must not block it).
            ExchangeJournalComposer.Compose(evaluated.Totals, settlements)
                .Where(line => line.Slot is not null).Select(line => line.Slot!).Distinct().ToList(),
            settlements.Select(plan => plan.Method).Distinct().ToList(),
            businessDate,
            cancellationToken);
        var period = await store.LockAccountingPeriodAsync(businessId, businessDate, cancellationToken);
        if (!period.IsOpen)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.AccountingPeriodClosed,
                period.Exists
                    ? $"The accounting period '{period.Name}' is {period.Status}; exchanges cannot be posted into it."
                    : $"No open accounting period covers {businessDate:yyyy-MM-dd}.");

        // 8: number (D-19).
        var prefix = (await store.FindOutletInvoicePrefixAsync(request.ReceivingOutletId, cancellationToken))?.Trim();
        if (string.IsNullOrEmpty(prefix))
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.OutletPrefixMissing, "This outlet has no invoice prefix. Set one in the outlet settings first.");
        var sequence = await store.AllocateExchangeSequenceAsync(businessId, request.ReceivingOutletId, businessDate, cancellationToken);
        var exchangeNumber = string.Create(CultureInfo.InvariantCulture, $"{prefix}X{businessDate:yyMMdd}{sequence}");

        // 9: the record is inserted once, with final values, at the end (the database keeps it immutable afterwards).
        // Its id is reserved now so every stock, cash and journal reference can point at it.
        var exchange = BuildExchange(businessId, evaluated, request, actor, session, evaluated.TerminalId, key, fingerprint, exchangeNumber, nowUtc);
        exchange.Id = await store.ReserveExchangeIdAsync(cancellationToken);

        // 10: the shared returned-quantity entitlement (C-03). The original sale's status never changes (D-14).
        var returnLines = exchange.ReturnLines.ToList();
        var restoredPackaging = 0m;
        for (var index = 0; index < evaluated.Returns.Count; index++)
        {
            var evaluatedReturn = evaluated.Returns[index];
            var line = returnLines[index];
            if (evaluatedReturn.SaleItem is { } item)
                item.ReturnedQuantity += evaluatedReturn.Input.Quantity;
            else
                evaluatedReturn.SourceReplacement!.ReturnedQuantity += evaluatedReturn.Input.Quantity;

            // 11a: accepted goods return to the receiving outlet's sellable stock (EX-05, EX-06).
            var inventory = await StockInAsync(evaluatedReturn.VariantId);
            inventory.Quantity += evaluatedReturn.Input.Quantity;
            var unitCost = evaluatedReturn.SaleItem?.HistoricalUnitCost ?? evaluatedReturn.SourceReplacement!.HistoricalUnitCost;
            stockLedger.WriteEntryWithValuation(
                evaluatedReturn.VariantId, request.ReceivingOutletId, "outlet", StockLedgerTransactionType.Exchange,
                evaluatedReturn.Input.Quantity, 0m, inventory.Quantity, StockLedgerReferenceType.Exchange, exchange.Id,
                $"Exchange {exchangeNumber} return", actor.UserId, nowUtc, unitCost, evaluatedReturn.ContentCost, businessDate);

            var tracksPackState = returnConfigs.GetValueOrDefault(evaluatedReturn.VariantId)?.PackStateTrackingEnabled == true;
            if (evaluatedReturn.SaleItem is { } saleItem
                && (saleItem.PackState is not null || saleItem.Components.Count > 0 || tracksPackState))
            {
                var packaging = await SaleUomReversal.RestoreAsync(
                    evaluatedReturn.Sale!, saleItem, evaluatedReturn.Input.Quantity, evaluatedReturn.Input.PackagingReusable, actor.UserId,
                    packState, stockLedger, async (variantId, _) => await StockInAsync(variantId), cancellationToken,
                    tracksPackState, evaluatedReturn.AlreadyReturned,
                    request.ReceivingOutletId, StockLedgerReferenceType.Exchange, exchange.Id, StockLedgerTransactionType.Exchange);
                line.CostRestored += packaging;
                restoredPackaging += packaging;
            }
            else if (evaluatedReturn.SourceReplacement is not null && tracksPackState && packState is not null)
            {
                // A re-exchanged item has no recorded pack form here: it comes back as loose stock.
                await packState.ApplyAsync(new PackStateChange(businessId, evaluatedReturn.VariantId, request.ReceivingOutletId, "outlet",
                    PackStates.Loose, string.Empty, 0m, 0, evaluatedReturn.Input.Quantity, 0m,
                    PackStateOperations.SaleReversal, StockLedgerReferenceType.Exchange, exchange.Id, actor.UserId), cancellationToken);
            }
        }

        // 11b: replacements leave the receiving outlet's stock, with packaging and pack form like a sale (UOM D04/D10).
        var replacementLines = exchange.ReplacementLines.ToList();
        var packagingCost = await IssueReplacementsAsync(businessId, evaluated, replacementLines, inventories, exchange, actor.UserId, nowUtc, businessDate, cancellationToken);

        foreach (var variantId in stockVariantIds.Where(id => packState is not null
                     && (returnConfigs.GetValueOrDefault(id)?.PackStateTrackingEnabled == true
                         || evaluated.Replacements.Any(line => line.Input.VariantId == id && line.Uom.Form is not null))))
            await packState!.EnsureReconciledAsync(businessId, variantId, request.ReceivingOutletId, "outlet",
                inventories[variantId].Quantity, cancellationToken);

        // Final cost figures include packaging restored and issued.
        exchange.ReturnCost = evaluated.Totals.ReturnCost + restoredPackaging;
        exchange.ReplacementCost = evaluated.Totals.ReplacementCost + packagingCost;
        var totals = evaluated.Totals with { ReturnCost = exchange.ReturnCost, ReplacementCost = exchange.ReplacementCost };

        // 12: cash. A refund paid in cash may never exceed the expected drawer cash (EX-31, D-02); drawer lock is last.
        var cashOut = -settlements.Where(plan => plan.CashEffect < 0m).Sum(plan => plan.CashEffect);
        if (cashOut > 0m)
        {
            await store.AcquireDrawerLockAsync(businessId, session.Id, cancellationToken);
            var facts = await cashSessions.GetFactsAsync(session.Id, cancellationToken)
                ?? throw ExchangeException.Conflict(ExchangeErrorCodes.SessionRequired, "The cash session was not found.");
            var expected = CashSessionExpectedCashCalculator.Calculate(facts, nowUtc).ExpectedClosingCash;
            if (expected < cashOut)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.CashInsufficient,
                    $"The drawer is expected to hold {ExchangeCalculator.Money(expected)}, less than the {ExchangeCalculator.Money(cashOut)} cash refund. Choose another refund method.");
        }

        var settlementRows = new List<ExchangeSettlement>(settlements.Count);
        for (var index = 0; index < settlements.Count; index++)
        {
            var plan = settlements[index];
            long? movementId = null;
            if (plan.CashEffect != 0m)
            {
                var movement = await cashMovements.RecordAsync(new CashSessionMovementRequest(
                    session.Id,
                    plan.CashEffect > 0m ? CashSessionMovementTypes.ExchangeCashIn : CashSessionMovementTypes.ExchangeCashOut,
                    plan.CashEffect,
                    "exchange",
                    exchange.Id,
                    $"exchange:{exchange.Id}:settlement:{index + 1}",
                    null,
                    null,
                    $"Exchange {exchangeNumber}",
                    actor.UserId), cancellationToken);
                movementId = movement.Movement.Id;
            }

            settlementRows.Add(new ExchangeSettlement
            {
                Direction = plan.Direction,
                Method = plan.Method,
                Amount = plan.Amount,
                Tendered = plan.Tendered,
                ChangeGiven = plan.ChangeGiven,
                CashSessionId = session.Id,
                CashSessionMovementId = movementId,
                ExternalReference = plan.ExternalReference,
                ExternalConfirmedByUserId = plan.ExternalRefundConfirmed ? actor.UserId : null,
                ExternalConfirmedAtUtc = plan.ExternalRefundConfirmed ? nowUtc : null,
                CreatedAtUtc = nowUtc
            });
        }

        foreach (var row in settlementRows)
            exchange.Settlements.Add(row);

        // 13: the balanced journal, posted inside this transaction on the session business date (C-04, VI-02).
        exchange.JournalEntryId = await journal.PostAsync(new ExchangeJournalPosting(
            businessId, actor.UserId, request.ReceivingOutletId, evaluated.TerminalId, session.Id, businessDate, nowUtc,
            "Exchange", exchange.Id.ToString(CultureInfo.InvariantCulture), exchangeNumber,
            $"Product exchange {exchangeNumber}", $"exchange:{exchange.Id}:v1",
            ExchangeJournalComposer.Compose(totals, settlements)), cancellationToken);

        // 14: insert the record, consume the approvals, then lineage, salesperson fact (D-06, D-07), receipt, event and audit.
        await store.AddExchangeAsync(exchange, cancellationToken);
        foreach (var approval in approvals)
        {
            approval.Status = "consumed";
            approval.ConsumedByExchangeId = exchange.Id;
            approval.ConsumedAtUtc = nowUtc;
        }

        var fact = ExchangeCalculator.SalespersonFact(totals.NetAdjustmentNet, evaluated.Policy.NegativeTargetAdjustmentMode);
        await store.AddSalespersonAdjustmentAsync(new ExchangeSalespersonAdjustment
        {
            BusinessId = businessId,
            ExchangeId = exchange.Id,
            BusinessDate = businessDate,
            ReceivingOutletId = request.ReceivingOutletId,
            CashierUserId = actor.UserId,
            SalespersonUserId = request.SalespersonUserId,
            PositiveAmountNet = fact.Positive,
            NegativeAmountNet = fact.Negative,
            SignedAmountNet = fact.Signed,
            TargetEffectAmountNet = fact.TargetEffect,
            NegativeMode = evaluated.Policy.NegativeTargetAdjustmentMode,
            PolicyVersionId = evaluated.Policy.Id,
            CreatedAtUtc = nowUtc
        }, cancellationToken);
        await store.AddReceiptSnapshotAsync(new ExchangeReceiptSnapshot
        {
            ExchangeId = exchange.Id,
            DocumentVersion = 1,
            Document = JsonSerializer.Serialize(ExchangeReceiptComposer.Compose(exchange, evaluated, settlementRows), Json),
            CreatedAtUtc = nowUtc
        }, cancellationToken);
        await events.AppendAsync(businessId, "pos.exchange.completed", exchange.Id, "completed", new
        {
            businessId,
            exchangeId = exchange.Id,
            exchangeNumber,
            businessDate = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            receivingOutletId = request.ReceivingOutletId,
            customerId = exchange.CustomerId,
            completedAtUtc = nowUtc,
            netAdjustmentGross = totals.NetAdjustmentGross,
            netAdjustmentNet = totals.NetAdjustmentNet,
            returnLineIds = returnLines.Select(line => line.Id).ToArray(),
            replacementLineIds = replacementLines.Select(line => line.Id).ToArray(),
            loyaltyStatus = ExchangeLoyaltyStatuses.ModuleInactive
        }, nowUtc, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await store.AddLineageLinksAsync(
            returnLines.SelectMany(returned => replacementLines.Select(issued => new ExchangeLineageLink
            {
                ReturnLineId = returned.Id,
                ReplacementLineId = issued.Id
            })).ToList(),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("exchange.completed", $"Exchange {exchangeNumber} completed", exchange.Id, new Dictionary<string, object?>
        {
            ["exchangeNumber"] = exchangeNumber,
            ["receivingOutletId"] = request.ReceivingOutletId,
            ["cashSessionId"] = session.Id,
            ["sourceSaleIds"] = string.Join(',', sales.Select(sale => sale.Id)),
            ["netAdjustmentGross"] = totals.NetAdjustmentGross,
            ["settlementDirection"] = totals.Direction,
            ["approvals"] = string.Join(',', approvals.Select(approval => $"{approval.ApprovalType}:{approval.SupervisorUserId}")),
            ["journalEntryId"] = exchange.JournalEntryId,
            ["policyVersionId"] = evaluated.Policy.Id
        }, cancellationToken);

        await sessionValidator.ValidateFinalGateAsync(session, cancellationToken);
        return new ExchangeResultReadModel(exchange.Id, exchangeNumber, false);
    }

    private async Task<IReadOnlyList<ExchangeApproval>> ConsumeApprovalsAsync(
        long businessId,
        ConfirmExchangeCommand command,
        EvaluatedExchange evaluated,
        IReadOnlyList<ExchangeSettlementPlan> settlements,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var hashes = command.ApprovalTokens.Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => ExchangeCalculator.Sha256(token.Trim())).Distinct().ToList();
        var approvals = hashes.Count == 0 ? [] : await store.LockApprovalsAsync(businessId, hashes, cancellationToken);
        if (approvals.Count != hashes.Count)
            throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalInvalid, "A supervisor approval is not valid. Ask the supervisor to approve again.");

        var refundTerms = settlements.Count == 1 && settlements[0].Direction == ExchangeSettlementDirections.Refund
            ? new ExchangeRefundApprovalTerms(settlements[0].Amount, settlements[0].Method)
            : null;
        foreach (var approval in approvals)
        {
            var expectedHash = GrantExchangeApprovalsCommandHandler.PayloadHash(approval.ApprovalType, evaluated.PreviewHash, refundTerms);
            if (approval.Status != "granted" || approval.ExpiresAtUtc <= nowUtc || approval.CashierUserId != command.Actor.UserId
                || approval.ReceivingOutletId != command.Request.ReceivingOutletId
                || !string.Equals(approval.PayloadHash, expectedHash, StringComparison.Ordinal))
                throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalInvalid,
                    "A supervisor approval has expired, was used, or no longer matches this exchange. Ask the supervisor to approve again.");
        }

        var missing = evaluated.RequiredApprovals.Except(approvals.Select(approval => approval.ApprovalType), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.ApprovalRequired,
                $"This exchange needs supervisor approval: {string.Join(", ", missing)}.", new { required = missing });
        if (approvals.Select(approval => approval.ApprovalType).Except(evaluated.RequiredApprovals, StringComparer.Ordinal).Any())
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "An approval was supplied that this exchange does not need.");

        // Revocations, disabled users and removed outlet assignments take effect now (AC-45, AC-46d).
        foreach (var supervisor in approvals.GroupBy(approval => approval.SupervisorUserId))
        {
            var profile = await users.FindProfileAsync(supervisor.Key, cancellationToken);
            if (profile is null || !profile.IsActive || profile.MustResetPassword || profile.BusinessId != businessId)
                throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalInvalid, "The approving supervisor is no longer active.");
            await ExchangeSupervisorAuthorization.AuthorizeAsync(outletAccess, profile.Id, profile.RoleName, profile.RolePermissions,
                profile.PermissionOverrides, businessId, command.Request.ReceivingOutletId, supervisor.Select(approval => approval.ApprovalType));
        }

        return approvals;
    }

    private async Task<decimal> IssueReplacementsAsync(
        long businessId,
        EvaluatedExchange evaluated,
        IReadOnlyList<ExchangeReplacementLine> lines,
        Dictionary<long, RetailPOS.Core.Entities.Inventory> inventories,
        Exchange exchange,
        long actorUserId,
        DateTime nowUtc,
        DateTime businessDate,
        CancellationToken cancellationToken)
    {
        var outletId = evaluated.ReceivingOutletId;
        var packagingTotal = 0m;
        for (var index = 0; index < evaluated.Replacements.Count; index++)
        {
            var replacement = evaluated.Replacements[index];
            var line = lines[index];
            var inventory = inventories[replacement.Input.VariantId];
            inventory.Quantity -= replacement.Uom.BaseQuantity;
            if (inventory.Quantity < 0m)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.StockInsufficient, $"Not enough stock of variant ID {replacement.Input.VariantId}.");
            stockLedger.WriteEntryWithValuation(
                replacement.Input.VariantId, outletId, "outlet", StockLedgerTransactionType.Exchange,
                0m, replacement.Uom.BaseQuantity, inventory.Quantity, StockLedgerReferenceType.Exchange, exchange.Id,
                $"Exchange {exchange.ExchangeNumber} replacement", actorUserId, nowUtc, replacement.HistoricalUnitCost,
                MonetaryRounding.ToCurrency(replacement.HistoricalUnitCost * replacement.Uom.BaseQuantity), businessDate);

            foreach (var need in replacement.Uom.Packaging)
            {
                var packagingRow = inventories.GetValueOrDefault(need.VariantId);
                if (packagingRow is null || packagingRow.Quantity < need.Quantity)
                    throw ExchangeException.Unprocessable(ExchangeErrorCodes.StockInsufficient, $"Not enough packaging item {need.VariantId}.");
                var unitCost = await PackagingCostAsync(need.VariantId, cancellationToken);
                var value = MonetaryRounding.ToCurrency(unitCost * need.Quantity);
                packagingRow.Quantity -= need.Quantity;
                stockLedger.WriteEntryWithValuation(
                    need.VariantId, outletId, "outlet", StockLedgerTransactionType.Exchange, 0m, need.Quantity, packagingRow.Quantity,
                    StockLedgerReferenceType.Exchange, exchange.Id, $"Exchange {exchange.ExchangeNumber} packaging", actorUserId, nowUtc,
                    unitCost, value, businessDate);
                line.Components.Add(new ExchangeReplacementLineComponent { ComponentVariantId = need.VariantId, Quantity = need.Quantity, UnitCost = unitCost, Value = value });
                line.PackagingCost += value;
                packagingTotal += value;
            }

            if (replacement.Uom.Form is { } form && packState is not null)
            {
                var capitalized = 0m;
                if (form.State == PackStates.Prepared)
                {
                    var balances = await packState.GetBalancesForUpdateAsync(businessId, replacement.Input.VariantId, outletId, "outlet", cancellationToken);
                    var prepared = balances.FirstOrDefault(row =>
                        row.State == PackStates.Prepared && row.PackCode == form.PackCode && row.BaseQtyPerPack == form.BaseQtyPerPack);
                    capitalized = prepared is null ? 0m : CapitalizedValueShare.For(prepared.CapitalizedValue, prepared.PackCount, form.PackCount);
                }

                await packState.ApplyAsync(new PackStateChange(businessId, replacement.Input.VariantId, outletId, "outlet", form.State, form.PackCode,
                    form.BaseQtyPerPack, -form.PackCount, -replacement.Uom.BaseQuantity, -capitalized,
                    PackStateOperations.Sale, StockLedgerReferenceType.Exchange, exchange.Id, actorUserId), cancellationToken);
                line.PackagingCost += capitalized;
                packagingTotal += capitalized;
            }
        }

        return packagingTotal;
    }

    /// <summary>Packaging must have a known cost, as at checkout (PACK-002).</summary>
    private async Task<decimal> PackagingCostAsync(long variantId, CancellationToken cancellationToken)
    {
        var cost = await evaluator.ResolveCostAsync(variantId, cancellationToken);
        if (cost <= 0m)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.InvalidRequest,
                $"Packaging item {variantId} has no known cost. Receive it through a GRN first.");
        return cost;
    }

    private static Exchange BuildExchange(
        long businessId, EvaluatedExchange evaluated, ExchangeRequest request, ExchangeActor actor, CashSession session, long terminalId,
        string key, string fingerprint, string exchangeNumber, DateTime nowUtc)
    {
        var totals = evaluated.Totals;
        var exchange = new Exchange
        {
            BusinessId = businessId,
            ExchangeNumber = exchangeNumber,
            ReceivingOutletId = request.ReceivingOutletId,
            TerminalId = terminalId,
            CashSessionId = session.Id,
            BusinessDate = session.BusinessDate.Date,
            EligibilityLocalDate = evaluated.EligibilityLocalDate.ToDateTime(TimeOnly.MinValue),
            TimeZoneId = evaluated.TimeZoneId,
            CompletedAtUtc = nowUtc,
            CashierUserId = actor.UserId,
            SalespersonUserId = request.SalespersonUserId,
            CustomerId = evaluated.CustomerId,
            Status = ExchangeStatuses.Completed,
            ReasonCode = request.ReasonCode,
            ReasonOtherText = request.ReasonCode == ExchangeReasonCodes.Other ? request.ReasonOtherText?.Trim() : null,
            InternalNote = string.IsNullOrWhiteSpace(request.InternalNote) ? null : request.InternalNote.Trim(),
            PhoneVerificationResult = evaluated.PhoneVerificationResult,
            ReturnNet = totals.ReturnNet,
            ReturnVat = totals.ReturnVat,
            ReturnGross = totals.ReturnGross,
            ReturnCost = totals.ReturnCost,
            ReplacementNet = totals.ReplacementNet,
            ReplacementDiscount = totals.ReplacementDiscount,
            ReplacementVat = totals.ReplacementVat,
            ReplacementGross = totals.ReplacementGross,
            ReplacementCost = totals.ReplacementCost,
            NetAdjustmentNet = totals.NetAdjustmentNet,
            NetAdjustmentVat = totals.NetAdjustmentVat,
            NetAdjustmentGross = totals.NetAdjustmentGross,
            HighValueAmount = totals.HighValueAmount,
            HighValueThresholdApplied = evaluated.Policy.HighValueApprovalThreshold,
            SettlementDirection = totals.Direction,
            SettlementAmount = totals.SettlementAmount,
            PolicyVersionId = evaluated.Policy.Id,
            PolicySnapshot = JsonSerializer.Serialize(evaluated.Policy, Json),
            PreviewHash = evaluated.PreviewHash,
            LoyaltyStatus = ExchangeLoyaltyStatuses.ModuleInactive,
            IdempotencyKey = key,
            RequestFingerprint = fingerprint,
            CreatedAtUtc = nowUtc
        };

        foreach (var sale in evaluated.SourceSales)
            exchange.SourceInvoices.Add(new ExchangeSourceInvoice
            {
                SaleId = sale.Id,
                OriginalOutletId = sale.OutletId,
                CustomerId = sale.CustomerId,
                InvoiceNumberSnapshot = sale.SaleNumber,
                SaleBusinessDateSnapshot = sale.BusinessDate?.Date,
                PurchaseLocalDate = ExchangeCalculator.LocalDate(sale.SaleDate, evaluated.TimeZoneId).ToDateTime(TimeOnly.MinValue)
            });

        foreach (var returned in evaluated.Returns)
        {
            var line = new ExchangeReturnLine
            {
                SourceKind = returned.Input.SourceKind,
                SaleId = returned.Sale?.Id,
                SaleItemId = returned.SaleItem?.Id,
                SourceReplacementLineId = returned.SourceReplacement?.Id,
                RootSaleItemId = returned.SaleItem?.Id ?? 0,
                LineageDepth = returned.SourceDepth,
                VariantId = returned.VariantId,
                ReturnedQuantity = returned.Input.Quantity,
                CreditNet = returned.CreditNet,
                CreditVat = returned.CreditVat,
                CreditGross = returned.CreditNet + returned.CreditVat,
                CostRestored = returned.ContentCost,
                Condition = "resellable",
                PackagingReusable = returned.Input.PackagingReusable
            };
            var snapshot = returned.SaleItem?.ReadUomSnapshot() ?? returned.SourceReplacement?.ReadUomSnapshot();
            if (snapshot is not null)
                line.CaptureUomSnapshot(snapshot);
            exchange.ReturnLines.Add(line);
        }

        var depth = evaluated.Returns.Max(line => line.SourceDepth) + 1;
        foreach (var replacement in evaluated.Replacements)
        {
            var issued = new ExchangeReplacementLine
            {
                VariantId = replacement.Input.VariantId,
                IssuedQuantity = replacement.Uom.BaseQuantity,
                UnitPrice = replacement.Line.UnitPrice,
                LineDiscount = replacement.Line.LineDiscount,
                InvoiceDiscountShare = replacement.Line.InvoiceDiscountShare,
                TaxableAmount = replacement.Line.TaxableAmount,
                VatRate = replacement.Line.TaxRatePercent,
                VatAmount = replacement.Line.VatAmount,
                TaxCode = replacement.Line.TaxRatePercent > 0m ? "VAT" : null,
                TaxClassification = replacement.Line.TaxClassification,
                TaxInclusive = evaluated.ReplacementTotals.TaxInclusive,
                HistoricalUnitCost = replacement.HistoricalUnitCost,
                PackState = replacement.Uom.Form?.State,
                PackCode = replacement.Uom.Form?.PackCode,
                BaseQtyPerPack = replacement.Uom.Form?.BaseQtyPerPack,
                LineageDepth = depth,
                AppliedRuleId = replacement.Priced.AppliedRuleId,
                AppliedRuleName = replacement.Priced.AppliedRuleName
            };
            if (replacement.Uom.Resolution is { } resolution)
                issued.CaptureUomSnapshot(resolution.Snapshot);
            exchange.ReplacementLines.Add(issued);
        }

        return exchange;
    }

    /// <summary>The same request hashes the same way whatever the order of its parts.</summary>
    private static string Fingerprint(ConfirmExchangeCommand command)
    {
        var request = command.Request;
        var parts = new List<string>
        {
            command.PreviewHash,
            request.ReceivingOutletId.ToString(CultureInfo.InvariantCulture),
            string.Join(',', request.SourceSaleIds.OrderBy(id => id)),
            string.Join(';', request.Returns.Select(line => $"{line.SourceKind}:{line.SourceLineId}:{ExchangeCalculator.Quantity(line.Quantity)}:{line.PackagingReusable}")
                .OrderBy(text => text, StringComparer.Ordinal)),
            string.Join(';', request.Replacements.Select(line => $"{line.VariantId}:{line.SellingOptionId}:{ExchangeCalculator.Quantity(line.Quantity)}:{ExchangeCalculator.Money(line.DiscountAmount)}:{line.FulfilmentState}:{line.IncludePackaging}")),
            ExchangeCalculator.Money(request.InvoiceDiscount),
            string.Join(';', command.Settlements.Select(plan => $"{plan.Direction}:{plan.Method}:{ExchangeCalculator.Money(plan.Amount)}:{plan.Tendered}:{plan.ExternalReference}:{plan.ExternalRefundConfirmed}")
                .OrderBy(text => text, StringComparer.Ordinal))
        };
        return ExchangeCalculator.Sha256(string.Join('#', parts));
    }
}

/// <summary>Shared supervisor checks: every permission listed, plus server-verified access to the receiving outlet (OC-01).</summary>
public static class ExchangeSupervisorAuthorization
{
    public static async Task<string> AuthorizeAsync(
        IUserOutletAccess outletAccess, long supervisorId, string? roleName, string? rolePermissions, string? overrides,
        long businessId, long receivingOutletId, IEnumerable<string> types)
    {
        var permissions = Authentication.Commands.LoginCommandHandler.BuildEffectivePermissions(rolePermissions, overrides);
        if (types.Any(type => !Identity.Permissions.PermissionCatalog.IsGranted(permissions, GrantExchangeApprovalsCommandHandler.RequiredPermission[type])))
            throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalNotPermitted, "This supervisor is not allowed to approve this exception.");

        AuthorizedOutletsReadModel authorized;
        try
        {
            authorized = await outletAccess.GetAuthorizedOutletsAsync(new UserOutletAccessContext(
                supervisorId, false, string.Equals(roleName, "BusinessOwner", StringComparison.OrdinalIgnoreCase), businessId, null));
        }
        catch (UnauthorizedAccessException)
        {
            throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalNotPermitted, "This supervisor has no access to this outlet.");
        }

        if (authorized.IsGlobalAccess)
            return "global_entitlement";
        if (authorized.Outlets.Any(outlet => outlet.Id == receivingOutletId))
            return "assignment";
        throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalNotPermitted, "This supervisor has no access to this outlet.");
    }
}
