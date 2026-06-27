using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.Services;

namespace RetailPOS.API.Documents.Pdf;

public interface IGrnPdfMapper
{
    DocumentPdfRequest Map(GrnDto grn, RetailPOS.API.Settings.SystemSettings settings, bool showCostFields);
}

public sealed class GrnPdfMapper : IGrnPdfMapper
{
    public DocumentPdfRequest Map(GrnDto grn, RetailPOS.API.Settings.SystemSettings settings, bool showCostFields)
    {
        var formatting = new DocumentPdfFormattingOptions(
            CurrencySymbol: settings.Currency.CurrencySymbol,
            CurrencyCode: settings.Currency.CurrencyCode,
            DecimalPlaces: settings.Currency.DecimalPlaces,
            ThousandsSeparator: settings.Currency.ThousandsSeparator,
            DecimalSeparator: settings.Currency.DecimalSeparator,
            SymbolPosition: settings.Currency.SymbolPosition,
            DateFormat: settings.Company.DateFormat,
            TimeZone: settings.Company.TimeZone);

        var items = grn.Items.Select((item, index) => new DocumentPdfLineItem(
            LineNo: index + 1,
            ItemName: item.ProductName,
            VariantName: item.VariantName,
            Sku: null,
            ProductCode: item.ProductCode,
            Description: item.VariantAttributes,
            Quantity: item.ReceivedQty,
            Unit: null,
            UnitRate: item.UnitCost,
            Discount: null,
            Tax: null,
            LineTotal: item.TotalCost)).ToList();

        var subtotal = items.Sum(item => item.LineTotal ?? 0m);

        return new DocumentPdfRequest(
            DocumentType: DocumentPdfDocumentType.Grn,
            Title: "Goods Received Note",
            DocumentNumber: grn.GrnNumber,
            DocumentDate: grn.ReceivedDate,
            Company: new DocumentPdfCompanyInfo(
                CompanyName: settings.Company.CompanyName,
                AddressLines: DocumentPdfFormatting.SplitLines(settings.Company.Address),
                Phone: settings.Company.Phone,
                Email: settings.Company.Email,
                Website: settings.Company.Website,
                TaxNumber: settings.Company.TaxNumber,
                LogoBase64: settings.Company.LogoBase64),
            Formatting: formatting,
            BillTo: new DocumentPdfPartyInfo(
                Label: "Vendor",
                Name: grn.SupplierName,
                AddressLines: Array.Empty<string>()),
            ShipTo: new DocumentPdfPartyInfo(
                Label: "Warehouse",
                Name: grn.WarehouseName,
                AddressLines: Array.Empty<string>()),
            Items: items,
            Summary: new DocumentPdfSummary(
                Subtotal: subtotal,
                Discount: 0m,
                Tax: 0m,
                GrandTotal: subtotal),
            ShowCostFields: showCostFields,
            Notes: grn.Notes,
            ReferenceNumber: grn.PoOrderNumber,
            Status: grn.Status,
            Signatures: BuildSignatures(grn));
    }

    private static IReadOnlyList<DocumentPdfSignature> BuildSignatures(GrnDto grn)
    {
        var signatures = new List<DocumentPdfSignature>();

        if (!string.IsNullOrWhiteSpace(grn.CreatedByName))
        {
            signatures.Add(new DocumentPdfSignature("Received By", grn.CreatedByName));
        }

        return signatures;
    }
}

public interface IGrnPdfService
{
    Task<DocumentPdfFileResult> GenerateAsync(long grnId, bool showCostFields, CancellationToken cancellationToken = default);
}

public sealed class GrnPdfService : IGrnPdfService
{
    private readonly IGrnService _grnService;
    private readonly ISettingsService _settingsService;
    private readonly IGrnPdfMapper _mapper;
    private readonly IDocumentPdfRenderer _renderer;

    public GrnPdfService(
        IGrnService grnService,
        ISettingsService settingsService,
        IGrnPdfMapper mapper,
        IDocumentPdfRenderer renderer)
    {
        _grnService = grnService;
        _settingsService = settingsService;
        _mapper = mapper;
        _renderer = renderer;
    }

    public async Task<DocumentPdfFileResult> GenerateAsync(long grnId, bool showCostFields, CancellationToken cancellationToken = default)
    {
        var grn = await _grnService.GetByIdAsync(grnId);
        var settings = await _settingsService.GetAllSettingsAsync();

        var request = _mapper.Map(grn, settings, showCostFields);
        var pdfBytes = await _renderer.RenderAsync(request, cancellationToken);

        var fileName = DocumentPdfFileNameBuilder.Build(grn.GrnNumber, $"GRN-{grn.Id:D6}");
        return new DocumentPdfFileResult(pdfBytes, fileName);
    }
}
