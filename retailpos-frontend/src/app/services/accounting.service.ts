import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AccountDto, CreateAccountDto, UpdateAccountDto,
  TransactionDto, CreateTransactionDto, TransactionSearchRequest,
  ExpenseDto, ExpenseCategoryDto, CreateExpenseDto, UpdateExpenseDto, ApproveExpenseDto, ExpenseSearchRequest, ExpenseSummaryDto,
  OtherIncomeEntryDto, CreateOtherIncomeEntryDto, UpdateOtherIncomeEntryDto, ApproveOtherIncomeEntryDto, OtherIncomeSearchRequest,
  CreateSupplierPaymentDto, SupplierPaymentDto, CreateSupplierAdvanceDto, SupplierAdvanceDto,
  JournalEntryDto, CreateJournalEntryDto, UpdateJournalEntryDto, ReverseJournalEntryDto, JournalSearchRequest, JournalEntryAttachmentDto,
  BillDto, CreateBillDto, UpdateBillStatusDto, BillSearchRequest, BillSummaryDto,
  FinancialYearDto, CreateFinancialYearDto, UpdateFinancialYearDto, AccountingPeriodDto, CreateAccountingPeriodDto, UpdateAccountingPeriodDto,
  OpeningBalanceStatusDto, OpeningBalanceAdjustmentDto, PeriodCloseRun, FinancialYearCloseRun
} from '../models/accounting.model';
import { BankAccountOption, ReconciliationSession, ReconciliationCandidate, ReconciliationMatch, ReconciliationCalculation, StatementImport, StatementLine, StatementAdjustment, StatementException, ReconciliationMatchDetail, SettlementBatch } from '../models/accounting.model';
import { AccountingReportResponse, BalanceSheetReport, ComparativeBalanceSheetReport, EnhancedGeneralLedgerReport, CashFlowReport, ComparativeCashFlowReport, BusinessProfitabilityReport, ComparativeBusinessProfitabilityReport, OutletProfitabilityReport, ComparativeOutletProfitabilityReport, ProductContributionReport, ComparativeProductContributionReport, ApReportFilter, ApSubledgerReport, ApAgingReport, SupplierStatementReport, ApReconciliationReport, VatReportFilter, VatSummary, VatTransaction, VatReconciliation, VatException } from '../models/accounting.model';

@Injectable({
  providedIn: 'root'
})
export class AccountingService {
  private accountsUrl = `${environment.apiUrl}/accounts`;
  private transactionsUrl = `${environment.apiUrl}/transactions`;
  private accountingUrl = `${environment.apiUrl}/accounting`;
  private journalsUrl = `${this.accountingUrl}/journals`;
  private expensesUrl = `${this.accountingUrl}/expenses`;
  private otherIncomeUrl = `${this.accountingUrl}/incomes`;
  private supplierPaymentsUrl = `${this.accountingUrl}/supplier-payments`;
  private billsUrl = `${environment.apiUrl}/bills`;
  private openingBalancesUrl = `${this.accountingUrl}/opening-balances`;
  private reportsUrl = `${this.accountingUrl}/reports`;

  // Accounts signals
  accounts = signal<AccountDto[]>([]);
  // Transactions signals
  transactions = signal<TransactionDto[]>([]);
  transactionsTotalCount = signal<number>(0);
  journals = signal<JournalEntryDto[]>([]);
  journalsTotalCount = signal<number>(0);
  // Expenses signals
  expenses = signal<ExpenseDto[]>([]);
  expensesTotalCount = signal<number>(0);
  expenseSummary = signal<ExpenseSummaryDto | null>(null);
  expenseCategories = signal<ExpenseCategoryDto[]>([]);
  otherIncomeEntries = signal<OtherIncomeEntryDto[]>([]);
  otherIncomeTotalCount = signal<number>(0);
  // Bills signals
  bills = signal<BillDto[]>([]);
  billsTotalCount = signal<number>(0);
  billSummary = signal<BillSummaryDto | null>(null);

  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  // ─── Financial periods ─────────────────────────────────────────────────

  getFinancialYears(): Observable<any> {
    return this.http.get<any>(`${this.accountingUrl}/financial-years`);
  }

