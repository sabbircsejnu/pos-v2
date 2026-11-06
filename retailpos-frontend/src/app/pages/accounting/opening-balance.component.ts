import { CompanyDateService } from '../../services/company-date.service';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { OpeningBalanceStatusDto } from '../../models/accounting.model';

@Component({
  selector: 'app-opening-balance',
  standalone: true,
  imports: [AppDatePipe, CommonModule, FormsModule],
  templateUrl: './opening-balance.component.html',
  styleUrls: ['./opening-balance.component.css']
})
export class OpeningBalanceComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  status = signal<OpeningBalanceStatusDto | null>(null);
  isLoading = signal(false);
  isPosting = signal(false);
  adjustmentNotes = signal('');
  adjustmentAmount = signal(0);

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    this.loadStatus();
  }

  loadStatus(): void {
    this.isLoading.set(true);
    this.accountingService.getOpeningBalanceStatus().subscribe({
      next: (res) => {
        this.status.set(res.data || null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  postOpeningBalance(): void {
    this.isPosting.set(true);
    this.accountingService.postOpeningBalance().subscribe({
      next: () => {
        this.alertService.success('Opening balance posted');
        this.loadStatus();
        this.isPosting.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isPosting.set(false);
      }
    });
  }

  adjustOpeningBalance(): void {
    const amount = Number(this.adjustmentAmount());
    if (!this.adjustmentNotes().trim() || !Number.isFinite(amount)) {
      this.alertService.error('Please provide a valid adjustment amount and note');
      return;
    }

    const openingBalanceId = this.status()?.openingBalanceJournalEntryId;
    if (!openingBalanceId) {
      this.alertService.error('No opening balance journal exists yet');
      return;
    }

    this.isPosting.set(true);
    this.accountingService.adjustOpeningBalance(openingBalanceId, {
      reason: this.adjustmentNotes().trim(),
      adjustmentBusinessDate: this.companyToday.todayIso(),
      postingDate: this.companyToday.todayIso(),
      description: this.adjustmentNotes().trim(),
      referenceNumber: `ADJ-${openingBalanceId}`,
      lines: [
        {
          accountId: 1,
          debitAmount: amount > 0 ? amount : 0,
          creditAmount: amount < 0 ? Math.abs(amount) : 0,
          description: this.adjustmentNotes().trim()
        }
      ]
    }).subscribe({
      next: () => {
        this.alertService.success('Opening balance adjusted');
        this.adjustmentAmount.set(0);
        this.adjustmentNotes.set('');
        this.loadStatus();
        this.isPosting.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isPosting.set(false);
      }
    });
  }
}
