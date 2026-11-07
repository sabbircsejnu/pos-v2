import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { SaleService } from '../../services/sale.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import {
  RefundSaleDto,
  RefundSaleItemDto,
  SaleDto,
  SaleItemDto,
  SaleRefundDto,
  SaleRefundLineDto,
  saleStatusLabel
} from '../../models/sale.model';
import { ReturnCondition, UOM_ERROR_MESSAGES } from '../../models/uom.model';
import { fitsPrecision, formatUnitQuantity, newIdempotencyKey } from '../../services/uom.service';
import { apiErrorCode } from '../../utils/uom-line.util';
import { UiAlertComponent, UiButtonComponent, UiCardComponent, UiControlDirective, UiFormFieldComponent } from '../../components/shared/ui';
import { UiDialogComponent } from '../../components/shared/ui/ui-dialog.component';

/** One sale line in the refund dialog. A line left at zero is not returned. */
export interface RefundLineDraft {
  item: SaleItemDto;
  quantity: number | null;
  condition: ReturnCondition;
  packagingReusable: boolean;
}

/** A line with a unit snapshot. Everything else is a legacy pcs line. */
function isUnitLine(item: SaleItemDto): boolean {
  return !!item.unitCode;
}

/** Base quantity still returnable on a line, as stated by the server. */
function remainingOf(item: SaleItemDto): number {
  return Math.max(0, Number(item.remainingReturnableQuantity ?? 0));
}

/** Server code for an old sale whose cash share cannot be derived (`CashSessionErrors.CashRefundPolicyUndefined`). */
export const CASH_REFUND_POLICY_UNDEFINED = 'CASH_REFUND_POLICY_UNDEFINED';

@Component({
  selector: 'app-sale-details',
  standalone: true,
  imports: [AppDatePipe, CommonModule, FormsModule, AppCurrencyPipe, UiDialogComponent, UiButtonComponent, UiControlDirective, UiFormFieldComponent, UiAlertComponent, UiCardComponent],
  templateUrl: './sale-details.component.html',
  styleUrls: ['./sale-details.component.css']
})
export class SaleDetailsComponent implements OnInit {
  sale = signal<SaleDto | null>(null);
  isLoading = signal(false);
  isVoiding = signal(false);
  showVoidModal = signal(false);
  voidReason = signal('');
  isPrinting = signal(false);

  showRefundDialog = signal(false);
  isRefunding = signal(false);
  refundLines = signal<RefundLineDraft[]>([]);
  refundReason = signal('');
  refundCashAmount = signal<number | null>(null);
  refundError = signal('');
  /** Shown only after the server said this old sale needs the cash paid back stated (CASH_REFUND_POLICY_UNDEFINED). */
  needsCashAmount = signal(false);

  /**
   * One key per refund request. It is kept for a retry of the same request and renewed after a success or when the
   * request changed since it was last sent (the server refuses a key reused with another payload).
   */
  private refundIdempotencyKey = '';
  private refundKeyPayload: string | null = null;

