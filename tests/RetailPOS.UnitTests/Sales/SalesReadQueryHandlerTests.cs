using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Tests.Sales;

public sealed class SalesReadQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsSaleIdAndCancellationToken()
    {
        var expected = CreateSale();
        var store = new FakeSaleReadStore(expected);
        var handler = new GetSaleByIdQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetSaleByIdQuery(1201), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(1201, store.SaleId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Handle_ThrowsLegacyNotFoundMessageWhenSaleDoesNotExist()
    {
        var handler = new GetSaleByIdQueryHandler(new FakeSaleReadStore(null));

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(new GetSaleByIdQuery(9999), CancellationToken.None));

        Assert.Equal("Sale with ID 9999 not found", exception.Message);
    }

    [Fact]
    public async Task SearchHandler_ForwardsCriteriaAndCancellationToken()
    {
        var expected = new SaleSearchReadModel([CreateSale()], 4, 2, 1);
        var criteria = new SaleSearchCriteria(
            5001,
            7001,
            6001,
            new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 3),
            "completed",
            2,
            1);
        var store = new FakeSaleReadStore(null) { SearchResult = expected };
        var handler = new SearchSalesQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new SearchSalesQuery(criteria), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(criteria, store.Criteria);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task PrintReceipt_RecordsCurrentSaleAndReturnsItsReadModel()
    {
        var sale = CreateSale();
        var store = new FakeSaleReadStore(sale);
        var writer = new FakeReceiptHistoryWriter();
        var handler = new PrintSaleReceiptCommandHandler(store, writer);
        using var cancellation = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        var result = await handler.Handle(
            new PrintSaleReceiptCommand(sale.Id, 7001),
            cancellation.Token);

        Assert.Same(sale, result);
        Assert.Equal(sale.Id, writer.SaleId);
        Assert.Equal(sale.TerminalId, writer.TerminalId);
        Assert.Equal(7001, writer.PrintedByUserId);
        Assert.Equal("print", writer.ActionType);
        Assert.InRange(writer.PrintedAtUtc, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, writer.PrintedAtUtc.Kind);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Equal(cancellation.Token, writer.CancellationToken);
    }

    [Fact]
    public async Task PrintReceipt_DoesNotRecordHistoryWhenSaleIsMissing()
    {
        var writer = new FakeReceiptHistoryWriter();
        var handler = new PrintSaleReceiptCommandHandler(
            new FakeSaleReadStore(null),
            writer);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new PrintSaleReceiptCommand(9999, 7001),
                CancellationToken.None));

        Assert.Equal("Sale with ID 9999 not found", exception.Message);
        Assert.Null(writer.SaleId);
    }

    [Fact]
    public async Task PrintReceipt_PropagatesHistoryPersistenceFailure()
    {
        var expected = new InvalidOperationException("history persistence failed");
        var writer = new FakeReceiptHistoryWriter { Failure = expected };
        var handler = new PrintSaleReceiptCommandHandler(
            new FakeSaleReadStore(CreateSale()),
            writer);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new PrintSaleReceiptCommand(1201, 7001),
                CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("VOIDED")]
    [InlineData("refunded")]
    public async Task ReprintReceipt_AllowsFinalizedSaleAndRecordsReprintHistory(string status)
    {
        var sale = CreateSale() with { Status = status };
        var store = new FakeSaleReadStore(sale);
        var historyWriter = new FakeReceiptHistoryWriter();
        var handler = new ReprintSaleReceiptCommandHandler(store, historyWriter);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new ReprintSaleReceiptCommand(sale.Id, 7001),
            cancellation.Token);

        Assert.Same(sale, result);
        Assert.Equal("reprint", historyWriter.ActionType);
        Assert.Equal(sale.Id, historyWriter.SaleId);
        Assert.Equal(sale.TerminalId, historyWriter.TerminalId);
        Assert.Equal(7001, historyWriter.PrintedByUserId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Equal(cancellation.Token, historyWriter.CancellationToken);
    }

    [Fact]
    public async Task ReprintReceipt_RejectsUnfinalizedSaleBeforeRecordingHistory()
    {
        var historyWriter = new FakeReceiptHistoryWriter();
        var handler = new ReprintSaleReceiptCommandHandler(
            new FakeSaleReadStore(CreateSale() with { Status = "draft" }),
            historyWriter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new ReprintSaleReceiptCommand(1201, 7001),
                CancellationToken.None));

        Assert.Equal("Only finalized sales can be reprinted", exception.Message);
        Assert.Null(historyWriter.SaleId);
    }

    [Theory]
    [InlineData("Reprinted", "reprint", "ReceiptReprinted", "Success")]
    [InlineData("SavedAsPdf", "saved_pdf", "ReceiptSavedAsPdf", "Success")]
    [InlineData("unknown", "print_failed", "PrintFailure", "Failed")]
    public async Task RegisterReceiptAttempt_PreservesActionNormalizationAndAudit(
        string inputAction,
        string expectedHistoryAction,
        string expectedAuditAction,
        string expectedAuditStatus)
    {
        var sale = CreateSale();
        var historyWriter = new FakeReceiptHistoryWriter();
        var auditWriter = new FakeReceiptAttemptAuditWriter();
        var handler = new RegisterSaleReceiptAttemptCommandHandler(
            new FakeSaleReadStore(sale),
            historyWriter,
            auditWriter);
        using var cancellation = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        await handler.Handle(
            new RegisterSaleReceiptAttemptCommand(
                sale.Id,
                inputAction,
                "thermal",
                "printer response",
                7001),
            cancellation.Token);

        Assert.Equal(expectedHistoryAction, historyWriter.ActionType);
        Assert.Equal(7001, historyWriter.PrintedByUserId);
        Assert.InRange(historyWriter.PrintedAtUtc, before, DateTime.UtcNow);
        Assert.Equal(expectedAuditAction, auditWriter.Record?.ActionType);
        Assert.Equal(expectedAuditStatus, auditWriter.Record?.Status);
        Assert.Equal(sale.Id, auditWriter.Record?.SaleId);
        Assert.Equal(sale.SaleNumber, auditWriter.Record?.SaleNumber);
        Assert.Equal(sale.OutletId, auditWriter.Record?.OutletId);
        Assert.Equal("thermal", auditWriter.Record?.OutputMode);
        Assert.Equal(cancellation.Token, historyWriter.CancellationToken);
        Assert.Equal(cancellation.Token, auditWriter.CancellationToken);
    }

    [Theory]
    [InlineData("a4", "A4")]
    [InlineData("invalid", "80mm")]
    [InlineData("", "A4")]
    public async Task GenerateReceiptDocument_NormalizesModeAndRecordsHistoryOnce(
        string inputMode,
        string expectedMode)
    {
        var sale = CreateSale() with
        {
            Items =
            [
                new SaleItemReadModel(1, 3201, "Coffee", "SKU-1", 2, 10m, null, 1m, 19m, null, "Large")
            ],
            Payments =
            [
                new SalePaymentReadModel(1, "cash", 19m, 20m, 1m)
            ]
        };
        var history = new FakeReceiptHistoryWriter();
        var audit = new FakeReceiptActionAuditWriter();
        var handler = new GenerateSaleReceiptDocumentCommandHandler(
            new FakeSaleReadStore(sale),
            new FakeSaleReceiptSettingsProvider(new SaleReceiptSettingsReadModel(
                "A4", "80mm", true, true, false, "Header", "Thank you", "Returns", "Footer",
                "Company", "logo-data", "company phone", "VAT")),
            history,
            audit);
        using var cancellation = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        var document = await handler.Handle(
            new GenerateSaleReceiptDocumentCommand(1201, inputMode, 7001),
            cancellation.Token);

        Assert.Equal(expectedMode, document.OutputMode);
        Assert.Equal("Coffee", document.Items.Single().ProductName);
        Assert.Equal("Large", document.Items.Single().VariantLabel);
        Assert.Equal(2m, document.Summary.Quantity);
        Assert.Equal(20m, document.Summary.Subtotal);
        Assert.Equal(2m, document.Summary.Discount);
        Assert.Equal(19m, document.Summary.PaidAmount);
        Assert.Equal(1m, document.Summary.Change);
        Assert.Equal("Main Outlet", document.Header.OutletName);
        Assert.Equal("company phone", document.Header.Phone);
        Assert.Equal(1, history.RecordCount);
        Assert.Equal("generated", history.ActionType);
        Assert.Equal(7001, history.PrintedByUserId);
        Assert.InRange(history.PrintedAtUtc, before, DateTime.UtcNow);
        Assert.Equal(sale.Id, audit.Record?.SaleId);
        Assert.Equal(expectedMode, audit.Record?.OutputMode);
        Assert.Equal(cancellation.Token, history.CancellationToken);
        Assert.Equal(cancellation.Token, audit.CancellationToken);
    }

    [Fact]
    public async Task GenerateReceiptDocument_PropagatesRequiredHistoryFailureWithoutAudit()
    {
        var expected = new InvalidOperationException("history failed");
        var history = new FakeReceiptHistoryWriter { Failure = expected };
        var audit = new FakeReceiptActionAuditWriter();
        var handler = new GenerateSaleReceiptDocumentCommandHandler(
            new FakeSaleReadStore(CreateSale()),
            new FakeSaleReceiptSettingsProvider(new SaleReceiptSettingsReadModel(
                "80mm", "80mm", true, false, false, null, null, null, null,
                "Company", null, null, null)),
            history,
            audit);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new GenerateSaleReceiptDocumentCommand(1201, null, 7001),
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Null(audit.Record);
    }

    [Fact]
    public async Task GenerateReceiptDocument_ThrowsLegacyNotFoundMessageBeforeSettingsOrWrites()
    {
        var settings = new FakeSaleReceiptSettingsProvider(new SaleReceiptSettingsReadModel(
            "80mm", "80mm", true, false, false, null, null, null, null,
            "Company", null, null, null));
        var history = new FakeReceiptHistoryWriter();
        var audit = new FakeReceiptActionAuditWriter();
        var handler = new GenerateSaleReceiptDocumentCommandHandler(
            new FakeSaleReadStore(null),
            settings,
            history,
            audit);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new GenerateSaleReceiptDocumentCommand(9999, null, 7001),
                CancellationToken.None));

        Assert.Equal("Sale with ID 9999 not found", exception.Message);
        Assert.Equal(0, settings.CallCount);
        Assert.Equal(0, history.RecordCount);
        Assert.Null(audit.Record);
    }

    [Fact]
    public async Task GenerateReceiptPdf_UsesSharedCompositionAndRecordsOnlySavedPdfAction()
    {
        var sale = CreateSale();
        var history = new FakeReceiptHistoryWriter();
        var audit = new FakeReceiptActionAuditWriter();
        var renderer = new FakeSaleReceiptPdfRenderer();
        var handler = new GenerateSaleReceiptPdfCommandHandler(
            new FakeSaleReadStore(sale),
            new FakeSaleReceiptSettingsProvider(new SaleReceiptSettingsReadModel(
                "A4", "80mm", true, false, false, null, null, null, null,
                "Company", null, null, null)),
            renderer,
            history,
            audit);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GenerateSaleReceiptPdfCommand(1201, null, 7001),
            cancellation.Token);

        Assert.Equal("SALE-1201", result.SaleNumber);
        Assert.Equal(new byte[] { 37, 80, 68, 70 }, result.Content);
        Assert.Equal("A4", renderer.Document?.OutputMode);
        Assert.Equal("saved_pdf", history.ActionType);
        Assert.Equal(1, history.RecordCount);
        Assert.Equal("ReceiptSavedAsPdf", audit.Record?.ActionType);
        Assert.Equal("saved_pdf", audit.Record?.Action);
        Assert.Equal(cancellation.Token, history.CancellationToken);
        Assert.Equal(cancellation.Token, audit.CancellationToken);
    }

    [Fact]
    public async Task GenerateReceiptPdf_RequiredHistoryFailurePreventsAudit()
    {
        var history = new FakeReceiptHistoryWriter
        {
            Failure = new InvalidOperationException("history failed")
        };
        var audit = new FakeReceiptActionAuditWriter();
        var handler = new GenerateSaleReceiptPdfCommandHandler(
            new FakeSaleReadStore(CreateSale()),
            new FakeSaleReceiptSettingsProvider(new SaleReceiptSettingsReadModel(
                "80mm", "80mm", true, false, false, null, null, null, null,
                "Company", null, null, null)),
            new FakeSaleReceiptPdfRenderer(),
            history,
            audit);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new GenerateSaleReceiptPdfCommand(1201, null, 7001),
                CancellationToken.None));

        Assert.Equal(1, history.RecordCount);
        Assert.Equal("saved_pdf", history.ActionType);
        Assert.Null(audit.Record);
    }

    private static SaleReadModel CreateSale() => new(
        Id: 1201,
        SaleNumber: "SALE-1201",
        OutletId: 5001,
        OutletName: "Main Outlet",
        OutletAddress: "Dhaka",
        OutletContactNumber: null,
        TerminalId: null,
        TerminalName: null,
        CashSessionId: null,
        BusinessDate: null,
        SaleDateTime: null,
        CustomerId: null,
        CustomerName: null,
        CashierId: 7001,
        CashierName: "Cashier One",
        SaleDate: DateTime.UtcNow,
        TotalAmount: 12m,
        Discount: 1m,
        Tax: 0m,
        NetTotal: 11m,
        PaymentMethod: "cash",
        Status: "completed",
        CreatedAt: DateTime.UtcNow,
        Items: [],
        Payments: []);

    private sealed class FakeSaleReadStore(SaleReadModel? result) : ISaleReadStore
    {
        public long SaleId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public SaleSearchCriteria? Criteria { get; private set; }
        public SaleSearchReadModel SearchResult { get; set; } = new([], 0, 1, 10);

        public Task<SaleReadModel?> FindByIdAsync(
            long saleId,
            CancellationToken cancellationToken)
        {
            SaleId = saleId;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }

        public Task<SaleReadModel?> FindForReceiptAsync(
            long saleId,
            CancellationToken cancellationToken)
        {
            SaleId = saleId;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }

        public Task<SaleSearchReadModel> SearchAsync(
            SaleSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            Criteria = criteria;
            CancellationToken = cancellationToken;
            return Task.FromResult(SearchResult);
        }
    }

    private sealed class FakeReceiptHistoryWriter : ISaleReceiptHistoryWriter
    {
        public long? SaleId { get; private set; }
        public long? TerminalId { get; private set; }
        public long? PrintedByUserId { get; private set; }
        public DateTime PrintedAtUtc { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Exception? Failure { get; init; }

        public string? ActionType { get; private set; }

        public Task RecordAsync(
            long saleId,
            long? terminalId,
            long? printedByUserId,
            string actionType,
            DateTime printedAtUtc,
            CancellationToken cancellationToken)
        {
            SaleId = saleId;
            RecordCount++;
            TerminalId = terminalId;
            PrintedByUserId = printedByUserId;
            ActionType = actionType;
            PrintedAtUtc = printedAtUtc;
            CancellationToken = cancellationToken;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }

        public int RecordCount { get; private set; }
    }

    private sealed class FakeReceiptAttemptAuditWriter : ISaleReceiptAttemptAuditWriter
    {
        public SaleReceiptAttemptAuditRecord? Record { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task RecordBestEffortAsync(
            SaleReceiptAttemptAuditRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSaleReceiptSettingsProvider(SaleReceiptSettingsReadModel settings)
        : ISaleReceiptSettingsProvider
    {
        public int CallCount { get; private set; }

        public Task<SaleReceiptSettingsReadModel> GetAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(settings);
        }
    }

    private sealed class FakeReceiptActionAuditWriter : ISaleReceiptActionAuditWriter
    {
        public SaleReceiptActionAuditRecord? Record { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task RecordBestEffortAsync(
            SaleReceiptActionAuditRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSaleReceiptPdfRenderer : ISaleReceiptPdfRenderer
    {
        public SaleReceiptDocumentReadModel? Document { get; private set; }

        public byte[] Render(SaleReceiptDocumentReadModel document)
        {
            Document = document;
            return [37, 80, 68, 70];
        }
    }
}
