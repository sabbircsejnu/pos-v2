using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.Data;

public class RetailPOSDbContext : DbContext
{
    public RetailPOSDbContext(DbContextOptions<RetailPOSDbContext> options) : base(options)
    {
    }

    // Tenancy
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessFeatureSetting> BusinessFeatureSettings => Set<BusinessFeatureSetting>();
    public DbSet<BusinessReminderNotification> BusinessReminderNotifications => Set<BusinessReminderNotification>();
    public DbSet<BusinessPosSettings> BusinessPosSettings => Set<BusinessPosSettings>();
    public DbSet<SaleInvoiceCounter> SaleInvoiceCounters => Set<SaleInvoiceCounter>();

    // User & Role Management
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<UserWarehouseAssignment> UserWarehouseAssignments => Set<UserWarehouseAssignment>();
    public DbSet<UserOutletAssignment> UserOutletAssignments => Set<UserOutletAssignment>();
    public DbSet<UserContextRole> UserContextRoles => Set<UserContextRole>();

    // Locations
    public DbSet<Outlet> Outlets => Set<Outlet>();
    public DbSet<PosTerminal> PosTerminals => Set<PosTerminal>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    // Products & Inventory
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Variation> Variations => Set<Variation>();
    public DbSet<VariationOption> VariationOptions => Set<VariationOption>();
    public DbSet<ProductVariation> ProductVariations => Set<ProductVariation>();
    public DbSet<ProductVariantOption> ProductVariantOptions => Set<ProductVariantOption>();
    public DbSet<ProductVariationSelectedOption> ProductVariationSelectedOptions => Set<ProductVariationSelectedOption>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<BarcodeTemplate> BarcodeTemplates => Set<BarcodeTemplate>();
    public DbSet<BarcodeTemplateField> BarcodeTemplateFields => Set<BarcodeTemplateField>();
    public DbSet<BarcodePrintHistory> BarcodePrintHistories => Set<BarcodePrintHistory>();
    public DbSet<BarcodePrintHistoryItem> BarcodePrintHistoryItems => Set<BarcodePrintHistoryItem>();

    // Suppliers & Purchase
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Grn> Grns => Set<Grn>();
    public DbSet<GrnItem> GrnItems => Set<GrnItem>();

    // Customers & Sales
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashSessionMovement> CashSessionMovements => Set<CashSessionMovement>();
    public DbSet<CashSessionCloseSnapshot> CashSessionCloseSnapshots => Set<CashSessionCloseSnapshot>();
    public DbSet<CashSessionHandover> CashSessionHandovers => Set<CashSessionHandover>();
    public DbSet<CashSessionAttachment> CashSessionAttachments => Set<CashSessionAttachment>();
    public DbSet<CashDifferenceApproval> CashDifferenceApprovals => Set<CashDifferenceApproval>();
    public DbSet<SaleFinancialPosting> SaleFinancialPostings => Set<SaleFinancialPosting>();
    public DbSet<FinancialPostingWorkerHeartbeat> FinancialPostingWorkerHeartbeats => Set<FinancialPostingWorkerHeartbeat>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>(); // NEW — split-payment rows
    public DbSet<ReceiptPrintHistory> ReceiptPrintHistories => Set<ReceiptPrintHistory>();
    public DbSet<SaleVoid> SaleVoids => Set<SaleVoid>();
    public DbSet<HeldSale> HeldSales => Set<HeldSale>();           // NEW — parked/held carts

    // Stock Management
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockRequisition> StockRequisitions => Set<StockRequisition>();
    public DbSet<StockRequisitionLine> StockRequisitionLines => Set<StockRequisitionLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<StockCountOperationReceipt> StockCountOperationReceipts => Set<StockCountOperationReceipt>();
    public DbSet<StockCountSubmissionJob> StockCountSubmissionJobs => Set<StockCountSubmissionJob>();
    public DbSet<StockCountAdjustmentJob> StockCountAdjustmentJobs => Set<StockCountAdjustmentJob>();

    // Accounting
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<SupplierAdvance> SupplierAdvances => Set<SupplierAdvance>();
    public DbSet<SupplierPayableDocument> SupplierPayableDocuments => Set<SupplierPayableDocument>();
    public DbSet<SupplierAdvanceUtilization> SupplierAdvanceUtilizations => Set<SupplierAdvanceUtilization>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseAttachment> ExpenseAttachments => Set<ExpenseAttachment>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<ChartAccount> ChartAccounts => Set<ChartAccount>();
    public DbSet<AccountingConfiguration> AccountingConfigurations => Set<AccountingConfiguration>();
    public DbSet<AccountingOutletConfiguration> AccountingOutletConfigurations => Set<AccountingOutletConfiguration>();
    public DbSet<AccountingNumberSequence> AccountingNumberSequences => Set<AccountingNumberSequence>();
    public DbSet<FinancialYear> FinancialYears => Set<FinancialYear>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<AccountingPeriodCloseRun> AccountingPeriodCloseRuns => Set<AccountingPeriodCloseRun>();
    public DbSet<AccountingFinancialYearCloseRun> AccountingFinancialYearCloseRuns => Set<AccountingFinancialYearCloseRun>();
    public DbSet<AccountingCloseGateSnapshot> AccountingCloseGateSnapshots => Set<AccountingCloseGateSnapshot>();
    public DbSet<AccountingCloseExceptionApproval> AccountingCloseExceptionApprovals => Set<AccountingCloseExceptionApproval>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<SettlementBatch> SettlementBatches => Set<SettlementBatch>();
    public DbSet<LiquidityTransfer> LiquidityTransfers => Set<LiquidityTransfer>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<BankReconciliationSession> BankReconciliationSessions => Set<BankReconciliationSession>();
    public DbSet<BankReconciliationMatch> BankReconciliationMatches => Set<BankReconciliationMatch>();
    public DbSet<BankReconciliationMatchLine> BankReconciliationMatchLines => Set<BankReconciliationMatchLine>();
    public DbSet<BankStatementAdjustment> BankStatementAdjustments => Set<BankStatementAdjustment>();
    public DbSet<BankStatementException> BankStatementExceptions => Set<BankStatementException>();
    public DbSet<JournalEntryAttachment> JournalEntryAttachments => Set<JournalEntryAttachment>();
    public DbSet<OtherIncomeEntry> OtherIncomeEntries => Set<OtherIncomeEntry>();

    // Stock Ledger  // NEW
    public DbSet<StockLedger> StockLedgers => Set<StockLedger>();
    public DbSet<RetailPOS.Core.Entities.Uom.UomUnit> UomUnits => Set<RetailPOS.Core.Entities.Uom.UomUnit>();
    public DbSet<RetailPOS.Core.Entities.Uom.UomStandardConversion> UomStandardConversions => Set<RetailPOS.Core.Entities.Uom.UomStandardConversion>();
    public DbSet<RetailPOS.Core.Entities.Uom.VariantUomConfig> VariantUomConfigs => Set<RetailPOS.Core.Entities.Uom.VariantUomConfig>();
    public DbSet<RetailPOS.Core.Entities.Uom.VariantPurchaseUnitOption> VariantPurchaseUnitOptions => Set<RetailPOS.Core.Entities.Uom.VariantPurchaseUnitOption>();
    public DbSet<RetailPOS.Core.Entities.Uom.VariantSellingOption> VariantSellingOptions => Set<RetailPOS.Core.Entities.Uom.VariantSellingOption>();
    public DbSet<RetailPOS.Core.Entities.Uom.SellingOptionComponent> SellingOptionComponents => Set<RetailPOS.Core.Entities.Uom.SellingOptionComponent>();
    public DbSet<RetailPOS.Core.Entities.Uom.PackStateBalance> PackStateBalances => Set<RetailPOS.Core.Entities.Uom.PackStateBalance>();
    public DbSet<RetailPOS.Core.Entities.Uom.PackStateMovement> PackStateMovements => Set<RetailPOS.Core.Entities.Uom.PackStateMovement>();
    public DbSet<RetailPOS.Core.Entities.Uom.RepackJob> RepackJobs => Set<RetailPOS.Core.Entities.Uom.RepackJob>();
    public DbSet<RetailPOS.Core.Entities.Uom.RepackComponentLine> RepackComponentLines => Set<RetailPOS.Core.Entities.Uom.RepackComponentLine>();
    public DbSet<RetailPOS.Core.Entities.Uom.SaleItemComponent> SaleItemComponents => Set<RetailPOS.Core.Entities.Uom.SaleItemComponent>();
    public DbSet<RetailPOS.Core.Entities.Uom.SaleReturnLine> SaleReturnLines => Set<RetailPOS.Core.Entities.Uom.SaleReturnLine>();
    public DbSet<RetailPOS.Core.Entities.Uom.StockTransferItemAllocation> StockTransferItemAllocations => Set<RetailPOS.Core.Entities.Uom.StockTransferItemAllocation>();
    public DbSet<RetailPOS.Core.Entities.Uom.StockCountLineEntry> StockCountLineEntries => Set<RetailPOS.Core.Entities.Uom.StockCountLineEntry>();

