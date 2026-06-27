import { CommonModule, Location } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import {
  StockAdjustmentDto,
  canCancelStockAdjustment,
  canEditStockAdjustment,
  getStockAdjustmentStatusColor
} from '../../models/stock-adjustment.model';

@Component({
  selector: 'app-adjustment-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './adjustment-details.component.html',
  styleUrls: ['./adjustment-details.component.css']
})
export class AdjustmentDetailsComponent implements OnInit {
  adjustment = signal<StockAdjustmentDto | null>(null);
  isLoading = signal(true);
  showRejectModal = signal(false);
  rejectReason = signal('');

  constructor(
    private adjustmentService: StockAdjustmentService,
    public auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadAdjustment(+id);
    }
  }

  loadAdjustment(id: number): void {
    this.isLoading.set(true);
    this.adjustmentService.getById(id).subscribe({
      next: (res) => {
        this.adjustment.set(res.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.location.back();
      }
    });
  }

  submit(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    this.alertService.confirm('Submit this adjustment for approval?', () => {
      this.adjustmentService.submit(adjustment.id).subscribe({
        next: () => {
          this.alertService.success('Stock adjustment submitted for approval');
          this.loadAdjustment(adjustment.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    }, 'Submit Draft');
  }

  approve(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    this.alertService.confirm('Approve this adjustment and update inventory?', () => {
      this.adjustmentService.approve(adjustment.id).subscribe({
        next: () => {
          this.alertService.success('Stock adjustment approved');
          this.loadAdjustment(adjustment.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    }, 'Approve Adjustment');
  }

  openRejectModal(): void {
    this.rejectReason.set('');
    this.showRejectModal.set(true);
  }

  confirmReject(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    if (!this.rejectReason().trim()) {
      this.alertService.error('Please enter a rejection reason');
      return;
    }

    this.adjustmentService.reject(adjustment.id, { reason: this.rejectReason().trim() }).subscribe({
      next: () => {
        this.showRejectModal.set(false);
        this.alertService.success('Stock adjustment rejected');
        this.loadAdjustment(adjustment.id);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  cancelAdjustment(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    this.alertService.confirm('Cancel this stock adjustment?', () => {
      this.adjustmentService.cancel(adjustment.id).subscribe({
        next: () => {
          this.alertService.success('Stock adjustment cancelled');
          this.loadAdjustment(adjustment.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    }, 'Cancel Adjustment');
  }

  edit(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    this.router.navigate(['/stock-adjustments', adjustment.id, 'edit']);
  }

  deleteAdjustment(): void {
    const adjustment = this.adjustment();
    if (!adjustment) return;
    this.alertService.confirm('Delete this draft adjustment?', () => {
      this.adjustmentService.delete(adjustment.id).subscribe({
        next: () => {
          this.alertService.success('Stock adjustment deleted');
          this.router.navigate(['/stock-adjustments']);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    }, 'Delete Adjustment', 'Delete');
  }

  getStatusColor(status: string): string {
    return getStockAdjustmentStatusColor(status);
  }

  canEdit(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.edit') && canEditStockAdjustment(adjustment.status);
  }

  canSubmit(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.create') && adjustment.status === 'Draft';
  }

  canApprove(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.approve') && adjustment.status === 'PendingApproval';
  }

  canReject(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.reject') && adjustment.status === 'PendingApproval';
  }

  canCancel(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.edit') && canCancelStockAdjustment(adjustment.status);
  }

  canDelete(): boolean {
    const adjustment = this.adjustment();
    return !!adjustment && this.auth.hasPermission('stock_adjustments.delete') && adjustment.status === 'Draft';
  }

  goBack(): void {
    this.location.back();
  }

  formatDateTime(date?: string | null): string {
    if (!date) return '-';
    return new Date(date).toLocaleString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }
}
