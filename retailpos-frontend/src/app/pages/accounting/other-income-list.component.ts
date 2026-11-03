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
import { OtherIncomeEntryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-other-income-list',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './other-income-list.component.html',
  styleUrls: ['./other-income-list.component.css']
})
export class OtherIncomeListComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  Math = Math;
  filterStatus = signal('');
  filterStartDate = signal('');
  filterEndDate = signal('');
  pageNumber = signal(1);
  pageSize = signal(10);
  readonly statuses = ['Draft', 'Submitted', 'Approved', 'Rejected', 'Cancelled'];

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
    this.loadEntries();
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.filterStatus.set(this.listState.str(p, 'status'));
    this.filterStartDate.set(this.listState.str(p, 'startDate'));
    this.filterEndDate.set(this.listState.str(p, 'endDate'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      status: this.filterStatus() || undefined,
      startDate: this.filterStartDate() || undefined,
      endDate: this.filterEndDate() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    });
  }

  loadEntries(): void {
    this.accountingService.searchOtherIncome({
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
    this.loadEntries();
  }

  clearFilters(): void {
    this.filterStatus.set('');
    this.filterStartDate.set('');
    this.filterEndDate.set('');
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.loadEntries();
  }

  createEntry(): void {
    this.router.navigate(['/incomes/create']);
  }

  editEntry(id: number, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/incomes/edit', id]);
  }

  submitEntry(entry: OtherIncomeEntryDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.submitOtherIncome(entry.id).subscribe({
      next: () => {
        this.alertService.success('Other income submitted successfully');
        this.loadEntries();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  approveEntry(entry: OtherIncomeEntryDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.approveOtherIncome(entry.id).subscribe({
      next: () => {
        this.alertService.success('Other income approved successfully');
        this.loadEntries();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  rejectEntry(entry: OtherIncomeEntryDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt(`Reject ${entry.incomeCategory} income entry. Enter reason:`)?.trim();
    if (!reason) {
      return;
    }

    this.accountingService.rejectOtherIncome(entry.id, reason).subscribe({
      next: () => {
        this.alertService.success('Other income rejected successfully');
        this.loadEntries();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  cancelEntry(entry: OtherIncomeEntryDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt(`Cancel ${entry.incomeCategory} income entry. Enter reason:`)?.trim();
    if (!reason) {
      return;
    }

    this.accountingService.cancelOtherIncome(entry.id, reason).subscribe({
      next: () => {
        this.alertService.success('Other income cancelled successfully');
        this.loadEntries();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.syncUrl();
    this.loadEntries();
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr: string): string {
    return this.companyDates.format(dateStr, false);
  }

  canEdit(entry: OtherIncomeEntryDto): boolean {
    return (entry.status === 'Draft' || entry.status === 'Rejected') && this.authService.hasPermission('accounting.other_income.edit');
  }

  canSubmit(entry: OtherIncomeEntryDto): boolean {
    return (entry.status === 'Draft' || entry.status === 'Rejected') && this.authService.hasPermission('accounting.other_income.submit');
  }

  canApprove(entry: OtherIncomeEntryDto): boolean {
    return entry.status === 'Submitted' && this.authService.hasPermission('accounting.other_income.approve');
  }

  canReject(entry: OtherIncomeEntryDto): boolean {
    return entry.status === 'Submitted' && this.authService.hasPermission('accounting.other_income.reject');
  }

  canCancel(entry: OtherIncomeEntryDto): boolean {
    return entry.status !== 'Approved' && entry.status !== 'Cancelled' && this.authService.hasPermission('accounting.other_income.cancel');
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