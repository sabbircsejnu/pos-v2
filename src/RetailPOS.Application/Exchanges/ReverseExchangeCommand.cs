using System.Globalization;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Exchanges;

public sealed record ReverseExchangeCommand(
    long ExchangeId,
    string Reason,
    long? TerminalId,
    string IdempotencyKey,
    IReadOnlyList<ExchangeSettlementInput> Settlements,
    long ActorUserId) : IRequest<ExchangeReversalResultReadModel>;

public sealed record ExchangeReversalResultReadModel(long ExchangeId, long ReversalId, string ReversalNumber, bool Replayed);

public sealed record GetExchangeReversalEligibilityQuery(long ExchangeId) : IRequest<ExchangeReversalEligibilityReadModel>;

/// <param name="SettlementDirection">The reversal's own direction: a collected difference is refunded and vice versa.</param>
public sealed record ExchangeReversalEligibilityReadModel(bool CanReverse, IReadOnlyList<string> BlockingReasons, string SettlementDirection, decimal SettlementAmount);

/// <summary>The customer-facing document of a reversal (EX-12, EX-14). Like the exchange receipt it never carries the internal note.</summary>
public sealed record ExchangeReversalReceiptDocument(
    int Version,
    string Kind,
    string ReversalNumber,
    long ReversalId,
    string ExchangeNumber,
    long ExchangeId,
    long BusinessId,
    long OutletId,
    string BusinessDate,
    DateTime CompletedAtUtc,
    long ActorUserId,
    string Reason,
    decimal NetAdjustmentGross,
    string SettlementDirection,
    decimal SettlementAmount,
    IReadOnlyList<ExchangeReceiptSettlement> Settlements);

/// <summary>One compensating stock movement: the exact mirror of an exchange ledger row.</summary>
public sealed record ExchangeReversalMovement(ExchangeStockMovement Original, decimal QtyIn, decimal QtyOut);

public sealed record ExchangeReversalStockPlan(IReadOnlyList<ExchangeReversalMovement> Movements, decimal SkippedPackagingValue)
{
    /// <summary>Net quantity each variant must still have on hand for the reversal to take it back out.</summary>
    public IReadOnlyDictionary<long, decimal> NetOutgoing => Movements
        .GroupBy(movement => movement.Original.VariantId)
        .ToDictionary(group => group.Key, group => group.Sum(movement => movement.QtyOut - movement.QtyIn));
}

/// <summary>Pure reversal arithmetic (EX-14, C-05): the stock mirror, the opposite settlement and the mirrored journal totals.</summary>
public static class ExchangeReversalPlanner
{
    public static ExchangeReversalStockPlan PlanStock(Exchange exchange, IReadOnlyList<ExchangeStockMovement> rows)
    {
        var replacementVariants = exchange.ReplacementLines.Select(line => line.VariantId).ToHashSet();
        var packagingVariants = exchange.ReplacementLines.SelectMany(line => line.Components)
            .Select(component => component.ComponentVariantId)
            .Where(id => !replacementVariants.Contains(id))
            .ToHashSet();
        var movements = new List<ExchangeReversalMovement>(rows.Count);
        var skipped = 0m;
        foreach (var row in rows)
        {
            if (row.QtyOut > 0m && packagingVariants.Contains(row.VariantId) && row.QtyOut % 1m != 0m)
            {
                // Packaging issued with a replacement comes back only in whole items: part of a bottle cannot go back on
                // a shelf. A fraction stays expensed (its value is left in COGS by the reversal journal) — the same rule
                // returns and voids apply to packaging (SaleUomReversal).
                skipped += row.MovementValue;
                continue;
            }

            movements.Add(new ExchangeReversalMovement(row, row.QtyOut, row.QtyIn));
        }

        return new ExchangeReversalStockPlan(movements, skipped);
    }

    /// <summary>The totals the original journal was composed from (final costs, packaging included).</summary>
    public static ExchangeTotals OriginalTotals(Exchange exchange) => new(
        exchange.ReturnNet, exchange.ReturnVat, exchange.ReturnCost,
        exchange.ReplacementNet, exchange.ReplacementVat, exchange.ReplacementDiscount, exchange.ReplacementCost);

    /// <summary>The legs swapped, so the planner sees the reversal's own direction and amount.</summary>
    public static ExchangeTotals ReversalSettlementTotals(Exchange exchange) => new(
        exchange.ReplacementNet, exchange.ReplacementVat, 0m, exchange.ReturnNet, exchange.ReturnVat, 0m, 0m);

