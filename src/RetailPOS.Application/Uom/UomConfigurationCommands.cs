using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

public sealed record GetUomCatalogQuery(bool ActiveOnly = false, string? Search = null) : IQuery<UomCatalogReadModel>;

public sealed class GetUomCatalogQueryHandler(IUomCatalogStore store, IUomOperationScope scope)
    : IRequestHandler<GetUomCatalogQuery, UomCatalogReadModel>
{
    public Task<UomCatalogReadModel> Handle(GetUomCatalogQuery request, CancellationToken cancellationToken) =>
        store.GetCatalogAsync(scope.RequireBusinessId(), request.ActiveOnly, request.Search, cancellationToken);
}

public sealed record CreateUomUnitCommand(UomUnitInput Input) : ICommand<UomUnitReadModel>;

public sealed class CreateUomUnitCommandHandler(IUomCatalogStore store, IUomOperationScope scope, IUomAuditWriter audit)
    : IRequestHandler<CreateUomUnitCommand, UomUnitReadModel>
{
    public async Task<UomUnitReadModel> Handle(CreateUomUnitCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var input = UomUnitRules.Normalize(request.Input);
        await UomUnitRules.EnsureUniqueAsync(store, businessId, input, null, cancellationToken);

        var unit = new UomUnit
        {
            BusinessId = businessId,
            Code = input.Code,
            Name = input.Name,
            Symbol = input.Symbol,
            Dimension = input.Dimension,
            QuantityPrecision = input.QuantityPrecision,
            IsWholeUnit = input.IsWholeUnit
        };
        await using var transaction = await store.BeginAsync(cancellationToken);
        await store.AddAsync(unit, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync($"Unit of measurement created: {unit.Code}", "UomUnit", unit.Id.ToString(),
            new Dictionary<string, object?> { ["code"] = unit.Code, ["dimension"] = unit.Dimension, ["precision"] = unit.QuantityPrecision },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UomUnitRules.ToReadModel(unit, false);
    }
}

public sealed record UpdateUomUnitCommand(long UnitId, UomUnitInput Input, bool IsActive) : ICommand<UomUnitReadModel>;

public sealed class UpdateUomUnitCommandHandler(IUomCatalogStore store, IUomOperationScope scope, IUomAuditWriter audit)
    : IRequestHandler<UpdateUomUnitCommand, UomUnitReadModel>
{
    public async Task<UomUnitReadModel> Handle(UpdateUomUnitCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        // Seeded units are shared by every business, so they are read-only here. Another business's unit reads as missing.
        var unit = await store.FindBusinessUnitAsync(businessId, request.UnitId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unit {request.UnitId} not found");
        var input = UomUnitRules.Normalize(request.Input);
        await UomUnitRules.EnsureUniqueAsync(store, businessId, input, unit.Id, cancellationToken);

        var used = await store.IsUnitUsedAsync(unit.Id, cancellationToken);
        var incompatible = !string.Equals(unit.Code, input.Code, StringComparison.Ordinal)
            || !string.Equals(unit.Dimension, input.Dimension, StringComparison.Ordinal)
            || unit.QuantityPrecision > input.QuantityPrecision
            || (!unit.IsWholeUnit && input.IsWholeUnit);
        if (used && incompatible)
            throw new UomConflictException(
                UomErrorCodes.UnitInUse,
                "This unit is already in use. Its code and dimension cannot change and its precision cannot be reduced. Deactivate it instead.");

        var previousActive = unit.IsActive;
        unit.Code = input.Code;
        unit.Name = input.Name;
        unit.Symbol = input.Symbol;
        unit.Dimension = input.Dimension;
        unit.QuantityPrecision = input.QuantityPrecision;
        unit.IsWholeUnit = input.IsWholeUnit;
        unit.IsActive = request.IsActive;
        unit.UpdatedAt = DateTime.UtcNow;
        await using var transaction = await store.BeginAsync(cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync($"Unit of measurement updated: {unit.Code}", "UomUnit", unit.Id.ToString(),
            new Dictionary<string, object?> { ["code"] = unit.Code, ["wasActive"] = previousActive, ["isActive"] = unit.IsActive, ["precision"] = unit.QuantityPrecision },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UomUnitRules.ToReadModel(unit, used);
    }
}

internal static class UomUnitRules
{
    public static UomUnitInput Normalize(UomUnitInput input)
    {
        var code = (input.Code ?? string.Empty).Trim();
        var name = (input.Name ?? string.Empty).Trim();
        var symbol = (input.Symbol ?? string.Empty).Trim();
        var dimension = (input.Dimension ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length is 0 or > 20 || name.Length is 0 or > 100 || symbol.Length is 0 or > 20)
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "A unit needs a code and symbol of up to 20 characters and a name of up to 100.");
        if (!UomDimensions.All.Contains(dimension))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, $"'{input.Dimension}' is not a known dimension.");
        if (input.QuantityPrecision is < 0 or > UomUnit.MaxPrecision)
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, $"Quantity precision must be between 0 and {UomUnit.MaxPrecision}.");
        if (input.IsWholeUnit && input.QuantityPrecision != 0)
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "A whole-number unit must have a precision of 0.");
        return new UomUnitInput(code, name, symbol, dimension, input.QuantityPrecision, input.IsWholeUnit);
    }

    public static async Task EnsureUniqueAsync(IUomCatalogStore store, long businessId, UomUnitInput input, long? excluding, CancellationToken cancellationToken)
    {
        if (await store.CodeExistsAsync(businessId, input.Code, excluding, cancellationToken))
            throw new UomConflictException(UomErrorCodes.DuplicateUnit, $"A unit with code '{input.Code}' already exists.");
        if (await store.SymbolExistsAsync(businessId, input.Symbol, excluding, cancellationToken))
            throw new UomConflictException(UomErrorCodes.DuplicateUnit, $"A unit with symbol '{input.Symbol}' already exists.");
    }

    public static UomUnitReadModel ToReadModel(UomUnit unit, bool inUse) => new(
        unit.Id, unit.BusinessId is null, unit.Code, unit.Name, unit.Symbol, unit.Dimension,
        unit.QuantityPrecision, unit.IsWholeUnit, unit.IsActive, inUse);
}

public sealed record GetVariantUnitConfigQuery(long VariantId) : IQuery<VariantUnitConfigReadModel>;

public sealed class GetVariantUnitConfigQueryHandler(IVariantUomReader reader, IVariantUomConfigStore store, IUomOperationScope scope)
    : IRequestHandler<GetVariantUnitConfigQuery, VariantUnitConfigReadModel>
{
    public async Task<VariantUnitConfigReadModel> Handle(GetVariantUnitConfigQuery request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        if (await store.FindVariantAsync(businessId, request.VariantId, cancellationToken) is null)
            throw new KeyNotFoundException($"Variant {request.VariantId} not found");
        var current = await reader.GetCurrentAsync(businessId, [request.VariantId], cancellationToken);
        return new VariantUnitConfigReadModel(
            request.VariantId,
            await store.HasStockActivityAsync(request.VariantId, cancellationToken),
            current.GetValueOrDefault(request.VariantId),
            await store.GetHistoryAsync(businessId, request.VariantId, cancellationToken));
    }
}

/// <summary>Reads current configurations in bulk, for screens that list many variants.</summary>
public sealed record GetVariantUnitConfigsQuery(IReadOnlyList<long> VariantIds) : IQuery<IReadOnlyList<ResolvedVariantUom>>;

public sealed class GetVariantUnitConfigsQueryHandler(IVariantUomReader reader, IUomOperationScope scope)
    : IRequestHandler<GetVariantUnitConfigsQuery, IReadOnlyList<ResolvedVariantUom>>
{
    public async Task<IReadOnlyList<ResolvedVariantUom>> Handle(GetVariantUnitConfigsQuery request, CancellationToken cancellationToken)
    {
        var current = await reader.GetCurrentAsync(scope.RequireBusinessId(), request.VariantIds.Distinct().Take(500).ToList(), cancellationToken);
        return current.Values.ToList();
    }
}

public sealed record UpdateVariantUnitConfigCommand(long VariantId, int? ExpectedVersion, VariantUomConfigInput Input) : ICommand<VariantUnitConfigReadModel>;

public sealed class UpdateVariantUnitConfigCommandHandler(
    IVariantUomConfigStore store,
    IUomCatalogStore catalog,
    IVariantUomReader reader,
    IPackStateLedger packState,
    IUomOperationScope scope,
    IUomAuditWriter audit) : IRequestHandler<UpdateVariantUnitConfigCommand, VariantUnitConfigReadModel>
{
    public async Task<VariantUnitConfigReadModel> Handle(UpdateVariantUnitConfigCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        if (await store.FindVariantAsync(businessId, request.VariantId, cancellationToken) is null)
            throw new KeyNotFoundException($"Variant {request.VariantId} not found");

        var input = request.Input;
        var unitIds = input.PurchaseOptions.Select(option => option.UnitId)
            .Concat(input.SellingOptions.Where(option => option.UnitId.HasValue).Select(option => option.UnitId!.Value))
            .Append(input.BaseUnitId)
            .Distinct()
            .ToList();
        var units = (await catalog.GetUnitsAsync(businessId, unitIds, cancellationToken)).ToDictionary(unit => unit.Id);
        var conversions = await catalog.GetStandardConversionsAsync(cancellationToken);
        var plan = VariantUomConfigValidator.Validate(input, units, conversions);

        var componentIds = input.SellingOptions.SelectMany(option => option.Components).Select(component => component.ComponentVariantId).Distinct().ToList();
        if (componentIds.Contains(request.VariantId))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "An item cannot be its own packaging.");
        var known = await store.FindBusinessVariantIdsAsync(businessId, componentIds, cancellationToken);
        var missing = componentIds.Except(known).ToList();
        if (missing.Count > 0)
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, $"Packaging item {string.Join(", ", missing)} was not found.");
        var componentConfigs = await reader.GetCurrentAsync(businessId, componentIds, cancellationToken);
        if (componentConfigs.Values.Any(config => config.SellingOptions.Any(option => option.Components.Count > 0)))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "A packaging item cannot itself consume packaging.");
        // Packaging leaves stock as whole items, so it has no sealed or loose state to keep in step.
        if (componentConfigs.Values.Any(config => config.PackStateTrackingEnabled))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "A packaging item cannot track sealed and loose stock.");
        if (input.PackStateTrackingEnabled && await store.IsUsedAsPackagingAsync(request.VariantId, cancellationToken))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "This item is used as packaging, so it cannot track sealed and loose stock.");
        foreach (var component in input.SellingOptions.SelectMany(option => option.Components))
            UomLineResolver.EnsureBaseQuantityAllowed(
                componentConfigs.GetValueOrDefault(component.ComponentVariantId),
                component.Quantity,
                "Packaging quantity");

        await using var transaction = await store.BeginAsync(request.VariantId, cancellationToken);
        try
        {
            var current = await store.FindCurrentForUpdateAsync(businessId, request.VariantId, cancellationToken);
            if (request.ExpectedVersion is not null && request.ExpectedVersion != (current?.Version ?? 0))
                throw new UomConflictException(UomErrorCodes.ConfigChanged, "The unit configuration was changed by someone else. Reload and review it.");

            var baseUnit = units[input.BaseUnitId];
            var currentBaseIsPcs = current is null;
            var baseChanges = current is not null
                ? current.BaseUnitId != input.BaseUnitId
                : !string.Equals(baseUnit.Code, UomUnit.PcsCode, StringComparison.OrdinalIgnoreCase);
            if (baseChanges && await store.HasStockActivityAsync(request.VariantId, cancellationToken))
                throw new UomConflictException(
                    UomErrorCodes.BaseUnitLocked,
                    currentBaseIsPcs
                        ? "This item already has stock or transactions in pcs, so its base unit cannot change."
                        : "This item already has stock or transactions, so its base unit cannot change.");

            var wasTracking = current?.PackStateTrackingEnabled ?? false;
            if (wasTracking && !input.PackStateTrackingEnabled
                && await store.HasPhysicalPackBalancesAsync(request.VariantId, cancellationToken))
                throw new UomConflictException(
                    UomErrorCodes.PackStateMismatch,
                    "Sealed or prepared packs are still held. Open or sell them before turning off pack tracking.");

            var now = DateTime.UtcNow;
            if (current is not null)
                current.EffectiveToUtc = now;
            await store.SaveChangesAsync(cancellationToken);

            var next = new VariantUomConfig
            {
                BusinessId = businessId,
                VariantId = request.VariantId,
                BaseUnitId = input.BaseUnitId,
                Version = (current?.Version ?? 0) + 1,
                PackStateTrackingEnabled = input.PackStateTrackingEnabled,
                RepackingEnabled = input.RepackingEnabled,
                EffectiveFromUtc = now,
                CreatedBy = actor,
                CreatedAt = now,
                PurchaseOptions = plan.Purchase.Select(option => new VariantPurchaseUnitOption
                {
                    Code = option.Code,
                    UnitId = option.UnitId,
                    Label = option.Label,
                    FactorNumerator = option.FactorNumerator,
                    FactorDenominator = option.FactorDenominator,
                    IsDefault = option.IsDefault,
                    IsSealedPack = option.IsSealedPack
                }).ToList(),
                SellingOptions = plan.Selling.Select(option => new VariantSellingOption
                {
                    Kind = option.Kind,
                    Code = option.Code,
                    Label = option.Label,
                    UnitId = option.UnitId,
                    BaseQuantity = option.BaseQuantity,
                    FactorNumerator = option.FactorNumerator,
                    FactorDenominator = option.FactorDenominator,
                    Price = option.Price,
                    IsDefault = option.IsDefault,
                    SealedPackCode = option.SealedPackCode,
                    IsPreparedEligible = option.IsPreparedEligible,
                    Components = option.Components.Select(component => new SellingOptionComponent
                    {
                        ComponentVariantId = component.ComponentVariantId,
                        Quantity = component.Quantity,
                        IsRequired = component.IsRequired
                    }).ToList()
                }).ToList()
            };
            await store.AddAsync(next, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);

            // Turning tracking on or off classifies existing stock as loose, so the allocations always sum to stock.
            if (input.PackStateTrackingEnabled != wasTracking)
            {
                foreach (var location in await store.GetStockedLocationsAsync(request.VariantId, cancellationToken))
                {
                    var balances = await packState.GetBalancesForUpdateAsync(businessId, request.VariantId, location.LocationId, location.LocationType, cancellationToken);
                    var loose = balances.Where(row => row.State == PackStates.Loose).Sum(row => row.BaseQuantity);
                    var delta = input.PackStateTrackingEnabled ? location.Quantity - loose : -loose;
                    if (delta != 0m)
                        await packState.ApplyAsync(
                            new PackStateChange(businessId, request.VariantId, location.LocationId, location.LocationType,
                                PackStates.Loose, string.Empty, 0m, 0, delta, 0m,
                                PackStateOperations.Initialize, "variant_uom_config", next.Id, actor),
                            cancellationToken);
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync($"Unit configuration version {next.Version} saved for variant {request.VariantId}", "VariantUomConfig", next.Id.ToString(),
                new Dictionary<string, object?>
                {
                    ["variantId"] = request.VariantId,
                    ["version"] = next.Version,
                    ["baseUnit"] = baseUnit.Code,
                    ["packTracking"] = next.PackStateTrackingEnabled,
                    ["repacking"] = next.RepackingEnabled,
                    ["purchaseOptions"] = plan.Purchase.Count,
                    ["sellingOptions"] = plan.Selling.Count
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        var saved = await reader.GetCurrentAsync(businessId, [request.VariantId], cancellationToken);
        return new VariantUnitConfigReadModel(
            request.VariantId,
            await store.HasStockActivityAsync(request.VariantId, cancellationToken),
            saved.GetValueOrDefault(request.VariantId),
            await store.GetHistoryAsync(businessId, request.VariantId, cancellationToken));
    }
}

public sealed record ValidatedPurchaseOption(string Code, long UnitId, string Label, decimal FactorNumerator, decimal FactorDenominator, bool IsDefault, bool IsSealedPack);

public sealed record ValidatedSellingOption(
    string Kind, string Code, string Label, long? UnitId, decimal? BaseQuantity, decimal FactorNumerator, decimal FactorDenominator,
    decimal Price, bool IsDefault, string? SealedPackCode, bool IsPreparedEligible, IReadOnlyList<SellingComponentInput> Components);

public sealed record ValidatedConfigPlan(IReadOnlyList<ValidatedPurchaseOption> Purchase, IReadOnlyList<ValidatedSellingOption> Selling);

/// <summary>Rejects invalid conversions and options before anything is written (PROD-002, PROD-003, PROD-008).</summary>
public static class VariantUomConfigValidator
{
    public static ValidatedConfigPlan Validate(
        VariantUomConfigInput input,
        IReadOnlyDictionary<long, ResolvedUnit> units,
        IReadOnlyList<StandardConversionRow> conversions)
    {
        if (!units.TryGetValue(input.BaseUnitId, out var baseUnit))
            throw Invalid("The base unit was not found or is inactive.");
        if (baseUnit.Dimension == UomDimensions.Packaging)
            throw Invalid("A packaging name such as jar cannot be a base unit. Choose what the stock is measured in.");

        var purchase = new List<ValidatedPurchaseOption>();
        foreach (var option in input.PurchaseOptions)
        {
            var code = Code(option.Code, "purchase unit");
            if (!units.TryGetValue(option.UnitId, out var unit))
                throw Invalid($"The unit of purchase option '{code}' was not found or is inactive.");
            var (numerator, denominator) = Factor(unit, baseUnit, option.FactorNumerator, option.FactorDenominator, conversions);
            // Rejects a package size the base unit cannot hold exactly.
            UomQuantityMath.ToBase(1m, numerator, denominator, baseUnit.Precision, baseUnit.Code);
            if (option.IsSealedPack && !input.PackStateTrackingEnabled)
                throw Invalid($"'{code}' is marked as a sealed pack, which needs pack tracking.");
            purchase.Add(new ValidatedPurchaseOption(code, option.UnitId, Label(option.Label, code), numerator, denominator, option.IsDefault, option.IsSealedPack));
        }

        EnsureUnique(purchase.Select(option => option.Code), "purchase unit");
        if (purchase.Count > 0 && purchase.Count(option => option.IsDefault) != 1)
            throw Invalid("Choose exactly one default purchase unit.");

        var sealedCodes = purchase.Where(option => option.IsSealedPack).Select(option => option.Code).ToHashSet(StringComparer.Ordinal);
        var selling = new List<ValidatedSellingOption>();
        foreach (var option in input.SellingOptions)
        {
            var code = Code(option.Code, "selling option");
            if (option.Price < 0m)
                throw Invalid($"The price of '{code}' cannot be negative.");
            if (Core.Money.MonetaryRounding.ToCurrency(option.Price) != option.Price)
                throw Invalid($"The price of '{code}' cannot have more than 2 decimal places.");
            foreach (var component in option.Components)
                if (component.Quantity <= 0m)
                    throw Invalid($"Packaging quantity of '{code}' must be greater than zero.");
            if (option.Components.Select(component => component.ComponentVariantId).Distinct().Count() != option.Components.Count)
                throw Invalid($"'{code}' lists the same packaging item twice.");

            if (option.Kind == SellingOptionKinds.Pack)
            {
                if (option.BaseQuantity is not > 0m)
                    throw Invalid($"Pack '{code}' needs a quantity greater than zero.");
                UomQuantityMath.EnsurePrecision(option.BaseQuantity.Value, baseUnit.Precision, $"Pack '{code}' quantity");
                var sealedCode = string.IsNullOrWhiteSpace(option.SealedPackCode) ? null : option.SealedPackCode.Trim();
                if (sealedCode is not null && !sealedCodes.Contains(sealedCode))
                    throw Invalid($"Pack '{code}' sells sealed stock '{sealedCode}', which is not a sealed purchase unit of this item.");
                if (option.IsPreparedEligible && !input.PackStateTrackingEnabled)
                    throw Invalid($"Prepared pack '{code}' needs pack tracking.");
                selling.Add(new ValidatedSellingOption(
                    SellingOptionKinds.Pack, code, Label(option.Label, code), null,
                    UomQuantityMath.Normalize(option.BaseQuantity.Value), 1m, 1m, option.Price, option.IsDefault,
                    sealedCode, option.IsPreparedEligible, option.Components));
            }
            else if (option.Kind == SellingOptionKinds.Custom)
            {
                if (option.UnitId is null || !units.TryGetValue(option.UnitId.Value, out var unit))
                    throw Invalid($"Custom option '{code}' needs an active unit.");
                var (numerator, denominator) = UomStandardConverter.Resolve(unit, baseUnit, conversions);
                selling.Add(new ValidatedSellingOption(
                    SellingOptionKinds.Custom, code, Label(option.Label, code), unit.Id, null, numerator, denominator,
                    option.Price, option.IsDefault, null, false, option.Components));
            }
            else
            {
                throw Invalid($"'{option.Kind}' is not a selling option kind.");
            }
        }

        EnsureUnique(selling.Select(option => option.Code), "selling option");
        if (selling.Count(option => option.IsDefault) > 1)
            throw Invalid("Choose at most one default selling option.");
        if (input.RepackingEnabled && (!input.PackStateTrackingEnabled || !selling.Any(option => option.IsPreparedEligible)))
            throw Invalid("Repacking needs pack tracking and at least one pack that can be prepared.");
        if (!string.Equals(baseUnit.Code, UomUnit.PcsCode, StringComparison.OrdinalIgnoreCase) && selling.Count == 0)
            throw Invalid("An item measured in a unit other than pcs needs at least one selling option.");

        return new ValidatedConfigPlan(purchase, selling);
    }

    /// <summary>A compatible physical unit uses its standard factor; only a packaging name takes a product-specific size (D02).</summary>
    private static (decimal Numerator, decimal Denominator) Factor(
        ResolvedUnit unit,
        ResolvedUnit baseUnit,
        decimal numerator,
        decimal denominator,
        IReadOnlyList<StandardConversionRow> conversions)
    {
        if (unit.Dimension != UomDimensions.Packaging)
            return UomStandardConverter.Resolve(unit, baseUnit, conversions);
        if (numerator <= 0m || denominator <= 0m)
            throw Invalid($"The size of {unit.Symbol} must be greater than zero.");
        return (UomQuantityMath.Normalize(numerator), UomQuantityMath.Normalize(denominator));
    }

    private static string Code(string? code, string what)
    {
        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 40)
            throw Invalid($"A {what} needs a code of up to 40 characters.");
        return trimmed;
    }

    private static string Label(string? label, string code)
    {
        var trimmed = string.IsNullOrWhiteSpace(label) ? code : label.Trim();
        return trimmed.Length > 100 ? throw Invalid($"The label of '{code}' is longer than 100 characters.") : trimmed;
    }

    private static void EnsureUnique(IEnumerable<string> codes, string what)
    {
        var duplicate = codes.GroupBy(code => code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw Invalid($"The {what} code '{duplicate.Key}' is used twice.");
    }

    private static UomValidationException Invalid(string message) => new(UomErrorCodes.InvalidConfiguration, message);
}

/// <summary>A server preview of a selling line. It authorizes nothing: checkout recomputes everything (CALC-005).</summary>
public sealed record PreviewUnitSaleQuery(long VariantId, long SellingOptionId, decimal EnteredQuantity, long? ExpectedConfigVersionId) : IQuery<UnitSaleQuoteReadModel>;

public sealed record UnitSaleQuoteReadModel(
    string SelectedUnit,
    decimal EnteredQuantity,
    string BaseUnit,
    decimal BaseQuantity,
    decimal PriceBeforeDiscountAndVat,
    long EffectiveConfigVersionId,
    bool IsPreviewOnly = true);

public sealed class PreviewUnitSaleQueryHandler(IVariantUomReader reader, IUomOperationScope scope)
    : IRequestHandler<PreviewUnitSaleQuery, UnitSaleQuoteReadModel>
{
    public async Task<UnitSaleQuoteReadModel> Handle(PreviewUnitSaleQuery request, CancellationToken cancellationToken)
    {
        var configs = await reader.GetCurrentAsync(scope.RequireBusinessId(), [request.VariantId], cancellationToken);
        if (!configs.TryGetValue(request.VariantId, out var config))
            throw new UomValidationException(UomErrorCodes.OptionNotFound, "This item is sold in pcs and has no selling options.");
        var line = UomLineResolver.ResolveSale(config, request.SellingOptionId, request.EnteredQuantity, request.ExpectedConfigVersionId);
        return new UnitSaleQuoteReadModel(line.Snapshot.UnitCode, line.Snapshot.EnteredQuantity, config.BaseUnit.Code, line.BaseQuantity, line.LineGross, config.ConfigId);
    }
}
