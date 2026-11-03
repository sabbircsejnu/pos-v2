using MediatR;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings;

namespace RetailPOS.Application.Settings.Commands;

public sealed record UpdateCompanySettingsCommand(CompanySettings Settings)
    : IRequest<SystemSettings>;

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

public sealed class UpdateCompanySettingsCommandHandler(ISystemSettingsStore store)
    : IRequestHandler<UpdateCompanySettingsCommand, SystemSettings>
{
    public Task<SystemSettings> Handle(
        UpdateCompanySettingsCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateCompanyAsync(request.Settings, cancellationToken);
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
        CancellationToken cancellationToken) =>
        store.UpdateReceiptAsync(request.Settings, cancellationToken);
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
