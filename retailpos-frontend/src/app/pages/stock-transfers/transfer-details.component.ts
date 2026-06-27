import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { RouterLink } from '@angular/router';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import {
  ReceiveStockTransferDto,
  StockTransferDto,
  getTransferTypeLabel,
  getTransferStatusLabel,
  getTransferStatusColor
} from '../../models/stock-transfer.model';

interface ReceiveLineInput {
  variantId: number;
  variantSku: string;
  productName: string;
  transferQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  remarks: string;
}

@Component({
  selector: 'app-transfer-details',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './transfer-details.component.html',
  styleUrls: ['./transfer-details.component.css']
})
export class TransferDetailsComponent implements OnInit {
  transfer = signal<StockTransferDto | null>(null);
  isLoading = signal(true);
  defaultLocation = signal<{ id: number; type: 'outlet' | 'warehouse' } | null>(null);

  showReceiveModal = signal(false);
  receiveItems = signal<ReceiveLineInput[]>([]);
  receiveNotes = signal('');
  isReceiving = signal(false);

  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingAction?: (reason: string) => void;

  constructor(
    private transferService: StockTransferService,
    public auth: AuthService,
    private userOutletAccess: UserOutletAccessService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadDefaultLocation();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadTransfer(+id);
    }
  }

  private loadDefaultLocation(): void {
    this.userOutletAccess.load().subscribe({
      next: (response) => {
        const auth = response?.data;
        if (auth?.defaultLocationId && auth?.defaultLocationType) {
          this.defaultLocation.set({ id: auth.defaultLocationId, type: auth.defaultLocationType });
          return;
        }

        this.defaultLocation.set(null);
      },
      error: () => {
        this.defaultLocation.set(null);
      }
    });
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
        this.location.back();
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
    this.receiveItems.set(
      t.items.map(item => {
        const transferQty = item.transferQuantity > 0 ? item.transferQuantity : item.quantity;
        return {
          variantId: item.variantId,
          variantSku: item.variantSku,
          productName: item.productName,
          transferQuantity: transferQty,
          acceptedQuantity: transferQty,
          rejectedQuantity: 0,
          remarks: item.remarks || ''
        };
      })
    );
    this.receiveNotes.set('');
    this.showReceiveModal.set(true);
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

  closeReceiveModal(): void {
    this.showReceiveModal.set(false);
    this.receiveItems.set([]);
    this.receiveNotes.set('');
    this.isReceiving.set(false);
  }

  updateReceiveAccepted(index: number, value: number): void {
    this.receiveItems.update(items => {
      const updated = [...items];
      const row = updated[index];
      const accepted = Math.max(0, Number(value) || 0);
      const boundedAccepted = Math.min(accepted, row.transferQuantity);
      updated[index] = {
        ...row,
        acceptedQuantity: boundedAccepted,
        rejectedQuantity: row.transferQuantity - boundedAccepted
      };
      return updated;
    });
  }

  updateReceiveRejected(index: number, value: number): void {
    this.receiveItems.update(items => {
      const updated = [...items];
      const row = updated[index];
      const rejected = Math.max(0, Number(value) || 0);
      const boundedRejected = Math.min(rejected, row.transferQuantity);
      updated[index] = {
        ...row,
        rejectedQuantity: boundedRejected,
        acceptedQuantity: row.transferQuantity - boundedRejected
      };
      return updated;
    });
  }

  updateReceiveRemarks(index: number, value: string): void {
    this.receiveItems.update(items => {
      const updated = [...items];
      updated[index] = { ...updated[index], remarks: value };
      return updated;
    });
  }

  submitReceive(): void {
    const t = this.transfer();
    if (!t) return;

    const invalidRow = this.receiveItems().find(
      row => row.acceptedQuantity < 0 ||
        row.rejectedQuantity < 0 ||
        row.acceptedQuantity + row.rejectedQuantity !== row.transferQuantity
    );

    if (invalidRow) {
      this.alertService.error('Accepted and rejected quantities must total transfer quantity for every row');
      return;
    }

    const payload: ReceiveStockTransferDto = {
      notes: this.receiveNotes().trim() || undefined,
      items: this.receiveItems().map(row => ({
        variantId: row.variantId,
        acceptedQuantity: row.acceptedQuantity,
        rejectedQuantity: row.rejectedQuantity,
        remarks: row.remarks?.trim() || undefined
      }))
    };

    this.isReceiving.set(true);
    this.transferService.receive(t.id, payload).subscribe({
      next: () => {
        this.alertService.success('Transfer received and stock updated');
        this.closeReceiveModal();
        this.loadTransfer(t.id);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isReceiving.set(false);
      }
    });
  }

  goBack(): void {
    this.location.back();
  }

  getStatusLabel(status: string): string {
    return getTransferStatusLabel(status);
  }

  getTypeLabel(type: string): string {
    return getTransferTypeLabel(type);
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

  getEffectiveTransferQty(item: { transferQuantity: number; quantity: number }): number {
    return item.transferQuantity > 0 ? item.transferQuantity : item.quantity;
  }

  canReceiveOrRejectCurrentTransfer(): boolean {
    const transfer = this.transfer();
    const userDefault = this.defaultLocation();
    if (!transfer || !userDefault) {
      return false;
    }

    return transfer.toLocationId === userDefault.id
      && transfer.toLocationType.toLowerCase() === userDefault.type;
  }
}
