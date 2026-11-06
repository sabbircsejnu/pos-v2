using MediatR;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Commands;

/// <summary>
/// Opens a Cash Session (FR-CS-03, D8, D9). The BusinessDate, the opening instant, the effective time zone and the
/// duration limit are all resolved by the server; nothing about them is accepted from the client.
/// </summary>
public sealed record OpenCashSessionCommand(
    long OutletId,
    long TerminalId,
    decimal OpeningCashAmount,
    string? OpeningNotes,
    long OpenedByUserId,
    string? IdempotencyKey = null) : IRequest<CashSessionReadModel>;

public sealed class OpenCashSessionCommandHandler(
    ICashSessionStore store,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    IBusinessPosSettingsStore settingsStore,
    ICompanyTimeZoneProvider timeZones,
    ICashSessionOpenAuditWriter auditWriter,
    ICashSessionOpenedPublisher eventPublisher,
    ICashSessionEventWriter eventWriter,
    TimeProvider clock) : IRequestHandler<OpenCashSessionCommand, CashSessionReadModel>
{
    public async Task<CashSessionReadModel> Handle(
        OpenCashSessionCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outletId = await outletAuthorizer.AuthorizeWriteAsync(request.OutletId, cancellationToken);
        if (request.OpeningCashAmount < 0 || decimal.Round(request.OpeningCashAmount, 2) != request.OpeningCashAmount)
            throw new CashSessionException(
                CashSessionErrorCodes.OpeningCashInvalid,
                "Opening cash must be zero or a positive amount with at most two decimals.");

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is { Length: > 64 })
            throw new CashSessionException(CashSessionErrorCodes.IdempotencyConflict, "The idempotency key is too long.");

        if (key is not null)
        {
            var replay = await store.FindByIdempotencyKeyAsync(outletId, key, cancellationToken);
            if (replay is not null)
                return Replay(replay, request);
        }

        var context = await store.GetOpenContextAsync(outletId, cancellationToken)
            ?? throw new KeyNotFoundException($"Outlet with ID {outletId} not found.");
        var terminal = ResolveTerminal(context, request.TerminalId);

        var settings = await settingsStore.GetAsync(context.BusinessId, cancellationToken);
        var timeZoneId = await timeZones.GetTimeZoneIdAsync(context.BusinessId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var businessDate = CashSessionTime.BusinessDateFor(now, timeZoneId);

        var session = new CashSession
        {
            BusinessId = context.BusinessId,
            OutletId = outletId,
            TerminalId = terminal.Id,
            BusinessDate = businessDate,
            OpenedAt = now,
            OpeningCashAmount = request.OpeningCashAmount,
            Status = CashSessionStatuses.Open,
            OpenedByUserId = request.OpenedByUserId,
            OpeningNotes = string.IsNullOrWhiteSpace(request.OpeningNotes) ? null : request.OpeningNotes.Trim(),
            TimeZoneIdSnapshot = timeZoneId,
            DurationSnapshotHours = settings.MaxCashSessionDurationHours,
            OpenIdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var added = await store.AddOpenSessionAsync(session, cancellationToken);
            if (added == CashSessionAddResult.TerminalAlreadyOpen)
                throw new CashSessionException(
                    CashSessionErrorCodes.AlreadyOpen,
                    "An active cash session already exists for this terminal. Close it before opening a new one.",
                    409);
            if (added == CashSessionAddResult.IdempotencyReplay)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                var existing = await store.FindByIdempotencyKeyAsync(outletId, key!, cancellationToken)
                    ?? throw new CashSessionException(CashSessionErrorCodes.IdempotencyConflict, "Idempotent open could not be replayed.", 409);
                return Replay(existing, request);
            }

            await eventWriter.AppendAsync(
                new CashSessionEvent(
                    CashSessionEventTypes.Opened,
                    context.BusinessId,
                    "CashSession",
                    session.Id.ToString(),
                    "opened",
                    now,
                    new
                    {
                        businessId = context.BusinessId,
                        cashSessionId = session.Id,
                        outletId,
                        terminalId = terminal.Id,
                        businessDate = businessDate.ToString("yyyy-MM-dd"),
                        openedAtUtc = now,
                        timeZoneId,
                        durationHours = session.DurationSnapshotHours,
                        openingCash = session.OpeningCashAmount,
                        openedByUserId = request.OpenedByUserId
                    }),
                cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        var snapshot = CashSessionMapper.ToSnapshot(session);
        await auditWriter.RecordOpenedAsync(snapshot, request.OpeningCashAmount, cancellationToken);
        await eventPublisher.PublishOpenedAsync(snapshot, cancellationToken);
        return CashSessionMapper.ToReadModel(session, now);
    }

    private static CashSessionTerminalInfo ResolveTerminal(CashSessionOpenContext context, long requestedTerminalId)
    {
        if (context.ActiveTerminals.Count == 0)
            throw new CashSessionException(
                CashSessionErrorCodes.NoActiveTerminal,
                "No active terminal is configured for this outlet. Add or activate a terminal before opening a cash session.");

        if (requestedTerminalId > 0)
        {
            return context.ActiveTerminals.FirstOrDefault(terminal => terminal.Id == requestedTerminalId)
                ?? throw new CashSessionException(
                    CashSessionErrorCodes.TerminalInvalid,
                    "Selected terminal is not active for this outlet.");
        }

        // D11: exactly one active terminal is auto-selected; several require an explicit choice (no server default).
        if (context.ActiveTerminals.Count == 1)
            return context.ActiveTerminals[0];

        throw new CashSessionException(
            CashSessionErrorCodes.TerminalRequired,
            "This outlet has several active terminals. Select the terminal to open.");
    }

    private CashSessionReadModel Replay(CashSession existing, OpenCashSessionCommand request)
    {
        var sameRequest = existing.OutletId == request.OutletId
            && (request.TerminalId <= 0 || existing.TerminalId == request.TerminalId)
            && existing.OpeningCashAmount == request.OpeningCashAmount
            && existing.OpenedByUserId == request.OpenedByUserId;
        if (!sameRequest)
            throw new CashSessionException(
                CashSessionErrorCodes.IdempotencyConflict,
                "This idempotency key was already used for a different cash session request.",
                409);

        return CashSessionMapper.ToReadModel(existing, clock.GetUtcNow().UtcDateTime);
    }
}
