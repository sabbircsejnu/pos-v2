import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';

type AccountingReportView = 'general-ledger' | 'trial-balance' | 'profit-loss';
const REPORT_VIEWS: readonly AccountingReportView[] = ['general-ledger', 'trial-balance', 'profit-loss'];

@Component({
  selector: 'app-accounting-reports',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './accounting-reports.component.html',
  styleUrls: ['./accounting-reports.component.css']
})
export class AccountingReportsComponent implements OnInit {
  view = signal<AccountingReportView>('general-ledger');
  fromDate = signal(new Date().toISOString().split('T')[0]);
  toDate = signal(new Date().toISOString().split('T')[0]);
  ledger = signal<any[]>([]);
  reportSummary = signal<any>(null);
  isLoading = signal(false);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    // Each report has its own route (/reports/<view>); the URL is the source of truth for the view.
    this.route.url.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(segments => {
      const last = segments[segments.length - 1]?.path as AccountingReportView | undefined;
      this.view.set(last && REPORT_VIEWS.includes(last) ? last : 'general-ledger');
      this.runReport();
    });
  }

  runReport(): void {
    this.isLoading.set(true);
    const request = this.resolveRequest();
    request.subscribe({
      next: (res) => {
        this.ledger.set(res.data?.entries || []);
        this.reportSummary.set(res.data?.summary || null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  private resolveRequest() {
    switch (this.view()) {
      case 'trial-balance':
        return this.accountingService.getTrialBalance(this.fromDate(), this.toDate());
      case 'profit-loss':
        return this.accountingService.getProfitLoss(this.fromDate(), this.toDate());
      default:
        return this.accountingService.getGeneralLedger(this.fromDate(), this.toDate());
    }
  }

  setView(view: AccountingReportView): void {
    if (view === this.view()) {
      this.runReport();
      return;
    }
    this.router.navigate(['/reports', view]);
  }
}
