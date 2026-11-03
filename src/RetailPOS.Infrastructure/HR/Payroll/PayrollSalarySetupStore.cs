using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed partial class PayrollSalarySetupStore(RetailPOSDbContext db, TimeProvider? clock = null)
    : IPayrollSalarySetupStore, IApprovalKernelWorkflow, IApprovalTargetSnapshotReader
{
    public async Task<IReadOnlyList<PayrollSalaryComponentResponse>> ComponentsAsync(
        long businessId, string? search, CancellationToken cancellationToken)
    {
        var query = db.PayrollSalaryComponents.AsNoTracking()
            .Where(component => component.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(component =>
                component.Code.ToLower().Contains(term) || component.Name.ToLower().Contains(term));
        }
        var rows = await query.OrderBy(component => component.Code).ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public async Task<PayrollSalaryComponentResponse> ComponentAsync(
        long businessId, long id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollSalaryComponents.AsNoTracking()
            .SingleOrDefaultAsync(component => component.BusinessId == businessId && component.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Salary component not found.");
        return Map(row);
    }

    public async Task<PayrollSalaryComponentResponse> SaveComponentAsync(
        long businessId,
        long? id,
        long? expectedVersion,
        PayrollSalaryComponentData? data,
        CancellationToken cancellationToken)
    {
        await LockBusinessAsync(businessId, cancellationToken);
        var row = id.HasValue
            ? await db.PayrollSalaryComponents.SingleOrDefaultAsync(
                component => component.BusinessId == businessId && component.Id == id.Value,
                cancellationToken) ?? throw new KeyNotFoundException("Salary component not found.")
            : new PayrollSalaryComponent { BusinessId = businessId };
        if (id.HasValue)
        {
            EnsureVersion(row.Version, expectedVersion);
            if (!row.IsActive)
                throw new PayrollSalaryConflictException("Inactive salary components cannot be changed.");
        }

        if (data is null)
        {
            if (await db.PayrollSalaryStructureLines.AnyAsync(line =>
                    line.BusinessId == businessId && line.SalaryComponentId == row.Id
                    && db.PayrollSalaryStructures.Any(structure =>
                        structure.BusinessId == businessId && structure.Id == line.StructureId && structure.IsActive),
                    cancellationToken))
                throw new PayrollSalaryConflictException(
                    "A salary component used by an active structure cannot be deactivated.");
            row.IsActive = false;
        }
        else
        {
            var code = NormalizeCode(data.Code);
            if (id.HasValue && !string.Equals(row.Code, code, StringComparison.Ordinal))
                throw new PayrollSalaryConflictException("A salary component code is immutable after creation.");
            if (await db.PayrollSalaryComponents.AnyAsync(component =>
                    component.BusinessId == businessId && component.Code == code && component.Id != row.Id,
                    cancellationToken))
                throw new PayrollSalaryConflictException("Salary component code already exists in this Business.");

            row.Code = code;
            row.Name = data.Name.Trim();
            row.ComponentType = data.ComponentType;
            row.ValueType = data.ValueType;
            row.TaxTreatmentCode = string.IsNullOrWhiteSpace(data.TaxTreatmentCode)
                ? null
                : data.TaxTreatmentCode.Trim();
            row.IsRecurring = data.IsRecurring;
            row.CalculationOrder = data.CalculationOrder;
            row.FormulaExpression = data.FormulaExpression?.Trim();
        }

        if (id.HasValue)
            row.Version = checked(row.Version + 1);
        else
            db.PayrollSalaryComponents.Add(row);

        await ValidateComponentSetAsync(businessId, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<IReadOnlyList<PayrollSalaryStructureResponse>> StructuresAsync(
        long businessId, string? search, CancellationToken cancellationToken)
    {
        var query = db.PayrollSalaryStructures.AsNoTracking()
            .Include(structure => structure.Lines)
            .Where(structure => structure.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(structure =>
                structure.Code.ToLower().Contains(term) || structure.Name.ToLower().Contains(term));
        }
        var rows = await query.OrderBy(structure => structure.Code).ToListAsync(cancellationToken);
        return await MapStructuresAsync(businessId, rows, cancellationToken);
    }

    public async Task<PayrollSalaryStructureResponse> StructureAsync(
        long businessId, long id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollSalaryStructures.AsNoTracking()
            .Include(structure => structure.Lines)
            .SingleOrDefaultAsync(structure => structure.BusinessId == businessId && structure.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Salary structure not found.");
        return (await MapStructuresAsync(businessId, [row], cancellationToken))[0];
    }

    public async Task<PayrollSalaryStructureResponse> SaveStructureAsync(
        long businessId,
        long? id,
        long? expectedVersion,
        PayrollSalaryStructureData? data,
        CancellationToken cancellationToken)
    {
        await LockBusinessAsync(businessId, cancellationToken);
        var row = id.HasValue
            ? await db.PayrollSalaryStructures.Include(structure => structure.Lines)
                .SingleOrDefaultAsync(structure =>
                    structure.BusinessId == businessId && structure.Id == id.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Salary structure not found.")
            : new PayrollSalaryStructure { BusinessId = businessId };
        if (id.HasValue)
        {
            EnsureVersion(row.Version, expectedVersion);
            if (!row.IsActive)
                throw new PayrollSalaryConflictException("Inactive salary structures cannot be changed.");
        }

        if (data is null)
        {
            row.IsActive = false;
        }
        else
        {
            var code = NormalizeCode(data.Code);
            if (id.HasValue && !string.Equals(row.Code, code, StringComparison.Ordinal))
                throw new PayrollSalaryConflictException("A salary structure code is immutable after creation.");
            if (await db.PayrollSalaryStructures.AnyAsync(structure =>
                    structure.BusinessId == businessId && structure.Code == code && structure.Id != row.Id,
                    cancellationToken))
                throw new PayrollSalaryConflictException("Salary structure code already exists in this Business.");

            var componentIds = data.Lines.Select(line => line.SalaryComponentId).ToList();
            var components = await db.PayrollSalaryComponents
                .Where(component => component.BusinessId == businessId
                    && component.IsActive && componentIds.Contains(component.Id))
                .ToListAsync(cancellationToken);
            if (components.Count != componentIds.Count)
                throw new ArgumentException(
                    "Salary structures may include only active components from the same Business.");
            PayrollSalaryFormulaRules.ValidateComponentGraph(
                await db.PayrollSalaryComponents.Where(component => component.BusinessId == businessId)
                    .ToListAsync(cancellationToken));
            var newLines = data.Lines.Select(line => new PayrollSalaryStructureLine
            {
                BusinessId = businessId,
                SalaryComponentId = line.SalaryComponentId,
                DefaultValue = line.DefaultValue,
                FormulaOverride = line.FormulaOverride?.Trim(),
                CalculationOrder = line.CalculationOrder
            }).ToArray();
            PayrollSalaryFormulaRules.ValidateStructureFormulas(newLines, components);

            row.Code = code;
            row.Name = data.Name.Trim();
            row.IsActive = true;
            if (id.HasValue)
            {
                db.PayrollSalaryStructureLines.RemoveRange(row.Lines);
                row.Lines.Clear();
                await SaveAsync(cancellationToken);
            }
            row.Lines = newLines.ToList();
        }

        if (id.HasValue)
            row.Version = checked(row.Version + 1);
        else
            db.PayrollSalaryStructures.Add(row);

        await SaveAsync(cancellationToken);
        return (await MapStructuresAsync(businessId, [row], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<PayrollSalaryStructureResponse>> MapStructuresAsync(
        long businessId, IReadOnlyList<PayrollSalaryStructure> rows, CancellationToken cancellationToken)
    {
        var ids = rows.SelectMany(structure => structure.Lines)
            .Select(line => line.SalaryComponentId).Distinct().ToList();
        var components = await db.PayrollSalaryComponents.AsNoTracking()
            .Where(component => component.BusinessId == businessId && ids.Contains(component.Id))
            .ToDictionaryAsync(component => component.Id, cancellationToken);
        return rows.Select(structure => new PayrollSalaryStructureResponse(
            structure.Id,
            structure.BusinessId,
            new PayrollSalaryStructureData(
                structure.Code,
                structure.Name,
                structure.Lines.OrderBy(line => line.CalculationOrder).ThenBy(line => line.Id)
                    .Select(line =>
                    {
                        if (!components.TryGetValue(line.SalaryComponentId, out var component))
                            throw new InvalidOperationException("Salary structure component ownership is inconsistent.");
                        return new PayrollSalaryStructureLineData(
                            component.Id, line.DefaultValue, line.FormulaOverride, line.CalculationOrder);
                    }).ToArray()),
            structure.IsActive,
            structure.Version)).ToArray();
    }

    private async Task ValidateComponentSetAsync(long businessId, CancellationToken cancellationToken)
    {
        var components = await db.PayrollSalaryComponents
            .Where(component => component.BusinessId == businessId)
            .ToListAsync(cancellationToken);
        foreach (var tracked in db.PayrollSalaryComponents.Local.Where(component =>
                     component.BusinessId == businessId
                     && db.Entry(component).State != EntityState.Deleted))
        {
            if (!components.Contains(tracked))
                components.Add(tracked);
        }
        PayrollSalaryFormulaRules.ValidateComponentGraph(components);
        var structures = await db.PayrollSalaryStructures.Include(structure => structure.Lines)
            .Where(structure => structure.BusinessId == businessId && structure.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var structure in structures)
        {
            var usedIds = structure.Lines.Select(line => line.SalaryComponentId).ToArray();
            var usedComponents = components.Where(component => usedIds.Contains(component.Id)).ToArray();
            PayrollSalaryFormulaRules.ValidateStructureFormulas(structure.Lines, usedComponents);
        }
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll salary setup writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 4)})", cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollSalaryConflictException(
                "Salary setup was changed by another request.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollSalaryConflictException(
                "A salary setup identity already exists in this Business.", exception);
        }
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static void EnsureVersion(long actual, long? expected)
    {
        if (actual != expected)
            throw new PayrollSalaryConflictException("Salary setup changed since it was read.");
    }

    private static PayrollSalaryComponentResponse Map(PayrollSalaryComponent row) =>
        new(row.Id, row.BusinessId,
            new(row.Code, row.Name, row.ComponentType, row.ValueType, row.TaxTreatmentCode,
                row.IsRecurring, row.CalculationOrder, row.FormulaExpression),
            row.IsActive, row.Version);
}
