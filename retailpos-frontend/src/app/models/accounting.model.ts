export interface AccountDto {
  id: number;
  name: string;
  type: string; // asset, liability, expense, revenue
  balance: number;
}

export interface AccountingReportResponse<T> { data: T; }
export interface CloseGateSnapshot { id: number; evaluationSequence: number; gateCode: string; status: string; expectedAmount?: number | null; actualAmount?: number | null; details?: string | null; isHistoricalCutoverLimitation: boolean; evaluatedAt: string; hasApprovedException: boolean; approvedExceptionReason?: string | null; exceptionApprovedBy?: number | null; exceptionApprovedAt?: string | null; }
export interface PeriodCloseRun { id: number; accountingPeriodId: number; runNumber: number; status: string; evaluationSequence: number; preparedBy: number; preparedAt: string; approvedBy?: number | null; approvedAt?: string | null; executedBy?: number | null; executedAt?: string | null; executionReason?: string | null; reopenedBy?: number | null; reopenedAt?: string | null; reopenReason?: string | null; gates: CloseGateSnapshot[]; }
export interface FinancialYearCloseRun { id: number; financialYearId: number; finalAccountingPeriodId: number; finalPeriodCloseRunId: number; runNumber: number; status: string; preparedBy: number; preparedAt: string; approvedBy?: number | null; approvedAt?: string | null; executedBy?: number | null; executedAt?: string | null; yearEndJournalEntryId?: number | null; yearEndJournalNumber?: string | null; reversalJournalEntryId?: number | null; reopenedBy?: number | null; reopenedAt?: string | null; reopenReason?: string | null; }
export interface BankAccountOption { id: number; displayName: string; chartAccountId: number; outletId?: number | null; isActive: boolean; }
export interface ReconciliationSession { id: number; businessId: number; bankAccountId: number; periodStart: string; periodEnd: string; statementOpeningBalance?: number | null; statementClosingBalance?: number | null; glOpeningBalance?: number | null; status: string; }
export interface ReconciliationCandidate { bankStatementLineId?: number | null; journalEntryLineId?: number | null; date: string; signedAmount: number; description: string; externalReference?: string | null; journalEntryId?: number | null; journalNumber?: string | null; sourceType?: string | null; sourceReference?: string | null; }
export interface ReconciliationMatch { id: number; sessionId: number; status: string; statementTotal: number; glTotal: number; }
export interface ReconciliationCalculation { statementOpeningBalance: number; statementMovement: number; statementClosingBalance: number; glOpeningBalance: number; glMovement: number; glClosingBalance: number; matchedStatementAmount: number; matchedStatementCount: number; matchedGlAmount: number; matchedGlCount: number; unmatchedStatementAmount: number; unmatchedStatementCount: number; unmatchedGlAmount: number; unmatchedGlCount: number; reconciliationDifference: number; unresolvedExceptionCount: number; }
export interface StatementImport { id: number; businessId: number; bankAccountId: number; sourceFileName: string; sourceFileChecksum: string; importedRowCount: number; acceptedRowCount: number; rejectedRowCount: number; status: string; errorSummary?: string | null; }
export interface StatementLine { id: number; bankAccountId: number; bankStatementImportId: number; sourceRowNumber: number; transactionDate: string; valueDate?: string | null; externalTransactionReference?: string | null; description: string; debitAmount: number; creditAmount: number; signedAmount: number; runningBalance?: number | null; }
export interface StatementAdjustment { id: number; bankStatementLineId: number; adjustmentType: string; amount: number; status: string; journalEntryId?: number | null; reversalJournalEntryId?: number | null; }
export interface StatementException { id: number; bankStatementLineId: number; classification: string; status: string; reason?: string | null; resolutionNotes?: string | null; createdBy: number; createdAt: string; resolvedBy?: number | null; resolvedAt?: string | null; }
export interface ReconciliationMatchDetail { id: number; sessionId: number; status: string; statementTotal: number; glTotal: number; statementLineIds: number[]; journalEntryLineIds: number[]; }
export interface SettlementBatch { id: number; settlementType: string; settlementDate: string; grossClearingAmount: number; settlementFeeAmount: number; netBankAmount: number; status: string; externalReference?: string | null; journalEntryId?: number | null; reversalJournalEntryId?: number | null; bankAccountId: number; }
export interface VatReportFilter { dateFrom: string; dateTo: string; outletId?: number; warehouseId?: number; vatDirection?: string; sourceType?: string; sourceReference?: string; taxCode?: string; vatRate?: number; }
export interface VatTransaction { postingDate: string; sourceType: string; sourceReference: string; journalEntryId?: number | null; journalNumber?: string | null; outletId?: number | null; warehouseId?: number | null; taxCode: string; vatRate?: number | null; taxableAmount: number; vatAmount: number; vatDirection: string; originalJournalEntryId?: number | null; exceptionReason?: string | null; }
export interface VatSummary { dateFrom: string; dateTo: string; netTaxableSales: number; outputVatFromSales: number; salesReturnVatAdjustments: number; netTaxableOtherIncome: number; otherIncomeOutputVat: number; totalOutputVat: number; netTaxablePurchases: number; purchaseInputVat: number; netTaxableExpenses: number; expenseInputVat: number; totalInputVat: number; netVatPosition: number; position: string; exceptionCount: number; breakdown: Array<{ taxCode: string; vatRate?: number | null; taxableAmount: number; outputVat: number; inputVat: number }>; }
export interface VatReconciliation { reportOutputVat: number; glOutputVat: number; outputDifference: number; outputStatus: string; reportInputVat: number; glInputVat: number; inputDifference: number; inputStatus: string; reportNetVatPosition: number; glNetVatPosition: number; overallDifference: number; }
export interface VatException { postingDate?: string | null; sourceType: string; sourceReference: string; journalEntryId?: number | null; journalNumber?: string | null; reason: string; knownVatAmount?: number | null; }