  /** Client-side estimate only: line subtotals pro-rated by the quantity returned. The server computes the refund. */
  estimatedRefundTotal = computed(() =>
    this.refundLines().reduce((total, line) => total + this.estimatedRefund(line), 0));

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    public saleService: SaleService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loadSale(id);
  }

  private loadSale(id: number): void {
    this.isLoading.set(true);
    this.saleService.getSaleById(id).subscribe({
      next: (res) => {
        this.sale.set(res.data || res);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  canVoid(): boolean {
    const s = this.sale();
    if (!s || s.status?.toLowerCase() !== 'completed') return false;
    if (!this.authService.hasPermission('sales.void')) return false;
    const saleDate = new Date(s.saleDate);
    const today = new Date();
    return saleDate.toDateString() === today.toDateString();
  }

  openVoidModal(): void {
    this.voidReason.set('');
    this.showVoidModal.set(true);
  }

  closeVoidModal(): void {
    this.showVoidModal.set(false);
  }

  confirmVoid(): void {
    if (!this.voidReason().trim()) {
      this.alertService.error('Please enter a reason for voiding this sale');
      return;
    }
    const s = this.sale();
    if (!s) return;

    this.isVoiding.set(true);
    this.saleService.voidSale(s.id, { reason: this.voidReason() }).subscribe({
      next: () => {
        this.alertService.success('Sale voided successfully');
        this.showVoidModal.set(false);
        this.isVoiding.set(false);
        this.loadSale(s.id);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isVoiding.set(false);
      }
    });
  }

  isUnitLine(item: SaleItemDto): boolean {
    return isUnitLine(item);
  }

  /** The quantity the customer bought: the entered unit when there is a snapshot, else the plain line quantity. */
  displayQuantity(item: SaleItemDto): string {
    return item.enteredQuantity !== null && item.enteredQuantity !== undefined
      ? formatUnitQuantity(item.enteredQuantity, item.unitLabel || item.unitCode)
      : String(item.quantity);
  }

  baseQuantityText(item: SaleItemDto): string {
    return formatUnitQuantity(item.quantity, item.baseUnitCode);
  }

  displayUnitPrice(item: SaleItemDto): number {
    return item.enteredUnitPrice ?? item.unitPrice;
  }

  /**
   * Refund is offered for every completed or partially refunded sale with something left to return, when the user holds
   * `sales.refund`. The server stays authoritative for the permission, the outlet and the quantities.
   */
  canRefund(): boolean {
    const s = this.sale();
    const status = s?.status?.toLowerCase();
    if (!s || (status !== 'completed' && status !== 'partially_refunded')) return false;
    if (!this.authService.hasPermission('sales.refund')) return false;
    return (s.items ?? []).some(item => remainingOf(item) > 0);
  }

  remaining(item: SaleItemDto): number {
    return remainingOf(item);
  }

  /** Recorded refunds, oldest first. */
  refunds(): SaleRefundDto[] {
    return this.sale()?.refunds ?? [];
  }

  /** A sale closed as refunded before refund history was recorded has no refund entries. */
  refundedWithoutHistory(): boolean {
    const s = this.sale();
    return s?.status?.toLowerCase() === 'refunded' && this.refunds().length === 0;
  }

  statusLabel(status: string | null | undefined): string {
    return saleStatusLabel(status);
  }

  /** The sale line a refund line returned, for its name and unit. */
  refundedItem(line: SaleRefundLineDto): SaleItemDto | undefined {
    return this.sale()?.items?.find(item => item.id === line.saleItemId);
  }

  refundLineQuantity(line: SaleRefundLineDto, quantity: number): string {
    return formatUnitQuantity(quantity, this.refundedItem(line)?.baseUnitCode);
  }

  conditionLabel(condition: string | null | undefined): string {
    if (condition === 'non_resellable') return 'Non-resellable';
    if (condition === 'resellable') return 'Resellable';
    return condition ?? '';
  }

  openRefundDialog(): void {
    this.refundLines.set((this.sale()?.items ?? []).map(item => ({
      item,
      quantity: 0,
      condition: 'resellable' as ReturnCondition,
      packagingReusable: false
    })));
    this.refundReason.set('');
    this.refundCashAmount.set(null);
    this.needsCashAmount.set(false);
    this.refundError.set('');
    this.renewRefundKey();
    this.showRefundDialog.set(true);
  }

  updateRefundLine(index: number, changes: Partial<RefundLineDraft>): void {
    this.refundLines.update(lines => lines.map((line, i) => {
      if (i !== index) return line;
      const next = { ...line, ...changes };
      // Packaging of a non-resellable line never goes back to stock.
      return next.condition === 'non_resellable' ? { ...next, packagingReusable: false } : next;
    }));
  }

  /** Client-side estimate for one line: subtotal x returned / sold. The final amount comes from the server. */
  estimatedRefund(line: RefundLineDraft): number {
    const quantity = Number(line.quantity ?? 0);
    const sold = Number(line.item.quantity ?? 0);
    if (!(quantity > 0) || !(sold > 0)) return 0;
    return Number(line.item.subtotal ?? 0) * quantity / sold;
  }

  /** The first problem with the entered refund, phrased for the user; empty when it can be sent. */
  refundProblem(): string {
    const lines = this.refundLines();
    for (const line of lines) {
      const quantity = Number(line.quantity ?? 0);
      const name = line.item.productName;
      if (!Number.isFinite(quantity)) return `${name}: enter a valid quantity.`;
      if (quantity < 0) return `${name}: the quantity cannot be negative.`;
      if (quantity > remainingOf(line.item)) {
        return `${name}: the quantity cannot exceed the ${this.remainingText(line.item)} still returnable.`;
      }
      if (!fitsPrecision(quantity, isUnitLine(line.item) ? 6 : 0)) {
        return isUnitLine(line.item) ? `${name}: use at most 6 decimal places.` : `${name}: the quantity must be a whole number.`;
      }
    }
    if (!lines.some(line => Number(line.quantity ?? 0) > 0)) return 'Enter a quantity for at least one line.';
    if (!this.refundReason().trim()) return 'Enter a reason for this refund.';
    if (this.needsCashAmount()) {
      const cash = Number(this.refundCashAmount() ?? 0);
      if (cash <= 0 || !fitsPrecision(cash, 2)) return 'Enter the cash paid back, with at most 2 decimal places.';
    }
    return '';
  }

  refundUnitOf(item: SaleItemDto): string {
    return formatUnitQuantity(item.returnedQuantity, item.baseUnitCode);
  }

  /** `488 ml` for a unit line, `3 pcs` for a plain line. */
  refundUnit(item: SaleItemDto): string {
    return formatUnitQuantity(item.quantity, item.baseUnitCode);
  }

  remainingText(item: SaleItemDto): string {
    return formatUnitQuantity(remainingOf(item), item.baseUnitCode);
  }

  /** Every line states its condition; only unit lines carry the packaging flag. */
  private toRefundItem(line: RefundLineDraft): RefundSaleItemDto {
    const dto: RefundSaleItemDto = {
      variantId: line.item.variantId,
      quantity: Number(line.quantity),
      saleItemId: line.item.id,
      condition: line.condition
    };
    if (isUnitLine(line.item)) dto.packagingReusable = line.packagingReusable;
    return dto;
  }

  private renewRefundKey(): void {
    this.refundIdempotencyKey = newIdempotencyKey('refund');
    this.refundKeyPayload = null;
  }

  confirmRefund(): void {
    const s = this.sale();
    if (!s || this.isRefunding()) return;
    const problem = this.refundProblem();
    if (problem) {
      this.refundError.set(problem);
      return;
    }

    const request: RefundSaleDto = {
      items: this.refundLines().filter(line => Number(line.quantity ?? 0) > 0).map(line => this.toRefundItem(line)),
      reason: this.refundReason().trim()
    };
    if (this.needsCashAmount()) request.cashRefundAmount = Number(this.refundCashAmount());

    // A changed request is a new refund attempt and gets a new key; an unchanged retry repeats the key.
    const payload = JSON.stringify(request);
    if (this.refundKeyPayload !== null && this.refundKeyPayload !== payload) this.renewRefundKey();
    this.refundKeyPayload = payload;
    const dto: RefundSaleDto = { ...request, idempotencyKey: this.refundIdempotencyKey };

    this.refundError.set('');
    this.isRefunding.set(true);
    this.saleService.refundSale(s.id, dto).subscribe({
      next: () => {
        this.isRefunding.set(false);
        this.renewRefundKey();
        this.showRefundDialog.set(false);
        this.alertService.success('Refund recorded successfully');
        this.loadSale(s.id);
      },
      // The dialog stays open with everything entered, so the refund can be corrected and sent again.
      error: (err) => {
        this.isRefunding.set(false);
        this.refundError.set(this.refundErrorMessage(err));
      }
    });
  }

  private refundErrorMessage(err: unknown): string {
    // 403 is access denied: shown here, never a logout.
    if ((err as { status?: number } | null)?.status === 403) {
      return 'Access denied: you do not have permission to refund this sale.';
    }
    const code = apiErrorCode(err);
    if (code === CASH_REFUND_POLICY_UNDEFINED) {
      this.needsCashAmount.set(true);
      return 'This older sale has no recorded payment split. Enter the cash paid back to the customer, then refund again.';
    }
    if (code === 'SALE_REFUND_IDEMPOTENCY_KEY_REQUIRED' || code === 'IDEMPOTENCY_KEY_PAYLOAD_MISMATCH') {
      return 'The refund request could not be matched to an earlier attempt. Check the lines and refund again.';
    }
    return UOM_ERROR_MESSAGES[code] ?? this.errorHandler.extractErrorMessage(err);
  }

  /** Records the reprint on the server (print history, audited), then opens the same receipt as a PDF to print. */
  reprintReceipt(): void {
    const s = this.sale();
    if (!s || this.isPrinting()) return;
    this.isPrinting.set(true);
    this.saleService.reprintSaleReceipt(s.id).subscribe({
      next: () => this.openReceiptPdf(s.id, true),
      error: (err) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  downloadReceiptPdf(): void {
    const s = this.sale();
    if (!s || this.isPrinting()) return;
    this.isPrinting.set(true);
    this.openReceiptPdf(s.id, false);
  }

  private openReceiptPdf(saleId: number, forPrinting: boolean): void {
    this.saleService.getReceiptPdf(saleId, '80mm').subscribe({
      next: (blob) => {
        this.isPrinting.set(false);
        const url = URL.createObjectURL(blob);
        if (forPrinting) {
          window.open(url, '_blank', 'noopener');
          setTimeout(() => URL.revokeObjectURL(url), 60_000);
          return;
        }
        const link = document.createElement('a');
        link.href = url;
        link.download = `receipt-${this.sale()?.saleNumber || saleId}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  goBack(): void {
    this.location.back();
  }

  getStatusClass(status: string): string {
    switch (status?.toLowerCase()) {
      case 'completed': return 'status-completed';
      case 'partially_refunded': return 'status-partial';
      case 'void':
      case 'voided':
      case 'refunded': return 'status-void';
      default: return 'status-default';
    }
  }

  getPaymentIcon(method: string): string {
    switch (method?.toLowerCase()) {
      case 'cash': return 'fas fa-money-bill-wave';
      case 'card': return 'fas fa-credit-card';
      case 'mobile': return 'fas fa-mobile-alt';
      default: return 'fas fa-dollar-sign';
    }
  }
}