    // Audit (legacy — kept for backward compat during transition)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Audit (v2)
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuditEventEntity> AuditEventEntities => Set<AuditEventEntity>();
    public DbSet<AuditEventFieldChange> AuditEventFieldChanges => Set<AuditEventFieldChange>();
    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();
    public DbSet<ApprovalPolicyStep> ApprovalPolicySteps => Set<ApprovalPolicyStep>();
    public DbSet<ApprovalInstance> ApprovalInstances => Set<ApprovalInstance>();
    public DbSet<ApprovalInstanceStep> ApprovalInstanceSteps => Set<ApprovalInstanceStep>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<HrOrgUnit> HrOrgUnits => Set<HrOrgUnit>();
    public DbSet<OperationalLocationProjection> OperationalLocationProjections => Set<OperationalLocationProjection>();
    public DbSet<HrPosition> HrPositions => Set<HrPosition>();
    public DbSet<HrEmploymentType> HrEmploymentTypes => Set<HrEmploymentType>();
    public DbSet<HrEmployee> HrEmployees => Set<HrEmployee>();
    public DbSet<HrEmploymentPeriod> HrEmploymentPeriods => Set<HrEmploymentPeriod>();
    public DbSet<HrEmployeeLifecycleEvent> HrEmployeeLifecycleEvents => Set<HrEmployeeLifecycleEvent>();
    public DbSet<HrEmployeePositionAssignment> HrEmployeePositionAssignments =>
        Set<HrEmployeePositionAssignment>();
    public DbSet<HrEmployeeUserLink> HrEmployeeUserLinks => Set<HrEmployeeUserLink>();
    public DbSet<HrEmployeeLocationAssignment> HrEmployeeLocationAssignments =>
        Set<HrEmployeeLocationAssignment>();
    public DbSet<HrEmployeeLocationTransferRequest> HrEmployeeLocationTransferRequests =>
        Set<HrEmployeeLocationTransferRequest>();
    public DbSet<HrDocumentType> HrDocumentTypes => Set<HrDocumentType>();
    public DbSet<HrEmployeeDocument> HrEmployeeDocuments => Set<HrEmployeeDocument>();
    public DbSet<HrEmployeeDocumentVersion> HrEmployeeDocumentVersions => Set<HrEmployeeDocumentVersion>();
    public DbSet<HrImportBatch> HrImportBatches => Set<HrImportBatch>();
    public DbSet<HrImportRow> HrImportRows => Set<HrImportRow>();
    public DbSet<HrLeaveType> HrLeaveTypes => Set<HrLeaveType>();
    public DbSet<HrLeavePolicyVersion> HrLeavePolicyVersions => Set<HrLeavePolicyVersion>();
    public DbSet<HrLeaveEntitlement> HrLeaveEntitlements => Set<HrLeaveEntitlement>();
    public DbSet<HrLeaveLedgerEntry> HrLeaveLedgerEntries => Set<HrLeaveLedgerEntry>();
    public DbSet<HrLeaveYearLifecycleEvent> HrLeaveYearLifecycleEvents => Set<HrLeaveYearLifecycleEvent>();
    public DbSet<HrLeaveRequest> HrLeaveRequests => Set<HrLeaveRequest>();
    public DbSet<HrLeaveRequestDay> HrLeaveRequestDays => Set<HrLeaveRequestDay>();
    public DbSet<HrLeaveRequestAttachmentVersion> HrLeaveRequestAttachmentVersions => Set<HrLeaveRequestAttachmentVersion>();
    public DbSet<PayrollSalaryComponent> PayrollSalaryComponents => Set<PayrollSalaryComponent>();
    public DbSet<PayrollSalaryStructure> PayrollSalaryStructures => Set<PayrollSalaryStructure>();
    public DbSet<PayrollSalaryStructureLine> PayrollSalaryStructureLines => Set<PayrollSalaryStructureLine>();
    public DbSet<PayrollSalarySetupRequest> PayrollSalarySetupRequests => Set<PayrollSalarySetupRequest>();
    public DbSet<PayrollSalaryRevisionRequest> PayrollSalaryRevisionRequests => Set<PayrollSalaryRevisionRequest>();
    public DbSet<PayrollEmployeeSalaryVersion> PayrollEmployeeSalaryVersions => Set<PayrollEmployeeSalaryVersion>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollPeriodCloseAssessment> PayrollPeriodCloseAssessments =>
        Set<PayrollPeriodCloseAssessment>();
    public DbSet<PayrollPeriodReopenEvent> PayrollPeriodReopenEvents => Set<PayrollPeriodReopenEvent>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollRunEmployeeSelection> PayrollRunEmployeeSelections =>
        Set<PayrollRunEmployeeSelection>();
    public DbSet<PayrollEmployeeResult> PayrollEmployeeResults => Set<PayrollEmployeeResult>();
    public DbSet<PayrollCalculationPolicyVersion> PayrollCalculationPolicyVersions =>
        Set<PayrollCalculationPolicyVersion>();
    public DbSet<PayrollMonetaryConfigurationVersion> PayrollMonetaryConfigurationVersions =>
        Set<PayrollMonetaryConfigurationVersion>();
    public DbSet<PayrollEmployeePaymentDestination> PayrollEmployeePaymentDestinations =>
        Set<PayrollEmployeePaymentDestination>();
    public DbSet<PayrollEmployeeResultLine> PayrollEmployeeResultLines => Set<PayrollEmployeeResultLine>();
    public DbSet<PayrollPayment> PayrollPayments => Set<PayrollPayment>();
    public DbSet<PayrollPaymentBatch> PayrollPaymentBatches => Set<PayrollPaymentBatch>();
    public DbSet<PayrollPaymentBankFileVersion> PayrollPaymentBankFileVersions =>
        Set<PayrollPaymentBankFileVersion>();
    public DbSet<PayrollPaymentAllocation> PayrollPaymentAllocations => Set<PayrollPaymentAllocation>();
    public DbSet<PayrollPaymentEvidence> PayrollPaymentEvidence => Set<PayrollPaymentEvidence>();
    public DbSet<PayrollPaymentReconciliation> PayrollPaymentReconciliations =>
        Set<PayrollPaymentReconciliation>();
    public DbSet<PayrollAccountingPosting> PayrollAccountingPostings => Set<PayrollAccountingPosting>();
    public DbSet<PayrollAccountingComponentMapping> PayrollAccountingComponentMappings =>
        Set<PayrollAccountingComponentMapping>();
    public DbSet<PayrollArtifactVersion> PayrollArtifactVersions => Set<PayrollArtifactVersion>();
    public DbSet<PayrollPayslipDelivery> PayrollPayslipDeliveries => Set<PayrollPayslipDelivery>();
    public DbSet<PayrollEmployeeAdjustmentVersion> PayrollEmployeeAdjustmentVersions =>
        Set<PayrollEmployeeAdjustmentVersion>();
    public DbSet<PayrollEmployeeLoan> PayrollEmployeeLoans => Set<PayrollEmployeeLoan>();
    public DbSet<PayrollLoanScheduleVersion> PayrollLoanScheduleVersions =>
        Set<PayrollLoanScheduleVersion>();
    public DbSet<PayrollLoanScheduleLine> PayrollLoanScheduleLines => Set<PayrollLoanScheduleLine>();
    public DbSet<PayrollLoanRepayment> PayrollLoanRepayments => Set<PayrollLoanRepayment>();
    public DbSet<PayrollLoanRepaymentAllocation> PayrollLoanRepaymentAllocations =>
        Set<PayrollLoanRepaymentAllocation>();
    public DbSet<PayrollFinalSettlement> PayrollFinalSettlements => Set<PayrollFinalSettlement>();
    public DbSet<PayrollFinalSettlementRevision> PayrollFinalSettlementRevisions =>
        Set<PayrollFinalSettlementRevision>();
    public DbSet<PayrollFinalSettlementLine> PayrollFinalSettlementLines =>
        Set<PayrollFinalSettlementLine>();
    public DbSet<PayrollFinalSettlementApprovalSubmission> PayrollFinalSettlementApprovalSubmissions =>
        Set<PayrollFinalSettlementApprovalSubmission>();
    public DbSet<PayrollFinalSettlementAccountingPosting> PayrollFinalSettlementAccountingPostings =>
        Set<PayrollFinalSettlementAccountingPosting>();
    public DbSet<RetailPOS.Core.Entities.HR.Overtime.HrOvertimePolicyVersion> HrOvertimePolicyVersions =>
        Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimePolicyVersion>();
    public DbSet<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry> HrOvertimeEntries =>
        Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry>();
    public DbSet<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeCalculationEvidence> HrOvertimeCalculationEvidence =>
        Set<RetailPOS.Core.Entities.HR.Overtime.HrOvertimeCalculationEvidence>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrExitCase> HrExitCases =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrExitCase>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplate> HrClearanceTemplates =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplate>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplateItem> HrClearanceTemplateItems =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplateItem>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrClearanceItem> HrClearanceItems =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrClearanceItem>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrExitCompletionEvidence> HrExitCompletionEvidence =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrExitCompletionEvidence>();
    public DbSet<RetailPOS.Core.Entities.HR.Exit.HrExitCaseEvent> HrExitCaseEvents =>
        Set<RetailPOS.Core.Entities.HR.Exit.HrExitCaseEvent>();