export interface ApReportFilter { asOfDate?: string; dateFrom?: string; dateTo?: string; supplierId?: number; outletId?: number; warehouseId?: number; status?: string; disputed?: boolean; agingBucket?: string; sourceReference?: string; }
export interface ApPayableRow { payableDocumentId: number; supplierId: number; supplierName: string; sourceDocumentType: string; sourceDocumentId: number; sourceReference: string; recognitionDate: string; supplierInvoiceNumber?: string | null; supplierInvoiceDate?: string | null; dueDate: string; originalPayableAmount: number; effectivePaymentAllocations: number; effectiveAdvanceUtilisations: number; reversedPaymentAllocations: number; reversedAdvanceUtilisations: number; outstandingAmount: number; status: string; isDisputed: boolean; disputeReason?: string | null; warehouseId?: number | null; sourceOutletId?: number | null; sourceJournalEntryId: number; sourceJournalNumber?: string | null; }
export interface ApSupplierTotal { supplierId: number; supplierName: string; totalOutstanding: number; disputedAmount: number; }
export interface ApExceptionSummary { legacyUnreconciledCount: number; supplierOwnershipExceptionCount: number; details: string[]; }
export interface ApSubledgerReport { asOfDate: string; totalOutstanding: number; rows: ApPayableRow[]; supplierTotals: ApSupplierTotal[]; exceptions: ApExceptionSummary; }
export interface ApAgingReport extends ApSubledgerReport { currentNotDue: number; overdue1To30: number; overdue31To60: number; overdue61To90: number; overdue90Plus: number; disputedAmount: number; }
export interface SupplierStatementRow { date: string; eventType: string; payableDocumentId: number; sourceReference: string; journalReference?: string | null; debit: number; credit: number; runningBalance: number; }
export interface SupplierStatementReport { supplierId: number; supplierName: string; dateFrom: string; dateTo: string; openingBalance: number; newPayables: number; supplierPayments: number; supplierAdvanceApplications: number; reversals: number; closingBalance: number; rows: SupplierStatementRow[]; }
export interface ApReconciliationReport { asOfDate: string; accountId: number; accountSlot: string; subledgerTotal: number; generalLedgerBalance: number; reconciliationDifference: number; unreconciledDetails: string[]; }

export interface BalanceSheetAccount {
  accountId: number;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalanceType: string;
  presentationRole?: string | null;
  normalBalance: number;
  statementBalance: number;
  rollupNormalBalance: number;
  rollupStatementBalance: number;
  children: BalanceSheetAccount[];
}

export interface BalanceSheetSection { name: string; total: number; accounts: BalanceSheetAccount[]; }

export interface BalanceSheetReport {
  asOfDate: string;
  isManagementView: boolean;
  cutoverWarning?: string | null;
  assets: BalanceSheetSection;
  liabilities: BalanceSheetSection;
  equity: BalanceSheetSection;
  currentYearEarnings: number;
  unclosedPriorYearEarnings: number;
  totalAssets: number;
  totalLiabilitiesAndEquity: number;
  reconciliationDifference: number;
}

