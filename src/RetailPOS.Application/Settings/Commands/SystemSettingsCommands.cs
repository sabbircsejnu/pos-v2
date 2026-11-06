using RetailPOS.Application.BusinessAdministration;
using MediatR;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Queries;

namespace RetailPOS.Application.Settings.Commands;

public sealed record UpdateCompanySettingsCommand(
    CompanySettings Settings,
    long? BusinessId = null,
    long? UserId = null) : IRequest<SystemSettings>;

public sealed record UpdateCurrencySettingsCommand(CurrencySettings Settings)
    : IRequest<SystemSettings>;

public sealed record UpdateTaxSettingsCommand(TaxSettings Settings)
    : IRequest<SystemSettings>;

public sealed record UpdateReceiptSettingsCommand(ReceiptSettings Settings)
    : IRequest<SystemSettings>;

public sealed record UpdateInvoiceNumberSettingsCommand(InvoiceNumberSettings Settings)
    : IRequest<SystemSettings>;

public sealed record UpdateInventorySettingsCommand(InventorySettings Settings)
    : IRequest<SystemSettings>;

public sealed record AllocateNextInvoiceNumberCommand(DateTime UtcNow) : IRequest<string>;

public sealed class UpdateCompanySettingsCommandHandler(
    ISystemSettingsStore store,
    IBusinessPosSettingsStore businessSettings,
    IBusinessPosSettingsAuditWriter businessSettingsAudit,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<UpdateCompanySettingsCommand, SystemSettings>
{
    public async Task<SystemSettings> Handle(
        UpdateCompanySettingsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.BusinessId is not long businessId)
            return await store.UpdateCompanyAsync(request.Settings, cancellationToken);

        // Inside a Business the time zone is that Business's own; the shared value stays for the others.
        var requestedZone = request.Settings.TimeZone?.Trim();
        var currentZone = await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken);
        if (!string.IsNullOrEmpty(requestedZone)
            && !string.Equals(requestedZone, currentZone, StringComparison.Ordinal))
        {
            var (before, after) = await businessSettings.UpdateAsync(
                businessId,
                new BusinessPosSettingsUpdate(DefaultTimeZoneId: requestedZone),
                request.UserId,
                cancellationToken);
            await businessSettingsAudit.RecordUpdatedAsync(before, after, request.UserId, cancellationToken);
            currentZone = after.DefaultTimeZoneId;
        }

        request.Settings.TimeZone = (await store.GetAsync(cancellationToken)).Company.TimeZone;
        var saved = await store.UpdateCompanyAsync(request.Settings, cancellationToken);
        return GetSystemSettingsQueryHandler.WithCompanyTimeZone(saved, currentZone);
    }
}

public sealed class UpdateCurrencySettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateCurrencySettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateCurrencySettingsCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateCurrencyAsync(request.Settings, cancellationToken);
}

public sealed class UpdateTaxSettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateTaxSettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateTaxSettingsCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateTaxAsync(request.Settings, cancellationToken);
}

public sealed class UpdateReceiptSettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateReceiptSettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateReceiptSettingsCommand request,
        CancellationToken cancellationToken)
    {
        if (BusinessReceiptFooterRules.ContainsPoweredByLine(request.Settings.Footer))
            throw new ArgumentException(
                "The Powered by footer is managed by the Platform Super Admin and cannot be set in Receipt Settings.");

        return store.UpdateReceiptAsync(request.Settings, cancellationToken);
    }
}

public sealed class UpdateInvoiceNumberSettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateInvoiceNumberSettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateInvoiceNumberSettingsCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateInvoiceNumberAsync(request.Settings, cancellationToken);
}

public sealed class UpdateInventorySettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateInventorySettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateInventorySettingsCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateInventoryAsync(request.Settings, cancellationToken);
}

public sealed class AllocateNextInvoiceNumberCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<AllocateNextInvoiceNumberCommand, string>
{
    public Task<string> Handle(
        AllocateNextInvoiceNumberCommand request,
        CancellationToken cancellationToken) =>
        store.AllocateNextInvoiceNumberAsync(request.UtcNow, cancellationToken);
}
