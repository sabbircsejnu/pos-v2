using MediatR;

namespace RetailPOS.Application.HR.Organisation;

public sealed record CreateHrOrgUnitCommand(HrOrgUnitWriteData Data) : IRequest<HrOrgUnitReadModel>;

public sealed record UpdateHrOrgUnitCommand(
    long OrgUnitId,
    long ExpectedVersion,
    HrOrgUnitWriteData Data) : IRequest<HrOrgUnitReadModel?>;

public sealed record ReconcileOperationalLocationsCommand(int PageSize = 250)
    : IRequest<OperationalLocationReconciliationResult>;

public sealed class CreateHrOrgUnitCommandHandler(
    IHrOrganisationAccess access,
    IHrOrgUnitWriteStore store,
    RetailPOS.Application.AuditEvents.Abstractions.IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<CreateHrOrgUnitCommand, HrOrgUnitReadModel>
{
    public async Task<HrOrgUnitReadModel> Handle(
        CreateHrOrgUnitCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManageOrgUnits,
            request.Data.OperationalLocationProjectionId, cancellationToken);
        await EnsureParentLocationAccessAsync(businessId, request.Data.ParentOrgUnitId, access, store,
            cancellationToken);
        Validate(request.Data);
        var actorId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RetailPOS.Application.AuditEvents.Abstractions.RequiredAuditOperation(
                businessId, "create", "hr", "Create organisation unit", "HrOrgUnit", request.Data.Code),
            token => store.CreateAsync(businessId, actorId, request.Data, token),
            cancellationToken);
    }

    internal static void Validate(HrOrgUnitWriteData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.Code) || data.Code.Trim().Length > 50)
            throw new ArgumentException("Organisation unit code is required and must not exceed 50 characters.");
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 150)
            throw new ArgumentException("Organisation unit name is required and must not exceed 150 characters.");
        if (string.IsNullOrWhiteSpace(data.UnitType) || data.UnitType.Trim().Length > 40)
            throw new ArgumentException("Organisation unit type is required and must not exceed 40 characters.");
        if (data.EffectiveFrom.HasValue && data.EffectiveTo.HasValue
            && data.EffectiveTo.Value < data.EffectiveFrom.Value)
            throw new ArgumentException("Organisation unit end date cannot precede its effective start date.");
    }

    internal static async Task EnsureParentLocationAccessAsync(
        long businessId,
        long? parentOrgUnitId,
        IHrOrganisationAccess access,
        IHrOrgUnitWriteStore store,
        CancellationToken cancellationToken)
    {
        if (!parentOrgUnitId.HasValue)
            return;
        var parentLocationId = await store.GetLocationProjectionIdAsync(
            businessId, parentOrgUnitId.Value, cancellationToken);
        if (parentLocationId.HasValue)
            await access.EnsureAsync(businessId, HrOrganisationOperation.ManageOrgUnits,
                parentLocationId, cancellationToken);
    }
}

public sealed class UpdateHrOrgUnitCommandHandler(
    IHrOrganisationAccess access,
    IHrOrgUnitWriteStore store,
    RetailPOS.Application.AuditEvents.Abstractions.IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<UpdateHrOrgUnitCommand, HrOrgUnitReadModel?>
{
    public async Task<HrOrgUnitReadModel?> Handle(
        UpdateHrOrgUnitCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManageOrgUnits,
            request.Data.OperationalLocationProjectionId, cancellationToken);
        await CreateHrOrgUnitCommandHandler.EnsureParentLocationAccessAsync(
            businessId, request.Data.ParentOrgUnitId, access, store, cancellationToken);
        if (request.OrgUnitId <= 0 || request.ExpectedVersion <= 0)
            throw new ArgumentException("Organisation unit identity and expected version must be positive.");
        CreateHrOrgUnitCommandHandler.Validate(request.Data);
        var actorId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RetailPOS.Application.AuditEvents.Abstractions.RequiredAuditOperation(
                businessId, "update", "hr", "Update organisation unit", "HrOrgUnit", request.OrgUnitId.ToString()),
            token => store.UpdateAsync(businessId, actorId, request.OrgUnitId, request.ExpectedVersion,
                request.Data, token),
            result => result is not null,
            cancellationToken);
    }
}

public sealed class ReconcileOperationalLocationsCommandHandler(
    IHrOrganisationAccess access,
    IOperationalLocationSourceReadStore sourceStore,
    IOperationalLocationProjectionWriteStore projectionStore,
    RetailPOS.Application.AuditEvents.Abstractions.IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<ReconcileOperationalLocationsCommand, OperationalLocationReconciliationResult>
{
    public async Task<OperationalLocationReconciliationResult> Handle(
        ReconcileOperationalLocationsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PageSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize), "Page size must be between 1 and 1000.");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ReconcileOperationalLocations, null,
            cancellationToken);
        return await auditTransaction.ExecuteAsync(
            new RetailPOS.Application.AuditEvents.Abstractions.RequiredAuditOperation(
                businessId, "reconcile", "hr", "Reconcile operational location projections",
                "OperationalLocationProjection", businessId.ToString()),
            async token =>
            {
                var outletRead = await SyncTypeAsync("Outlet", request.PageSize, businessId, token);
                var warehouseRead = await SyncTypeAsync("Warehouse", request.PageSize, businessId, token);
                return new OperationalLocationReconciliationResult(
                    businessId,
                    outletRead.Read,
                    warehouseRead.Read,
                    outletRead.Changed + warehouseRead.Changed,
                    outletRead.Stale + warehouseRead.Stale);
            },
            cancellationToken);
    }

    private async Task<(int Read, int Changed, int Stale)> SyncTypeAsync(
        string sourceType,
        int pageSize,
        long businessId,
        CancellationToken cancellationToken)
    {
        long afterId = 0;
        var read = 0;
        var changed = 0;
        var stale = 0;
        while (true)
        {
            var page = await sourceStore.GetPageAsync(businessId, sourceType, afterId, pageSize, cancellationToken);
            foreach (var item in page)
            {
                var result = await projectionStore.UpsertFromSourceAsync(item, cancellationToken);
                if (result is ProjectionUpsertResult.Inserted or ProjectionUpsertResult.Updated)
                    changed++;
                else if (result == ProjectionUpsertResult.Stale)
                    stale++;
                read++;
                afterId = long.Parse(item.SourceEntityId, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (page.Count < pageSize)
                return (read, changed, stale);
        }
    }
}