export interface ComparativeBalanceSheetReport { current: BalanceSheetReport; comparison: BalanceSheetReport; }

export interface EnhancedGeneralLedgerRow {
  journalEntryId: number;
  journalEntryLineId: number;
  postingDate: string;
  businessDate?: string | null;
  journalNumber?: string | null;
  journalType: string;
  referenceType: string;
  referenceId: string;
  referenceNumber?: string | null;
  narration?: string | null;
  debitAmount: number;
  creditAmount: number;
  runningBalance: number;
  outletId?: number | null;
  warehouseId?: number | null;
  customerId?: number | null;
  supplierId?: number | null;
  paymentMethodId?: string | null;
  costCenterId?: string | null;
  reversalOfJournalEntryId?: number | null;
  reversedByJournalEntryId?: number | null;
}

export interface EnhancedGeneralLedgerReport {
  accountId: number;
  accountCode: string;
  accountName: string;
  normalBalanceType: string;
  openingBalance: number;
  totalDebits: number;
  totalCredits: number;
  closingBalance: number;
  rows: EnhancedGeneralLedgerRow[];
}

export interface CashFlowRow {
  journalEntryId: number;
  journalEntryLineId: number;
  postingDate: string;
  businessDate?: string | null;
  journalNumber?: string | null;
  journalType: string;
  referenceType: string;
  referenceId: string;
  referenceNumber?: string | null;
  narration?: string | null;
  accountSlot: string;
  accountId: number;
  accountCode: string;
  accountName: string;
  inflow: number;
  outflow: number;
  netMovement: number;
  classificationReason: string;
  counterpartAccounts: string;
  counterpartAccountIds: number[];
  outletId?: number | null;
  warehouseId?: number | null;
  customerId?: number | null;
  supplierId?: number | null;
  paymentMethodId?: string | null;
  costCenterId?: string | null;
  reversalOfJournalEntryId?: number | null;
  reversedByJournalEntryId?: number | null;
}

export interface CashFlowSection {
  activityType: 'Operating' | 'Investing' | 'Financing' | 'Unclassified' | string;
  totalInflows: number;
  totalOutflows: number;
  netMovement: number;
  rows: CashFlowRow[];
}

export interface CashFlowReport {
  dateFrom: string;
  dateTo: string;
  method: string;
  openingLiquidity: number;
  closingLiquidity: number;
  operatingNetMovement: number;
  investingNetMovement: number;
  financingNetMovement: number;
  unclassifiedNetMovement: number;
  totalExternalNetMovement: number;
  glDerivedMovement: number;
  reconciliationDifference: number;
  internalTransferNetMovement: number;
  cutoverAndNonCashNetMovement: number;
  sections: CashFlowSection[];
  internalTransferRows: CashFlowRow[];
  cutoverAndNonCashRows: CashFlowRow[];
}

export interface ComparativeCashFlowReport { current: CashFlowReport; comparison: CashFlowReport; }

export interface BusinessProfitabilityReport {
  dateFrom: string;
  dateTo: string;
  revenue: number;
  costOfGoodsSold: number;
  expenses: number;
  netProfit: number;
  authoritativePnlNetProfit: number;
  reconciliationDifference: number;
  source: string;
}

export interface OutletProfitabilityRow {
  outletId?: number | null;
  outletName: string;
  revenue: number;
  costOfGoodsSold: number;
  expenses: number;
  netProfit: number;
  unallocatedReason?: string | null;
}

export interface OutletProfitabilityReport {
  dateFrom: string;
  dateTo: string;
  outlets: OutletProfitabilityRow[];
  unallocated: OutletProfitabilityRow;
  businessRevenue: number;
  businessCostOfGoodsSold: number;
  businessExpenses: number;
  businessNetProfit: number;
  attributedRevenue: number;
  attributedCostOfGoodsSold: number;
  attributedExpenses: number;
  attributedNetProfit: number;
  reconciliationDifference: number;
}

export interface ProductContributionRow {
  productId: number;
  productName: string;
  productCode?: string | null;
  categoryId: number;
  categoryName: string;
  revenue: number;
  costOfGoodsSold: number;
  contribution: number;
  marginPercent: number;
}

export interface CategoryContributionRow {
  categoryId: number;
  categoryName: string;
  revenue: number;
  costOfGoodsSold: number;
  contribution: number;
  marginPercent: number;
}

