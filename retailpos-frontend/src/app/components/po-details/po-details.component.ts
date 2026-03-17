import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { PurchaseOrder, getPOStatusLabel, getPOStatusColor } from '../../models/purchase-order.model';

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

  // Rejection/Cancellation reason
  showReasonModal = signal(false);
  reasonAction = signal<'reject' | 'cancel'>('reject');
  reason = signal<string>('');

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private poService: PurchaseOrderService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.poId.set(+id);
      this.loadPurchaseOrder(+id);
    } else {
      this.alertService.error('Invalid purchase order ID');
      this.router.navigate(['/purchase-orders']);
    }
  }

  loadPurchaseOrder(id: number): void {
    this.isLoading.set(true);
    this.poService.getById(id).subscribe({
      next: (po) => {
        this.po.set(po);
        this.isLoading.set(false);
      },
      error: (err: any) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.router.navigate(['/purchase-orders']);
      }
    });
  }

  canEdit(): boolean {
    const status = this.po()?.status;
    return status === 'draft' || status === 'pending';
  }

  canSubmit(): boolean {
    return this.po()?.status === 'draft';
  }

  canApprove(): boolean {
    return this.po()?.status === 'pending';
  }

  canReject(): boolean {
    return this.po()?.status === 'pending';
  }

  canCancel(): boolean {
    const status = this.po()?.status;
    return status === 'draft' || status === 'pending' || status === 'approved';
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
    const action$ = this.reasonAction() === 'reject'
      ? this.poService.reject(this.poId(), this.reason())
      : this.poService.cancel(this.poId(), this.reason());

    action$.subscribe({
      next: () => {
        this.isProcessing.set(false);
        this.closeReasonModal();
        const message = this.reasonAction() === 'reject'
          ? 'Purchase order rejected'
          : 'Purchase order cancelled';
        this.alertService.success(message);
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
            this.router.navigate(['/purchase-orders']);
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
    this.router.navigate(['/purchase-orders']);
  }

  formatCurrency(amount: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD'
    }).format(amount);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  getStatusLabel(status: string): string {
    return getPOStatusLabel(status);
  }

  getStatusColor(status: string): string {
    return getPOStatusColor(status);
  }

  printPo(): void {
    // TODO: Implement PDF generation/print
    this.alertService.info('Print feature coming soon');
  }
}