    public static string ReversalDirection(Exchange exchange) => ExchangeCalculator.Direction(-exchange.NetAdjustmentGross);

    /// <summary>Settlements expressed in the original exchange's direction, for the mirrored journal.</summary>
    public static IReadOnlyList<ExchangeSettlementPlan> InOriginalDirection(IReadOnlyList<ExchangeSettlementPlan> plans) =>
        plans.Select(plan => plan with
        {
            Direction = plan.Direction == ExchangeSettlementDirections.Collect ? ExchangeSettlementDirections.Refund : ExchangeSettlementDirections.Collect
        }).ToList();

    public static ExchangeSalespersonFact NegatedFact(ExchangeSalespersonAdjustment original)
    {
        var signed = -original.SignedAmountNet;
        return new ExchangeSalespersonFact(signed > 0m ? signed : 0m, signed < 0m ? signed : 0m, signed, -original.TargetEffectAmountNet);
    }
}

public sealed class GetExchangeReversalEligibilityQueryHandler(
    IBusinessScope businessScope,
    IExchangeStore store,
    ISaleMutationOutletAuthorizer outletAuthorizer) : IRequestHandler<GetExchangeReversalEligibilityQuery, ExchangeReversalEligibilityReadModel>
{
    public async Task<ExchangeReversalEligibilityReadModel> Handle(GetExchangeReversalEligibilityQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var exchange = await store.LoadExchangeAsync(businessId, request.ExchangeId, cancellationToken)
            ?? throw ExchangeException.NotFound(ExchangeErrorCodes.NotFound, "The exchange was not found.");
        await outletAuthorizer.AuthorizeAsync(exchange.ReceivingOutletId, cancellationToken);

        var reasons = new List<string>();
        if (exchange.Status != ExchangeStatuses.Completed || exchange.ReversalId is not null)
            reasons.Add("This exchange has already been reversed.");
        if (exchange.ReplacementLines.Any(line => line.ReturnedQuantity > 0m))
            reasons.Add("A replacement from this exchange was exchanged again. Reverse the later exchange first.");

        if (reasons.Count == 0)
        {
            var plan = ExchangeReversalPlanner.PlanStock(exchange, await store.LoadStockMovementsAsync(exchange.Id, exchange.ReceivingOutletId, cancellationToken));
            var needed = plan.NetOutgoing.Where(entry => entry.Value > 0m).ToList();
            var stock = await store.ReadOutletStockAsync(exchange.ReceivingOutletId, needed.Select(entry => entry.Key).ToList(), cancellationToken);
            foreach (var (variantId, quantity) in needed)
                if (stock.GetValueOrDefault(variantId) < quantity)
                    reasons.Add($"The returned goods (variant ID {variantId}) are no longer in stock at this outlet: {ExchangeCalculator.Quantity(stock.GetValueOrDefault(variantId))} available, {ExchangeCalculator.Quantity(quantity)} needed.");
        }

        return new ExchangeReversalEligibilityReadModel(
            reasons.Count == 0, reasons, ExchangeReversalPlanner.ReversalDirection(exchange), Math.Abs(exchange.NetAdjustmentGross));
    }
}

