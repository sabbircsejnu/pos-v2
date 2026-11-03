using MediatR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Attendance;

/// <summary>Approval workflow result for an attendance correction submission or replay.</summary>
public sealed record HrAttendanceCorrectionSubmissionResult(
    Guid CorrectionId,
    long ApprovalInstanceId,
    ApprovalInstanceStatus Status,
    int? CurrentStepSequence,
    bool IsReplay);

/// <summary>Retained before, proposed, and applied evidence for an attendance correction.</summary>
public sealed record HrAttendanceCorrectionResponse(
    Guid CorrectionId,
    long DailyAttendanceId,
    long EmployeeId,
    DateOnly WorkDate,
    long OperationalLocationProjectionId,
    int CorrectionSequence,
    int ExpectedProcessingVersion,
    string Status,
    string Reason,
    string BeforeEvidenceJson,
    string ProposedEvidenceJson,
    string? AppliedAfterEvidenceJson,
    long RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    long? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    long ApprovalInstanceId,
    int Version);

public sealed record RequestHrAttendanceCorrectionCommand(
    Guid CorrectionId,
    long DailyAttendanceId,
    int ExpectedProcessingVersion,
    HrAttendanceCorrectionValues Values,
    string Reason) : IRequest<HrAttendanceCorrectionSubmissionResult>;

public sealed record GetHrAttendanceCorrectionsQuery(long DailyAttendanceId)
    : IRequest<IReadOnlyList<HrAttendanceCorrectionResponse>>;

public sealed record HrAttendanceCorrectionExistingSubmission(
    Guid CorrectionId,
    string RequestHash,
    long ApprovalInstanceId,
    ApprovalInstanceStatus Status,
    int? CurrentStepSequence);

public interface IHrAttendanceCorrectionStore
{
    Task<long> GetLocationIdAsync(long businessId, long dailyAttendanceId, CancellationToken ct);
    Task<HrAttendanceCorrectionExistingSubmission?> FindSubmissionAsync(
        long businessId, Guid correctionId, CancellationToken ct);
    Task<IReadOnlyList<HrAttendanceCorrectionResponse>> ListAsync(
        long businessId, long dailyAttendanceId, CancellationToken ct);
}

public sealed class HrAttendanceCorrectionHandlers(
    IHrOrganisationAccess access,
    IHrAttendanceCorrectionStore store,
    ISender sender)
    : IRequestHandler<RequestHrAttendanceCorrectionCommand, HrAttendanceCorrectionSubmissionResult>,
      IRequestHandler<GetHrAttendanceCorrectionsQuery, IReadOnlyList<HrAttendanceCorrectionResponse>>
{
    public async Task<HrAttendanceCorrectionSubmissionResult> Handle(
        RequestHrAttendanceCorrectionCommand request, CancellationToken ct)
    {
        HrAttendanceCorrectionRules.ValidateShape(request.CorrectionId, request.DailyAttendanceId,
            request.ExpectedProcessingVersion, request.Values, request.Reason);
        var businessId = access.RequireBusinessId();
        var locationId = await store.GetLocationIdAsync(businessId, request.DailyAttendanceId, ct);
        await access.EnsureAsync(businessId, HrOrganisationOperation.CorrectAttendance, locationId, ct);
        access.RequireRealUserId();
        var payload = new HrAttendanceCorrectionTargetPayload(request.CorrectionId, request.DailyAttendanceId,
            request.ExpectedProcessingVersion, request.Values, request.Reason.Trim());
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payloadJson)));

        var existing = await store.FindSubmissionAsync(businessId, request.CorrectionId, ct);
        if (existing is not null)
            return ReplayOrConflict(existing, requestHash);

        try
        {
            var approval = await sender.Send(new SubmitApprovalCommand(
                ApprovalProcessType.AttendanceCorrection,
                HrAttendanceCorrectionApprovalConstants.TargetType,
                request.CorrectionId.ToString("D"),
                ApprovalScopeType.Location,
                locationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.ExpectedProcessingVersion,
                requestHash,
                payloadJson), ct);
            return new(request.CorrectionId, approval.InstanceId, approval.Status,
                approval.CurrentStepSequence, approval.IsReplay);
        }
        catch (InvalidOperationException)
        {
            var concurrent = await store.FindSubmissionAsync(businessId, request.CorrectionId, ct);
            if (concurrent is null) throw;
            return ReplayOrConflict(concurrent, requestHash);
        }
    }

    public async Task<IReadOnlyList<HrAttendanceCorrectionResponse>> Handle(
        GetHrAttendanceCorrectionsQuery request, CancellationToken ct)
    {
        if (request.DailyAttendanceId <= 0)
            throw new ArgumentException("A valid daily attendance ID is required.");
        var businessId = access.RequireBusinessId();
        var locationId = await store.GetLocationIdAsync(businessId, request.DailyAttendanceId, ct);
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewAttendance, locationId, ct);
        return await store.ListAsync(businessId, request.DailyAttendanceId, ct);
    }

    private static HrAttendanceCorrectionSubmissionResult ReplayOrConflict(
        HrAttendanceCorrectionExistingSubmission existing, string requestHash)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Correction ID was already used with different correction data.");
        return new(existing.CorrectionId, existing.ApprovalInstanceId, existing.Status,
            existing.CurrentStepSequence, true);
    }
}

public static class HrAttendanceCorrectionApprovalConstants
{
    public const string TargetType = "HrAttendanceCorrection";
}
