import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { AccountingPeriodDto, CreateAccountingPeriodDto, CreateFinancialYearDto, FinancialYearDto, UpdateFinancialYearDto } from '../../models/accounting.model';

@Component({
  selector: 'app-financial-years',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './financial-years.component.html',
  styleUrls: ['./financial-years.component.css']
})
export class FinancialYearsComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  years = signal<FinancialYearDto[]>([]);
  periods = signal<AccountingPeriodDto[]>([]);
  selectedYearId = signal<number | null>(null);
  isLoading = signal(false);
  isSavingYear = signal(false);
  isSavingPeriod = signal(false);
  editingYearId = signal<number | null>(null);

  yearCode = signal('');
  yearName = signal('');
  yearStartDate = signal(this.companyToday.todayIso());
  yearEndDate = signal(this.companyToday.todayIso());
  yearIsCurrent = signal(false);

  periodNumber = signal(1);
  periodName = signal('');
  periodStartDate = signal(this.companyToday.todayIso());
  periodEndDate = signal(this.companyToday.todayIso());

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    this.loadYears();
  }

  loadYears(): void {
    this.isLoading.set(true);
    this.accountingService.getFinancialYears().subscribe({
      next: (res) => {
        const items = res.data || [];
        this.years.set(items);
        if (!this.selectedYearId() && items.length > 0) {
          this.selectYear(items[0].id);
        } else if (this.selectedYearId()) {
          const exists = items.some((year: FinancialYearDto) => year.id === this.selectedYearId());
          if (!exists) {
            this.selectedYearId.set(null);
            this.periods.set([]);
          }
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  selectYear(id: number): void {
    this.selectedYearId.set(id);
    this.loadPeriods(id);
  }

  startCreateYear(): void {
    this.editingYearId.set(null);
    this.yearCode.set('');
    this.yearName.set('');
    this.yearStartDate.set(this.companyToday.todayIso());
    this.yearEndDate.set(this.companyToday.todayIso());
    this.yearIsCurrent.set(false);
  }

  startEditYear(year: FinancialYearDto): void {
    this.editingYearId.set(year.id);
    this.yearCode.set(year.yearCode);
    this.yearName.set(year.name);
    this.yearStartDate.set(year.startDate.split('T')[0]);
    this.yearEndDate.set(year.endDate.split('T')[0]);
    this.yearIsCurrent.set(year.isCurrent);
  }

  saveYear(): void {
    if (!this.yearCode().trim() || !this.yearName().trim()) {
      this.alertService.error('Year code and name are required');
      return;
    }

    const dto: CreateFinancialYearDto | UpdateFinancialYearDto = {
      yearCode: this.yearCode().trim(),
      name: this.yearName().trim(),
      startDate: this.yearStartDate(),
      endDate: this.yearEndDate(),
      ...(this.editingYearId() ? {} : { isCurrent: this.yearIsCurrent() })
    };

    this.isSavingYear.set(true);
    const request = this.editingYearId()
      ? this.accountingService.updateFinancialYear(this.editingYearId()!, dto as UpdateFinancialYearDto)
      : this.accountingService.createFinancialYear(dto as CreateFinancialYearDto);

    request.subscribe({
      next: () => {
        this.alertService.success(this.editingYearId() ? 'Financial year updated' : 'Financial year created');
        this.editingYearId.set(null);
        this.loadYears();
        this.isSavingYear.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSavingYear.set(false);
      }
    });
  }

  activateYear(year: FinancialYearDto): void {
    this.accountingService.activateFinancialYear(year.id).subscribe({
      next: () => {
        this.alertService.success('Financial year activated');
        this.loadYears();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  closeYear(year: FinancialYearDto): void {
    const reason = window.prompt('Optional reason for closing the financial year')?.trim();
    this.accountingService.closeFinancialYear(year.id, reason || undefined).subscribe({
      next: () => {
        this.alertService.success('Financial year closed');
        this.loadYears();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  reopenYear(year: FinancialYearDto): void {
    const reason = window.prompt('Reason for reopening the financial year')?.trim();
    if (!reason) {
      return;
    }
    this.accountingService.reopenFinancialYear(year.id, reason).subscribe({
      next: () => {
        this.alertService.success('Financial year reopened');
        this.loadYears();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  loadPeriods(yearId: number): void {
    this.accountingService.getPeriods(yearId).subscribe({
      next: (res) => this.periods.set(res.data || []),
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.periods.set([]);
      }
    });
  }

  savePeriod(): void {
    const yearId = this.selectedYearId();
    if (!yearId) {
      return;
    }
    if (!this.periodName().trim()) {
      this.alertService.error('Period name is required');
      return;
    }

    const dto: CreateAccountingPeriodDto = {
      periodNumber: this.periodNumber(),
      name: this.periodName().trim(),
      startDate: this.periodStartDate(),
      endDate: this.periodEndDate(),
      status: 'Open'
    };

    this.isSavingPeriod.set(true);
    this.accountingService.createPeriod(yearId, dto).subscribe({
      next: () => {
        this.alertService.success('Accounting period created');
        this.periodName.set('');
        this.periodNumber.set(this.periodNumber() + 1);
        this.periodStartDate.set(this.companyToday.todayIso());
        this.periodEndDate.set(this.companyToday.todayIso());
        this.loadPeriods(yearId);
        this.isSavingPeriod.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSavingPeriod.set(false);
      }
    });
  }

  closePeriod(period: AccountingPeriodDto): void {
    const reason = window.prompt('Optional reason for closing the period')?.trim();
    this.accountingService.closePeriod(period.id, reason || undefined).subscribe({
      next: () => {
        this.alertService.success('Accounting period closed');
        this.loadPeriods(this.selectedYearId()!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  lockPeriod(period: AccountingPeriodDto): void {
    const reason = window.prompt('Reason for locking the period')?.trim();
    if (!reason) {
      return;
    }
    this.accountingService.lockPeriod(period.id, reason).subscribe({
      next: () => {
        this.alertService.success('Accounting period locked');
        this.loadPeriods(this.selectedYearId()!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  reopenPeriod(period: AccountingPeriodDto): void {
    const reason = window.prompt('Reason for reopening the period')?.trim();
    if (!reason) {
      return;
    }
    this.accountingService.reopenPeriod(period.id, reason).subscribe({
      next: () => {
        this.alertService.success('Accounting period reopened');
        this.loadPeriods(this.selectedYearId()!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  canManageFinancialYears(): boolean {
    return this.auth.hasPermission('accounting.financial_years.manage');
  }

  canManagePeriods(): boolean {
    return this.auth.hasPermission('accounting.financial_years.manage') || this.auth.hasPermission('accounting.periods.close') || this.auth.hasPermission('accounting.periods.reopen');
  }
}
