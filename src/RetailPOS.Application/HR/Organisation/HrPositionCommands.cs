using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Application.HR.Organisation;

public sealed record CreateHrPositionCommand(HrPositionWriteData Data) : IRequest<HrPositionReadModel>;

public sealed record UpdateHrPositionCommand(
    long PositionId,
    long ExpectedVersion,
    HrPositionWriteData Data) : IRequest<HrPositionReadModel?>;

public sealed record DeactivateHrPositionCommand(
    long PositionId,
    long ExpectedVersion,
    DateOnly EffectiveTo) : IRequest<HrPositionReadModel?>;

public sealed class CreateHrPositionCommandHandler(
    IHrOrganisationAccess access,
    IHrOrgUnitWriteStore orgUnits,
    IHrPositionStore positions,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<CreateHrPositionCommand, HrPositionReadModel>
{
    public async Task<HrPositionReadModel> Handle(
        CreateHrPositionCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request.Data);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
            null, cancellationToken);
        await EnsureReferenceScopesAsync(businessId, request.Data, access, orgUnits, positions,
            cancellationToken);
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "create", "hr", "Create position", "HrPosition",
                request.Data.Code.Trim()),
            token => positions.CreateAsync(businessId, request.Data, token),
            cancellationToken);
    }

    internal static void Validate(HrPositionWriteData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.Code) || data.Code.Trim().Length > 50)
            throw new ArgumentException("Position code is required and must not exceed 50 characters.");
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 150)
            throw new ArgumentException("Position name is required and must not exceed 150 characters.");
        if (data.OrgUnitId <= 0)
            throw new ArgumentException("A valid Department / Team is required.");
        if (data.ReportsToPositionId is <= 0)
            throw new ArgumentException("Position reference IDs must be positive.");
        if (!Enum.TryParse<HrPositionStatus>(data.Status, ignoreCase: true, out var status)
            || !Enum.IsDefined(status) || status == HrPositionStatus.Inactive)
            throw new ArgumentException("Position status is invalid; use the deactivate operation to make a position inactive.");
    }

    internal static async Task EnsureReferenceScopesAsync(
        long businessId,
        HrPositionWriteData data,
        IHrOrganisationAccess access,
        IHrOrgUnitWriteStore orgUnits,
        IHrPositionStore positions,
        CancellationToken cancellationToken)
    {
        var orgLocationId = await orgUnits.GetLocationProjectionIdAsync(
            businessId, data.OrgUnitId, cancellationToken);
        if (orgLocationId.HasValue)
            await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
                orgLocationId, cancellationToken);

        if (data.ReportsToPositionId.HasValue)
        {
            var reportsToLocationId = await positions.GetPositionLocationProjectionIdAsync(
                businessId, data.ReportsToPositionId.Value, cancellationToken);
            if (reportsToLocationId.HasValue)
                await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
                    reportsToLocationId, cancellationToken);
        }
    }
}

public sealed class UpdateHrPositionCommandHandler(
    IHrOrganisationAccess access,
    IHrOrgUnitWriteStore orgUnits,
    IHrPositionStore positions,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<UpdateHrPositionCommand, HrPositionReadModel?>
{
    public async Task<HrPositionReadModel?> Handle(
        UpdateHrPositionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PositionId <= 0 || request.ExpectedVersion <= 0)
            throw new ArgumentException("Position identity and expected version must be positive.");
        CreateHrPositionCommandHandler.Validate(request.Data);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
            null, cancellationToken);
        var currentLocationId = await positions.GetPositionLocationProjectionIdAsync(
            businessId, request.PositionId, cancellationToken);
        if (currentLocationId.HasValue)
            await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
                currentLocationId, cancellationToken);
        await CreateHrPositionCommandHandler.EnsureReferenceScopesAsync(
            businessId, request.Data, access, orgUnits, positions, cancellationToken);
        access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "update", "hr", "Update position", "HrPosition",
                request.PositionId.ToString()),
            token => positions.UpdateAsync(businessId, request.PositionId, request.ExpectedVersion,
                request.Data, token),
            result => result is not null,
            cancellationToken);
    }
}

public sealed class DeactivateHrPositionCommandHandler(
    IHrOrganisationAccess access,
    IHrPositionStore positions,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<DeactivateHrPositionCommand, HrPositionReadModel?>
{
    public async Task<HrPositionReadModel?> Handle(
        DeactivateHrPositionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PositionId <= 0 || request.ExpectedVersion <= 0)
            throw new ArgumentException("Position identity and expected version must be positive.");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions, null,
            cancellationToken);
        var locationId = await positions.GetPositionLocationProjectionIdAsync(
            businessId, request.PositionId, cancellationToken);
        if (locationId.HasValue)
            await access.EnsureAsync(businessId, HrOrganisationOperation.ManagePositions,
                locationId, cancellationToken);
        access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "deactivate", "hr", "Deactivate position",
                "HrPosition", request.PositionId.ToString()),
            token => positions.DeactivateAsync(businessId, request.PositionId,
                request.ExpectedVersion, request.EffectiveTo, token),
            result => result is not null,
            cancellationToken);
    }
}
