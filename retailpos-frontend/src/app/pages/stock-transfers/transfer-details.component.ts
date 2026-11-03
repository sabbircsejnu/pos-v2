import { CompanyDateService } from '../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { RouterLink } from '@angular/router';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { ProductVariantCellComponent } from '../../components/shared/product-variant-cell.component';
import { AuthorizedOutletsDto } from '../../models/report.model';
import {
  ReceiveStockTransferDto,
  StockTransferDto,
  getTransferTypeLabel,
  getTransferStatusLabel,
  getTransferStatusColor
} from '../../models/stock-transfer.model';
import { canDispatchTransferFromSource, canManageTransferFromSource, canReceiveTransferAtDestination } from '../../utils/stock-transfer-access.util';

interface ReceiveLineInput {
  variantId: number;
  productCode?: string;
  variantSku: string;
  productName: string;
  variantAttributes?: string;
  variantDisplay?: string;
  primaryImageThumb?: string;
  transferQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  remarks: string;
}

interface DetailField {
  label: string;
  value: string;
  emphasize?: boolean;
}

interface AuditField {
  label: string;
  name?: string;
  at?: string;
}

@Component({
  selector: 'app-transfer-details',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ProductVariantCellComponent],
  templateUrl: './transfer-details.component.html',
  styleUrls: ['./transfer-details.component.css']
})
export class TransferDetailsComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  transfer = signal<StockTransferDto | null>(null);
  isLoading = signal(true);
  authorizedAccess = signal<AuthorizedOutletsDto | null>(null);
  sourceAccess = signal<AuthorizedOutletsDto | null>(null);

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
        this.authorizedAccess.set((response?.data ?? null) as AuthorizedOutletsDto | null);
      },
      error: () => {
        this.authorizedAccess.set(null);
      }
    });

    this.userOutletAccess.load('StockTransferSource').subscribe({
      next: (response) => {
        this.sourceAccess.set((response?.data ?? null) as AuthorizedOutletsDto | null);
      },
      error: () => {
        this.sourceAccess.set(null);
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
          productCode: item.productCode,
          variantSku: item.variantSku,
          productName: item.productName,
          variantAttributes: item.variantAttributes,
          variantDisplay: item.variantDisplay,
          primaryImageThumb: item.primaryImageThumb,
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
    return this.companyDates.format(date, false);
  }

  formatDateTime(date: string): string {
    if (!date) return '-';
    return this.companyDates.format(date, true);
  }

  getEffectiveTransferQty(item: { transferQuantity: number; quantity: number }): number {
    return item.transferQuantity > 0 ? item.transferQuantity : item.quantity;
  }

  getTransferReference(): string {
    const transfer = this.transfer();
    if (!transfer) {
      return 'Stock Transfer';
    }

    return transfer.transferNo || `Transfer #${transfer.id}`;
  }

  getHeaderCreatedDate(): string {
    const createdAt = this.transfer()?.createdAt;
    return createdAt ? this.formatDate(createdAt) : '-';
  }

  getTotalTransferQuantity(): number {
    return (this.transfer()?.items || []).reduce((sum, item) => sum + this.getEffectiveTransferQty(item), 0);
  }

  getTotalAcceptedQuantity(): number {
    return (this.transfer()?.items || []).reduce((sum, item) => sum + (item.acceptedQuantity || 0), 0);
  }

  getTotalRejectedQuantity(): number {
    return (this.transfer()?.items || []).reduce((sum, item) => sum + (item.rejectedQuantity || 0), 0);
  }

  isRequisitionTransfer(): boolean {
    return this.transfer()?.transferType?.toLowerCase() === 'requisition';
  }

  formatLocationType(value?: string): string {
    if (!value?.trim()) {
      return '-';
    }

    const normalized = value.trim().toLowerCase();
    return normalized.charAt(0).toUpperCase() + normalized.slice(1);
  }

  getBasicInformationFields(): DetailField[] {
    const transfer = this.transfer();
    if (!transfer) {
      return [];
    }

    const fields: DetailField[] = [
      { label: 'From Location', value: transfer.fromLocationName || '-' },
      { label: 'From Location Type', value: this.formatLocationType(transfer.fromLocationType) },
      { label: 'To Location', value: transfer.toLocationName || '-' },
      { label: 'To Location Type', value: this.formatLocationType(transfer.toLocationType) },
      { label: 'Transfer Date', value: this.formatDate(transfer.transferDate) },
      { label: 'Transfer Type', value: this.getTypeLabel(transfer.transferType) }
    ];

    if (transfer.relatedRequisitionId) {
      fields.push({ label: 'Related Requisition', value: `REQ Reference #${transfer.relatedRequisitionId}` });
    }

    return fields;
  }

  getSummaryFields(): DetailField[] {
    const transfer = this.transfer();
    if (!transfer) {
      return [];
    }

    return [
      { label: 'Total Items', value: String(transfer.items.length) },
      { label: 'Total Transfer Quantity', value: String(this.getTotalTransferQuantity()) },
      { label: 'Total Accepted Quantity', value: String(this.getTotalAcceptedQuantity()) },
      { label: 'Total Rejected Quantity', value: String(this.getTotalRejectedQuantity()) },
      { label: 'Current Status', value: this.getStatusLabel(transfer.status), emphasize: true }
    ];
  }

  getAuditFields(): AuditField[] {
    const transfer = this.transfer();
    if (!transfer) {
      return [];
    }

    return [
      { label: 'Created', name: transfer.creatorName, at: transfer.createdAt },
      { label: 'Submitted', name: transfer.submittedBy ? `User #${transfer.submittedBy}` : undefined, at: transfer.submittedAt },
      { label: 'Approved', name: transfer.approverName, at: transfer.approvedAt },
      { label: 'Dispatched', name: transfer.dispatchedBy ? `User #${transfer.dispatchedBy}` : undefined, at: transfer.dispatchedAt },
      { label: 'Received', name: transfer.receivedBy ? `User #${transfer.receivedBy}` : undefined, at: transfer.receivedAt },
      { label: 'Rejected', name: transfer.rejectedBy ? `User #${transfer.rejectedBy}` : undefined, at: transfer.rejectedAt },
      { label: 'Cancelled', name: transfer.cancelledBy ? `User #${transfer.cancelledBy}` : undefined, at: transfer.cancelledAt }
    ].filter(field => !!field.name || !!field.at);
  }

  hasRelatedRecords(): boolean {
    const transfer = this.transfer();
    return !!transfer?.relatedRequisitionId;
  }

  hasAnyWorkflowActions(): boolean {
    return this.canApproveAction()
      || this.canRejectAction()
      || this.canCancelAction()
      || this.canSendAction()
      || this.canReceiveAction();
  }

  canApproveAction(): boolean {
    const transfer = this.transfer();
    return !!transfer
      && (transfer.status === 'submitted' || transfer.status === 'pending')
      && this.auth.hasPermission('stock_transfers.approve')
      && this.canApproveCurrentTransfer();
  }

  canRejectAction(): boolean {
    const transfer = this.transfer();
    return !!transfer
      && transfer.status === 'in_transit'
      && this.auth.hasPermission('stock_transfers.reject_receive')
      && this.canReceiveOrRejectCurrentTransfer();
  }

  canCancelAction(): boolean {
    const transfer = this.transfer();
    return !!transfer
      && (transfer.status === 'draft' || transfer.status === 'submitted' || transfer.status === 'pending' || transfer.status === 'approved')
      && this.auth.hasPermission('stock_transfers.cancel')
      && this.canCancelCurrentTransfer();
  }

  canSendAction(): boolean {
    const transfer = this.transfer();
    return !!transfer
      && transfer.status === 'approved'
      && this.auth.hasPermission('stock_transfers.dispatch')
      && this.canSendCurrentTransfer();
  }

  canReceiveAction(): boolean {
    const transfer = this.transfer();
    return !!transfer
      && transfer.status === 'in_transit'
      && this.auth.hasPermission('stock_transfers.receive')
      && this.canReceiveOrRejectCurrentTransfer();
  }

  canReceiveOrRejectCurrentTransfer(): boolean {
    const transfer = this.transfer();
    if (!transfer) {
      return false;
    }

    return canReceiveTransferAtDestination(transfer, this.authorizedAccess());
  }

  canApproveCurrentTransfer(): boolean {
    const transfer = this.transfer();
    return !!transfer && canManageTransferFromSource(transfer, this.sourceAccess());
  }

  canCancelCurrentTransfer(): boolean {
    const transfer = this.transfer();
    return !!transfer && canManageTransferFromSource(transfer, this.sourceAccess());
  }

  canSendCurrentTransfer(): boolean {
    const transfer = this.transfer();
    return !!transfer && canDispatchTransferFromSource(
      transfer,
      this.authorizedAccess(),
      this.auth.hasPermission('stock_transfers.transfer_from_any_location'),
      this.sourceAccess()
    );
  }
}