export interface ProductContributionReport {
  dateFrom: string;
  dateTo: string;
  contributionBasis: string;
  products: ProductContributionRow[];
  categories: CategoryContributionRow[];
  attributedRevenue: number;
  attributedCostOfGoodsSold: number;
  attributedContribution: number;
  authoritativeRevenue: number;
  authoritativeCostOfGoodsSold: number;
  unallocatedRevenue: number;
  unallocatedCostOfGoodsSold: number;
  revenueReconciliationDifference: number;
  cogsReconciliationDifference: number;
  historicalLimitation?: string | null;
}

export interface ComparativeBusinessProfitabilityReport { current: BusinessProfitabilityReport; comparison: BusinessProfitabilityReport; }
export interface ComparativeOutletProfitabilityReport { current: OutletProfitabilityReport; comparison: OutletProfitabilityReport; }
export interface ComparativeProductContributionReport { current: ProductContributionReport; comparison: ProductContributionReport; }

export interface CreateAccountDto {
  name: string;
  type: string;
}

export interface UpdateAccountDto {
  name: string;
  type: string;
}

export interface TransactionDto {
  id: number;
  accountId: number;
  accountName: string;
  amount: number;
  type: string; // debit, credit
  description?: string;
  transactionDate: string;
  referenceType?: string;
  referenceId?: number;
}

export interface CreateTransactionDto {
  accountId: number;
  amount: number;
  type: string;
  description?: string;
  transactionDate: string;
  referenceType?: string;
  referenceId?: number;
}

