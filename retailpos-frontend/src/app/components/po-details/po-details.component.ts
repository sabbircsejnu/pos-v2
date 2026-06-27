import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { PurchaseOrder, getPOStatusLabel, getPOStatusColor } from '../../models/purchase-order.model';
import { CurrencyService } from '../../services/currency.service';
import { DocumentPdfService } from '../../services/document-pdf.service';

@Component({
  selector: 'app-po-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './po-details.component.html',
  styleUrl: './po-details.component.css'
})
export class PoDetailsComponent implements OnInit {
  poId = signal<number>(0);
  po = signal<PurchaseOrder | null>(null);
  isLoading = signal(false);
  isProcessing = signal(false);

  // Rejection/Cancellation/Send-back reason
  showReasonModal = signal(false);
  reasonAction = signal<'reject' | 'cancel' | 'send_back'>('reject');
  reason = signal<string>('');

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private poService: PurchaseOrderService,
    private documentPdfService: DocumentPdfService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.poId.set(+id);
      this.loadPurchaseOrder(+id);
    } else {
      this.alertService.error('Invalid purchase order ID');
      this.location.back();
    }
  }

  loadPurchaseOrder(id: number): void {
    this.isLoading.set(true);
    this.poService.getById(id).subscribe({
      next: (response) => {
        this.po.set(response.data);
        this.isLoading.set(false);
      },
      error: (err: any) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.location.back();
      }
    });
  }

  canEdit(): boolean {
    const status = this.po()?.status;
    return status === 'draft' || status === 'sent_back';
  }

  canSubmit(): boolean {
    const status = this.po()?.status;
    return status === 'draft' || status === 'sent_back';
  }

  canApprove(): boolean {
    return this.po()?.status === 'pending';
  }

  canReject(): boolean {
    return this.po()?.status === 'pending';
  }

  canSendBack(): boolean {
    return this.po()?.status === 'pending';
  }

  canCancel(): boolean {
    const status = this.po()?.status;
    return status === 'draft' || status === 'pending' || status === 'sent_back' || status === 'approved';
  }

  canDelete(): boolean {
    return this.po()?.status === 'draft';
  }

  editPo(): void {
    this.router.navigate(['/purchase-orders/edit', this.poId()]);
  }

  submitForApproval(): void {
    this.alertService.confirm(
      'Submit this purchase order for approval?',
      () => {
        this.isProcessing.set(true);
        this.poService.submit(this.poId()).subscribe({
          next: () => {
            this.isProcessing.set(false);
            this.alertService.success('Purchase order submitted for approval');
            this.loadPurchaseOrder(this.poId());
          },
          error: (err: any) => {
            this.isProcessing.set(false);
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
  }

  approvePo(): void {
    this.alertService.confirm(
      'Approve this purchase order?',
      () => {
        this.isProcessing.set(true);
        this.poService.approve(this.poId()).subscribe({
          next: () => {
            this.isProcessing.set(false);
            this.alertService.success('Purchase order approved successfully');
            this.loadPurchaseOrder(this.poId());
          },
          error: (err: any) => {
            this.isProcessing.set(false);
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
  }

  showRejectModal(): void {
    this.reasonAction.set('reject');
    this.reason.set('');
    this.showReasonModal.set(true);
  }

  showCancelModal(): void {
    this.reasonAction.set('cancel');
    this.reason.set('');
    this.showReasonModal.set(true);
  }

  showSendBackModal(): void {
    this.reasonAction.set('send_back');
    this.reason.set('');
    this.showReasonModal.set(true);
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.reason.set('');
  }

  confirmReasonAction(): void {
    if (!this.reason().trim()) {
      this.alertService.error('Please provide a reason');
      return;
    }

    this.isProcessing.set(true);
    let action$;
    let successMsg: string;

    switch (this.reasonAction()) {
      case 'reject':
        action$ = this.poService.reject(this.poId(), this.reason());
        successMsg = 'Purchase order rejected';
        break;
      case 'send_back':
        action$ = this.poService.sendBack(this.poId(), this.reason());
        successMsg = 'Purchase order sent back for correction';
        break;
      default:
        action$ = this.poService.cancel(this.poId(), this.reason());
        successMsg = 'Purchase order cancelled';
    }

    action$.subscribe({
      next: () => {
        this.isProcessing.set(false);
        this.closeReasonModal();
        this.alertService.success(successMsg);
        this.loadPurchaseOrder(this.poId());
      },
      error: (err: any) => {
        this.isProcessing.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  deletePo(): void {
    this.alertService.confirm(
      'Delete this purchase order? This action cannot be undone.',
      () => {
        this.isProcessing.set(true);
        this.poService.delete(this.poId()).subscribe({
          next: () => {
            this.isProcessing.set(false);
            this.alertService.success('Purchase order deleted successfully');
            this.location.back();
          },
          error: (err: any) => {
            this.isProcessing.set(false);
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
  }

  backToList(): void {
    this.location.back();
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  getVariantDetails(item: { variantName?: string; variantAttributes?: string }): string {
    const parts = [item.variantName, item.variantAttributes]
      .map(v => (v ?? '').trim())
      .filter(v => this.hasReadableText(v));

    return parts.join(' - ');
  }

  private hasReadableText(value: string): boolean {
    const trimmed = value.trim();
    if (!trimmed) return false;
    if (trimmed === '{}' || trimmed === '[]' || trimmed.toLowerCase() === 'null') return false;
    if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) return false;
    return true;
  }

  getStatusLabel(status: string): string {
    return getPOStatusLabel(status);
  }

  getStatusColor(status: string): string {
    return getPOStatusColor(status);
  }

  printPo(): void {
    if (!this.po()) {
      return;
    }

    this.isProcessing.set(true);
    this.documentPdfService.downloadPurchaseOrder(this.poId()).subscribe({
      next: (response) => {
        this.documentPdfService.triggerBrowserDownload(response, `${this.po()!.poNumber || `PO-${this.poId().toString().padStart(6, '0')}`}.pdf`);
        this.isProcessing.set(false);
      },
      error: (err: any) => {
        this.isProcessing.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  viewLatestGrn(): void {
    const latestGrnId = this.po()?.latestGrnId;
    if (!latestGrnId) {
      return;
    }

    this.router.navigate(['/grn', latestGrnId]);
  }
}
