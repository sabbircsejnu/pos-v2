import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  StockTransferDto,
  getTransferStatusLabel,
  getTransferStatusColor
} from '../../models/stock-transfer.model';

@Component({
  selector: 'app-transfer-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transfer-details.component.html',
  styleUrls: ['./transfer-details.component.css']
})
export class TransferDetailsComponent implements OnInit {
  transfer = signal<StockTransferDto | null>(null);
  isLoading = signal(true);

  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingAction?: (reason: string) => void;

  constructor(
    private transferService: StockTransferService,
    private route: ActivatedRoute,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadTransfer(+id);
    }
  }

  loadTransfer(id: number): void {
    this.isLoading.set(true);
    this.transferService.getById(id).subscribe({
      next: (res) => {
        this.transfer.set(res.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.router.navigate(['/stock-transfers']);
      }
    });
  }

  approve(): void {
    const t = this.transfer();
    if (!t) return;
    this.alertService.confirm('Approve this stock transfer?', () => {
      this.transferService.approve(t.id).subscribe({
        next: () => {
          this.alertService.success('Transfer approved');
          this.loadTransfer(t.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  reject(): void {
    const t = this.transfer();
    if (!t) return;
    this.openReasonModal('Reject Transfer', (reason) => {
      this.transferService.reject(t.id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer rejected');
          this.loadTransfer(t.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  send(): void {
    const t = this.transfer();
    if (!t) return;
    this.alertService.confirm('Mark this transfer as in-transit?', () => {
      this.transferService.send(t.id).subscribe({
        next: () => {
          this.alertService.success('Transfer marked as in-transit');
          this.loadTransfer(t.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  receive(): void {
    const t = this.transfer();
    if (!t) return;
    this.alertService.confirm('Receive this transfer and update stock?', () => {
      this.transferService.receive(t.id).subscribe({
        next: () => {
          this.alertService.success('Transfer received and stock updated');
          this.loadTransfer(t.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  cancel(): void {
    const t = this.transfer();
    if (!t) return;
    this.openReasonModal('Cancel Transfer', (reason) => {
      this.transferService.cancel(t.id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer cancelled');
          this.loadTransfer(t.id);
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  private openReasonModal(title: string, action: (reason: string) => void): void {
    this.reasonModalTitle.set(title);
    this.reasonText.set('');
    this.pendingAction = action;
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    if (!this.reasonText().trim()) {
      this.alertService.error('Please enter a reason');
      return;
    }
    this.showReasonModal.set(false);
    if (this.pendingAction) {
      this.pendingAction(this.reasonText().trim());
      this.pendingAction = undefined;
    }
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.pendingAction = undefined;
  }

  goBack(): void {
    this.router.navigate(['/stock-transfers']);
  }

  getStatusLabel(status: string): string {
    return getTransferStatusLabel(status);
  }

  getStatusColor(status: string): string {
    return getTransferStatusColor(status);
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }

  formatDateTime(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }
}
