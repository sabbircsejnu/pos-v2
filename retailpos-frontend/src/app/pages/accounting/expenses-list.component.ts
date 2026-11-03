import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { CurrencyService } from '../../services/currency.service';
import { AuthService } from '../../services/auth.service';
import { ExpenseCategoryDto, ExpenseDto } from '../../models/accounting.model';
import { CashSessionService, CashSessionTerminalOption } from '../../services/cash-session.service';
import { UiDialogComponent } from '../../components/shared/ui/ui-dialog.component';
import { paymentSourceLabel, payoutState } from './expense-payment-sources';

@Component({
  selector: 'app-expenses-list',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, UiDialogComponent],
  templateUrl: './expenses-list.component.html',
  styleUrls: ['./expenses-list.component.css']
})
export class ExpensesListComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  Math = Math;

  // Filters
  filterCategory = signal('');
  filterStatus = signal('');
  filterStartDate = signal('');
  filterEndDate = signal('');

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);

  categories = signal<ExpenseCategoryDto[]>([]);
  readonly statuses = ['Draft', 'Submitted', 'Approved', 'Rejected', 'Cancelled'];

  readonly sourceLabel = paymentSourceLabel;
  readonly payout = payoutState;
  private readonly cashSessions = injectCompanyDates(CashSessionService);

  // Drawer payout dialog: the cashier picks the terminal whose open session pays the cash out.
  payingExpense = signal<ExpenseDto | null>(null);
  payTerminals = signal<CashSessionTerminalOption[]>([]);
  payTerminalId = signal<number | null>(null);
  payError = signal<string | null>(null);
  isPaying = signal(false);

  constructor(
    public accountingService: AccountingService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService,
    public authService: AuthService
  ) {}

  ngOnInit(): void {
    this.restoreFromUrl();
    this.loadCategories();
    this.loadSummary();
    this.loadExpenses();
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.filterCategory.set(this.listState.str(p, 'category'));
    this.filterStatus.set(this.listState.str(p, 'status'));
    this.filterStartDate.set(this.listState.str(p, 'startDate'));
    this.filterEndDate.set(this.listState.str(p, 'endDate'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      category: this.filterCategory() || undefined,
      status: this.filterStatus() || undefined,
      startDate: this.filterStartDate() || undefined,
      endDate: this.filterEndDate() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    });
  }

  loadCategories(): void {
    this.accountingService.getExpenseCategories().subscribe({
      next: (res) => this.categories.set(res.data || []),
      error: () => this.categories.set([])
    });
  }

  loadSummary(): void {
    this.accountingService.getExpenseSummary().subscribe();
  }

  loadExpenses(): void {
    this.accountingService.searchExpenses({
      category: this.filterCategory() || undefined,
      status: this.filterStatus() || undefined,
      startDate: this.filterStartDate() || undefined,
      endDate: this.filterEndDate() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    }).subscribe();
  }

  applyFilters(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.loadExpenses();
  }

  clearFilters(): void {
    this.filterCategory.set('');
    this.filterStatus.set('');
    this.filterStartDate.set('');
    this.filterEndDate.set('');
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.loadExpenses();
  }

  createExpense(): void {
    this.router.navigate(['/expenses/create']);
  }

  editExpense(id: number, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/expenses/edit', id]);
  }

  deleteExpense(id: number, category: string, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm(
      `Are you sure you want to delete this ${category} expense?`,
      () => {
        this.accountingService.deleteExpense(id).subscribe({
          next: () => {
            this.alertService.success('Expense deleted successfully');
            this.loadSummary();
            this.loadExpenses();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
  }

  submitExpense(expense: ExpenseDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.submitExpense(expense.id).subscribe({
      next: () => {
        this.alertService.success('Expense submitted successfully');
        this.loadExpenses();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  approveExpense(expense: ExpenseDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.approveExpense(expense.id).subscribe({
      next: () => {
        this.alertService.success('Expense approved successfully');
        this.loadExpenses();
        this.loadSummary();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  rejectExpense(expense: ExpenseDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt(`Reject ${expense.category} expense. Enter reason:`)?.trim();
    if (!reason) {
      return;
    }

    this.accountingService.rejectExpense(expense.id, reason).subscribe({
      next: () => {
        this.alertService.success('Expense rejected successfully');
        this.loadExpenses();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  cancelExpense(expense: ExpenseDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt(`Cancel ${expense.category} expense. Enter reason:`)?.trim();
    if (!reason) {
      return;
    }

    this.accountingService.cancelExpense(expense.id, reason).subscribe({
      next: () => {
        this.alertService.success('Expense cancelled successfully');
        this.loadExpenses();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.syncUrl();
    this.loadExpenses();
  }

  getSummaryCategories(): string[] {
    const summary = this.accountingService.expenseSummary();
    return summary ? Object.keys(summary.byCategory) : [];
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr: string): string {
    return this.companyDates.format(dateStr, false);
  }

  canEdit(expense: ExpenseDto): boolean {
    return (expense.status === 'Draft' || expense.status === 'Rejected') && this.authService.hasPermission('accounting.expenses.edit');
  }

  canDelete(expense: ExpenseDto): boolean {
    return expense.status === 'Draft' && this.authService.hasPermission('accounting.expenses.cancel');
  }

  canSubmit(expense: ExpenseDto): boolean {
    return (expense.status === 'Draft' || expense.status === 'Rejected') && this.authService.hasPermission('accounting.expenses.submit');
  }

  canPayFromDrawer(expense: ExpenseDto): boolean {
    return payoutState(expense) === 'awaiting_payout' && !!expense.outletId && this.authService.hasPermission('cash_sessions.expenses.pay');
  }

  openPayFromDrawer(expense: ExpenseDto, event: Event): void {
    event.stopPropagation();
    if (!expense.outletId) return;
    this.payingExpense.set(expense);
    this.payError.set(null);
    this.payTerminalId.set(null);
    this.payTerminals.set([]);
    this.cashSessions.openContext(expense.outletId).subscribe({
      next: (res) => {
        const open = (res.data?.terminals ?? []).filter(terminal => terminal.hasOpenSession);
        this.payTerminals.set(open);
        this.payTerminalId.set(open.length === 1 ? open[0].id : null);
        if (open.length === 0) this.payError.set('No terminal of this outlet has an open cash session. Open one before paying out cash.');
      },
      error: (err) => this.payError.set(this.errorHandler.extractErrorMessage(err))
    });
  }

  closePayFromDrawer(): void {
    if (this.isPaying()) return;
    this.payingExpense.set(null);
  }

  confirmPayFromDrawer(): void {
    const expense = this.payingExpense();
    const terminalId = this.payTerminalId();
    if (!expense || !terminalId || this.isPaying()) return;
    this.isPaying.set(true);
    this.payError.set(null);
    this.accountingService.payExpenseFromDrawer(expense.id, terminalId).subscribe({
      next: () => {
        this.isPaying.set(false);
        this.payingExpense.set(null);
        this.alertService.success('Drawer payout recorded');
        this.loadExpenses();
      },
      error: (err) => {
        this.isPaying.set(false);
        this.payError.set(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  canApprove(expense: ExpenseDto): boolean {
    return expense.status === 'Submitted' && this.authService.hasPermission('accounting.expenses.approve');
  }

  canReject(expense: ExpenseDto): boolean {
    return expense.status === 'Submitted' && this.authService.hasPermission('accounting.expenses.reject');
  }

  canCancel(expense: ExpenseDto): boolean {
    return expense.status !== 'Approved' && expense.status !== 'Cancelled' && this.authService.hasPermission('accounting.expenses.cancel');
  }

  getCategoryNames(): string[] {
    return this.categories().map(x => x.name);
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'Approved':
        return 'bg-green-100 text-green-800';
      case 'Submitted':
        return 'bg-amber-100 text-amber-800';
      case 'Rejected':
        return 'bg-red-100 text-red-800';
      case 'Cancelled':
        return 'bg-slate-100 text-slate-700';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }
}