    // Pricing Engine
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<OutletPriceOverride> OutletPriceOverrides => Set<OutletPriceOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        RetailPOS.Infrastructure.Integration.IntegrationModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.Pos.CashSessionModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.Sales.SaleFinancialPostingModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.AccountingJournals.FrozenSystemJournalModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Approvals.ApprovalKernelModelConfiguration.Configure(
            modelBuilder, Database.ProviderName);
        RetailPOS.Infrastructure.HR.Organisation.HrOrganisationModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Organisation.HrPositionModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Employees.HrEmployeeModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Employees.HrEmployeeHistoryModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Employees.HrOpeningImportModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollSalaryModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollPeriodModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollRunModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollMonetaryConfigurationModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeePaymentDestinationModelConfiguration.Configure(
            modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollRunCalculationModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollPaymentModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollAccountingPostingModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollArtifactModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollCalculationPolicyModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollSalaryModelConfiguration.ConfigureAdjustments(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollLoanModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollFinalSettlementModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Payroll.PayrollFinalSettlementExecutionModelConfiguration.Configure(
            modelBuilder);
        RetailPOS.Infrastructure.Outlets.OutletSalesTargetModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Calendars.HrCalendarModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Attendance.HrRosterModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Attendance.HrDeviceModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Attendance.HrRawPunchModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Attendance.HrDailyAttendanceModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Attendance.HrAttendanceCorrectionModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Leave.HrLeaveModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Leave.HrLeaveRequestModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Overtime.HrOvertimeModelConfiguration.Configure(modelBuilder);
        RetailPOS.Infrastructure.HR.Exit.HrExitModelConfiguration.Configure(modelBuilder);
        UomModelConfiguration.Configure(modelBuilder);

        // Configure PostgreSQL naming convention (snake_case)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names to snake_case
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            // Column names to snake_case
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            // Foreign key names to snake_case
            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var key in entity.GetForeignKeys())
            {
                key.SetConstraintName(ToSnakeCase(key.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }

        UomModelConfiguration.ConfigureConcurrency(modelBuilder);

        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            modelBuilder.Entity<ApprovalInstance>().Property(row => row.XMin).HasColumnName("xmin");

        // Role Configuration
        modelBuilder.Entity<Business>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.SubscriptionPlan).HasMaxLength(100);
            entity.Property(e => e.PoweredByText).HasMaxLength(150);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<BusinessFeatureSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FeatureKey).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.FeatureKey }).IsUnique();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.FeatureSettings)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessReminderNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReminderType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(500).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.ReminderType, e.TargetAt });

            entity.HasOne(e => e.Business)
                .WithMany(b => b.ReminderNotifications)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Role Configuration
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Permissions).HasColumnType("jsonb").IsRequired();
        });

        // User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PermissionOverrides)
                .HasColumnType("jsonb")
                .HasDefaultValue("[]")
                .IsRequired();
            entity.Property(e => e.InventoryLocationAccessScope)
                .HasMaxLength(40)
                .HasDefaultValue(User.InventoryAccessAssignedOnly)
                .IsRequired();
            entity.Property(e => e.ContextSwitchEnabled).HasDefaultValue(false);
            entity.Property(e => e.ContextLocationScope)
                .HasMaxLength(20)
                .HasDefaultValue(User.ContextLocationSelected)
                .IsRequired();
            entity.Property(e => e.ContextRoleScope)
                .HasMaxLength(20)
                .HasDefaultValue(User.ContextRoleMapped)
                .IsRequired();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_users_context_location_scope",
                    "context_location_scope IN ('selected', 'all')");
                table.HasCheckConstraint(
                    "ck_users_context_role_scope",
                    "context_role_scope IN ('mapped', 'all')");
            });

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Users)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Users)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Outlet Configuration
        modelBuilder.Entity<Outlet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Address).IsRequired();
            entity.Property(e => e.ContactNumber).HasMaxLength(20);
            entity.Property(e => e.InvoicePrefix).HasMaxLength(12);
            entity.Property(e => e.TimeZoneId).HasMaxLength(64);
            entity.Property(e => e.InvoicePrintBranchName).HasMaxLength(150);
            entity.Property(e => e.BinNumber).HasMaxLength(50);
            entity.Property(e => e.Mushak).HasMaxLength(50);
            entity.Property(e => e.ReturnExchangePolicy).HasMaxLength(2000);
            entity.Property(e => e.VatLawNotification).HasMaxLength(2000);
            entity.HasIndex(e => new { e.BusinessId, e.InvoicePrefix })
                .IsUnique()
                .HasFilter("invoice_prefix IS NOT NULL");
            entity.Property(e => e.SourceVersion).HasDefaultValue(1L).IsConcurrencyToken();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Outlets)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Manager)
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

            modelBuilder.Entity<PosTerminal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
                entity.Property(e => e.Code).HasMaxLength(40).IsRequired();
                entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
                entity.Property(e => e.IsDefault).HasDefaultValue(false).IsRequired();

                entity.HasIndex(e => new { e.OutletId, e.Code }).IsUnique();
                entity.HasIndex(e => new { e.OutletId, e.IsDefault });

                entity.HasOne(e => e.Outlet)
                .WithMany(o => o.PosTerminals)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Cascade);
            });

        // Warehouse Configuration
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Address).IsRequired();
            entity.Property(e => e.SourceVersion).HasDefaultValue(1L).IsConcurrencyToken();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Warehouses)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Manager)
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Invitation token for onboarding and first-time password setup
        modelBuilder.Entity<UserInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Purpose).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ConsumedAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.Invitations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ReplacedByTokenHash).HasMaxLength(128);
            entity.Property(e => e.RevokedReason).HasMaxLength(200);

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.RevokedAt, e.ExpiresAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserWarehouseAssignment Configuration
        modelBuilder.Entity<UserWarehouseAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique constraint: one warehouse per user
            entity.HasIndex(e => new { e.UserId, e.WarehouseId }).IsUnique();

            // Indexes for efficient querying
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.WarehouseId);
            entity.HasIndex(e => e.BusinessId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.WarehouseAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserContextRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.RoleId }).IsUnique();
            entity.HasIndex(e => e.RoleId);
            entity.HasIndex(e => e.BusinessId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.ContextRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserOutletAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.OutletId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OutletId);
            entity.HasIndex(e => e.BusinessId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.OutletAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Category Configuration
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.Id, e.BusinessId });
            entity.HasIndex(e => e.BusinessId);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);

            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            // A parent category must belong to the same Business.
            entity.HasOne(e => e.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(e => new { e.ParentCategoryId, e.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Product Configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.Id, e.BusinessId });
            entity.HasIndex(e => e.BusinessId);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ProductCode).HasMaxLength(50);
            // Unique partial index per Business: only non-null product codes must be unique.
            entity.HasIndex(e => new { e.BusinessId, e.ProductCode })
                  .IsUnique()
                  .HasFilter("product_code IS NOT NULL");
            entity.Property(e => e.BasePrice).HasPrecision(10, 2);
            entity.Property(e => e.CostPrice).HasPrecision(10, 2);
            entity.Property(e => e.TaxRate).HasPrecision(5, 2);
            entity.Property(e => e.TaxClassification).HasMaxLength(20);
            entity.ToTable(t => t.HasCheckConstraint(
                "ck_products_tax_classification",
                "tax_classification IS NULL OR tax_classification IN ('standard','zero_rated','exempt')"));
            entity.ToTable(t => t.HasCheckConstraint(
                "ck_products_tax_rate_range",
                "tax_rate IS NULL OR (CAST(tax_rate AS NUMERIC) >= 0 AND CAST(tax_rate AS NUMERIC) <= 100)"));
            entity.HasIndex(e => new { e.BusinessId, e.Barcode }).IsUnique();
            entity.Property(e => e.Barcode).HasMaxLength(50);

            // Status is stored as smallint; default 1 (Active).
            entity.Property(e => e.Status)
                  .HasConversion<int>()
                  .HasColumnType("smallint")
                  .HasDefaultValue(ProductStatus.Active);

            // IsActive is a computed C# property — not persisted.
            entity.Ignore(e => e.IsActive);

            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            // The category must belong to the same Business as the product.
            entity.HasOne(e => e.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(e => new { e.CategoryId, e.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ProductVariant Configuration
        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Sku }).IsUnique();
            entity.Property(e => e.Sku).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.Barcode }).IsUnique();
            entity.Property(e => e.Barcode).HasMaxLength(50);
            entity.Property(e => e.Attributes).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.PriceAdjustment).HasPrecision(10, 2);
            entity.Property(e => e.CostAdjustment).HasPrecision(10, 2);

            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            // A variant must belong to the same Business as its product.
            entity.HasOne(e => e.Product)
                .WithMany(p => p.ProductVariants)
                .HasForeignKey(e => new { e.ProductId, e.BusinessId })
                .HasPrincipalKey(p => new { p.Id, p.BusinessId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Variation Configuration
        modelBuilder.Entity<Variation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AutoSelectAllOptions)
                .HasColumnName("default_select_all_options")
                .HasDefaultValue(false);
        });

        // VariationOption Configuration
        modelBuilder.Entity<VariationOption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PriceAdjustment).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Variation)
                .WithMany(v => v.Options)
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariation Configuration
        modelBuilder.Entity<ProductVariation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Product)
                .WithMany(p => p.ProductVariations)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variation)
                .WithMany(v => v.ProductVariations)
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariantOption Configuration
        modelBuilder.Entity<ProductVariantOption>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.ProductVariantOptions)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Option)
                .WithMany(o => o.ProductVariantOptions)
                .HasForeignKey(e => e.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariationSelectedOption Configuration
        // Stores which specific options are selected for each variation type on a product.
        modelBuilder.Entity<ProductVariationSelectedOption>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Each (product, variation, option) triple must be unique.
            entity.HasIndex(e => new { e.ProductId, e.VariationId, e.OptionId }).IsUnique();

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variation)
                .WithMany()
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Option)
                .WithMany()
                .HasForeignKey(e => e.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductImage Configuration
        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalName).HasMaxLength(260).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.OriginalPath).HasMaxLength(300).IsRequired();
            entity.Property(e => e.MediumPath).HasMaxLength(300).IsRequired();
            entity.Property(e => e.ThumbPath).HasMaxLength(300).IsRequired();

            entity.HasIndex(e => new { e.ProductId, e.SortOrder });
            // exactly one primary per product — partial unique index on Postgres
            entity.HasIndex(e => e.ProductId)
                .IsUnique()
                .HasFilter("is_primary = true")
                .HasDatabaseName("ux_product_images_one_primary_per_product");

            entity.HasOne(e => e.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodeTemplate Configuration
        modelBuilder.Entity<BarcodeTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.TemplateType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.PaperType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LabelWidthMm).HasPrecision(8, 2);
            entity.Property(e => e.LabelHeightMm).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IsDefault })
                .HasFilter("is_default = true")
                .HasDatabaseName("ux_barcode_templates_one_default_per_business");

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodeTemplateField Configuration
        modelBuilder.Entity<BarcodeTemplateField>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FieldKey).HasMaxLength(60).IsRequired();
            entity.Property(e => e.FontWeight).HasMaxLength(20);
            entity.Property(e => e.Align).HasMaxLength(20);
            entity.Property(e => e.X).HasPrecision(8, 2);
            entity.Property(e => e.Y).HasPrecision(8, 2);
            entity.Property(e => e.Width).HasPrecision(8, 2);
            entity.Property(e => e.Height).HasPrecision(8, 2);
            entity.Property(e => e.FontSize).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.TemplateId, e.FieldKey }).IsUnique();
            entity.HasIndex(e => new { e.TemplateId, e.SortOrder });

            entity.HasOne(e => e.Template)
                .WithMany(t => t.Fields)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodePrintHistory Configuration
        modelBuilder.Entity<BarcodePrintHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PrintMode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SourceModule).HasMaxLength(60);
            entity.Property(e => e.SourceReferenceType).HasMaxLength(60);
            entity.Property(e => e.LabelWidthMm).HasPrecision(8, 2);
            entity.Property(e => e.LabelHeightMm).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.BusinessId, e.PrintedAt });
            entity.HasIndex(e => new { e.TemplateId, e.PrintedAt });
            entity.HasIndex(e => new { e.PrintedByUserId, e.PrintedAt });
            entity.HasIndex(e => new { e.SourceReferenceType, e.SourceReferenceId });

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PrintedByUser)
                .WithMany()
                .HasForeignKey(e => e.PrintedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // BarcodePrintHistoryItem Configuration
        modelBuilder.Entity<BarcodePrintHistoryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.VariantName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.VariantSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.BarcodeValue).HasMaxLength(100);
            entity.Property(e => e.SellingPrice).HasPrecision(10, 2);

            entity.HasIndex(e => e.PrintHistoryId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.PrintHistory)
                .WithMany(h => h.Items)
                .HasForeignKey(e => e.PrintHistoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Inventory Configuration
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.BatchNumber).HasMaxLength(50);

            // Optimistic concurrency token — maps to PostgreSQL's built-in xmin system column.
            // No DDL column is required; xmin is always present on every PostgreSQL row.
            // ValueGeneratedOnAddOrUpdate tells EF Core NOT to include xmin in INSERT/UPDATE
            // column lists (it's a system column — writing it directly causes a PostgreSQL error).
            // EF Core reads the new value back via RETURNING xmin after each write, then
            // includes it in the WHERE clause of the next UPDATE to detect concurrent changes.
            entity.Property(e => e.XMin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType }).IsUnique();

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.Inventories)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Supplier Configuration
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Contact).HasMaxLength(255);
            entity.Property(e => e.CreditLimit).HasPrecision(10, 2);
            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique().HasFilter("business_id is not null");
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        // PurchaseOrder Configuration
        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable(table => table.HasCheckConstraint(
                "ck_purchase_orders_single_destination",
                "(warehouse_id IS NOT NULL AND outlet_id IS NULL) OR (warehouse_id IS NULL AND outlet_id IS NOT NULL)"));
            entity.Ignore(e => e.LocationType);
            entity.Ignore(e => e.LocationId);
            entity.Ignore(e => e.LocationName);
            entity.Ignore(e => e.LocationBusinessId);
            entity.Property(e => e.PoNumber).HasMaxLength(30).IsRequired();
            entity.HasIndex(e => e.PoNumber).IsUnique();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(64);
            entity.HasIndex(e => new { e.WarehouseId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
            entity.HasIndex(e => new { e.OutletId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL AND outlet_id IS NOT NULL");
            entity.Property(e => e.TotalAmount).HasPrecision(10, 2);
            // status values: draft | pending | sent_back | approved | partially_received | fully_received | completed | cancelled | rejected
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);

            entity.HasOne(e => e.Supplier)
                .WithMany(s => s.PurchaseOrders)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Warehouse)
                .WithMany(w => w.PurchaseOrders)
                .HasForeignKey(e => e.WarehouseId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.PurchaseOrders)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // PurchaseOrderItem Configuration
        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasColumnType("numeric");
            entity.Property(e => e.Discount).HasPrecision(5, 2);
            entity.Property(e => e.Tax).HasPrecision(5, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.Unit).HasMaxLength(30);

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Items)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.PurchaseOrderItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Grn Configuration
        modelBuilder.Entity<Grn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);    // NEW

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Grns)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.Grns)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // GrnItem Configuration
        modelBuilder.Entity<GrnItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitCost).HasColumnType("numeric");
            entity.Property(e => e.TaxableAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(500);        // NEW

            entity.HasOne(e => e.Grn)
                .WithMany(g => g.Items)
                .HasForeignKey(e => e.GrnId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PurchaseOrderItem)
                .WithMany(poi => poi.GrnItems)
                .HasForeignKey(e => e.PoItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Customer Configuration
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.Id, e.BusinessId });
            entity.Property(e => e.CustomerCode).HasMaxLength(30);
            // Codes (including the protected WALKIN) are unique per Business, never globally.
            entity.HasIndex(e => new { e.BusinessId, e.CustomerCode })
                .IsUnique()
                .HasFilter("customer_code IS NOT NULL");
            entity.Property(e => e.IsSystem).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.PhoneNormalized).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(255);

            // Normalized phone is unique within a Business for registered (non-system) customers.
            entity.HasIndex(e => new { e.BusinessId, e.PhoneNormalized })
                .IsUnique()
                .HasFilter("phone_normalized IS NOT NULL");

            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BusinessPosSettings>(entity =>
        {
            entity.ToTable("business_pos_settings");
            entity.HasKey(e => e.BusinessId);
            entity.Property(e => e.DefaultTimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TaxName).HasMaxLength(30).IsRequired();
            entity.Property(e => e.DefaultTaxRate).HasPrecision(5, 2);
            entity.Property(e => e.MaxManualDiscountPercent).HasPrecision(5, 2);
            entity.Property(e => e.Version).HasDefaultValue(1L).IsConcurrencyToken();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_business_pos_settings_duration", "max_cash_session_duration_hours BETWEEN 1 AND 20");
                t.HasCheckConstraint("ck_business_pos_settings_tax_rate", "CAST(default_tax_rate AS NUMERIC) BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_business_pos_settings_discount", "CAST(max_manual_discount_percent AS NUMERIC) BETWEEN 0 AND 100");
            });
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SaleInvoiceCounter>(entity =>
        {
            entity.ToTable("sale_invoice_counters");
            entity.HasKey(e => new { e.BusinessId, e.OutletId, e.BusinessDate });
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Outlet>().WithMany().HasForeignKey(e => e.OutletId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OpeningCashAmount).HasPrecision(18, 2);
            entity.Property(e => e.ExpectedClosingCashAmount).HasPrecision(18, 2);
            entity.Property(e => e.ActualClosingCashAmount).HasPrecision(18, 2);
            entity.Property(e => e.DifferenceAmount).HasPrecision(18, 2);
            entity.Property(e => e.OpeningNotes).HasMaxLength(1000);
            entity.Property(e => e.ClosingNotes).HasMaxLength(1000);
            entity.Property(e => e.VarianceReason).HasMaxLength(1000);
            entity.Property(e => e.TimeZoneIdSnapshot).HasMaxLength(64).IsRequired().HasDefaultValue("UTC");
            entity.Property(e => e.DurationSnapshotHours).HasDefaultValue(20);
            entity.Property(e => e.OpenIdempotencyKey).HasMaxLength(64);
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.Ignore(e => e.CutoffAtUtc);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_sessions_status", "status IN ('open','closed')");
                t.HasCheckConstraint("ck_cash_sessions_duration", "duration_snapshot_hours BETWEEN 1 AND 20");
                t.HasCheckConstraint("ck_cash_sessions_opening_cash", "opening_cash_amount >= 0");
            });
            entity.HasIndex(e => new { e.OutletId, e.OpenIdempotencyKey })
                .IsUnique()
                .HasFilter("open_idempotency_key IS NOT NULL")
                .HasDatabaseName("ux_cash_sessions_open_idempotency");
            entity.HasIndex(e => new { e.OutletId, e.TerminalId, e.Status });
            entity.HasIndex(e => new { e.OutletId, e.TerminalId })
                .IsUnique()
                .HasFilter("status = 'open'")
                .HasDatabaseName("ux_cash_sessions_one_open_per_terminal");
            entity.HasIndex(e => new { e.OutletId, e.BusinessDate });
            entity.HasIndex(e => e.OpenedAt);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.CashSessions)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Terminal)
                .WithMany(t => t.CashSessions)
                .HasForeignKey(e => e.TerminalId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.OpenedByUser)
                .WithMany(u => u.OpenedCashSessions)
                .HasForeignKey(e => e.OpenedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ClosedByUser)
                .WithMany(u => u.ClosedCashSessions)
                .HasForeignKey(e => e.ClosedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Sale Configuration
        modelBuilder.Entity<Sale>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SaleNumber).HasMaxLength(30).IsRequired();    // UPDATED
            entity.Property(e => e.TotalAmount).HasPrecision(10, 2);
            entity.Property(e => e.Discount).HasPrecision(10, 2);
            entity.Property(e => e.Tax).HasPrecision(10, 2);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(64);             // UPDATED
            entity.Property(e => e.IdempotencyFingerprint).HasMaxLength(64);
            entity.Property(e => e.InvoicePrefix).HasMaxLength(12);

            // D13: final invoice numbers are unique per Business; the structured key is unique per day and outlet.
            entity.HasIndex(e => new { e.BusinessId, e.SaleNumber })
                .IsUnique()
                .HasFilter("business_id IS NOT NULL AND sale_number <> ''");
            entity.HasIndex(e => new { e.BusinessId, e.OutletId, e.BusinessDate, e.InvoiceSequence })
                .IsUnique()
                .HasFilter("invoice_sequence IS NOT NULL");
            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.BusinessDate).HasColumnType("date");

            entity.HasIndex(e => e.SaleDate);
            entity.HasIndex(e => e.BusinessDate);
            entity.HasIndex(e => e.OutletId);
            entity.HasIndex(e => e.TerminalId);
            entity.HasIndex(e => e.CashSessionId);
            // Idempotency: unique per outlet to prevent double-submission
            entity.HasIndex(e => new { e.OutletId, e.IdempotencyKey })
                  .IsUnique()
                  .HasFilter("idempotency_key IS NOT NULL");                      // UPDATED

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Sales)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Terminal)
                .WithMany(t => t.Sales)
                .HasForeignKey(e => e.TerminalId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CashSession)
                .WithMany(cs => cs.Sales)
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Sales)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            // Sale has two User navigations (cashier, salesperson), so convention discovery no longer creates the cashier
            // relationship before the snake_case pass above; the existing database names are therefore pinned explicitly.
            entity.HasOne(e => e.Cashier)
                .WithMany(u => u.Sales)
                .HasForeignKey(e => e.CashierId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("f_k_sales__users_cashier_id");
            entity.HasIndex(e => e.CashierId).HasDatabaseName("i_x_sales_cashier_id");

            entity.HasOne(e => e.Salesperson)
                .WithMany()
                .HasForeignKey(e => e.SalespersonUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("f_k_sales__users_salesperson_user_id");
            entity.HasIndex(e => e.SalespersonUserId).HasDatabaseName("i_x_sales_salesperson_user_id");
        });

        // SaleItem Configuration
        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasColumnType("numeric");
            entity.Property(e => e.Subtotal).HasPrecision(10, 2);
            entity.Property(e => e.HistoricalUnitCost).HasColumnType("numeric");
            entity.Property(e => e.PackagingCost).HasPrecision(18, 4);
            entity.Property(e => e.PackState).HasMaxLength(20);
            entity.Property(e => e.PackCode).HasMaxLength(40);
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.Property(e => e.DiscountAmount).HasPrecision(10, 2);
            entity.Property(e => e.TaxableAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.TaxClassification).HasMaxLength(20);
            entity.Property(e => e.AppliedRuleName).HasMaxLength(200);           // UPDATED

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.SaleItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // NEW — SalePayment Configuration
        modelBuilder.Entity<SalePayment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Method).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Tendered).HasPrecision(10, 2);
            entity.Property(e => e.ChangeGiven).HasPrecision(10, 2);
            entity.Property(e => e.NetCashRetained).HasPrecision(10, 2);
            entity.HasIndex(e => e.PaymentAccountId);

            entity.HasIndex(e => e.SaleId);

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.Payments)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.AccountReference).HasMaxLength(120);
            entity.Property(e => e.OpeningStatementBalance).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.ChartAccountId }).IsUnique();
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChartAccount).WithMany().HasForeignKey(e => e.ChartAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Outlet).WithMany().HasForeignKey(e => e.OutletId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SettlementBatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SettlementType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ExternalReference).HasMaxLength(120);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.GrossClearingAmount).HasPrecision(18, 2);
            entity.Property(e => e.SettlementFeeAmount).HasPrecision(18, 2);
            entity.Property(e => e.NetBankAmount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.ExternalReference }).IsUnique().HasFilter("external_reference IS NOT NULL");
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ClearingAccount).WithMany().HasForeignKey(e => e.ClearingAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JournalEntry).WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReversalJournalEntry).WithMany().HasForeignKey(e => e.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LiquidityTransfer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransferType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Reference).HasMaxLength(120);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique();
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SourceAccount).WithMany().HasForeignKey(e => e.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.DestinationAccount).WithMany().HasForeignKey(e => e.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceBankAccount).WithMany().HasForeignKey(e => e.SourceBankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.DestinationBankAccount).WithMany().HasForeignKey(e => e.DestinationBankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JournalEntry).WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReversalJournalEntry).WithMany().HasForeignKey(e => e.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementImport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceFileName).HasMaxLength(260).IsRequired();
            entity.Property(e => e.SourceFileChecksum).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceFormat).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ErrorSummary).HasMaxLength(2000);
            entity.Property(e => e.OpeningBalance).HasPrecision(18, 2);
            entity.Property(e => e.ClosingBalance).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.BankAccountId, e.SourceFileChecksum }).IsUnique();
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalTransactionReference).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.DebitAmount).HasPrecision(18, 2);
            entity.Property(e => e.CreditAmount).HasPrecision(18, 2);
            entity.Property(e => e.SignedAmount).HasPrecision(18, 2);
            entity.Property(e => e.RunningBalance).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.Property(e => e.NormalizedSearchText).HasMaxLength(2200);
            entity.Property(e => e.RawReferenceText).HasMaxLength(2000);
            entity.HasIndex(e => new { e.BusinessId, e.BankAccountId, e.TransactionDate });
            entity.HasIndex(e => new { e.BankStatementImportId, e.SourceRowNumber }).IsUnique();
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Import).WithMany(x => x.Lines).HasForeignKey(e => e.BankStatementImportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BankReconciliationSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.StatementOpeningBalance).HasPrecision(18, 2);
            entity.Property(e => e.StatementClosingBalance).HasPrecision(18, 2);
            entity.Property(e => e.GlOpeningBalance).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.BankAccountId, e.PeriodStart, e.PeriodEnd }).IsUnique().HasFilter("status IN ('open','in_progress','ready_for_close')");
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankReconciliationMatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.StatementTotal).HasPrecision(18, 2);
            entity.Property(e => e.GlTotal).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.BankAccountId, e.Status });
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Session).WithMany(x => x.Matches).HasForeignKey(e => e.BankReconciliationSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BankReconciliationMatchLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SignedAmount).HasPrecision(18, 2);
            entity.HasIndex(e => e.BankStatementLineId).IsUnique().HasFilter("bank_statement_line_id IS NOT NULL AND is_active = true");
            entity.HasIndex(e => e.JournalEntryLineId).IsUnique().HasFilter("journal_entry_line_id IS NOT NULL AND is_active = true");
            entity.HasOne(e => e.Match).WithMany(x => x.Lines).HasForeignKey(e => e.BankReconciliationMatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.StatementLine).WithMany().HasForeignKey(e => e.BankStatementLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JournalEntryLine).WithMany().HasForeignKey(e => e.JournalEntryLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementAdjustment>(entity =>
        {
            entity.HasKey(e => e.Id); entity.Property(e => e.AdjustmentType).HasMaxLength(30).IsRequired(); entity.Property(e => e.Status).HasMaxLength(20).IsRequired(); entity.Property(e => e.Amount).HasPrecision(18, 2); entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired(); entity.Property(e => e.Reason).HasMaxLength(500); entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique(); entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade); entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(e => e.StatementLine).WithMany().HasForeignKey(e => e.BankStatementLineId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(e => e.JournalEntry).WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(e => e.ReversalJournalEntry).WithMany().HasForeignKey(e => e.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementException>(entity =>
        {
            entity.HasKey(e => e.Id); entity.Property(e => e.Classification).HasMaxLength(30).IsRequired(); entity.Property(e => e.Status).HasMaxLength(20).IsRequired(); entity.Property(e => e.Reason).HasMaxLength(500); entity.Property(e => e.ResolutionNotes).HasMaxLength(1000); entity.HasIndex(e => new { e.BusinessId, e.BankAccountId, e.Status }); entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade); entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(e => e.StatementLine).WithMany().HasForeignKey(e => e.BankStatementLineId).OnDelete(DeleteBehavior.Restrict);
        });

        // NEW — HeldSale Configuration
        modelBuilder.Entity<HeldSale>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ItemsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.DiscountPercent).HasPrecision(5, 2);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Note).HasMaxLength(300);

            entity.HasIndex(e => e.OutletId);
            entity.HasIndex(e => e.CashierId);
            entity.HasIndex(e => e.HeldAt);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Cashier)
                .WithMany()
                .HasForeignKey(e => e.CashierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReceiptPrintHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).HasMaxLength(20).IsRequired();

            entity.HasIndex(e => e.SaleId);
            entity.HasIndex(e => e.PrintedAt);

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.ReceiptPrintHistories)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Terminal)
                .WithMany(t => t.ReceiptPrintHistories)
                .HasForeignKey(e => e.TerminalId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PrintedByUser)
                .WithMany(u => u.ReceiptPrintHistories)
                .HasForeignKey(e => e.PrintedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SaleVoid>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PreviousStatus).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();

            entity.HasIndex(e => e.SaleId).IsUnique();
            entity.HasIndex(e => e.VoidedAt);

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.SaleVoids)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VoidedByUser)
                .WithMany(u => u.SaleVoids)
                .HasForeignKey(e => e.VoidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // StockTransfer Configuration
        modelBuilder.Entity<StockTransfer>(entity =>
        {
            // One system return per transfer, whatever the application does.
            entity.HasIndex(e => e.OriginalTransferId)
                .IsUnique()
                .HasFilter("original_transfer_id IS NOT NULL");
            entity.HasOne(e => e.OriginalTransfer)
                .WithMany()
                .HasForeignKey(e => e.OriginalTransferId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransferNo).HasMaxLength(40);
            entity.Property(e => e.TransferType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FromLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ToLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasIndex(e => e.TransferNo)
                .IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TransferDate);
            entity.HasIndex(e => new { e.FromLocationType, e.FromLocationId });
            entity.HasIndex(e => new { e.ToLocationType, e.ToLocationId });
            entity.HasIndex(e => e.RelatedRequisitionId);

            entity.HasOne(e => e.Approver)
                .WithMany(u => u.StockTransfersApproved)
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Dispatcher)
                .WithMany()
                .HasForeignKey(e => e.DispatchedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Receiver)
                .WithMany()
                .HasForeignKey(e => e.ReceivedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Canceller)
                .WithMany()
                .HasForeignKey(e => e.CancelledBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Updater)
                .WithMany()
                .HasForeignKey(e => e.UpdatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.StockTransfersCreated)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RelatedRequisition)
                .WithMany(r => r.Transfers)
                .HasForeignKey(e => e.RelatedRequisitionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // StockTransferItem Configuration
        modelBuilder.Entity<StockTransferItem>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "ck_stock_transfer_items_discrepancy_not_negative",
                "missing_quantity >= 0 AND damaged_quantity >= 0"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitCost).HasPrecision(18, 4);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasOne(e => e.Transfer)
                .WithMany(t => t.Items)
                .HasForeignKey(e => e.TransferId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.StockTransferItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockRequisition Configuration
        modelBuilder.Entity<StockRequisition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequisitionNo).HasMaxLength(40).IsRequired();
            entity.Property(e => e.RequestingLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SourceLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);

            entity.HasIndex(e => e.RequisitionNo)
                .IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RequestDate);
            entity.HasIndex(e => new { e.RequestingLocationType, e.RequestingLocationId });
            entity.HasIndex(e => new { e.SourceLocationType, e.SourceLocationId });

            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequestedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Approver)
                .WithMany()
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Closer)
                .WithMany()
                .HasForeignKey(e => e.ClosedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Updater)
                .WithMany()
                .HasForeignKey(e => e.UpdatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StockRequisitionLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasIndex(e => e.RequisitionId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.Requisition)
                .WithMany(r => r.Lines)
                .HasForeignKey(e => e.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockAdjustment Configuration
        modelBuilder.Entity<StockAdjustment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AdjustmentNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);

            entity.HasIndex(e => e.AdjustmentNumber)
                .IsUnique();

            entity.HasIndex(e => e.Status);

            entity.HasIndex(e => e.SourceStockCountId)
                .IsUnique()
                .HasFilter("source_stock_count_id is not null");

            entity.HasOne(e => e.Adjuster)
                .WithMany(u => u.StockAdjustments)
                .HasForeignKey(e => e.AdjustedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Approver)
                .WithMany(u => u.StockAdjustmentsApproved)
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany(u => u.StockAdjustmentsRejected)
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Canceller)
                .WithMany(u => u.StockAdjustmentsCancelled)
                .HasForeignKey(e => e.CancelledBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Lines)
                .WithOne(l => l.StockAdjustment)
                .HasForeignKey(l => l.StockAdjustmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockAdjustmentLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.HasIndex(e => e.StockAdjustmentId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.StockAdjustmentLines)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockCount Configuration
        modelBuilder.Entity<StockCount>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.StockCountNo).HasMaxLength(40).IsRequired();
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            entity.Property(e => e.Remarks).HasMaxLength(1000);
            entity.Property(e => e.CountRevision).HasDefaultValue(0L);
            entity.Property(e => e.BaselineConsistencyStatus)
                .HasMaxLength(40)
                .HasDefaultValue("Unverified")
                .IsRequired();
            entity.Property(e => e.MovementReconciliationStatus)
                .HasMaxLength(40)
                .HasDefaultValue(StockCount.MovementReconciliationNotStarted)
                .IsRequired();

            entity.HasIndex(e => e.StockCountNo).IsUnique();
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.LocationId);
            entity.HasIndex(e => e.StockCountDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId, e.CreatedAt });

            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId })
                .IsUnique()
                .HasFilter("status in ('Draft','Submitted')");

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Approver)
                .WithMany()
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Lines)
                .WithOne(l => l.StockCount)
                .HasForeignKey(l => l.StockCountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockCountLine>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProductName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ProductCode).HasMaxLength(100).IsRequired();
            entity.Property(e => e.VariantName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CurrentStock).HasPrecision(18, 3).IsRequired();
            entity.Property(e => e.PhysicalStock).HasPrecision(18, 3);
            entity.Property(e => e.Difference).HasPrecision(18, 3);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasIndex(e => e.StockCountId);
            entity.HasIndex(e => e.VariantId);
            entity.HasIndex(e => new { e.StockCountId, e.VariantId }).IsUnique();

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCountOperationReceipt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceSessionId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.OperationType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.CountedQuantity).HasPrecision(28, 8);
            entity.Property(e => e.RawBarcode).HasMaxLength(50);
            entity.Property(e => e.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Outcome).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RejectionCode).HasMaxLength(80);
            entity.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();

            entity.HasIndex(e => new { e.StockCountId, e.ClientOperationId }).IsUnique();
            entity.HasIndex(e => new { e.StockCountId, e.DeviceSessionId, e.ClientSequence }).IsUnique();
            entity.HasIndex(e => new { e.StockCountId, e.AcceptedRevision });

            entity.HasOne(e => e.StockCount)
                .WithMany()
                .HasForeignKey(e => e.StockCountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockCountSubmissionJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActorRoleName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FailureCode).HasMaxLength(80);
            entity.Property(e => e.RequestCorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_stock_count_submission_jobs_status",
                    "status IN ('Accepted','Processing','RetryScheduled','ErpSubmitted','Failed')");
                table.HasCheckConstraint(
                    "ck_stock_count_submission_jobs_revision",
                    "accepted_count_revision > 0 AND attempt_count >= 0");
            });

            entity.HasIndex(e => new { e.StockCountId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => e.StockCountId)
                .IsUnique()
                .HasFilter("status IN ('Accepted','Processing','RetryScheduled')")
                .HasDatabaseName("ux_stock_count_submission_jobs_active_session");
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc, e.LeaseExpiresAtUtc });
            entity.HasIndex(e => e.OutboxMessageId).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.OutletId, e.CreatedAtUtc });

            entity.HasOne(e => e.StockCount)
                .WithMany()
                .HasForeignKey(e => e.StockCountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCountAdjustmentJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ApproverRoleName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FailureCode).HasMaxLength(80);
            entity.Property(e => e.EvidenceStatus).HasMaxLength(30);
            entity.Property(e => e.BlockedReasonCodes).HasMaxLength(500);
            entity.Property(e => e.RequestCorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_stock_count_adjustment_jobs_status",
                    "status IN ('Pending','Processing','RetryScheduled','Completed','Blocked','Failed')");
                table.HasCheckConstraint(
                    "ck_stock_count_adjustment_jobs_revision",
                    "approved_count_revision >= 0 AND attempt_count >= 0");
            });

            // Exactly one job (and therefore one effective adjustment) per approved StockCount revision.
            entity.HasIndex(e => new { e.StockCountId, e.ApprovedCountRevision })
                .IsUnique()
                .HasDatabaseName("ux_stock_count_adjustment_jobs_count_revision");
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc, e.LeaseExpiresAtUtc });
            entity.HasIndex(e => e.OutboxMessageId).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.CreatedAtUtc });

            entity.HasOne(e => e.StockCount)
                .WithMany()
                .HasForeignKey(e => e.StockCountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Account Configuration
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Balance).HasPrecision(10, 2);

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Accounts)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChartAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AccountCode).HasMaxLength(30).IsRequired();
            entity.Property(e => e.AccountName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.AccountType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.NormalBalanceType).HasMaxLength(20).IsRequired();

            entity.HasIndex(e => new { e.BusinessId, e.AccountCode }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.AccountType, e.IsActive });
            entity.HasIndex(e => new { e.BusinessId, e.ParentAccountId });

            entity.HasOne(e => e.Business)
                .WithMany(b => b.ChartAccounts)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentAccount)
                .WithMany(e => e.ChildAccounts)
                .HasForeignKey(e => e.ParentAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.BusinessId).IsUnique();
            entity.Property(e => e.JournalNumberPrefix).HasMaxLength(20).IsRequired();
            entity.Property(e => e.JournalSequenceResetPolicy).HasMaxLength(40).IsRequired();
            entity.Property(e => e.DefaultSupplierPaymentTermsDays).HasDefaultValue(30);

            entity.HasOne(e => e.Business)
                .WithMany(b => b.AccountingConfigurations)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CashAccount).WithMany().HasForeignKey(e => e.CashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PettyCashAccount).WithMany().HasForeignKey(e => e.PettyCashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.DefaultBankAccount).WithMany().HasForeignKey(e => e.DefaultBankAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.InventoryAccount).WithMany().HasForeignKey(e => e.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SalesAccount).WithMany().HasForeignKey(e => e.SalesAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SalesDiscountAccount).WithMany().HasForeignKey(e => e.SalesDiscountAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SalesReturnAccount).WithMany().HasForeignKey(e => e.SalesReturnAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CogsAccount).WithMany().HasForeignKey(e => e.CogsAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.InventoryShortageAccount).WithMany().HasForeignKey(e => e.InventoryShortageAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.InventoryGainAccount).WithMany().HasForeignKey(e => e.InventoryGainAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupplierPayableAccount).WithMany().HasForeignKey(e => e.SupplierPayableAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupplierAdvanceAccount).WithMany().HasForeignKey(e => e.SupplierAdvanceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CustomerAdvanceAccount).WithMany().HasForeignKey(e => e.CustomerAdvanceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OtherIncomeAccount).WithMany().HasForeignKey(e => e.OtherIncomeAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OwnerCapitalAccount).WithMany().HasForeignKey(e => e.OwnerCapitalAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OwnerDrawingsAccount).WithMany().HasForeignKey(e => e.OwnerDrawingsAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RetainedEarningsAccount).WithMany().HasForeignKey(e => e.RetainedEarningsAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RoundingDifferenceAccount).WithMany().HasForeignKey(e => e.RoundingDifferenceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashDifferenceGainAccount).WithMany().HasForeignKey(e => e.CashDifferenceGainAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CashDifferenceLossAccount).WithMany().HasForeignKey(e => e.CashDifferenceLossAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.VatPayableAccount).WithMany().HasForeignKey(e => e.VatPayableAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.VatReceivableAccount).WithMany().HasForeignKey(e => e.VatReceivableAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CardClearingAccount).WithMany().HasForeignKey(e => e.CardClearingAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.MobileMfsClearingAccount).WithMany().HasForeignKey(e => e.MobileMfsClearingAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SupplierPayableDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceDocumentType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.SourceReference).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SupplierInvoiceNumber).HasMaxLength(80);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.DisputeReason).HasMaxLength(500);
            entity.Property(e => e.OriginalPayableAmount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.SourceJournalEntryId }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.SupplierId, e.DueDate });
            entity.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SourceJournalEntry).WithMany().HasForeignKey(e => e.SourceJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SupplierAdvanceUtilization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasIndex(e => new { e.BusinessId, e.SupplierAdvanceId, e.SupplierPayableDocumentId, e.JournalEntryId }).IsUnique();
            entity.HasOne(e => e.SupplierAdvance).WithMany().HasForeignKey(e => e.SupplierAdvanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SupplierPayableDocument).WithMany(e => e.AdvanceUtilizations).HasForeignKey(e => e.SupplierPayableDocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JournalEntry).WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingNumberSequence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SequenceType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ScopeKey).HasMaxLength(60).IsRequired();
            entity.Property(e => e.Prefix).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ResetPolicy).HasMaxLength(40).IsRequired();

            entity.HasIndex(e => new { e.BusinessId, e.SequenceType, e.ScopeKey }).IsUnique();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.AccountingNumberSequences)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FinancialYear)
                .WithMany(y => y.NumberSequences)
                .HasForeignKey(e => e.FinancialYearId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingOutletConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.BusinessId, e.OutletId }).IsUnique();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.AccountingOutletConfigurations)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.OutletCashAccount).WithMany().HasForeignKey(e => e.OutletCashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OutletPettyCashAccount).WithMany().HasForeignKey(e => e.OutletPettyCashAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OutletBankAccount).WithMany().HasForeignKey(e => e.OutletBankAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        // Transaction Configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Type).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.ReferenceType).HasMaxLength(50);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FinancialYear>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.YearCode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.YearCode }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IsCurrent });
            entity.HasIndex(e => e.BusinessId)
                .IsUnique()
                .HasFilter("is_current = true")
                .HasDatabaseName("ux_financial_years_business_current_true");

            entity.HasOne(e => e.Business)
                .WithMany(b => b.FinancialYears)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountingPeriod>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.StatusReason).HasMaxLength(500);

            entity.HasIndex(e => new { e.FinancialYearId, e.PeriodNumber }).IsUnique();
            entity.HasIndex(e => new { e.FinancialYearId, e.Status });

            entity.HasOne(e => e.FinancialYear)
                .WithMany(y => y.Periods)
                .HasForeignKey(e => e.FinancialYearId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountingPeriodCloseRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ExecutionReason).HasMaxLength(500);
            entity.Property(e => e.ReopenReason).HasMaxLength(500);
            entity.HasIndex(e => new { e.BusinessId, e.AccountingPeriodId, e.RunNumber }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.AccountingPeriodId, e.Status });
            entity.HasIndex(e => e.AccountingPeriodId).IsUnique()
                .HasFilter("status IN ('prepared', 'approved', 'executed')")
                .HasDatabaseName("ux_accounting_period_close_runs_period_active");
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AccountingPeriod).WithMany().HasForeignKey(e => e.AccountingPeriodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingCloseGateSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GateCode).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ExpectedAmount).HasPrecision(18, 2);
            entity.Property(e => e.ActualAmount).HasPrecision(18, 2);
            entity.Property(e => e.Details).HasMaxLength(1000);
            entity.HasIndex(e => new { e.AccountingPeriodCloseRunId, e.EvaluationSequence, e.GateCode }).IsUnique();
            entity.HasOne(e => e.CloseRun).WithMany(e => e.GateSnapshots).HasForeignKey(e => e.AccountingPeriodCloseRunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountingCloseExceptionApproval>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(e => e.AccountingCloseGateSnapshotId).IsUnique();
            entity.HasOne(e => e.CloseRun).WithMany(e => e.ExceptionApprovals).HasForeignKey(e => e.AccountingPeriodCloseRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GateSnapshot).WithMany(e => e.ExceptionApprovals).HasForeignKey(e => e.AccountingCloseGateSnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingFinancialYearCloseRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReopenReason).HasMaxLength(500);
            entity.HasIndex(e => new { e.BusinessId, e.FinancialYearId, e.RunNumber }).IsUnique();
            entity.HasIndex(e => e.FinancialYearId).IsUnique().HasFilter("status IN ('prepared', 'approved', 'executed')");
            entity.HasIndex(e => e.YearEndJournalEntryId).IsUnique().HasFilter("year_end_journal_entry_id IS NOT NULL");
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FinancialYear).WithMany().HasForeignKey(e => e.FinancialYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FinalAccountingPeriod).WithMany().HasForeignKey(e => e.FinalAccountingPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FinalPeriodCloseRun).WithMany().HasForeignKey(e => e.FinalPeriodCloseRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.YearEndJournalEntry).WithMany().HasForeignKey(e => e.YearEndJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReversalJournalEntry).WithMany().HasForeignKey(e => e.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JournalNumber).HasMaxLength(30);
            entity.Property(e => e.JournalType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ReferenceType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ReferenceId).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ReferenceNumber).HasMaxLength(80);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.BusinessDate).HasColumnType("date");

            entity.HasIndex(e => new { e.BusinessId, e.JournalNumber }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.JournalType, e.ReferenceType, e.ReferenceId })
                .IsUnique()
                .HasFilter("is_system_generated = true AND status <> 'Reversed'");
            entity.HasIndex(e => new { e.BusinessId, e.PostingDate });
            entity.HasIndex(e => new { e.BusinessId, e.OutletId, e.PostingDate });
            entity.HasIndex(e => new { e.Status, e.PostingDate });
            entity.HasIndex(e => e.ReversalOfJournalEntryId)
                .HasDatabaseName("i_x_journal_entries_reversal_of_journal_entry_id");
            entity.HasIndex(e => e.ReversedByJournalEntryId)
                .HasDatabaseName("i_x_journal_entries_reversed_by_journal_entry_id");

            entity.HasOne(e => e.Business)
                .WithMany(b => b.JournalEntries)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FinancialYear)
                .WithMany(y => y.JournalEntries)
                .HasForeignKey(e => e.FinancialYearId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AccountingPeriod)
                .WithMany(p => p.JournalEntries)
                .HasForeignKey(e => e.AccountingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Terminal)
                .WithMany()
                .HasForeignKey(e => e.TerminalId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CashSession)
                .WithMany()
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ReversalOfJournalEntry)
                .WithMany(e => e.ReversalEntries)
                .HasForeignKey(e => e.ReversalOfJournalEntryId)
                .HasConstraintName("f_k_journal_entries_journal_entries_reversal_of_journal_entry_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReversedByJournalEntry)
                .WithMany()
                .HasForeignKey(e => e.ReversedByJournalEntryId)
                .HasConstraintName("f_k_journal_entries_journal_entries_reversed_by_journal_entry_id")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntryAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.StoredFileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.RelativePath).HasMaxLength(500).IsRequired();

            entity.HasIndex(e => e.JournalEntryId);

            entity.HasOne(e => e.JournalEntry)
                .WithMany(e => e.Attachments)
                .HasForeignKey(e => e.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalEntryLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.Property(e => e.DebitAmount).HasPrecision(18, 2);
            entity.Property(e => e.CreditAmount).HasPrecision(18, 2);
            entity.Property(e => e.PaymentMethodId).HasMaxLength(50);
            entity.Property(e => e.CostCenterId).HasMaxLength(50);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("c_k_journal_entry_lines_amount_sides", "(debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)");
            });

            entity.HasIndex(e => e.JournalEntryId);
            entity.HasIndex(e => e.AccountId);
            entity.HasIndex(e => new { e.AccountId, e.OutletId });
            entity.HasIndex(e => e.SupplierId);
            entity.HasIndex(e => e.CustomerId);

            entity.HasOne(e => e.JournalEntry)
                .WithMany(e => e.Lines)
                .HasForeignKey(e => e.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Account)
                .WithMany(e => e.JournalEntryLines)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Supplier)
                .WithMany()
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OtherIncomeEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IncomeCategory).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.TaxableAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.PaymentMethod).HasMaxLength(40);
            entity.Property(e => e.ReferenceNumber).HasMaxLength(80);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.AttachmentPath).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue(OtherIncomeStatuses.Draft).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);

            entity.HasIndex(e => new { e.BusinessId, e.IncomeDate });
            entity.HasIndex(e => new { e.BusinessId, e.OutletId, e.IncomeDate });
            entity.HasIndex(e => new { e.BusinessId, e.Status });

            entity.HasOne(e => e.Business)
                .WithMany(b => b.OtherIncomeEntries)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.IncomeAccount)
                .WithMany()
                .HasForeignKey(e => e.IncomeAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PaymentAccount)
                .WithMany()
                .HasForeignKey(e => e.PaymentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.JournalEntry)
                .WithMany()
                .HasForeignKey(e => e.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExpenseCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.ExpenseCategories)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ExpenseAccount)
                .WithMany(a => a.ExpenseCategories)
                .HasForeignKey(e => e.ExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Bill Configuration
        modelBuilder.Entity<Bill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AmountDue).HasPrecision(10, 2);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();

            entity.HasOne(e => e.Supplier)
                .WithMany(s => s.Bills)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Bills)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Expense Configuration
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.TaxableAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.ExpenseNumber).HasMaxLength(40);
            entity.Property(e => e.PaymentMethodId).HasMaxLength(50);
            entity.Property(e => e.ReferenceNumber).HasMaxLength(80);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.AttachmentPath).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue(ExpenseStatuses.Draft).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.PaymentSource).HasMaxLength(20);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_expenses_payment_source",
                    "payment_source IS NULL OR payment_source IN ('bank','petty_cash','pos_drawer')");
                // D7 database backstop: a drawer/petty-cash expense is never approved by its creator or submitter
                // unless the audited Business Owner exception flag was recorded with the approval.
                t.HasCheckConstraint(
                    "ck_expenses_maker_checker",
                    "payment_source IS NULL OR payment_source = 'bank' OR approved_by IS NULL OR self_approved_under_owner_exception"
                    + " OR (approved_by IS DISTINCT FROM created_by AND approved_by IS DISTINCT FROM submitted_by)");
            });
            entity.HasIndex(e => e.DisbursedCashSessionId);
            entity.HasIndex(e => new { e.BusinessId, e.ExpenseDate });
            entity.HasOne<CashSession>()
                .WithMany()
                .HasForeignKey(e => e.DisbursedCashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.Status });
            entity.HasIndex(e => new { e.BusinessId, e.ExpenseNumber }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.WarehouseId });
            entity.ToTable(table => table.HasCheckConstraint(
                "ck_expenses_single_location",
                "outlet_id IS NULL OR warehouse_id IS NULL"));

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Expenses)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Expenses)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ExpenseCategory)
                .WithMany(c => c.Expenses)
                .HasForeignKey(e => e.ExpenseCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ExpenseAccount)
                .WithMany()
                .HasForeignKey(e => e.ExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PaymentAccount)
                .WithMany()
                .HasForeignKey(e => e.PaymentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.JournalEntry)
                .WithMany()
                .HasForeignKey(e => e.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExpenseAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.StorageKey).HasMaxLength(500).IsRequired();

            entity.HasIndex(e => new { e.BusinessId, e.ExpenseId });

            entity.HasOne(e => e.Expense)
                .WithMany(e => e.Attachments)
                .HasForeignKey(e => e.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Module).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Details).HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasMaxLength(45);

            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AuditEvent Configuration
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ActionSummary).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Module).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PrimaryEntityType).HasMaxLength(80);
            entity.Property(e => e.PrimaryEntityId).HasMaxLength(80);
            entity.Property(e => e.RequestMethod).HasMaxLength(10);
            entity.Property(e => e.RequestPath).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.DeviceName).HasMaxLength(120);
            entity.Property(e => e.Browser).HasMaxLength(60);
            entity.Property(e => e.Os).HasMaxLength(60);
            entity.Property(e => e.Status).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Metadata).HasColumnType("jsonb");

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.RealUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Module, e.ActionType, e.CreatedAt });
            entity.HasIndex(e => new { e.PrimaryEntityType, e.PrimaryEntityId });
            entity.HasIndex(e => new { e.OutletId, e.CreatedAt });
        });

        // AuditEventEntity Configuration
        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(80).IsRequired();
            entity.Property(e => e.OperationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.Property(e => e.IsInternalOperation).HasDefaultValue(false);
            entity.Property(e => e.InternalOperationName).HasMaxLength(120);

            entity.HasIndex(e => e.AuditEventId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });

            entity.HasOne(e => e.AuditEvent)
                .WithMany(a => a.Entities)
                .HasForeignKey(e => e.AuditEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditEventFieldChange Configuration
        modelBuilder.Entity<AuditEventFieldChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FieldName).HasMaxLength(120).IsRequired();
            entity.Property(e => e.OldValue).HasColumnType("jsonb");
            entity.Property(e => e.NewValue).HasColumnType("jsonb");
            entity.Property(e => e.OldDisplayValue).HasMaxLength(500);
            entity.Property(e => e.NewDisplayValue).HasMaxLength(500);
            entity.Property(e => e.ReferenceEntityType).HasMaxLength(80);
            entity.Property(e => e.IsReferenceField).HasDefaultValue(false);

            entity.HasIndex(e => e.AuditEventEntityId);

            entity.HasOne(e => e.AuditEventEntity)
                .WithMany(a => a.FieldChanges)
                .HasForeignKey(e => e.AuditEventEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriceRule Configuration
        modelBuilder.Entity<PriceRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.RuleType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DiscountType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DiscountValue).HasPrecision(10, 2);
            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            // Hot-path index: the matching query filters on Business + IsActive + RuleType + TargetId
            entity.HasIndex(e => new { e.BusinessId, e.IsActive, e.RuleType, e.TargetId });
            entity.HasIndex(e => new { e.ValidFrom, e.ValidTo });
        });

        // OutletPriceOverride Configuration
        modelBuilder.Entity<OutletPriceOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OverrideType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OverrideValue).HasPrecision(10, 2);
            entity.HasOne<Business>()
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            // One *active* override per outlet × variant; inactive rows are excluded so
            // a new override can be created after the old one is deactivated.
            entity.HasIndex(e => new { e.OutletId, e.ProductVariantId })
                  .IsUnique()
                  .HasFilter("is_active = true");
            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ProductVariant)
                .WithMany()
                .HasForeignKey(e => e.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================================
        // NEW — StockLedger Configuration
        // =====================================================================
        modelBuilder.Entity<StockLedger>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.TransactionType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ReferenceType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Remarks).HasMaxLength(500);
            entity.Property(e => e.UnitCost).HasColumnType("numeric");
            entity.Property(e => e.MovementValue).HasPrecision(18, 4);
            entity.Property(e => e.IsCostValuationReliable).HasDefaultValue(false);
            entity.Property(e => e.BusinessDate).HasColumnType("date");

            // Core query paths: look up the full timeline for a variant+location,
            // or jump straight to all rows belonging to one source document.
            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType, e.CreatedAt });
            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType, e.MovementSequence });

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        if (Database.IsNpgsql())
        {
            // Legacy timestamp value handling must not change the persisted schema.
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            {
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime)
                    && property.GetColumnType() is null)
                    property.SetColumnType("timestamp with time zone");
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAuditImmutability()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is RetailPOS.Core.Entities.HR.Attendance.HrDeviceUserMapping
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted || !entry.OriginalValues.GetValue<bool>("IsActive")
                    || entry.CurrentValues.GetValue<bool>("IsActive")
                    || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("EffectiveToUtc" or "IsActive" or "Version")))
                    throw new InvalidOperationException("Device mapping history is immutable except for closure.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Attendance.HrRosterAssignment
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var originalStatus = entry.OriginalValues.GetValue<string>("Status");
                if (entry.State == EntityState.Deleted || originalStatus == "Cancelled"
                    || originalStatus == "Published" && (entry.CurrentValues.GetValue<string>("Status") != "Cancelled"
                        || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("Status" or "Version"))))
                    throw new InvalidOperationException("Published roster context is immutable; only future cancellation is supported.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Attendance.HrRawAttendancePunch
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                        ("ProcessingStatus" or "ProcessingError" or "ProcessingVersion")))
                    throw new InvalidOperationException("Raw attendance punch evidence is immutable; only processing metadata may change.");
            }
            if (entry.Entity is PayrollSalarySetupRequest
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Salary setup request evidence cannot be deleted.");

                var immutableFields = new[]
                {
                    nameof(PayrollSalarySetupRequest.BusinessId),
                    nameof(PayrollSalarySetupRequest.EmployeeId),
                    nameof(PayrollSalarySetupRequest.SalaryStructureId),
                    nameof(PayrollSalarySetupRequest.SalaryStructureVersion),
                    nameof(PayrollSalarySetupRequest.EffectiveFrom),
                    nameof(PayrollSalarySetupRequest.OperationalLocationProjectionId),
                    nameof(PayrollSalarySetupRequest.StructureSnapshotJson),
                    nameof(PayrollSalarySetupRequest.ComponentSnapshotJson),
                    nameof(PayrollSalarySetupRequest.RequestHash),
                    nameof(PayrollSalarySetupRequest.IdempotencyPayloadHash),
                    nameof(PayrollSalarySetupRequest.IdempotencyKey),
                    nameof(PayrollSalarySetupRequest.Reason),
                    nameof(PayrollSalarySetupRequest.CreatedByUserId),
                    nameof(PayrollSalarySetupRequest.CreatedAtUtc)
                };
                if (immutableFields.Any(field => entry.Property(field).IsModified))
                    throw new InvalidOperationException("Salary setup proposal and approval evidence are immutable.");

                var changedFields = entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
                var originalStatus = entry.OriginalValues.GetValue<string>(nameof(PayrollSalarySetupRequest.Status));
                var currentStatus = entry.CurrentValues.GetValue<string>(nameof(PayrollSalarySetupRequest.Status));
                var allowedFields = originalStatus == "Draft" && currentStatus == "PendingApproval"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollSalarySetupRequest.Status),
                        nameof(PayrollSalarySetupRequest.SubmittedByUserId),
                        nameof(PayrollSalarySetupRequest.SubmittedAtUtc),
                        nameof(PayrollSalarySetupRequest.SubmittedTargetVersion),
                        nameof(PayrollSalarySetupRequest.ApprovalInstanceId),
                        nameof(PayrollSalarySetupRequest.Version)
                    }
                    : originalStatus == "PendingApproval"
                        && currentStatus is "Approved" or "Rejected" or "Cancelled"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollSalarySetupRequest.Status),
                        nameof(PayrollSalarySetupRequest.DecidedByUserId),
                        nameof(PayrollSalarySetupRequest.DecidedAtUtc),
                        nameof(PayrollSalarySetupRequest.DecisionReason),
                        nameof(PayrollSalarySetupRequest.Version)
                    }
                    : [];

                if (allowedFields.Count == 0 || changedFields.Count == 0
                    || changedFields.Except(allowedFields).Any()
                    || entry.CurrentValues.GetValue<long>(nameof(PayrollSalarySetupRequest.Version))
                        != checked(entry.OriginalValues.GetValue<long>(nameof(PayrollSalarySetupRequest.Version)) + 1))
                    throw new InvalidOperationException(
                        "Salary setup requests allow only versioned submission and terminal approval transitions.");
            }
            if (entry.Entity is PayrollEmployeeSalaryVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Approved employee salary snapshots are immutable.");
            if (entry.Entity is PayrollRun && entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Payroll run evidence cannot be deleted.");
            if (entry.Entity is PayrollRun && entry.State == EntityState.Modified)
            {
                var changedFields = entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
                var calculationTimestamped = entry.CurrentValues.GetValue<DateTime?>(
                    nameof(PayrollRun.CalculatedAtUtc)).HasValue;
                var firstCalculation = entry.OriginalValues.GetValue<DateTime?>(
                    nameof(PayrollRun.CalculatedAtUtc)) is null && calculationTimestamped;
                var recalculation = entry.OriginalValues.GetValue<DateTime?>(
                        nameof(PayrollRun.CalculatedAtUtc)).HasValue
                    && calculationTimestamped
                    && entry.CurrentValues.GetValue<int>(nameof(PayrollRun.CalculationVersion))
                        == checked(entry.OriginalValues.GetValue<int>(nameof(PayrollRun.CalculationVersion)) + 1)
                    && !StringComparer.Ordinal.Equals(
                        entry.OriginalValues.GetValue<string?>(nameof(PayrollRun.CalculationInputHash)),
                        entry.CurrentValues.GetValue<string?>(nameof(PayrollRun.CalculationInputHash)));
                var isCalculation = firstCalculation || recalculation;
                var versionAdvanced = entry.CurrentValues.GetValue<long>(nameof(PayrollRun.Version))
                    == checked(entry.OriginalValues.GetValue<long>(nameof(PayrollRun.Version)) + 1);
                var approvalFields = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(PayrollRun.Status), nameof(PayrollRun.ApprovalInstanceId),
                    nameof(PayrollRun.SubmittedByUserId), nameof(PayrollRun.SubmittedAtUtc),
                    nameof(PayrollRun.SubmittedTargetVersion), nameof(PayrollRun.ApprovalEvidenceHash),
                    nameof(PayrollRun.DecidedByUserId), nameof(PayrollRun.DecidedAtUtc),
                    nameof(PayrollRun.DecisionReason), nameof(PayrollRun.Version)
                };
                var submissionFields = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(PayrollRun.Status), nameof(PayrollRun.ApprovalInstanceId),
                    nameof(PayrollRun.SubmittedByUserId), nameof(PayrollRun.SubmittedAtUtc),
                    nameof(PayrollRun.SubmittedTargetVersion), nameof(PayrollRun.ApprovalEvidenceHash),
                    nameof(PayrollRun.Version)
                };
                var decisionFields = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(PayrollRun.Status), nameof(PayrollRun.DecidedByUserId),
                    nameof(PayrollRun.DecidedAtUtc), nameof(PayrollRun.DecisionReason),
                    nameof(PayrollRun.Version)
                };
                var isApprovalSubmission =
                    entry.OriginalValues.GetValue<string>(nameof(PayrollRun.Status)) == PayrollRunStatuses.Draft
                    && entry.CurrentValues.GetValue<string>(nameof(PayrollRun.Status))
                        == PayrollRunStatuses.PendingApproval
                    && entry.CurrentValues.GetValue<long?>(nameof(PayrollRun.SubmittedTargetVersion))
                        == entry.OriginalValues.GetValue<long>(nameof(PayrollRun.Version))
                    && entry.CurrentValues.GetValue<DateTime?>(nameof(PayrollRun.SubmittedAtUtc)).HasValue
                    && entry.CurrentValues.GetValue<long?>(nameof(PayrollRun.SubmittedByUserId)).HasValue
                    && entry.CurrentValues.GetValue<string?>(nameof(PayrollRun.ApprovalEvidenceHash)) is not null
                    && changedFields.Except(submissionFields).Count() == 0;
                var isApprovalDecision =
                    entry.OriginalValues.GetValue<string>(nameof(PayrollRun.Status))
                        == PayrollRunStatuses.PendingApproval
                    && entry.CurrentValues.GetValue<string>(nameof(PayrollRun.Status))
                        is PayrollRunStatuses.Approved or PayrollRunStatuses.Rejected or PayrollRunStatuses.Cancelled
                    && entry.OriginalValues.GetValue<long?>(nameof(PayrollRun.ApprovalInstanceId)).HasValue
                    && entry.CurrentValues.GetValue<long?>(nameof(PayrollRun.DecidedByUserId)).HasValue
                    && entry.CurrentValues.GetValue<DateTime?>(nameof(PayrollRun.DecidedAtUtc)).HasValue
                    && changedFields.Except(decisionFields).Count() == 0;
                var isApprovalInstanceBinding =
                    entry.OriginalValues.GetValue<string>(nameof(PayrollRun.Status))
                        == PayrollRunStatuses.PendingApproval
                    && entry.CurrentValues.GetValue<string>(nameof(PayrollRun.Status))
                        == PayrollRunStatuses.PendingApproval
                    && !entry.OriginalValues.GetValue<long?>(nameof(PayrollRun.ApprovalInstanceId)).HasValue
                    && entry.CurrentValues.GetValue<long?>(nameof(PayrollRun.ApprovalInstanceId)).HasValue
                    && changedFields.SetEquals(new[] { nameof(PayrollRun.ApprovalInstanceId) });
                var isApprovalTransition =
                    ((isApprovalSubmission || isApprovalDecision) && versionAdvanced
                        && changedFields.Count > 0)
                    || isApprovalInstanceBinding
                    && changedFields.Except(approvalFields).Count() == 0;
                var calculationVersionAdvanced = entry.CurrentValues.GetValue<int>(
                        nameof(PayrollRun.CalculationVersion))
                    == checked(entry.OriginalValues.GetValue<int>(nameof(PayrollRun.CalculationVersion)) + 1);
                if ((!isCalculation || !calculationVersionAdvanced || !versionAdvanced)
                    && !isApprovalTransition
                    || changedFields.Count == 0
                    || isCalculation && changedFields.Except(new[]
                    {
                        nameof(PayrollRun.CalculatedAtUtc), nameof(PayrollRun.CalculationVersion),
                        nameof(PayrollRun.CalculationInputHash), nameof(PayrollRun.Version)
                    }).Any())
                    throw new InvalidOperationException(
                        "Payroll runs allow only versioned calculation transitions.");
            }
            if (entry.Entity is PayrollRunEmployeeSelection
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    "Payroll run eligibility evidence is immutable after creation.");
            if (entry.Entity is PayrollEmployeeResult
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Calculated payroll results are immutable.");
            if (entry.Entity is PayrollEmployeeResultLine
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Calculated payroll component lines are immutable.");
            if (entry.Entity is PayrollFinalSettlementRevision or PayrollFinalSettlementLine
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    "Final settlement calculation revisions and lines are immutable.");
            if (entry.Entity is PayrollFinalSettlement
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not ("CurrentRevisionNo" or "Version"))
                    || entry.CurrentValues.GetValue<int>("CurrentRevisionNo")
                        != checked(entry.OriginalValues.GetValue<int>("CurrentRevisionNo") + 1)
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Final settlement identity is immutable; revisions advance the current version.");
            }
            if (entry.Entity is PayrollCalculationPolicyVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Payroll calculation policy versions are immutable.");
            if (entry.Entity is PayrollMonetaryConfigurationVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Payroll monetary configuration versions are immutable.");
            if (entry.Entity is PayrollEmployeePaymentDestination
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    "Payroll payment destination history is immutable; append a new effective version.");
            if (entry.Entity is PayrollPeriod && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Payroll period history cannot be deleted.");

                var period = (PayrollPeriod)entry.Entity;
                var originalStatus = entry.OriginalValues.GetValue<string>(nameof(PayrollPeriod.Status));
                var currentStatus = entry.CurrentValues.GetValue<string>(nameof(PayrollPeriod.Status));
                var hasCorrectionReopenEvent = ChangeTracker.Entries<PayrollPeriodReopenEvent>()
                    .Any(eventEntry => eventEntry.State == EntityState.Added
                        && eventEntry.Entity.BusinessId == period.BusinessId
                        && eventEntry.Entity.PayrollPeriodId == period.Id
                        && eventEntry.Entity.Purpose == "Correction"
                        && eventEntry.Entity.PreviousStatus == originalStatus
                        && eventEntry.Entity.ResultPeriodVersion
                            == checked(entry.OriginalValues.GetValue<long>(nameof(PayrollPeriod.Version)) + 1)
                        && eventEntry.Entity.OriginalPayrollRunId.HasValue
                        && eventEntry.Entity.CorrectionPayrollRunId.HasValue
                        && PayrollRuns.Local.Any(run =>
                            run.BusinessId == period.BusinessId
                            && run.Id == eventEntry.Entity.CorrectionPayrollRunId
                            && run.RunType == PayrollRunTypes.Correction
                            && run.OriginalPayrollRunId == eventEntry.Entity.OriginalPayrollRunId));
                var isControlledCorrectionReopen = currentStatus == PayrollPeriodStatuses.Processing
                    && originalStatus is (PayrollPeriodStatuses.Approved
                        or PayrollPeriodStatuses.Posted or PayrollPeriodStatuses.Closed)
                    && hasCorrectionReopenEvent;
                if (originalStatus == PayrollPeriodStatuses.Closed && !isControlledCorrectionReopen)
                    throw new InvalidOperationException("Closed Payroll periods are immutable.");

                var immutableFields = new[]
                {
                    nameof(PayrollPeriod.BusinessId),
                    nameof(PayrollPeriod.PeriodCode),
                    nameof(PayrollPeriod.PeriodStart),
                    nameof(PayrollPeriod.PeriodEnd),
                    nameof(PayrollPeriod.PayDate),
                    nameof(PayrollPeriod.CreatedAtUtc),
                    nameof(PayrollPeriod.CreatedByUserId),
                    nameof(PayrollPeriod.OpenedAtUtc),
                    nameof(PayrollPeriod.OpenedByUserId)
                };
                var changedFields = entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
                var isStartProcessing = originalStatus == PayrollPeriodStatuses.Open
                    && entry.CurrentValues.GetValue<string>(nameof(PayrollPeriod.Status))
                        == PayrollPeriodStatuses.Processing;
                var transitionFields = (originalStatus, currentStatus) switch
                {
                    (PayrollPeriodStatuses.Open, PayrollPeriodStatuses.Processing) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ProcessingAtUtc),
                        nameof(PayrollPeriod.ProcessingByUserId), nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Processing, PayrollPeriodStatuses.Open) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Processing, PayrollPeriodStatuses.Locked) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.LockedAtUtc),
                        nameof(PayrollPeriod.LockedByUserId), nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Locked, PayrollPeriodStatuses.Processing) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Approved, PayrollPeriodStatuses.Locked) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Approved, PayrollPeriodStatuses.Processing)
                        when isControlledCorrectionReopen => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Posted, PayrollPeriodStatuses.Processing)
                        when isControlledCorrectionReopen => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Closed, PayrollPeriodStatuses.Processing)
                        when isControlledCorrectionReopen => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ReopenedAtUtc),
                        nameof(PayrollPeriod.ReopenedByUserId), nameof(PayrollPeriod.ReopenReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Locked, PayrollPeriodStatuses.Approved) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ApprovedAtUtc),
                        nameof(PayrollPeriod.ApprovedByUserId), nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Approved, PayrollPeriodStatuses.Posted) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.PostedAtUtc),
                        nameof(PayrollPeriod.PostedByUserId), nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Approved, PayrollPeriodStatuses.Closed) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ClosedAtUtc),
                        nameof(PayrollPeriod.ClosedByUserId), nameof(PayrollPeriod.ClosedReason),
                        nameof(PayrollPeriod.Version)
                    },
                    (PayrollPeriodStatuses.Posted, PayrollPeriodStatuses.Closed) => new[]
                    {
                        nameof(PayrollPeriod.Status), nameof(PayrollPeriod.ClosedAtUtc),
                        nameof(PayrollPeriod.ClosedByUserId), nameof(PayrollPeriod.ClosedReason),
                        nameof(PayrollPeriod.Version)
                    },
                    _ => []
                };
                var allowedFields = transitionFields.ToHashSet(StringComparer.Ordinal);

                if (immutableFields.Any(field => entry.Property(field).IsModified)
                    || allowedFields.Count == 0 || changedFields.Count == 0
                    || changedFields.Except(allowedFields).Any()
                    || entry.CurrentValues.GetValue<long>(nameof(PayrollPeriod.Version))
                        != checked(entry.OriginalValues.GetValue<long>(nameof(PayrollPeriod.Version)) + 1))
                    throw new InvalidOperationException(
                        "Payroll periods allow only controlled, versioned lifecycle transitions.");
            }
            if (entry.Entity is PayrollPeriodCloseAssessment
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Payroll period close assessments are immutable.");
            if (entry.Entity is PayrollPeriodReopenEvent
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Payroll period reopen history is immutable.");
            if (entry.Entity is PayrollSalaryRevisionRequest
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Salary revision request evidence cannot be deleted.");
                var immutableFields = new[]
                {
                    nameof(PayrollSalaryRevisionRequest.BusinessId),
                    nameof(PayrollSalaryRevisionRequest.EmployeeId),
                    nameof(PayrollSalaryRevisionRequest.SourceSalaryVersionId),
                    nameof(PayrollSalaryRevisionRequest.EffectiveFrom),
                    nameof(PayrollSalaryRevisionRequest.OperationalLocationProjectionId),
                    nameof(PayrollSalaryRevisionRequest.StructureSnapshotJson),
                    nameof(PayrollSalaryRevisionRequest.CurrentComponentSnapshotJson),
                    nameof(PayrollSalaryRevisionRequest.ProposedComponentSnapshotJson),
                    nameof(PayrollSalaryRevisionRequest.RequestHash),
                    nameof(PayrollSalaryRevisionRequest.IdempotencyPayloadHash),
                    nameof(PayrollSalaryRevisionRequest.IdempotencyKey),
                    nameof(PayrollSalaryRevisionRequest.Reason),
                    nameof(PayrollSalaryRevisionRequest.CreatedByUserId),
                    nameof(PayrollSalaryRevisionRequest.CreatedAtUtc)
                };
                if (immutableFields.Any(field => entry.Property(field).IsModified))
                    throw new InvalidOperationException("Salary revision proposal evidence is immutable.");
                var changedFields = entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
                var originalStatus = entry.OriginalValues.GetValue<string>(
                    nameof(PayrollSalaryRevisionRequest.Status));
                var currentStatus = entry.CurrentValues.GetValue<string>(
                    nameof(PayrollSalaryRevisionRequest.Status));
                var allowedFields = originalStatus == "Draft" && currentStatus == "PendingApproval"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollSalaryRevisionRequest.Status),
                        nameof(PayrollSalaryRevisionRequest.SubmittedByUserId),
                        nameof(PayrollSalaryRevisionRequest.SubmittedAtUtc),
                        nameof(PayrollSalaryRevisionRequest.SubmittedTargetVersion),
                        nameof(PayrollSalaryRevisionRequest.ApprovalInstanceId),
                        nameof(PayrollSalaryRevisionRequest.Version)
                    }
                    : originalStatus == "PendingApproval"
                        && currentStatus is "Approved" or "Rejected" or "Cancelled"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollSalaryRevisionRequest.Status),
                        nameof(PayrollSalaryRevisionRequest.DecidedByUserId),
                        nameof(PayrollSalaryRevisionRequest.DecidedAtUtc),
                        nameof(PayrollSalaryRevisionRequest.DecisionReason),
                        nameof(PayrollSalaryRevisionRequest.Version)
                    }
                    : [];
                if (allowedFields.Count == 0 || changedFields.Count == 0
                    || changedFields.Except(allowedFields).Any()
                    || entry.CurrentValues.GetValue<long>(nameof(PayrollSalaryRevisionRequest.Version))
                        != checked(entry.OriginalValues.GetValue<long>(nameof(PayrollSalaryRevisionRequest.Version)) + 1))
                    throw new InvalidOperationException(
                        "Salary revision requests allow only versioned submission and terminal approval transitions.");
            }
            if (entry.Entity is PayrollEmployeeAdjustmentVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    "Payroll adjustment versions are immutable; changes must append a successor version.");
            if (entry.Entity is PayrollEmployeeLoan
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Employee loan evidence cannot be deleted.");
                var immutableFields = new[]
                {
                    nameof(PayrollEmployeeLoan.BusinessId),
                    nameof(PayrollEmployeeLoan.EmployeeId),
                    nameof(PayrollEmployeeLoan.OperationalLocationProjectionId),
                    nameof(PayrollEmployeeLoan.LoanType),
                    nameof(PayrollEmployeeLoan.PrincipalAmount),
                    nameof(PayrollEmployeeLoan.RecoveryStartDate),
                    nameof(PayrollEmployeeLoan.RecoveryMode),
                    nameof(PayrollEmployeeLoan.FixedInstallment),
                    nameof(PayrollEmployeeLoan.ProposedScheduleJson),
                    nameof(PayrollEmployeeLoan.RequestHash),
                    nameof(PayrollEmployeeLoan.IdempotencyKey),
                    nameof(PayrollEmployeeLoan.IdempotencyPayloadHash),
                    nameof(PayrollEmployeeLoan.Reason),
                    nameof(PayrollEmployeeLoan.Reference),
                    nameof(PayrollEmployeeLoan.CreatedByUserId),
                    nameof(PayrollEmployeeLoan.CreatedAtUtc)
                };
                if (immutableFields.Any(field => entry.Property(field).IsModified))
                    throw new InvalidOperationException("Employee loan request evidence is immutable.");

                var originalStatus = entry.OriginalValues.GetValue<string>(nameof(PayrollEmployeeLoan.Status));
                var currentStatus = entry.CurrentValues.GetValue<string>(nameof(PayrollEmployeeLoan.Status));
                var changedFields = entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
                var allowedFields = originalStatus == "Draft" && currentStatus == "PendingApproval"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollEmployeeLoan.Status),
                        nameof(PayrollEmployeeLoan.SubmittedByUserId),
                        nameof(PayrollEmployeeLoan.SubmittedAtUtc),
                        nameof(PayrollEmployeeLoan.SubmittedTargetVersion),
                        nameof(PayrollEmployeeLoan.ApprovalInstanceId),
                        nameof(PayrollEmployeeLoan.Version)
                    }
                    : originalStatus == "PendingApproval"
                        && currentStatus is "Active" or "Rejected" or "Cancelled"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollEmployeeLoan.Status),
                        nameof(PayrollEmployeeLoan.ApprovedAmount),
                        nameof(PayrollEmployeeLoan.DisbursedDate),
                        nameof(PayrollEmployeeLoan.OutstandingAmount),
                        nameof(PayrollEmployeeLoan.CurrentScheduleVersionNo),
                        nameof(PayrollEmployeeLoan.DecidedByUserId),
                        nameof(PayrollEmployeeLoan.DecidedAtUtc),
                        nameof(PayrollEmployeeLoan.DecisionReason),
                        nameof(PayrollEmployeeLoan.Version)
                    }
                    : originalStatus == "Active" && currentStatus is "Active" or "Settled"
                    ? new HashSet<string>(StringComparer.Ordinal)
                    {
                        nameof(PayrollEmployeeLoan.Status),
                        nameof(PayrollEmployeeLoan.OutstandingAmount),
                        nameof(PayrollEmployeeLoan.CurrentScheduleVersionNo),
                        nameof(PayrollEmployeeLoan.Version)
                    }
                    : [];
                if (allowedFields.Count == 0 || changedFields.Count == 0
                    || changedFields.Except(allowedFields).Any()
                    || entry.CurrentValues.GetValue<long>(nameof(PayrollEmployeeLoan.Version))
                        != checked(entry.OriginalValues.GetValue<long>(nameof(PayrollEmployeeLoan.Version)) + 1))
                    throw new InvalidOperationException(
                        "Employee loans allow only versioned approval, schedule and repayment transitions.");
            }
            if (entry.Entity is PayrollLoanScheduleVersion or PayrollLoanScheduleLine
                or PayrollLoanRepayment or PayrollLoanRepaymentAllocation
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Loan schedule and repayment evidence is append-only.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Leave.HrLeaveLedgerEntry
                or RetailPOS.Core.Entities.HR.Leave.HrLeavePolicyVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Leave ledger entries and policy versions are immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Leave.HrLeaveEntitlement
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not ("Status" or "CloseVersion" or "Version"))
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Leave-year entitlement snapshots are immutable; only versioned lifecycle state changes are supported.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Leave.HrLeaveYearLifecycleEvent
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Leave-year lifecycle evidence is immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Leave.HrLeaveRequestDay
                or RetailPOS.Core.Entities.HR.Leave.HrLeaveRequestAttachmentVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Leave request day and attachment evidence is immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Leave.HrLeaveRequest
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Leave request history cannot be deleted.");
                var originalStatus = entry.OriginalValues.GetValue<string>("Status");
                var currentStatus = entry.CurrentValues.GetValue<string>("Status");
                var validTransition = originalStatus == currentStatus
                    || originalStatus == "Draft" && currentStatus is "PendingApproval" or "Cancelled"
                    || originalStatus == "PendingApproval" && currentStatus is "Approved" or "Rejected" or "Cancelled"
                    || originalStatus == "Approved" && currentStatus == "Cancelled";
                if (!validTransition
                    || entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                        ("Status" or "DecidedByUserId" or "DecidedAtUtc" or "DecisionReason"
                            or "ApprovalInstanceId" or "CurrentAttachmentVersionId"
                            or "SubmittedTargetVersion" or "Version"))
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Leave request evidence is immutable except for audited workflow transitions and attachment versioning.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplate
                or RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplateItem
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Clearance template versions and items are immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Exit.HrExitCaseEvent
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Exit case lifecycle evidence is immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Exit.HrExitCompletionEvidence
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Exit completion evidence is immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Exit.HrExitCase
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not
                            ("Status" or "ActualLastWorkingDate" or "ApprovalInstanceId" or "Version"))
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Exit case identity and frozen snapshots are immutable; workflow state requires one version increment.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Exit.HrClearanceItem
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not
                            ("Status" or "ClearedByUserId" or "ClearedAtUtc" or "Comment"
                                or "EvidenceReference" or "Version"))
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Clearance item identity and frozen snapshots are immutable; status updates require one version increment.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Overtime.HrOvertimePolicyVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not ("EffectiveTo" or "Version"))
                    || entry.OriginalValues.GetValue<DateOnly?>("EffectiveTo") is not null
                    || entry.CurrentValues.GetValue<DateOnly?>("EffectiveTo") is null
                    || entry.CurrentValues.GetValue<long>("Version")
                        != checked(entry.OriginalValues.GetValue<long>("Version") + 1))
                    throw new InvalidOperationException(
                        "Overtime policy versions are immutable except for one-time effective-date closure.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Overtime.HrOvertimeCalculationEvidence
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Overtime calculation evidence is immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Overtime.HrOvertimeEntry
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Overtime calculation and approval history cannot be deleted.");

                var identityFields = new[]
                {
                    "BusinessId", "EmployeeId", "WorkDate", "DailyAttendanceId",
                    "OperationalLocationProjectionId"
                };
                var identityChanged = identityFields.Any(name =>
                    entry.Property(name).IsModified);
                var oldStatus = entry.OriginalValues.GetValue<string>("Status");
                var newStatus = entry.CurrentValues.GetValue<string>("Status");
                var oldCalculationVersion = entry.OriginalValues.GetValue<int>("CalculationVersion");
                var newCalculationVersion = entry.CurrentValues.GetValue<int>("CalculationVersion");
                var isRecalculation = newStatus == "Calculated"
                    && newCalculationVersion == checked(oldCalculationVersion + 1)
                    && oldStatus is "Calculated" or "Rejected" or "Cancelled" or "Superseded";
                var isWorkflowTransition = oldStatus switch
                {
                    "Calculated" => newStatus == "PendingApproval",
                    "PendingApproval" => newStatus is "PayrollEligible" or "Rejected" or "Cancelled",
                    "Approved" => newStatus == "PayrollEligible",
                    "Cancelled" or "Rejected" => newStatus == "Superseded",
                    _ => false
                };
                var isSupersede = oldStatus == "Calculated" && newStatus == "Superseded";
                var versionChangedOnce = entry.CurrentValues.GetValue<long>("Version")
                    == checked(entry.OriginalValues.GetValue<long>("Version") + 1);
                var disallowedWorkflowField = entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not
                        ("Status" or "ApprovedMinutes" or "ApprovalInstanceId" or "Version"));
                var disallowedCalculationField = entry.Properties.Any(property =>
                    property.IsModified && property.Metadata.Name is not
                        ("DayCategory" or "AttendanceProcessingVersion" or "PolicyVersionId"
                            or "PolicyVersionNo" or "EligibleMinutes" or "CalculatedMinutes"
                            or "ApprovedMinutes" or "Status" or "CalculationVersion"
                            or "CalculationFingerprint" or "RequestHash" or "PolicySnapshotJson"
                            or "AttendanceSnapshotJson" or "CalculationInputsJson"
                            or "ApprovalInstanceId" or "Version"));
                if (identityChanged || !versionChangedOnce
                    || !(isRecalculation && !disallowedCalculationField
                        || isWorkflowTransition && !disallowedWorkflowField
                            && !disallowedCalculationField
                        || isSupersede && !disallowedWorkflowField))
                    throw new InvalidOperationException(
                        "Overtime identity is immutable; calculations and approval state require versioned transitions.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.HR.Calendars.HrWeeklyOffPolicyDay
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Weekly-off version days are immutable.");
            if (entry.Entity is RetailPOS.Core.Entities.HR.Calendars.HrWeeklyOffPolicyVersion
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted || entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is not ("EffectiveTo" or "IsActive" or "Version")))
                    throw new InvalidOperationException("Weekly-off versions are immutable except for closure.");
            }
            if (entry.Entity is AuditEvent or AuditEventEntity or AuditEventFieldChange
                or HrEmployeeLifecycleEvent or HrImportRow)
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    throw new InvalidOperationException(
                        $"Audit records are immutable; cannot {entry.State} {entry.Entity.GetType().Name}.");
                }
            }
            if (entry.Entity is HrEmployeeUserLink && entry.State == EntityState.Modified)
            {
                var originalIsActive = (bool)entry.Property(nameof(HrEmployeeUserLink.IsActive)).OriginalValue!;
                if (!originalIsActive
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not (
                            nameof(HrEmployeeUserLink.IsActive)
                            or nameof(HrEmployeeUserLink.UnlinkedAtUtc)
                            or nameof(HrEmployeeUserLink.UnlinkedByUserId)
                            or nameof(HrEmployeeUserLink.UnlinkReason)
                            or nameof(HrEmployeeUserLink.Version))))
                    throw new InvalidOperationException("Employee Login User link history is immutable after unlink.");
            }
            if (entry.Entity is HrEmployeeLocationAssignment && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Employee location assignment history cannot be deleted.");
                var originalStatus = (string)entry.Property(
                    nameof(HrEmployeeLocationAssignment.Status)).OriginalValue!;
                if (originalStatus != "Effective"
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not (
                            nameof(HrEmployeeLocationAssignment.Status)
                            or nameof(HrEmployeeLocationAssignment.EffectiveTo)
                            or nameof(HrEmployeeLocationAssignment.Version))))
                    throw new InvalidOperationException("Employee location assignment history is immutable except for closure.");
            }
            if (entry.Entity is HrEmployeeLocationTransferRequest && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Employee location transfer request history cannot be deleted.");
                var originalStatus = (string)entry.Property(
                    nameof(HrEmployeeLocationTransferRequest.Status)).OriginalValue!;
                if (originalStatus is not ("PendingApproval" or "ApprovedPendingEffective")
                    || entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not (
                            nameof(HrEmployeeLocationTransferRequest.Status)
                            or nameof(HrEmployeeLocationTransferRequest.ApprovalInstanceId)
                            or nameof(HrEmployeeLocationTransferRequest.DecidedByUserId)
                            or nameof(HrEmployeeLocationTransferRequest.DecidedAtUtc)
                            or nameof(HrEmployeeLocationTransferRequest.DecisionReason)
                            or nameof(HrEmployeeLocationTransferRequest.Version))))
                    throw new InvalidOperationException("Terminal employee transfer request history is immutable.");
            }
            if (entry.Entity is HrEmployeeDocumentVersion && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Employee document versions are immutable and cannot be deleted.");
            if (entry.Entity is HrImportBatch && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Deleted || entry.Properties.Any(property => property.IsModified
                    && property.Metadata.Name is not (
                        nameof(HrImportBatch.Status) or nameof(HrImportBatch.IsDryRun)
                        or nameof(HrImportBatch.TotalRows) or nameof(HrImportBatch.ValidRows)
                        or nameof(HrImportBatch.InvalidRows) or nameof(HrImportBatch.Revision)
                        or nameof(HrImportBatch.ExecutedBy) or nameof(HrImportBatch.ExecutedAtUtc)
                        or nameof(HrImportBatch.Version))))
                    throw new InvalidOperationException("Import source and identity evidence is immutable.");
            }
            if (entry.Entity is BankStatementLine && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException($"Bank statement evidence is immutable; cannot {entry.State} BankStatementLine.");
            }

            if (entry.Entity is StockLedger && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"Stock ledger records are immutable; cannot {entry.State} {entry.Entity.GetType().Name}.");
            }
            if (entry.Entity is RetailPOS.Core.Entities.Accounting.FrozenSystemJournalOperation
                or RetailPOS.Core.Entities.Integration.IntegrationInboxMessage
                && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Completed integration/Accounting identity evidence is immutable.");

            if (entry.Entity is RetailPOS.Core.Entities.Integration.IntegrationOutboxMessage)
            {
                if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified
                    && entry.Properties.Any(property => property.IsModified && property.Metadata.Name
                        is not ("Status" or "AttemptCount" or "NextAttemptAtUtc" or "LeaseOwner"
                            or "LeaseExpiresAtUtc" or "PublishedAtUtc" or "LastErrorCode")))
                    throw new InvalidOperationException("Frozen outbox envelope/identity is immutable.");
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
