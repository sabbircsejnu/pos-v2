using System.Text.Json;
using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;

namespace RetailPOS.Application.Exchanges;

public sealed record GetExchangeHistoryQuery(string? Search, DateOnly? From, DateOnly? To, long? OutletId, string? Status, int Page, int PageSize)
    : IRequest<ExchangeHistoryPageReadModel>;

/// <summary>Exchange metrics by business date (EX-20, D-06, D-07). Dates default to today's business date.</summary>
public sealed record GetExchangeMetricsQuery(DateOnly? From, DateOnly? To, long? OutletId) : IRequest<ExchangeMetricsReadModel>;

public sealed record GetExchangeDetailQuery(long ExchangeId) : IRequest<ExchangeDetailReadModel>;

public sealed record GetExchangesBySaleQuery(long SaleId) : IRequest<IReadOnlyList<ExchangeHistoryItemReadModel>>;

public sealed record GetExchangeReceiptDocumentQuery(long ExchangeId) : IRequest<ExchangeReceiptDocumentReadModel>;

public sealed record ExchangeReceiptPdfResult(byte[] Content, string ExchangeNumber);

public sealed record GetExchangeReceiptPdfQuery(long ExchangeId, string? Mode) : IRequest<ExchangeReceiptPdfResult>;

public static class ExchangePrintActions
{
    public const string Print = "print";
    public const string Reprint = "reprint";
    public const string PrintAttemptFailed = "print_attempt_failed";
}

public sealed record RecordExchangeReceiptPrintCommand(long ExchangeId, long? TerminalId, string Action, long? UserId) : IRequest<Unit>;

