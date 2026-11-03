import { CompanyDateService } from '../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { StockCountService } from '../../services/stock-count.service';
import { StockCountDto } from '../../models/stock-count.model';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-stock-count-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-count-details.component.html',
  styleUrls: ['./stock-count-details.component.css']
})
export class StockCountDetailsComponent implements OnInit, OnDestroy {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  readonly phase3Enabled = environment.stockCountPhase3Enabled;
  stockCount = signal<StockCountDto | null>(null);
  isLoading = signal(true);
  isWorking = signal(false);
  readonly rejectionReasonMaxLength = 500;
  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    public auth: AuthService,
    private service: StockCountService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private alert: AlertService,
    private errorHandler: ErrorHandlerService,
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.location.back();
      return;
    }

    this.load(+id);
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  load(id: number): void {
    this.isLoading.set(true);
    this.service.getById(id).subscribe({
      next: (res) => {
        this.setStockCount(res.data as StockCountDto);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alert.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.location.back();
      }
    });
  }

  private setStockCount(item: StockCountDto): void {
    this.stockCount.set(item);
    this.stopPolling();
    if (item.adjustmentJob && !item.adjustmentJob.isTerminal) {
      this.pollTimer = setTimeout(() => this.refreshQuietly(item.id), 5000);
    }
  }

  private refreshQuietly(id: number): void {
    this.service.getById(id).subscribe({
      next: (res) => this.setStockCount(res.data as StockCountDto),
      error: () => {
        this.pollTimer = setTimeout(() => this.refreshQuietly(id), 15000);
      }
    });
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  /** Approved never means Adjusted: the adjustment is posted later by a background job. */
  adjustmentLabel(item: StockCountDto): string | null {
    switch (item.adjustmentState) {
      case 'Pending': return 'Approved - stock adjustment pending (inventory not changed yet)';
      case 'Processing': return 'Approved - stock adjustment is being posted';
      case 'RetryScheduled': return 'Approved - stock adjustment will be retried automatically';
      case 'Completed': return item.adjustmentJob?.stockAdjustmentId
        ? 'Adjusted - inventory updated and stock count completed'
        : item.adjustmentJob ? 'Completed - no stock differences to adjust' : 'Completed';
      case 'Blocked': return 'Adjustment blocked - movement evidence is ambiguous; inventory was not changed';
      case 'Failed': return 'Adjustment failed - inventory was not changed';
      case 'DraftGenerated': return 'Adjustment draft generated - awaiting stock adjustment approval';
      default: return null;
    }
  }

  adjustmentClass(item: StockCountDto): string {
    switch (item.adjustmentState) {
      case 'Completed': return 'border-emerald-300 bg-emerald-50 text-emerald-900';
      case 'Blocked':
      case 'Failed': return 'border-red-300 bg-red-50 text-red-900';
      default: return 'border-blue-300 bg-blue-50 text-blue-900';
    }
  }

  canOpenAdjustment(item: StockCountDto): boolean {
    return !!item.adjustmentJob?.stockAdjustmentId && this.auth.hasPermission('stock_adjustments.view');
  }

  openAdjustment(item: StockCountDto): void {
    const adjustmentId = item.adjustmentJob?.stockAdjustmentId;
    if (adjustmentId) {
      this.router.navigate(['/stock-adjustments', adjustmentId]);
    }
  }

  canUpload(item: StockCountDto): boolean {
    return item.status === 'Draft' && this.auth.hasPermission('StockCount.Upload');
  }

  canSubmit(item: StockCountDto): boolean {
    return item.status === 'Draft' && this.auth.hasPermission('StockCount.Submit');
  }

  canReject(item: StockCountDto): boolean {
    return item.status === 'Submitted' && this.auth.hasPermission('StockCount.Reject');
  }

  canReopen(item: StockCountDto): boolean {
    return item.status === 'Rejected' && this.auth.hasPermission('StockCount.Reopen');
  }

  canApprove(item: StockCountDto): boolean {
    return this.phase3Enabled && item.status === 'Submitted' && this.auth.hasPermission('StockCount.Approve');
  }

  canGenerateAdjustmentDraft(item: StockCountDto): boolean {
    return this.phase3Enabled && item.status === 'Approved' && !item.adjustmentJob
      && this.auth.hasPermission('StockCount.Approve');
  }

  uploadExcel(fileInput: HTMLInputElement): void {
    if (this.isWorking()) return;
    fileInput.value = '';
    fileInput.click();
  }

  onUploadSelected(event: Event): void {
    const item = this.stockCount();
    if (!item) return;

    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.isWorking.set(true);
    this.service.upload(item.id, file).subscribe({
      next: (res) => {
        this.stockCount.set(res.data as StockCountDto);
        this.isWorking.set(false);
        this.alert.success('Stock count Excel uploaded successfully');
      },
      error: (err) => {
        this.isWorking.set(false);
        this.alert.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  submitCount(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    this.alert.confirm(
      'Submit this stock count for review?',
      () => {
        this.isWorking.set(true);
        this.service.submit(item.id).subscribe({
          next: (res) => {
            this.stockCount.set(res.data as StockCountDto);
            this.isWorking.set(false);
            this.alert.success('Stock count submitted successfully');
          },
          error: (err) => {
            this.isWorking.set(false);
            this.alert.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Confirm Submit',
      'Submit'
    );
  }

  rejectCount(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    this.reasonModalTitle.set('Reject Stock Count');
    this.reasonText.set('');
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    const reason = this.reasonText().trim();
    if (!reason) {
      this.alert.error('Please enter a rejection reason');
      return;
    }

    if (reason.length > this.rejectionReasonMaxLength) {
      this.alert.error(`Rejection reason cannot exceed ${this.rejectionReasonMaxLength} characters`);
      return;
    }

    this.showReasonModal.set(false);
    this.reasonText.set('');

    this.isWorking.set(true);
    this.service.reject(item.id, reason).subscribe({
      next: (res) => {
        this.stockCount.set(res.data as StockCountDto);
        this.isWorking.set(false);
        this.alert.success('Stock count rejected');
      },
      error: (err) => {
        this.isWorking.set(false);
        this.alert.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.reasonText.set('');
  }

  approveCount(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    this.alert.confirm(
      'Approve this submitted stock count?',
      () => {
        this.isWorking.set(true);
        this.service.approve(item.id).subscribe({
          next: (res) => {
            this.setStockCount(res.data as StockCountDto);
            this.isWorking.set(false);
            this.alert.success(res.message || 'Stock count approved');
          },
          error: (err) => {
            this.isWorking.set(false);
            this.alert.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Confirm Approve',
      'Approve'
    );
  }

  reopenCount(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    this.alert.confirm(
      'Reopen this rejected stock count back to Draft?',
      () => {
        this.isWorking.set(true);
        this.service.reopen(item.id).subscribe({
          next: (res) => {
            this.stockCount.set(res.data as StockCountDto);
            this.isWorking.set(false);
            this.alert.success('Stock count reopened');
          },
          error: (err) => {
            this.isWorking.set(false);
            this.alert.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Confirm Reopen',
      'Reopen'
    );
  }

  generateAdjustmentDraft(): void {
    const item = this.stockCount();
    if (!item || this.isWorking()) return;

    this.alert.confirm(
      'Generate stock adjustment draft from differences?',
      () => {
        this.isWorking.set(true);
        this.service.generateAdjustmentDraft(item.id).subscribe({
          next: (res) => {
            this.stockCount.set(res.data as StockCountDto);
            this.isWorking.set(false);
            this.alert.success('Stock adjustment draft generated');
          },
          error: (err) => {
            this.isWorking.set(false);
            this.alert.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      },
      'Confirm Generate Draft',
      'Generate'
    );
  }

  download(): void {
    const item = this.stockCount();
    if (!item) return;

    this.service.downloadExcel(item.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `StockCount_${item.stockCountNo}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  print(): void {
    const item = this.stockCount();
    if (!item) return;

    this.service.getPrintData(item.id).subscribe({
      next: (res) => this.service.printDocument(res.data),
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  goBack(): void {
    this.location.back();
  }

  formatDate(date?: string | null): string {
    if (!date) return '-';
    return this.companyDates.format(date, false);
  }

  formatDateTime(date?: string | null): string {
    if (!date) return '-';
    return this.companyDates.format(date, true);
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Draft': return 'bg-gray-100 text-gray-700';
      case 'Submitted': return 'bg-blue-100 text-blue-700';
      case 'Rejected': return 'bg-red-100 text-red-700';
      case 'Approved': return 'bg-green-100 text-green-700';
      case 'AdjustmentGenerated': return 'bg-violet-100 text-violet-700';
      case 'Completed': return 'bg-emerald-100 text-emerald-800';
      default: return 'bg-gray-100 text-gray-700';
    }
  }
}