export interface TransactionSearchRequest {
  accountId?: number;
  startDate?: string;
  endDate?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface ExpenseDto {
  id: number;
  expenseNumber?: string;
  category: string;
  expenseCategoryId?: number;
  expenseAccountId?: number;
  amount: number;
  paymentMethodId?: string;
  paymentAccountId?: number;
  referenceNumber?: string;
  description?: string;
  attachmentPath?: string;
  expenseDate: string;
  outletId?: number;
  outletName?: string;
  warehouseId?: number | null;
  warehouseName?: string | null;
  status: string;
  journalEntryId?: number;
  createdAt: string;
  submittedAt?: string | null;
  approvedAt?: string | null;
  rejectedAt?: string | null;
  rejectedReason?: string;
  cancellationReason?: string;
  cancelledAt?: string | null;
  paymentSource?: string | null;
  disbursedAtUtc?: string | null;
  disbursedCashSessionId?: number | null;
  selfApprovedUnderOwnerException?: boolean;
}

export interface ExpenseCategoryDto {
  id: number;
  name: string;
  expenseAccountId: number;
  expenseAccountCode?: string;
  expenseAccountName?: string;
  isActive: boolean;
}

export interface ChartAccountRow {
  id: number;
  accountCode: string;
  accountName: string;
  accountType: string;
  parentAccountId?: number | null;
  isControlAccount: boolean;
  allowManualPosting: boolean;
  isActive: boolean;
  displayOrder: number;
  isExpensePaymentAccount: boolean;
  normalBalanceType?: string;
  isSystemAccount?: boolean;
}

export interface CreateChartAccountDto {
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalanceType: string;
  parentAccountId: number | null;
  isControlAccount: boolean;
  allowManualPosting: boolean;
  isActive: boolean;
  displayOrder: number;
}

/** Code, type and normal balance cannot change after creation, so the API does not accept them here. */
export interface UpdateChartAccountDto {
  accountName: string;
  parentAccountId: number | null;
  isControlAccount: boolean;
  allowManualPosting: boolean;
  isActive: boolean;
  displayOrder: number;
}

export interface ExpensePaymentAccountDto {
  id: number;
  accountCode: string;
  accountName: string;
  accountType: string;
}

/** A ledger account an expense category may be mapped to. */
export interface ExpenseAccountOptionDto {
  id: number;
  accountCode: string;
  accountName: string;
}

export interface ExpensePaymentAccountFilter {
  paymentSource?: string;
  outletId?: number;
  warehouseId?: number;
}

export interface ExpenseAttachmentDto {
  id: number;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  createdAt: string;
  createdBy?: number | null;
}

export interface CreateExpenseDto {
  /** bank | petty_cash | pos_drawer (server-validated; a POS drawer expense needs an outlet). */
  paymentSource?: string;
  expenseCategoryId: number;
  amount: number;
  paymentMethodId?: string;
  paymentAccountId: number;
  referenceNumber?: string;
  description?: string;
  attachmentPath?: string;
  expenseDate: string;
  outletId?: number;
  warehouseId?: number;
}

export interface UpdateExpenseDto {
  paymentSource?: string;
  expenseCategoryId: number;
  amount: number;
  paymentMethodId?: string;
  paymentAccountId: number;
  referenceNumber?: string;
  description?: string;
  attachmentPath?: string;
  expenseDate: string;
  outletId?: number;
  warehouseId?: number;
}

export interface ApproveExpenseDto {
  expenseAccountId?: number;
}

export interface ExpenseSearchRequest {
  category?: string;
  status?: string;
  startDate?: string;
  endDate?: string;
  outletId?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface ExpenseSummaryDto {
  totalAmount: number;
  byCategory: { [key: string]: number };
  count: number;
}

export interface SupplierPaymentAllocationLineDto {
  billId: number;
  amount: number;
}

export interface CreateSupplierPaymentDto {
  supplierId: number;
  outletId?: number;
  paymentDate: string;
  paymentMethod: string;
  paymentAccountId: number;
  amount: number;
  referenceNumber?: string;
  notes?: string;
  allocations?: SupplierPaymentAllocationLineDto[];
}

export interface SupplierPaymentDto {
  id: number;
  supplierId: number;
  supplierName: string;
  outletId?: number;
  paymentDate: string;
  postingDate?: string;
  paymentMethod: string;
  paymentAccountId: number;
  amount: number;
  allocatedAmount: number;
  advanceAmount: number;
  status: string;
  referenceNumber?: string;
  notes?: string;
  journalEntryId?: number;
}

export interface CreateSupplierAdvanceDto {
  supplierId: number;
  outletId?: number;
  paymentDate: string;
  paymentMethod: string;
  paymentAccountId: number;
  amount: number;
  referenceNumber?: string;
  notes?: string;
}

export interface SupplierAdvanceDto {
  id: number;
  supplierId: number;
  supplierName: string;
  outletId?: number;
  paymentDate: string;
  paymentMethod: string;
  paymentAccountId: number;
  amount: number;
  remainingAmount: number;
  appliedAmount: number;
  status: string;
  referenceNumber?: string;
  notes?: string;
  journalEntryId?: number;
}

export interface FinancialYearDto {
  id: number;
  yearCode: string;
  name: string;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
  isClosed: boolean;
  closedAt?: string | null;
  closedBy?: number | null;
  status?: string;
}

export interface CreateFinancialYearDto {
  yearCode: string;
  name: string;
  startDate: string;
  endDate: string;
  isCurrent?: boolean;
}

export interface UpdateFinancialYearDto {
  yearCode: string;
  name: string;
  startDate: string;
  endDate: string;
}

export interface AccountingPeriodDto {
  id: number;
  financialYearId: number;
  periodNumber: number;
  name: string;
  startDate: string;
  endDate: string;
  status: string;
  statusReason?: string | null;
  closedAt?: string | null;
  closedBy?: number | null;
  lockedAt?: string | null;
  lockedBy?: number | null;
  reopenedAt?: string | null;
  reopenedBy?: number | null;
}

export interface CreateAccountingPeriodDto {
  periodNumber: number;
  name: string;
  startDate: string;
  endDate: string;
  status: string;
}

export interface UpdateAccountingPeriodDto {
  periodNumber: number;
  name: string;
  startDate: string;
  endDate: string;
}

export interface OpeningBalanceStatusDto {
  hasPostedOpeningBalance: boolean;
  openingBalanceJournalEntryId?: number | null;
  openingBalanceJournalNumber?: string | null;
  openingBalancePostedAt?: string | null;
  isOpeningBalanceLocked: boolean;
  lockingJournalEntryId?: number | null;
  lockingJournalNumber?: string | null;
  lockingJournalType?: string | null;
  lockedAt?: string | null;
  canPostOpeningBalance: boolean;
  status?: string;
  notes?: string;
  postedAt?: string | null;
}

export interface OpeningBalanceAdjustmentDto {
  reason: string;
  adjustmentBusinessDate: string;
  postingDate: string;
  referenceNumber?: string;
  description?: string;
  lines: Array<{ accountId: number; debitAmount: number; creditAmount: number; outletId?: number | null; warehouseId?: number | null; description?: string }>;
}

export interface BillDto {
  id: number;
  supplierId: number;
  supplierName: string;
  poId?: number;
  poNumber?: string;
  amountDue: number;
  dueDate: string;
  status: string; // unpaid, partial, paid
  isOverdue: boolean;
}

export interface CreateBillDto {
  supplierId: number;
  poId?: number;
  amountDue: number;
  dueDate: string;
}

export interface UpdateBillStatusDto {
  status: string;
}

export interface BillSearchRequest {
  status?: string;
  supplierId?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface BillSummaryDto {
  totalUnpaid: number;
  totalOverdue: number;
  unpaidCount: number;
  overdueCount: number;
}

export interface OtherIncomeEntryDto {
  id: number;
  businessId: number;
  incomeDate: string;
  incomeCategory: string;
  incomeAccountId?: number;
  amount: number;
  paymentMethod?: string;
  paymentAccountId?: number;
  referenceNumber?: string;
  description?: string;
  status: string;
  outletId?: number;
  journalEntryId?: number;
  createdAt: string;
  submittedAt?: string | null;
  approvedAt?: string | null;
  rejectedAt?: string | null;
  rejectionReason?: string;
  cancelledAt?: string | null;
  cancellationReason?: string;
}

export interface CreateOtherIncomeEntryDto {
  incomeDate: string;
  incomeCategory: string;
  incomeAccountId?: number;
  amount: number;
  paymentMethod?: string;
  paymentAccountId?: number;
  referenceNumber?: string;
  description?: string;
  outletId?: number;
}

export interface UpdateOtherIncomeEntryDto extends CreateOtherIncomeEntryDto {}

export interface ApproveOtherIncomeEntryDto {
  incomeAccountId?: number;
}

export interface OtherIncomeSearchRequest {
  startDate?: string;
  endDate?: string;
  status?: string;
  outletId?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface JournalEntryAttachmentDto {
  id: number;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  createdAt: string;
  createdBy?: number;
}

export interface JournalEntryLineDto {
  id?: number;
  accountId: number;
  description?: string;
  debitAmount: number;
  creditAmount: number;
  outletId?: number;
  warehouseId?: number;
  supplierId?: number;
  customerId?: number;
  paymentMethodId?: string;
  costCenterId?: string;
}

export interface JournalEntryDto {
  id: number;
  businessId: number;
  journalNumber?: string;
  journalType: string;
  businessDate?: string | null;
  transactionDate: string;
  postingDate: string;
  financialYearId?: number;
  accountingPeriodId?: number;
  referenceType: string;
  referenceId: string;
  referenceNumber?: string;
  description?: string;
  outletId?: number;
  warehouseId?: number;
  terminalId?: number;
  cashSessionId?: number;
  status: string;
  isSystemGenerated: boolean;
  submittedAt?: string | null;
  submittedBy?: number;
  approvedAt?: string | null;
  approvedBy?: number;
  rejectedAt?: string | null;
  rejectedBy?: number;
  rejectionReason?: string;
  postedAt?: string | null;
  postedBy?: number;
  cancelledAt?: string | null;
  cancelledBy?: number;
  cancellationReason?: string;
  reversedAt?: string | null;
  reversedBy?: number;
  createdAt: string;
  createdBy?: number;
  updatedAt: string;
  updatedBy?: number;
  lines: JournalEntryLineDto[];
  attachments: JournalEntryAttachmentDto[];
}

export interface CreateJournalEntryDto {
  journalType?: string;
  transactionDate: string;
  postingDate: string;
  businessDate?: string;
  referenceType?: string;
  referenceId?: string;
  referenceNumber?: string;
  description: string;
  outletId?: number;
  warehouseId?: number;
  terminalId?: number;
  cashSessionId?: number;
  lines: JournalEntryLineDto[];
}

export interface UpdateJournalEntryDto extends CreateJournalEntryDto {}

export interface ReverseJournalEntryDto {
  reason: string;
  postingDate?: string;
  businessDate?: string;
}

export interface JournalSearchRequest {
  postingDateStart?: string;
  postingDateEnd?: string;
  businessDateStart?: string;
  businessDateEnd?: string;
  status?: string;
  outletId?: number;
  journalType?: string;
  journalNumber?: string;
  accountId?: number;
  referenceType?: string;
  referenceNumber?: string;
  createdBy?: number;
  postedBy?: number;
  sortBy?: string;
  sortDirection?: string;
  pageNumber?: number;
  pageSize?: number;
}