/// <summary>
/// Exchange history (EX-30, D-11): business plus the caller's sales read scope, applied to the receiving outlet. An exchange
/// outside it — another business, or an outlet the caller cannot read — is reported as not found.
/// </summary>
public sealed class ExchangeHistoryQueryHandlers(
    IBusinessScope businessScope,
    ISaleReadScopeProvider readScope,
    IExchangeReadStore readStore,
    IExchangeStore store,
    ICompanyTimeZoneProvider timeZones,
    IExchangeReceiptPdfRenderer pdfRenderer) :
    IRequestHandler<GetExchangeHistoryQuery, ExchangeHistoryPageReadModel>,
    IRequestHandler<GetExchangeDetailQuery, ExchangeDetailReadModel>,
    IRequestHandler<GetExchangeMetricsQuery, ExchangeMetricsReadModel>,
    IRequestHandler<GetExchangesBySaleQuery, IReadOnlyList<ExchangeHistoryItemReadModel>>,
    IRequestHandler<GetExchangeReceiptDocumentQuery, ExchangeReceiptDocumentReadModel>,
    IRequestHandler<GetExchangeReceiptPdfQuery, ExchangeReceiptPdfResult>,
    IRequestHandler<RecordExchangeReceiptPrintCommand, Unit>
{
    public const int MaxPageSize = 100;
    private const int MaxSearchLength = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExchangeHistoryPageReadModel> Handle(GetExchangeHistoryQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var scope = await readScope.GetAsync(request.OutletId, cancellationToken);
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, MaxPageSize);
        var search = request.Search?.Trim();
        if (search is { Length: > MaxSearchLength })
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, $"Search can have at most {MaxSearchLength} characters.");
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToLowerInvariant();
        if (status is not null && status != ExchangeStatuses.Completed && status != ExchangeStatuses.Reversed)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "Status must be 'completed' or 'reversed'.");
        if (request.From is { } from && request.To is { } to && from > to)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The start date must not be after the end date.");
        return await readStore.SearchAsync(businessId, scope.OutletIds, string.IsNullOrEmpty(search) ? null : search,
            request.From, request.To, status, page, pageSize, cancellationToken);
    }

    public const int MaxMetricsDays = 366;

    public async Task<ExchangeMetricsReadModel> Handle(GetExchangeMetricsQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var scope = await readScope.GetAsync(request.OutletId, cancellationToken);
        var today = ExchangeCalculator.LocalDate(DateTime.UtcNow, await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        var to = request.To ?? request.From ?? today;
        var from = request.From ?? to;
        if (from > to)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The start date must not be after the end date.");
        if (to.DayNumber - from.DayNumber >= MaxMetricsDays)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, $"Choose a period of at most {MaxMetricsDays} days.");
        return await readStore.GetMetricsAsync(businessId, scope.OutletIds, from, to, cancellationToken);
    }

    public async Task<ExchangeDetailReadModel> Handle(GetExchangeDetailQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return await readStore.GetDetailAsync(businessId, await VisibleOutletsAsync(cancellationToken), request.ExchangeId, cancellationToken)
            ?? throw NotFound();
    }

    public async Task<IReadOnlyList<ExchangeHistoryItemReadModel>> Handle(GetExchangesBySaleQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        // L3: a sale outside the user's outlets answers exactly like one that does not exist (no existence leak), the same
        // as exchange detail and receipts.
        var outlets = await VisibleOutletsAsync(cancellationToken);
        if (outlets.Count == 0 || !await readStore.SaleVisibleAsync(businessId, outlets, request.SaleId, cancellationToken))
            throw ExchangeException.NotFound(ExchangeErrorCodes.SourceNotFound, "The sale was not found.");
        return await readStore.ListBySaleAsync(businessId, outlets, request.SaleId, cancellationToken);
    }

    public async Task<ExchangeReceiptDocumentReadModel> Handle(GetExchangeReceiptDocumentQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var receipt = await readStore.GetReceiptAsync(businessId, await VisibleOutletsAsync(cancellationToken), request.ExchangeId, cancellationToken)
            ?? throw NotFound();
        using var parsed = JsonDocument.Parse(receipt.Json);
        var document = parsed.RootElement.Clone();
        var names = await readStore.GetVariantNamesAsync(businessId, VariantIds(document), cancellationToken);
        return new ExchangeReceiptDocumentReadModel(
            document, receipt.BusinessName, receipt.OutletName, receipt.CashierName, receipt.SalespersonName,
            names.ToDictionary(entry => entry.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), entry => entry.Value));
    }

    public async Task<ExchangeReceiptPdfResult> Handle(GetExchangeReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "thermal" : request.Mode.Trim().ToLowerInvariant();
        if (mode != "thermal" && mode != "a4")
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "Mode must be 'thermal' or 'a4'.");
        var receipt = await readStore.GetReceiptAsync(businessId, await VisibleOutletsAsync(cancellationToken), request.ExchangeId, cancellationToken)
            ?? throw NotFound();
        var document = JsonSerializer.Deserialize<ExchangeReceiptDocument>(receipt.Json, Json)
            ?? throw NotFound();
        var variantIds = document.Returned.Concat(document.Replacements).Select(line => line.VariantId).Distinct().ToList();
        var names = await readStore.GetVariantNamesAsync(businessId, variantIds, cancellationToken);
        var zone = CashSessionTime.FindZone(await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        var local = TimeZoneInfo.ConvertTimeFromUtc(UtcTime.Normalize(document.CompletedAtUtc), zone);
        var content = pdfRenderer.Render(new ExchangeReceiptPdfModel(
            mode, document, receipt.BusinessName, receipt.OutletName, receipt.CashierName, receipt.SalespersonName, receipt.CustomerName, local, names));
        return new ExchangeReceiptPdfResult(content, document.ExchangeNumber);
    }

    public async Task<Unit> Handle(RecordExchangeReceiptPrintCommand request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var visible = await readStore.FindVisibleAsync(businessId, await VisibleOutletsAsync(cancellationToken), request.ExchangeId, cancellationToken)
            ?? throw NotFound();
        if (request.TerminalId is { } terminalId && !await readStore.IsOutletTerminalAsync(visible.ReceivingOutletId, terminalId, cancellationToken))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "The terminal does not belong to the exchange's outlet.");
        await store.AddPrintHistoryAsync(new ExchangeReceiptPrintHistory
        {
            ExchangeId = visible.Id,
            TerminalId = request.TerminalId,
            PrintedByUserId = request.UserId,
            ActionType = request.Action,
            PrintedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    /// <summary>A caller without any readable outlet sees nothing; an id outside the scope is simply not found.</summary>
    private async Task<IReadOnlyList<long>> VisibleOutletsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await readScope.GetAsync(null, cancellationToken)).OutletIds;
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyCollection<long> VariantIds(JsonElement document)
    {
        var ids = new HashSet<long>();
        foreach (var property in new[] { "returned", "replacements" })
            if (document.TryGetProperty(property, out var lines) && lines.ValueKind == JsonValueKind.Array)
                foreach (var line in lines.EnumerateArray())
                    if (line.TryGetProperty("variantId", out var id) && id.TryGetInt64(out var value))
                        ids.Add(value);
        return ids;
    }

    private static ExchangeException NotFound() => ExchangeException.NotFound(ExchangeErrorCodes.NotFound, "The exchange was not found.");
}
