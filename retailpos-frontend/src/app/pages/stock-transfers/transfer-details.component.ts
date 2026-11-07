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
import { quantityStep } from '../../services/uom.service';
import { enteredUnitLabel, formatUnitTotals, roundQuantity, unitTotalLabel } from '../../utils/uom-quantity.util';
import { injectVariantUnits } from '../../utils/variant-units';
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
  missingQuantity: number;
  damagedQuantity: number;
  remarks: string;
  /** The line holds sealed or prepared packs: accepted whole or rejected whole. */
  wholeLineOnly: boolean;
}

type ReceiveQuantityField = 'acceptedQuantity' | 'rejectedQuantity' | 'missingQuantity' | 'damagedQuantity';

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
  readonly units = injectVariantUnits(() => this.transfer()?.items.map(item => item.variantId) ?? []);
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
          missingQuantity: 0,
          damagedQuantity: 0,
          remarks: item.remarks || '',
          wholeLineOnly: (item.allocations ?? []).some(allocation => allocation.state !== 'loose')
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

  /** Decimal places a row may be split to. Plain pcs stay whole; an unknown unit follows what was dispatched. */
  receivePrecision(row: ReceiveLineInput): number {
    return this.units.config(row.variantId)?.baseUnit.precision ?? (Number.isInteger(row.transferQuantity) ? 0 : 6);
  }

  receiveStep(row: ReceiveLineInput): string {
    return quantityStep(this.receivePrecision(row));
  }

  /**
   * Sets one quantity and rebalances the row so it always totals the dispatched quantity: reducing Accepted moves
   * the difference to Rejected (or to Missing on a return, which cannot be rejected again); any other change
   * takes from or gives back to Accepted.
   */
  updateReceiveQuantity(index: number, field: ReceiveQuantityField, value: number): void {
    this.receiveItems.update(items => {
      const updated = [...items];
      const row = { ...updated[index] };
      const others = (keep: ReceiveQuantityField[]) =>
        keep.reduce((total, name) => total + row[name], 0);
      const scale = 10 ** this.receivePrecision(row);
      const requested = Math.max(0, Math.floor(roundQuantity((Number(value) || 0) * scale)) / scale);

      if (field === 'acceptedQuantity') {
        row.acceptedQuantity = Math.min(requested, roundQuantity(row.transferQuantity - others(['missingQuantity', 'damagedQuantity'])));
        const remainder = roundQuantity(row.transferQuantity - row.acceptedQuantity - others(['missingQuantity', 'damagedQuantity']));
        if (this.isSystemReturn()) {
          row.missingQuantity = roundQuantity(row.missingQuantity + remainder);
        } else {
          row.rejectedQuantity = remainder;
        }
      } else {
        const fixed = (['rejectedQuantity', 'missingQuantity', 'damagedQuantity'] as ReceiveQuantityField[])
          .filter(name => name !== field);
        row[field] = Math.min(requested, roundQuantity(row.transferQuantity - others(fixed)));
        row.acceptedQuantity = roundQuantity(row.transferQuantity - row[field] - others(fixed));
      }

      updated[index] = row;
      return updated;
    });
  }

  /** The one way to settle a line holding packs. A system return cannot be rejected again, so it is only accepted. */
  setWholeLine(index: number, outcome: 'accept' | 'reject'): void {
    this.receiveItems.update(items => {
      const updated = [...items];
      const row = updated[index];
      const accepted = outcome === 'accept';
      updated[index] = {
        ...row,
        acceptedQuantity: accepted ? row.transferQuantity : 0,
        rejectedQuantity: accepted ? 0 : row.transferQuantity,
        missingQuantity: 0,
        damagedQuantity: 0
      };
      return updated;
    });
  }

  isSystemReturn(): boolean {
    return !!this.transfer()?.originalTransferId;
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
    if (!t || this.isReceiving()) return;

    const invalidRow = this.receiveItems().find(
      row => row.acceptedQuantity < 0 ||
        row.rejectedQuantity < 0 ||
        row.missingQuantity < 0 ||
        row.damagedQuantity < 0 ||
        roundQuantity(row.acceptedQuantity + row.rejectedQuantity + row.missingQuantity + row.damagedQuantity) !== row.transferQuantity
    );

    if (invalidRow) {
      this.alertService.error('Accepted, rejected, missing and damaged quantities must total the transfer quantity for every row');
      return;
    }

    if (this.receiveItems().some(row => row.wholeLineOnly
      && row.acceptedQuantity !== row.transferQuantity && row.rejectedQuantity !== row.transferQuantity)) {
      this.alertService.error('A line with sealed or prepared packs must be accepted whole or rejected whole');
      return;
    }

    if (this.receiveItems().some(row => row.missingQuantity + row.damagedQuantity > 0 && !row.remarks?.trim())) {
      this.alertService.error('Enter a reason in Remarks for every row with missing or damaged units');
      return;
    }

    const payload: ReceiveStockTransferDto = {
      notes: this.receiveNotes().trim() || undefined,
      items: this.receiveItems().map(row => ({
        variantId: row.variantId,
        acceptedQuantity: row.acceptedQuantity,
        rejectedQuantity: row.rejectedQuantity,
        missingQuantity: row.missingQuantity,
        damagedQuantity: row.damagedQuantity,
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

  /** The plain sum while only pcs are involved, otherwise one figure per unit. Unlike units are never added. */
  totalLabel(quantity: (item: StockTransferDto['items'][number]) => number): string {
    return unitTotalLabel((this.transfer()?.items || []).map(item => ({
      unitCode: this.units.unit(item.variantId) ?? 'pcs',
      quantity: quantity(item) || 0
    })));
  }

  enteredLabel(item: StockTransferDto['items'][number]): string | null {
    return enteredUnitLabel(item.uomEnteredQuantity, item.uomLabel);
  }

  /** `Sealed JAR500: 2 packs (1,000 ml) · Loose: 100 ml`. Empty for a line without stock forms. */
  allocationText(item: StockTransferDto['items'][number]): string {
    const unit = this.units.unit(item.variantId);
    return (item.allocations ?? []).map(allocation => {
      const quantity = formatUnitTotals([{ unitCode: unit ?? 'pcs', quantity: allocation.baseQuantity }]);
      if (allocation.state === 'loose') {
        return `Loose: ${quantity}`;
      }
      const form = allocation.state === 'sealed' ? 'Sealed' : 'Prepared';
      return `${form} ${allocation.packCode}: ${allocation.packCount} ${allocation.packCount === 1 ? 'pack' : 'packs'} (${quantity})`;
    }).join(' · ');
  }

  transferTotalLabel(): string {
    return this.totalLabel(item => this.getEffectiveTransferQty(item));
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
      { label: 'Total Transfer Quantity', value: this.transferTotalLabel() },
      { label: 'Total Accepted Quantity', value: this.totalLabel(item => item.acceptedQuantity) },
      { label: 'Total Rejected Quantity', value: this.totalLabel(item => item.rejectedQuantity) },
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
      && !transfer.originalTransferId
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
