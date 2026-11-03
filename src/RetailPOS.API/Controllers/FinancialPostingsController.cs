using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Controllers;

public sealed record SaleFinancialPostingModel(
    long Id,
    long SaleId,
    string Kind,
    string? ReversalType,
    string SourceKey,
    string Status,
    long OutletId,
    DateOnly BusinessDate,
    int Attempts,
    string? LastErrorCode,
    string? LastError,
    DateTime? NextAttemptAtUtc,
    DateTime? LastAttemptAtUtc,
    long? JournalEntryId,
    DateTime? PostedAtUtc,
    DateTime CreatedAtUtc);

public sealed record SaleFinancialPostingRetryModel(string Outcome, SaleFinancialPostingModel Posting);

/// <summary>Liveness of one worker instance. Batch volumes span Businesses and are therefore not exposed here.</summary>
public sealed record FinancialPostingWorkerLivenessModel(
    string Worker,
    string InstanceId,
    string HostName,
    string HostRole,
    DateTime? LastRunCompletedAtUtc,
    int ConsecutiveRunFailures,
    bool Stale);

public sealed record FinancialPostingSummaryModel(
    SaleFinancialPostingSummary Sales,
    int CashDifferencesDue,
    IReadOnlyList<FinancialPostingWorkerLivenessModel> Workers);

/// <summary>
/// D10 reconciliation surface: finance can see sale postings that are pending or failed, why, and retry one. The sale
/// itself is never affected by anything here. All reads and the retry are confined to the caller's Business.
/// </summary>
[ApiController]
[Route("api/financial-postings")]
[Authorize]
public sealed class FinancialPostingsController(
    ISender sender,
    IBusinessScope businessScope,
    ICashDifferenceStore cashDifferences,
    RetailPOS.Infrastructure.Sales.FinancialPostingHeartbeatStore heartbeats,
    TimeProvider clock) : ControllerBase
{
    /// <summary>A worker that has not completed a pass for this long is reported as stale.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    [HttpGet("sales")]
    [Authorize(Policy = "sales.financial_postings.view")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SaleFinancialPostingModel>>>> ListSalePostings(
        [FromQuery] string? status,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var rows = await sender.Send(new GetSaleFinancialPostingsQuery(status, take), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SaleFinancialPostingModel>>.SuccessResponse(rows.Select(ToModel).ToArray()));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "sales.financial_postings.view")]
    public async Task<ActionResult<ApiResponse<FinancialPostingSummaryModel>>> Summary(
        CancellationToken cancellationToken)
    {
        var sales = await sender.Send(new GetSaleFinancialPostingSummaryQuery(), cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var varianceDue = await cashDifferences.ListDueForPostingAsync(
            businessScope.RequireBusinessId(), null, now, 500, cancellationToken);
        var workers = (await heartbeats.ListAsync(now.AddDays(-1), cancellationToken))
            .Select(beat => new FinancialPostingWorkerLivenessModel(
                beat.WorkerName,
                beat.InstanceId,
                beat.HostName,
                beat.HostRole,
                beat.LastRunCompletedAtUtc,
                beat.ConsecutiveRunFailures,
                beat.LastRunCompletedAtUtc is null || now - beat.LastRunCompletedAtUtc.Value > StaleAfter))
            .ToArray();
        return Ok(ApiResponse<FinancialPostingSummaryModel>.SuccessResponse(
            new FinancialPostingSummaryModel(sales, varianceDue.Count, workers)));
    }

    [HttpPost("sales/{postingId:long}/retry")]
    [Authorize(Policy = "sales.financial_postings.retry")]
    public async Task<ActionResult<ApiResponse<SaleFinancialPostingRetryModel>>> RetrySalePosting(
        long postingId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RetrySaleFinancialPostingCommand(postingId), cancellationToken);
        return Ok(ApiResponse<SaleFinancialPostingRetryModel>.SuccessResponse(
            new SaleFinancialPostingRetryModel(result.Outcome.ToString(), ToModel(result.Posting))));
    }

    private static SaleFinancialPostingModel ToModel(SaleFinancialPosting row) =>
        new(
            row.Id,
            row.SaleId,
            row.Kind,
            row.ReversalType,
            row.SourceKey,
            row.Status,
            row.OutletId,
            DateOnly.FromDateTime(row.BusinessDate),
            row.Attempts,
            row.LastErrorCode,
            row.LastError,
            row.NextAttemptAtUtc,
            row.LastAttemptAtUtc,
            row.JournalEntryId,
            row.PostedAtUtc,
            row.CreatedAtUtc);
}