  createFinancialYear(dto: CreateFinancialYearDto): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/financial-years`, dto);
  }

  updateFinancialYear(id: number, dto: UpdateFinancialYearDto): Observable<any> {
    return this.http.put<any>(`${this.accountingUrl}/financial-years/${id}`, dto);
  }

  activateFinancialYear(id: number): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/financial-years/${id}/activate`, {});
  }

  closeFinancialYear(id: number, reason?: string): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/financial-years/${id}/close`, reason ? { reason } : {});
  }

  reopenFinancialYear(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/financial-years/${id}/reopen`, { reason });
  }

  getPeriods(financialYearId: number): Observable<any> {
    return this.http.get<any>(`${this.accountingUrl}/financial-years/${financialYearId}/periods`);
  }

  createPeriod(financialYearId: number, dto: CreateAccountingPeriodDto): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/financial-years/${financialYearId}/periods`, dto);
  }

  closePeriod(id: number, reason?: string): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/periods/${id}/close`, reason ? { reason } : {});
  }

  lockPeriod(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/periods/${id}/lock`, { reason });
  }

  reopenPeriod(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.accountingUrl}/periods/${id}/reopen`, { reason });
  }

  getPeriodCloseRuns(periodId?: number): Observable<{ data: PeriodCloseRun[] }> { return this.http.get<{ data: PeriodCloseRun[] }>(`${this.accountingUrl}/period-close-runs`, { params: periodId ? { accountingPeriodId: periodId } : {} }); }
  getPeriodCloseRun(id: number): Observable<{ data: PeriodCloseRun }> { return this.http.get<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${id}`); }
  preparePeriodClose(periodId: number): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/periods/${periodId}/prepare`, {}); }
  reevaluatePeriodClose(id: number): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${id}/re-evaluate`, {}); }
  approveCloseException(runId: number, gateId: number, reason: string): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${runId}/gates/${gateId}/approve-exception`, { reason }); }
  approvePeriodClose(id: number): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${id}/approve`, {}); }
  executePeriodClose(id: number, reason?: string): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${id}/execute`, { reason }); }
  reopenPeriodClose(id: number, reason: string): Observable<{ data: PeriodCloseRun }> { return this.http.post<{ data: PeriodCloseRun }>(`${this.accountingUrl}/period-close-runs/${id}/reopen`, { reason }); }
  downloadPeriodClosePackage(id: number): Observable<Blob> { return this.http.get(`${this.accountingUrl}/period-close-runs/${id}/package`, { responseType: 'blob' }); }
  getFinancialYearCloseRuns(yearId?: number): Observable<{ data: FinancialYearCloseRun[] }> { return this.http.get<{ data: FinancialYearCloseRun[] }>(`${this.accountingUrl}/financial-year-close-runs`, { params: yearId ? { financialYearId: yearId } : {} }); }
  prepareFinancialYearClose(yearId: number): Observable<{ data: FinancialYearCloseRun }> { return this.http.post<{ data: FinancialYearCloseRun }>(`${this.accountingUrl}/financial-year-close-runs/financial-years/${yearId}/prepare`, {}); }
  approveFinancialYearClose(id: number): Observable<{ data: FinancialYearCloseRun }> { return this.http.post<{ data: FinancialYearCloseRun }>(`${this.accountingUrl}/financial-year-close-runs/${id}/approve`, {}); }
  executeFinancialYearClose(id: number): Observable<{ data: FinancialYearCloseRun }> { return this.http.post<{ data: FinancialYearCloseRun }>(`${this.accountingUrl}/financial-year-close-runs/${id}/execute`, {}); }
  reopenFinancialYearClose(id: number, reason: string, postingDate: string): Observable<{ data: FinancialYearCloseRun }> { return this.http.post<{ data: FinancialYearCloseRun }>(`${this.accountingUrl}/financial-year-close-runs/${id}/reopen`, { reason, postingDate }); }
  downloadFinancialYearClosePackage(id: number): Observable<Blob> { return this.http.get(`${this.accountingUrl}/financial-year-close-runs/${id}/package`, { responseType: 'blob' }); }

  // ─── Opening balances ───────────────────────────────────────────────────

  getOpeningBalanceStatus(): Observable<any> {
    return this.http.get<any>(`${this.openingBalancesUrl}/status`);
  }

  postOpeningBalance(): Observable<any> {
    return this.http.post<any>(this.openingBalancesUrl, {});
  }

  adjustOpeningBalance(openingBalanceId: number, dto: OpeningBalanceAdjustmentDto): Observable<any> {
    return this.http.post<any>(`${this.openingBalancesUrl}/${openingBalanceId}/adjustments`, dto);
  }

  // ─── Reports ────────────────────────────────────────────────────────────

  getGeneralLedger(fromDate?: string, toDate?: string): Observable<any> {
    let params = new HttpParams();
    if (fromDate) params = params.set('dateFrom', fromDate);
    if (toDate) params = params.set('dateTo', toDate);
    return this.http.get<any>(`${this.reportsUrl}/general-ledger`, { params });
  }

  getTrialBalance(fromDate?: string, toDate?: string): Observable<any> {
    let params = new HttpParams();
    if (fromDate) params = params.set('dateFrom', fromDate);
    if (toDate) params = params.set('dateTo', toDate);
    return this.http.get<any>(`${this.reportsUrl}/trial-balance`, { params });
  }

  getProfitLoss(fromDate?: string, toDate?: string): Observable<any> {
    let params = new HttpParams();
    if (fromDate) params = params.set('dateFrom', fromDate);
    if (toDate) params = params.set('dateTo', toDate);
    return this.http.get<any>(`${this.reportsUrl}/profit-loss`, { params });
  }

  getCogsReport(filters: Record<string, string | number | undefined>): Observable<any> {
    return this.http.get<any>(`${this.reportsUrl}/cogs`, { params: this.reportParams(filters) });
  }

  getInventoryValuationReport(filters: Record<string, string | number | undefined>): Observable<any> {
    return this.http.get<any>(`${this.reportsUrl}/inventory-valuation`, { params: this.reportParams(filters) });
  }

  getApSubledger(filters: ApReportFilter): Observable<AccountingReportResponse<ApSubledgerReport>> { return this.http.get<AccountingReportResponse<ApSubledgerReport>>(`${this.reportsUrl}/ap-subledger`, { params: this.reportParams(filters as Record<string, string | number | undefined>) }); }
  getApAging(filters: ApReportFilter): Observable<AccountingReportResponse<ApAgingReport>> { return this.http.get<AccountingReportResponse<ApAgingReport>>(`${this.reportsUrl}/ap-aging`, { params: this.reportParams(filters as Record<string, string | number | undefined>) }); }
  getSupplierStatement(filters: ApReportFilter): Observable<AccountingReportResponse<SupplierStatementReport>> { return this.http.get<AccountingReportResponse<SupplierStatementReport>>(`${this.reportsUrl}/supplier-statement`, { params: this.reportParams(filters as Record<string, string | number | undefined>) }); }
  getApReconciliation(filters: ApReportFilter, advance = false): Observable<AccountingReportResponse<ApReconciliationReport>> { return this.http.get<AccountingReportResponse<ApReconciliationReport>>(`${this.reportsUrl}/${advance ? 'supplier-advance-reconciliation' : 'ap-reconciliation'}`, { params: this.reportParams(filters as Record<string, string | number | undefined>) }); }
  getVatSummary(filters: VatReportFilter): Observable<AccountingReportResponse<VatSummary>> { return this.http.get<AccountingReportResponse<VatSummary>>(`${this.reportsUrl}/vat-summary`, { params: this.reportParams(filters as unknown as Record<string, string | number | undefined>) }); }
  getVatDetails(filters: VatReportFilter): Observable<AccountingReportResponse<VatTransaction[]>> { return this.http.get<AccountingReportResponse<VatTransaction[]>>(`${this.reportsUrl}/vat-details`, { params: this.reportParams(filters as unknown as Record<string, string | number | undefined>) }); }
  getVatReconciliation(filters: VatReportFilter): Observable<AccountingReportResponse<VatReconciliation>> { return this.http.get<AccountingReportResponse<VatReconciliation>>(`${this.reportsUrl}/vat-reconciliation`, { params: this.reportParams(filters as unknown as Record<string, string | number | undefined>) }); }
  getVatExceptions(filters: VatReportFilter): Observable<AccountingReportResponse<{ items: VatException[] }>> { return this.http.get<AccountingReportResponse<{ items: VatException[] }>>(`${this.reportsUrl}/vat-exceptions`, { params: this.reportParams(filters as unknown as Record<string, string | number | undefined>) }); }

  getCashBookReport(filters: Record<string, string | number | string[] | undefined>): Observable<any> {
    let params = new HttpParams();
    Object.entries(filters).forEach(([key, value]) => {
      if (value === undefined || value === '') return;
      if (Array.isArray(value)) value.forEach(item => params = params.append(key, item));
      else params = params.set(key, String(value));
    });
    return this.http.get<any>(`${this.reportsUrl}/cash-book`, { params });
  }

  getCashFlow(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<CashFlowReport>> {
    return this.http.get<AccountingReportResponse<CashFlowReport>>(`${this.reportsUrl}/cash-flow`, { params: this.reportParams(filters) });
  }

  getComparativeCashFlow(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ComparativeCashFlowReport>> {
    return this.http.get<AccountingReportResponse<ComparativeCashFlowReport>>(`${this.reportsUrl}/cash-flow/comparative`, { params: this.reportParams(filters) });
  }

  exportCashFlow(filters: Record<string, string | number | undefined>): Observable<Blob> {
    return this.http.get(`${this.reportsUrl}/cash-flow/export`, { params: this.reportParams(filters), responseType: 'blob' });
  }

  getBusinessProfitability(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<BusinessProfitabilityReport>> {
    return this.http.get<AccountingReportResponse<BusinessProfitabilityReport>>(`${this.reportsUrl}/business-profitability`, { params: this.reportParams(filters) });
  }

  getComparativeBusinessProfitability(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ComparativeBusinessProfitabilityReport>> {
    return this.http.get<AccountingReportResponse<ComparativeBusinessProfitabilityReport>>(`${this.reportsUrl}/business-profitability/comparative`, { params: this.reportParams(filters) });
  }

  getOutletProfitability(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<OutletProfitabilityReport>> {
    return this.http.get<AccountingReportResponse<OutletProfitabilityReport>>(`${this.reportsUrl}/outlet-profitability`, { params: this.reportParams(filters) });
  }

  getComparativeOutletProfitability(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ComparativeOutletProfitabilityReport>> {
    return this.http.get<AccountingReportResponse<ComparativeOutletProfitabilityReport>>(`${this.reportsUrl}/outlet-profitability/comparative`, { params: this.reportParams(filters) });
  }

  getProductContribution(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ProductContributionReport>> {
    return this.http.get<AccountingReportResponse<ProductContributionReport>>(`${this.reportsUrl}/product-profitability`, { params: this.reportParams(filters) });
  }

  getComparativeProductContribution(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ComparativeProductContributionReport>> {
    return this.http.get<AccountingReportResponse<ComparativeProductContributionReport>>(`${this.reportsUrl}/product-profitability/comparative`, { params: this.reportParams(filters) });
  }

  exportProfitability(reportType: 'business' | 'outlet' | 'product', filters: Record<string, string | number | undefined>): Observable<Blob> {
    return this.http.get(`${this.reportsUrl}/profitability/export`, { params: this.reportParams({ ...filters, reportType }), responseType: 'blob' });
  }

  getAccountingConfiguration(): Observable<any> {
    return this.http.get<any>(`${this.accountingUrl}/configuration`);
  }

  getBankAccounts(): Observable<{ data: BankAccountOption[] }> { return this.http.get<{ data: BankAccountOption[] }>(`${this.accountingUrl}/bank-accounts`); }
  createReconciliationSession(dto: { bankAccountId: number; periodStart: string; periodEnd: string }): Observable<{ data: ReconciliationSession }> { return this.http.post<{ data: ReconciliationSession }>(`${this.accountingUrl}/reconciliation/sessions`, dto); }
  getReconciliationSessions(bankAccountId?: number): Observable<{ data: ReconciliationSession[] }> { let params = new HttpParams(); if (bankAccountId) params = params.set('bankAccountId', bankAccountId); return this.http.get<{ data: ReconciliationSession[] }>(`${this.accountingUrl}/reconciliation/sessions`, { params }); }
  getReconciliationCalculation(id: number): Observable<{ data: ReconciliationCalculation }> { return this.http.get<{ data: ReconciliationCalculation }>(`${this.accountingUrl}/reconciliation/sessions/${id}/calculation`); }
  getStatementCandidates(id: number): Observable<{ data: ReconciliationCandidate[] }> { return this.http.get<{ data: ReconciliationCandidate[] }>(`${this.accountingUrl}/reconciliation/sessions/${id}/statement-candidates`); }
  getGlCandidates(id: number): Observable<{ data: ReconciliationCandidate[] }> { return this.http.get<{ data: ReconciliationCandidate[] }>(`${this.accountingUrl}/reconciliation/sessions/${id}/gl-candidates`); }
  createReconciliationMatch(dto: { sessionId: number; statementLineIds: number[]; journalEntryLineIds: number[] }): Observable<{ data: ReconciliationMatch }> { return this.http.post<{ data: ReconciliationMatch }>(`${this.accountingUrl}/reconciliation/matches`, dto); }
  unmatchReconciliation(id: number, reason: string): Observable<{ data: ReconciliationMatch }> { return this.http.post<{ data: ReconciliationMatch }>(`${this.accountingUrl}/reconciliation/matches/${id}/unmatch`, { reason }); }
  prepareReconciliationClose(id: number): Observable<{ data: ReconciliationSession }> { return this.http.post<{ data: ReconciliationSession }>(`${this.accountingUrl}/reconciliation/sessions/${id}/prepare-close`, {}); }
  closeReconciliation(id: number, reason: string): Observable<{ data: ReconciliationSession }> { return this.http.post<{ data: ReconciliationSession }>(`${this.accountingUrl}/reconciliation/sessions/${id}/close`, { reason }); }
  reopenReconciliation(id: number, reason: string): Observable<{ data: ReconciliationSession }> { return this.http.post<{ data: ReconciliationSession }>(`${this.accountingUrl}/reconciliation/sessions/${id}/reopen`, { reason }); }
  importBankStatement(bankAccountId: number, file: File): Observable<{ data: StatementImport }> { const body = new FormData(); body.append('bankAccountId', String(bankAccountId)); body.append('file', file, file.name); return this.http.post<{ data: StatementImport }>(`${this.accountingUrl}/bank-statements/imports`, body); }
  getStatementImports(bankAccountId?: number): Observable<{ data: StatementImport[] }> { let params = new HttpParams(); if (bankAccountId) params = params.set('bankAccountId', bankAccountId); return this.http.get<{ data: StatementImport[] }>(`${this.accountingUrl}/bank-statements/imports`, { params }); }
  getStatementLines(importId?: number, bankAccountId?: number): Observable<{ data: StatementLine[] }> { let params = new HttpParams(); if (importId) params = params.set('importId', importId); if (bankAccountId) params = params.set('bankAccountId', bankAccountId); return this.http.get<{ data: StatementLine[] }>(`${this.accountingUrl}/bank-statements/lines`, { params }); }
  proposeStatementAdjustment(dto: { bankStatementLineId: number; adjustmentType: string; amount: number; reason: string; idempotencyKey: string }): Observable<{ data: StatementAdjustment }> { return this.http.post<{ data: StatementAdjustment }>(`${this.accountingUrl}/reconciliation/adjustments`, dto); }
  approveStatementAdjustment(id: number, reason: string, postingDate: string): Observable<{ data: StatementAdjustment }> { return this.http.post<{ data: StatementAdjustment }>(`${this.accountingUrl}/reconciliation/adjustments/${id}/approve`, { reason }, { params: { postingDate } }); }
  reverseStatementAdjustment(id: number, reason: string, postingDate: string): Observable<{ data: StatementAdjustment }> { return this.http.post<{ data: StatementAdjustment }>(`${this.accountingUrl}/reconciliation/adjustments/${id}/reverse`, { reason, postingDate }); }
  classifyStatementException(statementLineId: number, classification: string, reason: string): Observable<void> { return this.http.post<void>(`${this.accountingUrl}/reconciliation/exceptions`, { classification, reason }, { params: { statementLineId } }); }
  resolveStatementException(id: number, resolutionNotes: string): Observable<void> { return this.http.post<void>(`${this.accountingUrl}/reconciliation/exceptions/${id}/resolve`, { resolutionNotes }); }
  getReconciliationMatches(sessionId: number): Observable<{ data: ReconciliationMatchDetail[] }> { return this.http.get<{ data: ReconciliationMatchDetail[] }>(`${this.accountingUrl}/reconciliation/matches`, { params: { sessionId } }); }
  getStatementAdjustments(bankAccountId: number): Observable<{ data: StatementAdjustment[] }> { return this.http.get<{ data: StatementAdjustment[] }>(`${this.accountingUrl}/reconciliation/adjustments`, { params: { bankAccountId } }); }
  getStatementExceptions(bankAccountId: number): Observable<{ data: StatementException[] }> { return this.http.get<{ data: StatementException[] }>(`${this.accountingUrl}/reconciliation/exceptions`, { params: { bankAccountId } }); }
  getSettlements(): Observable<{ data: SettlementBatch[] }> { return this.http.get<{ data: SettlementBatch[] }>(`${this.accountingUrl}/settlements`); }

  updateAccountingConfiguration(dto: { retainedEarningsAccountId?: number | null; defaultSupplierPaymentTermsDays?: number; vatPayableAccountId?: number | null; vatReceivableAccountId?: number | null }): Observable<any> {
    return this.http.put<any>(`${this.accountingUrl}/configuration`, dto);
  }

  getActiveEquityChartAccounts(): Observable<any> {
    const params = new HttpParams().set('accountType', 'Equity').set('isActive', 'true');
    return this.http.get<any>(`${this.accountingUrl}/chart-accounts`, { params });
  }

  getChartAccounts(accountType?: string): Observable<{ data: Array<{ id: number; accountCode: string; accountName: string }> }> {
    let params = new HttpParams().set('isActive', 'true');
    if (accountType) params = params.set('accountType', accountType);
    return this.http.get<{ data: Array<{ id: number; accountCode: string; accountName: string }> }>(`${this.accountingUrl}/chart-accounts`, { params });
  }

  getBalanceSheet(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<BalanceSheetReport>> {
    return this.http.get<AccountingReportResponse<BalanceSheetReport>>(`${this.reportsUrl}/balance-sheet`, { params: this.reportParams(filters) });
  }

  getComparativeBalanceSheet(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<ComparativeBalanceSheetReport>> {
    return this.http.get<AccountingReportResponse<ComparativeBalanceSheetReport>>(`${this.reportsUrl}/balance-sheet/comparative`, { params: this.reportParams(filters) });
  }

  getEnhancedGeneralLedger(filters: Record<string, string | number | undefined>): Observable<AccountingReportResponse<EnhancedGeneralLedgerReport>> {
    return this.http.get<AccountingReportResponse<EnhancedGeneralLedgerReport>>(`${this.reportsUrl}/enhanced-general-ledger`, { params: this.reportParams(filters) });
  }

  private reportParams(filters: Record<string, string | number | undefined>): HttpParams {
    return Object.entries(filters).reduce((params, [key, value]) =>
      value === undefined || value === '' ? params : params.set(key, String(value)), new HttpParams());
  }

  // ─── Accounts ────────────────────────────────────────────────────────────

  /** Get all accounts, optionally filtered by type */
  getAccounts(type?: string): Observable<any> {
    let params = new HttpParams();
    if (type) params = params.set('type', type);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.accountsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.accounts.set(res.data?.accounts || []);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load accounts');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get single account by ID */
  getAccountById(id: number): Observable<any> {
    return this.http.get<any>(`${this.accountsUrl}/${id}`);
  }

  /** Create a new account */
  createAccount(dto: CreateAccountDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.accountsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update an existing account */
  updateAccount(id: number, dto: UpdateAccountDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.accountsUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Delete an account */
  deleteAccount(id: number): Observable<any> {
    return this.http.delete<any>(`${this.accountsUrl}/${id}`);
  }

  // ─── Transactions ─────────────────────────────────────────────────────────

  /** Get transactions with optional query filters */
  getTransactions(filters?: { accountId?: number; startDate?: string; endDate?: string }): Observable<any> {
    let params = new HttpParams();
    if (filters?.accountId) params = params.set('accountId', filters.accountId.toString());
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.transactionsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.transactions.set(res.data?.transactions || []);
          this.transactionsTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load transactions');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search transactions with pagination */
  searchTransactions(dto: TransactionSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.transactionsUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.transactions.set(res.data?.transactions || res.data || []);
          this.transactionsTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search transactions');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single transaction by ID */
  getTransactionById(id: number): Observable<any> {
    return this.http.get<any>(`${this.transactionsUrl}/${id}`);
  }

  /** Create a new transaction */
  createTransaction(dto: CreateTransactionDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.transactionsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Get ledger for an account */
  getAccountLedger(accountId: number): Observable<any> {
    const params = new HttpParams().set('accountId', accountId.toString());
    return this.http.get<any>(`${this.transactionsUrl}/ledger`, { params });
  }

  searchJournals(dto: JournalSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    const params = new HttpParams({ fromObject: Object.entries(dto).reduce((acc, [key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        acc[key] = String(value);
      }
      return acc;
    }, {} as Record<string, string>) });

    return this.http.get<any>(this.journalsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.journals.set(res.data?.entries || []);
          this.journalsTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load journals');
          this.isLoading.set(false);
        }
      })
    );
  }

  getJournalById(id: number): Observable<any> {
    return this.http.get<any>(`${this.journalsUrl}/${id}`);
  }

  createManualJournal(dto: CreateJournalEntryDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.journalsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  updateManualJournal(id: number, dto: UpdateJournalEntryDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.journalsUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  submitManualJournal(id: number): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/submit`, {});
  }

  approveManualJournal(id: number): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/approve`, {});
  }

  rejectManualJournal(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/reject`, { reason });
  }

  cancelManualJournal(id: number, reason?: string): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/cancel`, reason ? { reason } : {});
  }

  postManualJournal(id: number): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/post`, {});
  }

  reverseManualJournal(id: number, dto: ReverseJournalEntryDto): Observable<any> {
    return this.http.post<any>(`${this.journalsUrl}/${id}/reverse`, dto);
  }

  getJournalAttachments(id: number): Observable<any> {
    return this.http.get<any>(`${this.journalsUrl}/${id}/attachments`);
  }

  uploadJournalAttachment(id: number, file: File): Observable<any> {
    const formData = new FormData();
    formData.append('File', file);
    return this.http.post<any>(`${this.journalsUrl}/${id}/attachments`, formData);
  }

  deleteJournalAttachment(id: number, attachmentId: number): Observable<any> {
    return this.http.delete<any>(`${this.journalsUrl}/${id}/attachments/${attachmentId}`);
  }

  downloadJournalAttachment(id: number, attachmentId: number): Observable<Blob> {
    return this.http.get(`${this.journalsUrl}/${id}/attachments/${attachmentId}/download`, { responseType: 'blob' });
  }

  // ─── Expenses ─────────────────────────────────────────────────────────────

  getExpenseCategories(includeInactive = false): Observable<any> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<any>(`${this.expensesUrl}/categories`, { params }).pipe(
      tap({
        next: (res) => this.expenseCategories.set(res.data || []),
        error: () => this.expenseCategories.set([])
      })
    );
  }

  createExpenseCategory(dto: { name: string; expenseAccountId: number; isActive?: boolean }): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/categories`, dto);
  }

  updateExpenseCategory(id: number, dto: { name: string; expenseAccountId: number; isActive?: boolean }): Observable<any> {
    return this.http.put<any>(`${this.expensesUrl}/categories/${id}`, dto);
  }

  /** Get all expenses with optional filters */
  getExpenses(filters?: { category?: string; status?: string; startDate?: string; endDate?: string; outletId?: number }): Observable<any> {
    let params = new HttpParams();
    if (filters?.category) params = params.set('category', filters.category);
    if (filters?.status) params = params.set('status', filters.status);
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);
    if (filters?.outletId) params = params.set('outletId', filters.outletId.toString());

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.expensesUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.expenses.set(res.data?.expenses || []);
          this.expensesTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load expenses');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search expenses with pagination */
  searchExpenses(dto: ExpenseSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.expensesUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.expenses.set(res.data?.expenses || res.data || []);
          this.expensesTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search expenses');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single expense by ID */
  getExpenseById(id: number): Observable<any> {
    return this.http.get<any>(`${this.expensesUrl}/${id}`);
  }

  /** Create a new expense */
  createExpense(dto: CreateExpenseDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.expensesUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update an existing expense */
  updateExpense(id: number, dto: UpdateExpenseDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.expensesUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Delete an expense */
  deleteExpense(id: number): Observable<any> {
    return this.http.delete<any>(`${this.expensesUrl}/${id}`);
  }

  submitExpense(id: number): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/${id}/submit`, {});
  }

  approveExpense(id: number, dto: ApproveExpenseDto = {}): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/${id}/approve`, dto);
  }

  rejectExpense(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/${id}/reject`, { reason });
  }

  cancelExpense(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/${id}/cancel`, { reason });
  }

  /** Records the cash payout of an approved POS-drawer expense from the terminal's open session (idempotent on the server). */
  payExpenseFromDrawer(id: number, terminalId: number): Observable<any> {
    return this.http.post<any>(`${this.expensesUrl}/${id}/pay-from-drawer`, { terminalId });
  }

  /** Get expense summary */
  getExpenseSummary(): Observable<any> {
    return this.http.get<any>(`${this.expensesUrl}/summary`).pipe(
      tap({
        next: (res) => this.expenseSummary.set(res.data || null),
        error: () => this.expenseSummary.set(null)
      })
    );
  }

  getOtherIncome(filters?: { status?: string; startDate?: string; endDate?: string; outletId?: number }): Observable<any> {
    let params = new HttpParams();
    if (filters?.status) params = params.set('status', filters.status);
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);
    if (filters?.outletId) params = params.set('outletId', filters.outletId.toString());

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.otherIncomeUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.otherIncomeEntries.set(res.data?.items || []);
          this.otherIncomeTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load other income');
          this.isLoading.set(false);
        }
      })
    );
  }

  searchOtherIncome(dto: OtherIncomeSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.otherIncomeUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.otherIncomeEntries.set(res.data?.items || res.data || []);
          this.otherIncomeTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search other income');
          this.isLoading.set(false);
        }
      })
    );
  }

  getOtherIncomeById(id: number): Observable<any> {
    return this.http.get<any>(`${this.otherIncomeUrl}/${id}`);
  }

  createOtherIncome(dto: CreateOtherIncomeEntryDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.otherIncomeUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  updateOtherIncome(id: number, dto: UpdateOtherIncomeEntryDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.otherIncomeUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  submitOtherIncome(id: number): Observable<any> {
    return this.http.post<any>(`${this.otherIncomeUrl}/${id}/submit`, {});
  }

  approveOtherIncome(id: number, dto: ApproveOtherIncomeEntryDto = {}): Observable<any> {
    return this.http.post<any>(`${this.otherIncomeUrl}/${id}/approve`, dto);
  }

  rejectOtherIncome(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.otherIncomeUrl}/${id}/reject`, { reason });
  }

  cancelOtherIncome(id: number, reason: string): Observable<any> {
    return this.http.post<any>(`${this.otherIncomeUrl}/${id}/cancel`, { reason });
  }

  // ─── Supplier payments / advances ───────────────────────────────────────

  createSupplierPayment(dto: CreateSupplierPaymentDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.supplierPaymentsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message || 'Failed to create supplier payment'); this.isLoading.set(false); }
      })
    );
  }

  submitSupplierPayment(id: number): Observable<any> {
    return this.http.post<any>(`${this.supplierPaymentsUrl}/${id}/submit`, {});
  }

  approveSupplierPayment(id: number): Observable<any> {
    return this.http.post<any>(`${this.supplierPaymentsUrl}/${id}/approve`, {});
  }

  createSupplierAdvance(dto: CreateSupplierAdvanceDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(`${this.supplierPaymentsUrl}/advances`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message || 'Failed to create supplier advance'); this.isLoading.set(false); }
      })
    );
  }

  submitSupplierAdvance(id: number): Observable<any> {
    return this.http.post<any>(`${this.supplierPaymentsUrl}/advances/${id}/submit`, {});
  }

  approveSupplierAdvance(id: number): Observable<any> {
    return this.http.post<any>(`${this.supplierPaymentsUrl}/advances/${id}/approve`, {});
  }

  // ─── Bills ────────────────────────────────────────────────────────────────

  /** Get all bills, optionally filtered by status */
  getBills(status?: string): Observable<any> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.billsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.bills.set(res.data?.bills || []);
          this.billsTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load bills');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search bills with pagination */
  searchBills(dto: BillSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.billsUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.bills.set(res.data?.bills || res.data || []);
          this.billsTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search bills');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single bill by ID */
  getBillById(id: number): Observable<any> {
    return this.http.get<any>(`${this.billsUrl}/${id}`);
  }

  /** Create a new bill */
  createBill(dto: CreateBillDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.billsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update bill status */
  updateBillStatus(id: number, dto: UpdateBillStatusDto): Observable<any> {
    return this.http.put<any>(`${this.billsUrl}/${id}/status`, dto);
  }

  /** Get bill summary totals */
  getBillSummary(): Observable<any> {
    return this.http.get<any>(`${this.billsUrl}/summary`).pipe(
      tap({
        next: (res) => this.billSummary.set(res.data || null),
        error: () => this.billSummary.set(null)
      })
    );
  }
}