/// <summary>
/// The controlled, whole reversal of a completed exchange (EX-14, C-05, D-02, D-09, D-15). Every compensating effect —
/// shared returned quantities, the stock mirror, pack state, the opposite settlement, the mirrored journal, the negated
/// salesperson fact, receipt, outbox and audit — commits in one transaction. The original rows are never edited: only the
/// header gains <c>status = reversed</c> together with its <c>reversal_id</c>. No partial reversal, no cascade.
/// Lock order: exchange → sales (ascending) → replacement lines (ascending) → variants → session → inventories (one
/// statement) → pack state → accounting period → journal → drawer last.
/// </summary>
public sealed class ReverseExchangeCommandHandler(
    IBusinessScope businessScope,
    IExchangeStore store,
    ISaleMutationOutletAuthorizer outletAuthorizer,
    ISaleCreationStore saleStore,
    ISaleCashSessionValidator sessionValidator,
    ICashSessionStore cashSessions,
    ICashSessionMovementRecorder cashMovements,
    IStockLedgerWriter stockLedger,
    IExchangeJournalPoster journal,
    IExchangeAuditWriter audit,
    IExchangeEventWriter events,
    IVariantUomReader? uomReader = null,
    IPackStateLedger? packState = null) : IRequestHandler<ReverseExchangeCommand, ExchangeReversalResultReadModel>
{
    private const int MaxIdempotencyKeyLength = 120;
    private const int MaxReasonLength = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExchangeReversalResultReadModel> Handle(ReverseExchangeCommand command, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var key = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxIdempotencyKeyLength)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "A reversal needs an idempotency key of at most 120 characters.");
        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.ReasonRequired, "Enter the reason for reversing this exchange.");
        if (reason.Length > MaxReasonLength)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, $"The reason can have at most {MaxReasonLength} characters.");
        var fingerprint = Fingerprint(command, reason);

        var earlier = await store.FindReversalByIdempotencyKeyAsync(businessId, key, cancellationToken);
        if (earlier is not null)
            return Replay(earlier, fingerprint);

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await ReverseAsync(businessId, key, reason, fingerprint, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return result;
        }
        catch (Exception exception) when (!committed && store.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var winner = await store.FindReversalByIdempotencyKeyAsync(businessId, key, cancellationToken);
            if (winner is not null)
                return Replay(winner, fingerprint);
            // The one-reversal-per-exchange index, or a concurrent stock row: the exchange changed underneath.
            throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "Another transaction changed this exchange or its stock. Try again.");
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

    private static ExchangeReversalResultReadModel Replay(ExchangeReversal earlier, string fingerprint) =>
        string.Equals(earlier.RequestFingerprint, fingerprint, StringComparison.Ordinal)
            ? new ExchangeReversalResultReadModel(earlier.ExchangeId, earlier.Id, earlier.ReversalNumber, true)
            : throw ExchangeException.Conflict(ExchangeErrorCodes.IdempotencyMismatch, "This idempotency key was already used for a different reversal.");

    private async Task<ExchangeReversalResultReadModel> ReverseAsync(
        long businessId, string key, string reason, string fingerprint, ReverseExchangeCommand command, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var actorId = command.ActorUserId;

        // 1: the exchange row, then the actor's write access to its receiving outlet.
        var exchange = await store.LoadExchangeForUpdateAsync(businessId, command.ExchangeId, cancellationToken)
            ?? throw ExchangeException.NotFound(ExchangeErrorCodes.NotFound, "The exchange was not found.");
        // A retry that waited on the exchange lock must replay the committed reversal, not be re-evaluated against it.
        if (await store.FindReversalByIdempotencyKeyAsync(businessId, key, cancellationToken) is { } committedMeanwhile)
            return Replay(committedMeanwhile, fingerprint);
        var outletId = exchange.ReceivingOutletId;
        await outletAuthorizer.AuthorizeAsync(outletId, cancellationToken);
        if (exchange.Status != ExchangeStatuses.Completed || exchange.ReversalId is not null)
            throw ExchangeException.Conflict(ExchangeErrorCodes.AlreadyReversed, "This exchange has already been reversed.");

        // The reversal's own settlement: the opposite direction for the same amount (D-02, D-09 rules apply as at confirmation).
        var settlements = ExchangeSettlementPlanner.Plan(ExchangeReversalPlanner.ReversalSettlementTotals(exchange), command.Settlements);

        // 2: source sales FOR UPDATE ascending, then every replacement line (own and re-exchanged sources) in one ascending lock.
        var saleIds = exchange.SourceInvoices.Select(invoice => invoice.SaleId)
            .Concat(exchange.ReturnLines.Where(line => line.SaleId is not null).Select(line => line.SaleId!.Value))
            .Distinct().ToList();
        var sales = await store.LoadSourceSalesAsync(businessId, saleIds, forUpdate: true, cancellationToken);
        var sourceLineIds = exchange.ReturnLines.Where(line => line.SourceReplacementLineId is not null)
            .Select(line => line.SourceReplacementLineId!.Value).Distinct().ToList();
        var ownLineIds = exchange.ReplacementLines.Select(line => line.Id).ToList();
        var lockedQuantities = await store.LockReplacementLineQuantitiesAsync(ownLineIds.Concat(sourceLineIds).ToList(), cancellationToken);
        if (ownLineIds.Any(id => lockedQuantities.GetValueOrDefault(id) > 0m))
            throw ExchangeException.Conflict(ExchangeErrorCodes.ReversalBlockedDependency,
                "A replacement from this exchange was exchanged again. Reverse the later exchange first; there is no partial or cascading reversal.");
        var sourceLines = sourceLineIds.Count == 0
            ? []
            : await store.LoadReplacementLinesAsync(businessId, sourceLineIds, forUpdate: true, cancellationToken);

        // 3: UOM configurations of every variant whose physical form may change.
        var formVariantIds = exchange.ReturnLines.Select(line => line.VariantId)
            .Concat(exchange.ReplacementLines.Select(line => line.VariantId)).Distinct().ToList();
        var configs = uomReader is null
            ? new Dictionary<long, ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(businessId, formVariantIds, cancellationToken);
        bool Tracks(long variantId) => packState is not null && configs.GetValueOrDefault(variantId)?.PackStateTrackingEnabled == true;

        // 4: the active session at the receiving outlet (a new business date is allowed).
        var terminalId = await ExchangeTerminals.ResolveAsync(saleStore, outletId, command.TerminalId, cancellationToken);
        if (!await store.LockActiveTerminalAsync(businessId, outletId, terminalId, cancellationToken))
            throw ExchangeTerminals.Unavailable(terminalId);

        CashSession session;
        try
        {
            session = await sessionValidator.ValidateActiveForSaleAsync(outletId, terminalId, actorId, cancellationToken);
        }
        catch (CashSessionException exception)
        {
            throw ExchangeException.Conflict(ExchangeErrorCodes.SessionRequired, exception.Message);
        }

        var businessDate = session.BusinessDate.Date;

        // 5: the stock mirror, and every receiving-outlet inventory row it touches locked in one statement.
        var plan = ExchangeReversalPlanner.PlanStock(exchange, await store.LoadStockMovementsAsync(exchange.Id, outletId, cancellationToken));
        var inventories = await store.LockOutletInventoriesAsync(outletId,
            plan.Movements.Select(movement => movement.Original.VariantId).Concat(formVariantIds).Distinct().ToList(), cancellationToken);
        foreach (var (variantId, needed) in plan.NetOutgoing)
        {
            var available = inventories.TryGetValue(variantId, out var row) ? row.Quantity : 0m;
            if (needed > 0m && available < needed)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.ReversalStockUnavailable,
                    $"The returned goods (variant ID {variantId}) are no longer in stock at this outlet. Available: {ExchangeCalculator.Quantity(available)}, needed: {ExchangeCalculator.Quantity(needed)}.");
        }

        // 6: accounts and accounting period on the reversal business date (C-06, D-15, VI-18).
        await journal.EnsureAccountsAsync(
            outletId,
            [ExchangeJournalComposer.SalesReturn, ExchangeJournalComposer.Sales, ExchangeJournalComposer.SalesDiscount,
             ExchangeJournalComposer.VatPayable, ExchangeJournalComposer.Cogs, ExchangeJournalComposer.Inventory],
            settlements.Select(item => item.Method).Distinct().ToList(),
            businessDate,
            cancellationToken);
        var period = await store.LockAccountingPeriodAsync(businessId, businessDate, cancellationToken);
        if (!period.IsOpen)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.AccountingPeriodClosed,
                period.Exists
                    ? $"The accounting period '{period.Name}' is {period.Status}; the reversal cannot be posted into it."
                    : $"No open accounting period covers {businessDate:yyyy-MM-dd}.");

        var reversalId = await store.ReserveReversalIdAsync(cancellationToken);
        var reversalNumber = $"{exchange.ExchangeNumber}-RV";

        // 7: give the shared returned-quantity entitlement back (C-03).
        var saleItems = sales.SelectMany(sale => sale.Items).ToDictionary(item => item.Id);
        var sourceById = sourceLines.ToDictionary(line => line.Id);
        foreach (var line in exchange.ReturnLines)
        {
            if (line.SaleItemId is { } itemId)
            {
                var item = saleItems.GetValueOrDefault(itemId)
                    ?? throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "A source invoice line could not be locked. Try again.");
                item.ReturnedQuantity -= line.ReturnedQuantity;
                if (item.ReturnedQuantity < 0m)
                    throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "The source invoice's returned quantity is inconsistent. Try again.");
            }
            else
            {
                var source = sourceById.GetValueOrDefault(line.SourceReplacementLineId!.Value)
                    ?? throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "A source replacement line could not be locked. Try again.");
                source.ReturnedQuantity -= line.ReturnedQuantity;
                if (source.ReturnedQuantity < 0m)
                    throw ExchangeException.Conflict(ExchangeErrorCodes.ConcurrencyConflict, "The source replacement's returned quantity is inconsistent. Try again.");
            }
        }

        // 8: stock — replacement goods (and whole packaging) come back first, then the returned goods leave again.
        foreach (var movement in plan.Movements.Where(item => item.QtyIn > 0m).Concat(plan.Movements.Where(item => item.QtyOut > 0m)))
        {
            var variantId = movement.Original.VariantId;
            if (!inventories.TryGetValue(variantId, out var inventory))
                inventories[variantId] = inventory = await store.CreateOutletInventoryAsync(variantId, outletId, cancellationToken);
            inventory.Quantity += movement.QtyIn - movement.QtyOut;
            if (inventory.Quantity < 0m)
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.ReversalStockUnavailable,
                    $"The returned goods (variant ID {variantId}) are no longer in stock at this outlet.");
            stockLedger.WriteEntryWithValuation(
                variantId, outletId, "outlet", StockLedgerTransactionType.ExchangeReversal,
                movement.QtyIn, movement.QtyOut, inventory.Quantity, StockLedgerReferenceType.ExchangeReversal, reversalId,
                $"Exchange reversal {reversalNumber}", actorId, nowUtc, movement.Original.UnitCost, movement.Original.MovementValue,
                businessDate, reversalOfMovementId: movement.Original.Id);
        }

        // 9: pack state stays reconciled with the moved stock.
        await ApplyPackStateAsync(businessId, exchange, saleItems, outletId, reversalId, actorId, Tracks, cancellationToken);
        foreach (var variantId in formVariantIds.Where(Tracks))
            if (inventories.TryGetValue(variantId, out var row))
                await packState!.EnsureReconciledAsync(businessId, variantId, outletId, "outlet", row.Quantity, cancellationToken);

        // 10: a cash refund may never exceed the expected drawer cash (EX-31, no override); the drawer lock is last.
        var cashOut = -settlements.Where(item => item.CashEffect < 0m).Sum(item => item.CashEffect);
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
            var item = settlements[index];
            long? movementId = null;
            if (item.CashEffect != 0m)
            {
                var movement = await cashMovements.RecordAsync(new CashSessionMovementRequest(
                    session.Id,
                    item.CashEffect > 0m ? CashSessionMovementTypes.ExchangeCashIn : CashSessionMovementTypes.ExchangeCashOut,
                    item.CashEffect,
                    "exchange_reversal",
                    reversalId,
                    $"exchange-reversal:{reversalId}:settlement:{index + 1}",
                    null,
                    null,
                    $"Exchange reversal {reversalNumber}",
                    actorId), cancellationToken);
                movementId = movement.Movement.Id;
            }

            settlementRows.Add(new ExchangeSettlement
            {
                ExchangeId = exchange.Id,
                ExchangeReversalId = reversalId,
                Direction = item.Direction,
                Method = item.Method,
                Amount = item.Amount,
                Tendered = item.Tendered,
                ChangeGiven = item.ChangeGiven,
                CashSessionId = session.Id,
                CashSessionMovementId = movementId,
                ExternalReference = item.ExternalReference,
                ExternalConfirmedByUserId = item.ExternalRefundConfirmed ? actorId : null,
                ExternalConfirmedAtUtc = item.ExternalRefundConfirmed ? nowUtc : null,
                CreatedAtUtc = nowUtc
            });
        }

        // 11: the mirrored journal — every original line with debit and credit swapped, on the reversal business date.
        // Packaging that cannot come back in whole items stays in COGS, so the mirror omits exactly that value.
        var journalTotals = ExchangeReversalPlanner.OriginalTotals(exchange) with
        {
            ReplacementCost = exchange.ReplacementCost - plan.SkippedPackagingValue
        };
        var journalLines = ExchangeJournalComposer.Compose(journalTotals, ExchangeReversalPlanner.InOriginalDirection(settlements), reverse: true);
        var journalEntryId = await journal.PostAsync(new ExchangeJournalPosting(
            businessId, actorId, outletId, terminalId, session.Id, businessDate, nowUtc,
            "ExchangeReversal", reversalId.ToString(CultureInfo.InvariantCulture), reversalNumber,
            $"Product exchange reversal {reversalNumber} of {exchange.ExchangeNumber}", $"exchange-reversal:{reversalId}:v1",
            journalLines), cancellationToken);

        // 12: the reversal record first (its id is referenced by every row below), then the dependent rows and the header.
        var reversal = new ExchangeReversal
        {
            Id = reversalId,
            BusinessId = businessId,
            ExchangeId = exchange.Id,
            ReversalNumber = reversalNumber,
            BusinessDate = businessDate,
            ReceivingOutletId = outletId,
            TerminalId = terminalId,
            CashSessionId = session.Id,
            ActorUserId = actorId,
            Reason = reason,
            NetAdjustmentGross = -exchange.NetAdjustmentGross,
            RetainedPackagingCost = plan.SkippedPackagingValue,
            JournalEntryId = journalEntryId,
            IdempotencyKey = key,
            RequestFingerprint = fingerprint,
            CompletedAtUtc = nowUtc
        };
        await store.AddReversalAsync(reversal, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await store.AddSettlementsAsync(settlementRows, cancellationToken);
        var original = await store.FindSalespersonAdjustmentAsync(exchange.Id, cancellationToken);
        if (original is not null)
        {
            var fact = ExchangeReversalPlanner.NegatedFact(original);
            await store.AddSalespersonAdjustmentAsync(new ExchangeSalespersonAdjustment
            {
                BusinessId = businessId,
                ExchangeId = exchange.Id,
                ExchangeReversalId = reversalId,
                BusinessDate = businessDate,
                ReceivingOutletId = outletId,
                CashierUserId = actorId,
                SalespersonUserId = original.SalespersonUserId,
                PositiveAmountNet = fact.Positive,
                NegativeAmountNet = fact.Negative,
                SignedAmountNet = fact.Signed,
                TargetEffectAmountNet = fact.TargetEffect,
                NegativeMode = original.NegativeMode,
                PolicyVersionId = original.PolicyVersionId,
                CreatedAtUtc = nowUtc
            }, cancellationToken);
        }

        var direction = ExchangeReversalPlanner.ReversalDirection(exchange);
        await store.AddReceiptSnapshotAsync(new ExchangeReceiptSnapshot
        {
            ExchangeId = exchange.Id,
            ExchangeReversalId = reversalId,
            DocumentVersion = 1,
            Document = JsonSerializer.Serialize(new ExchangeReversalReceiptDocument(
                1, "reversal", reversalNumber, reversalId, exchange.ExchangeNumber, exchange.Id, businessId, outletId,
                businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), nowUtc, actorId, reason,
                -exchange.NetAdjustmentGross, direction, Math.Abs(exchange.NetAdjustmentGross),
                settlementRows.Select(row => new ExchangeReceiptSettlement(
                    row.Direction, row.Method, row.Amount, row.Tendered, row.ChangeGiven,
                    row.ExternalReference is { Length: > 4 } reference ? reference[^4..] : row.ExternalReference)).ToList()), Json),
            CreatedAtUtc = nowUtc
        }, cancellationToken);
        await events.AppendAsync(businessId, "pos.exchange.reversed", exchange.Id, "reversed", new
        {
            businessId,
            exchangeId = exchange.Id,
            exchangeNumber = exchange.ExchangeNumber,
            reversalId,
            reversalNumber,
            businessDate = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            receivingOutletId = outletId,
            customerId = exchange.CustomerId,
            completedAtUtc = nowUtc,
            netAdjustmentGross = -exchange.NetAdjustmentGross,
            netAdjustmentNet = -exchange.NetAdjustmentNet,
            loyaltyStatus = ExchangeLoyaltyStatuses.ModuleInactive
        }, nowUtc, cancellationToken);

        // The database allows exactly this change: completed → reversed together with the reversal id.
        exchange.Status = ExchangeStatuses.Reversed;
        exchange.ReversalId = reversalId;
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync("exchange.reversed", $"Exchange {exchange.ExchangeNumber} reversed ({reversalNumber})", exchange.Id, new Dictionary<string, object?>
        {
            ["exchangeNumber"] = exchange.ExchangeNumber,
            ["reversalId"] = reversalId,
            ["reversalNumber"] = reversalNumber,
            ["reason"] = reason,
            ["receivingOutletId"] = outletId,
            ["cashSessionId"] = session.Id,
            ["settlementDirection"] = direction,
            ["settlementAmount"] = Math.Abs(exchange.NetAdjustmentGross),
            ["journalEntryId"] = journalEntryId,
            ["skippedPackagingValue"] = plan.SkippedPackagingValue
        }, cancellationToken);

        await sessionValidator.ValidateFinalGateAsync(session, cancellationToken);
        return new ExchangeReversalResultReadModel(exchange.Id, reversalId, reversalNumber, false);
    }

    /// <summary>
    /// Pack form follows the moved stock (UOM D04/D10). Returned goods leave in the form the exchange put them back in
    /// (whole reusable packs as packs, otherwise loose). Replacement goods come back loose, except prepared packs, which
    /// come back as prepared packs with the capitalized value the exchange took out, so the mirrored journal and the
    /// stock valuation stay equal.
    /// </summary>
    private async Task ApplyPackStateAsync(
        long businessId,
        Exchange exchange,
        IReadOnlyDictionary<long, SaleItem> saleItems,
        long outletId,
        long reversalId,
        long actorId,
        Func<long, bool> tracks,
        CancellationToken cancellationToken)
    {
        if (packState is null)
            return;
        foreach (var line in exchange.ReplacementLines.Where(line => tracks(line.VariantId)))
        {
            var perPack = line.BaseQtyPerPack ?? 0m;
            if (line.PackState == PackStates.Prepared && perPack > 0m && line.IssuedQuantity % perPack == 0m)
                await packState.ApplyAsync(new PackStateChange(businessId, line.VariantId, outletId, "outlet", PackStates.Prepared,
                    line.PackCode ?? string.Empty, perPack, (int)(line.IssuedQuantity / perPack), line.IssuedQuantity, line.PackagingCost,
                    PackStateOperations.SaleReversal, StockLedgerReferenceType.ExchangeReversal, reversalId, actorId), cancellationToken);
            else
                await packState.ApplyAsync(new PackStateChange(businessId, line.VariantId, outletId, "outlet", PackStates.Loose,
                    string.Empty, 0m, 0, line.IssuedQuantity, 0m,
                    PackStateOperations.SaleReversal, StockLedgerReferenceType.ExchangeReversal, reversalId, actorId), cancellationToken);
        }

        foreach (var line in exchange.ReturnLines.Where(line => tracks(line.VariantId)))
        {
            var item = line.SaleItemId is { } itemId ? saleItems.GetValueOrDefault(itemId) : null;
            var perPack = item?.BaseQtyPerPack ?? 0m;
            var asPacks = item?.PackState is { } state && state != PackStates.Loose && line.PackagingReusable
                && perPack > 0m && line.ReturnedQuantity % perPack == 0m;
            if (asPacks)
            {
                var packs = (int)(line.ReturnedQuantity / perPack);
                var capitalized = 0m;
                if (item!.PackState == PackStates.Prepared)
                {
                    var balances = await packState.GetBalancesForUpdateAsync(businessId, line.VariantId, outletId, "outlet", cancellationToken);
                    var prepared = balances.FirstOrDefault(row =>
                        row.State == PackStates.Prepared && row.PackCode == (item.PackCode ?? string.Empty) && row.BaseQtyPerPack == perPack);
                    capitalized = prepared is null ? 0m : CapitalizedValueShare.For(prepared.CapitalizedValue, prepared.PackCount, packs);
                }

                await packState.ApplyAsync(new PackStateChange(businessId, line.VariantId, outletId, "outlet", item.PackState!,
                    item.PackCode ?? string.Empty, perPack, -packs, -line.ReturnedQuantity, -capitalized,
                    PackStateOperations.Sale, StockLedgerReferenceType.ExchangeReversal, reversalId, actorId), cancellationToken);
            }
            else
            {
                await packState.ApplyAsync(new PackStateChange(businessId, line.VariantId, outletId, "outlet", PackStates.Loose,
                    string.Empty, 0m, 0, -line.ReturnedQuantity, 0m,
                    PackStateOperations.Sale, StockLedgerReferenceType.ExchangeReversal, reversalId, actorId), cancellationToken);
            }
        }
    }

    private static string Fingerprint(ReverseExchangeCommand command, string reason) =>
        ExchangeCalculator.Sha256(string.Join('#',
            command.ExchangeId.ToString(CultureInfo.InvariantCulture),
            reason,
            command.TerminalId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join(';', command.Settlements
                .Select(item => $"{item.Direction}:{item.Method}:{ExchangeCalculator.Money(item.Amount)}:{item.Tendered}:{item.ExternalReference}:{item.ExternalRefundConfirmed}")
                .OrderBy(text => text, StringComparer.Ordinal))));
}
