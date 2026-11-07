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
import { RefundSaleDto, RefundSaleItemDto, SaleDto, SaleItemDto } from '../../models/sale.model';
import { ReturnCondition, UOM_ERROR_MESSAGES } from '../../models/uom.model';
import { fitsPrecision, formatUnitQuantity } from '../../services/uom.service';
import { apiErrorCode } from '../../utils/uom-line.util';
import { UiButtonComponent, UiControlDirective, UiFormFieldComponent } from '../../components/shared/ui';
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

@Component({
  selector: 'app-sale-details',
  standalone: true,
  imports: [AppDatePipe, CommonModule, FormsModule, AppCurrencyPipe, UiDialogComponent, UiButtonComponent, UiControlDirective, UiFormFieldComponent],
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

  /** Every line returned in full: the server then pays back the sale's remaining cash itself. */
  private readonly isFullRefund = computed(() =>
    this.refundLines().every(line => Number(line.quantity ?? 0) >= line.item.quantity));

  /** A partial refund of a sale paid in cash must state the cash actually paid back. */
  needsCashAmount = computed(() => !this.isFullRefund() && this.saleHasCashPayment());

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
   * The refund dialog is offered for sales with at least one unit line. A plain pcs sale has no refund screen today
   * and stays as it is; the server enforces the permission, the outlet and the single refund.
   */
  canRefund(): boolean {
    const s = this.sale();
    if (!s || s.status?.toLowerCase() !== 'completed') return false;
    if (!this.authService.hasPermission('sales.refund')) return false;
    return (s.items ?? []).some(isUnitLine);
  }

  private saleHasCashPayment(): boolean {
    const s = this.sale();
    if (!s) return false;
    return (s.payments ?? []).some(payment => payment.method?.toLowerCase() === 'cash')
      || s.paymentMethod?.toLowerCase() === 'cash';
  }

  openRefundDialog(): void {
    this.refundLines.set((this.sale()?.items ?? []).map(item => ({
      item,
      quantity: item.quantity,
      condition: 'resellable' as ReturnCondition,
      packagingReusable: false
    })));
    this.refundReason.set('');
    this.refundCashAmount.set(null);
    this.refundError.set('');
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

  /** The first problem with the entered refund, phrased for the user; empty when it can be sent. */
  refundProblem(): string {
    const lines = this.refundLines();
    if (!lines.some(line => Number(line.quantity ?? 0) > 0)) return 'Enter a quantity for at least one line.';
    for (const line of lines) {
      const quantity = Number(line.quantity ?? 0);
      const name = line.item.productName;
      if (quantity < 0) return `${name}: the quantity cannot be negative.`;
      if (quantity > line.item.quantity) return `${name}: the quantity cannot exceed the ${this.refundUnit(line.item)} sold.`;
      if (!fitsPrecision(quantity, isUnitLine(line.item) ? 6 : 0)) {
        return isUnitLine(line.item) ? `${name}: use at most 6 decimal places.` : `${name}: the quantity must be a whole number.`;
      }
    }
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

  /** A plain pcs line keeps the standing behaviour: no condition is sent and everything returns to stock. */
  private toRefundItem(line: RefundLineDraft): RefundSaleItemDto {
    const dto: RefundSaleItemDto = {
      variantId: line.item.variantId,
      quantity: Number(line.quantity),
      saleItemId: line.item.id
    };
    if (isUnitLine(line.item)) {
      dto.condition = line.condition;
      dto.packagingReusable = line.packagingReusable;
    }
    return dto;
  }

  confirmRefund(): void {
    const s = this.sale();
    if (!s || this.isRefunding()) return;
    const problem = this.refundProblem();
    if (problem) {
      this.refundError.set(problem);
      return;
    }

    const dto: RefundSaleDto = {
      items: this.refundLines().filter(line => Number(line.quantity ?? 0) > 0).map(line => this.toRefundItem(line)),
      reason: this.refundReason().trim()
    };
    if (this.needsCashAmount()) dto.cashRefundAmount = Number(this.refundCashAmount());

    this.refundError.set('');
    this.isRefunding.set(true);
    this.saleService.refundSale(s.id, dto).subscribe({
      next: () => {
        this.isRefunding.set(false);
        this.showRefundDialog.set(false);
        this.alertService.success('Sale refunded successfully');
        this.loadSale(s.id);
      },
      // The dialog stays open with everything entered, so the refund can be corrected and sent again.
      error: (err) => {
        this.isRefunding.set(false);
        this.refundError.set(UOM_ERROR_MESSAGES[apiErrorCode(err)] ?? this.errorHandler.extractErrorMessage(err));
      }
    });
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
