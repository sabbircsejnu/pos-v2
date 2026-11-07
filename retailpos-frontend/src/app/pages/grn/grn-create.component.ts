import { CompanyDateService } from '../../services/company-date.service';
import { getPurchaseLocationName, getPurchaseLocationTypeLabel } from '../../models/purchase-order.model';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, DestroyRef, OnInit, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { CurrencyService } from '../../services/currency.service';
import {
  PurchaseOrderForGrn,
  PurchaseOrderItemForGrn,
  CreateGrnDto,
  CreateGrnItemDto
} from '../../models/grn.model';

import { AuthService } from '../../services/auth.service';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { fitsPrecision, formatUnitQuantity, quantityStep, UomService } from '../../services/uom.service';
import { PurchaseOrderItem } from '../../models/purchase-order.model';
import { VariantUomConfig } from '../../models/uom.model';
import { checkPackBreakdown, isEntered, roundQuantity } from '../../utils/uom-line.util';

interface GrnLineItem extends PurchaseOrderItemForGrn {
  /** The accepted base quantity. */
  receivedQty: number;
  damagedQty?: number | null;
  rejectedQty?: number | null;
  acceptedSealedPacks?: number | null;
  acceptedLooseQty?: number | null;
  /** The variant's current configuration. Absent on a plain pcs line. */
  uomConfig?: VariantUomConfig;
  /** The PO line, for the purchase unit it was ordered in. Absent on a legacy pcs line. */
  poLine?: PurchaseOrderItem;
}

@Component({
  selector: 'app-grn-create',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './grn-create.component.html',
  styleUrls: ['./grn-create.component.css']
})
export class GrnCreateComponent implements OnInit {
  private readonly companyDates = inject(CompanyDateService);
  readonly locationTypeLabel = getPurchaseLocationTypeLabel;
  readonly locationName = getPurchaseLocationName;
  private imageSvc = inject(ProductImageService);
  private uom = inject(UomService);
  private purchaseOrders = inject(PurchaseOrderService);
  private auth = inject(AuthService);
  private destroyRef = inject(DestroyRef);
  /** When the units could not be loaded, the server alone decides which quantities may be fractional. */
  uomUnavailable = signal(false);
  resolveImage(path?: string | null): string { return this.imageSvc.resolveUrl(path); }

  pendingPOs = signal<PurchaseOrderForGrn[]>([]);
  selectedPO = signal<PurchaseOrderForGrn | null>(null);
  lineItems = signal<GrnLineItem[]>([]);
  selectedPoId = signal<number | null>(null);
  receivedDateTime = signal<string>('');
  isLoadingPOs = signal(false);
  isSubmitting = signal(false);
  private seededReceivedDateTime = '';

  constructor(
    private grnService: GrnService,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {
    // After a page refresh the time zone can arrive once the page is showing, so the default time follows it.
    // A time the user has already entered is theirs, and is kept.
    effect(() => {
      const timeZone = this.companyDates.timeZone();
      untracked(() => {
        if (this.receivedDateTime() === this.seededReceivedDateTime) {
          this.seededReceivedDateTime = this.toDateTimeLocalString(new Date(), timeZone);
          this.receivedDateTime.set(this.seededReceivedDateTime);
        }
      });
    });
  }

  ngOnInit(): void {
    this.loadPendingPOs();
  }

  loadPendingPOs(): void {
    this.isLoadingPOs.set(true);
    this.grnService.getPendingPOs().subscribe({
      next: (response) => {
        this.pendingPOs.set(response.data || []);
        this.isLoadingPOs.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoadingPOs.set(false);
      }
    });
  }

  onPoSelected(poId: number | null): void {
    if (!poId) {
      this.selectedPO.set(null);
      this.lineItems.set([]);
      return;
    }
    const po = this.pendingPOs().find(p => p.id === +poId) || null;
    this.selectedPO.set(po);
    if (po) {
      const items: GrnLineItem[] = po.items.map(item => ({
        ...item,
        orderedQty: item.orderedQty ?? item.quantity ?? 0,
        receivedQty: item.orderedQty ?? item.quantity ?? 0
      }));
      this.lineItems.set(items);
      this.loadLineUnits(po.id, items);
    }
  }

  /** Quantities stay in base units whatever happens here; this only adds the context around them. */
  private loadLineUnits(poId: number, items: GrnLineItem[]): void {
    this.uomUnavailable.set(false);
    forkJoin({
      configs: this.uom.getVariantConfigs(items.map(item => item.variantId)).pipe(catchError(() => {
        this.uomUnavailable.set(true);
        return of<VariantUomConfig[]>([]);
      })),
      // The purchase unit is context only, so a user who cannot open the PO can still receive it.
      po: this.auth.hasPermission('purchases.view')
        ? this.purchaseOrders.getById(poId).pipe(catchError(() => of(null)))
        : of(null)
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(({ configs, po }) => {
      if (this.selectedPO()?.id !== poId) return;
      const configByVariant = new Map(configs.map(config => [config.variantId, config]));
      const poLines = new Map<number, PurchaseOrderItem>(((po?.data?.items ?? []) as PurchaseOrderItem[]).map(line => [line.id, line]));
      this.lineItems.update(lines => lines.map(line => ({
        ...line,
        uomConfig: configByVariant.get(line.variantId),
        poLine: poLines.get(line.id)
      })));
    });
  }

  baseUnit(item: GrnLineItem): string {
    return item.uomConfig?.baseUnit.symbol ?? item.poLine?.uomUnitCode ?? 'pcs';
  }

  /** Fractional quantities are for configured items only. */
  stepFor(item: GrnLineItem): string {
    return item.uomConfig ? quantityStep(item.uomConfig.baseUnit.precision) : '1';
  }

  formatQuantity(quantity: number, item: GrnLineItem): string {
    return formatUnitQuantity(quantity, this.baseUnit(item));
  }

  /** `Ordered as 10 Carton`, for a line ordered in a purchase unit. */
  orderedAs(item: GrnLineItem): string {
    const line = item.poLine;
    if (!line?.uomOptionId || !line.uomEnteredQuantity) return '';
    return `Ordered as ${formatUnitQuantity(line.uomEnteredQuantity, line.uomLabel || line.uomUnitCode)}`;
  }

  isPackTracked(item: GrnLineItem): boolean {
    return !!item.uomConfig?.packStateTrackingEnabled;
  }

  /** Base quantity per sealed pack, from the PO's own conversion; zero when the line was not ordered in a sealed pack. */
  baseQtyPerPack(item: GrnLineItem): number {
    const line = item.poLine;
    const sealed = item.uomConfig?.purchaseOptions.some(option => option.id === line?.uomOptionId && option.isSealedPack);
    return sealed && line?.uomEnteredQuantity ? roundQuantity(line.quantity / line.uomEnteredQuantity) : 0;
  }

  /** The sealed/loose split must add up to the accepted quantity; the server refuses it otherwise. */
  breakdownMessage(item: GrnLineItem): string {
    if (!this.isPackTracked(item)) return '';
    return checkPackBreakdown(item.receivedQty, item.acceptedSealedPacks, item.acceptedLooseQty, this.baseQtyPerPack(item), this.baseUnit(item)).message;
  }

  /** How the physical count compares with the order. Informational: a partial receipt is allowed. */
  reconciliationMessage(item: GrnLineItem): string {
    const accepted = Number(item.receivedQty ?? 0);
    const damaged = Number(item.damagedQty ?? 0);
    const rejected = Number(item.rejectedQty ?? 0);
    const counted = roundQuantity(accepted + damaged + rejected);
    const difference = roundQuantity(counted - Number(item.orderedQty ?? 0));
    if (difference === 0) return '';
    const direction = difference < 0 ? 'short by' : 'over by';
    return `Accepted ${accepted} + damaged ${damaged} + rejected ${rejected} = ${this.formatQuantity(counted, item)}, ${direction} ${this.formatQuantity(Math.abs(difference), item)} against the order.`;
  }

  updateLine(index: number, field: 'damagedQty' | 'rejectedQty' | 'acceptedSealedPacks' | 'acceptedLooseQty', value: number | string | null): void {
    const items = [...this.lineItems()];
    items[index] = { ...items[index], [field]: isEntered(value) ? Number(value) : null };
    this.lineItems.set(items);
  }

  /** The first problem the server would refuse, phrased for the receiver. Input is kept so it can be corrected. */
  private lineProblem(item: GrnLineItem): string {
    const name = item.productName ?? 'This line';
    const quantities = [item.receivedQty, item.damagedQty, item.rejectedQty, item.acceptedLooseQty, item.acceptedSealedPacks];
    if (quantities.some(quantity => Number(quantity ?? 0) < 0)) {
      return `${name}: quantities cannot be negative`;
    }
    if (!Number.isInteger(Number(item.acceptedSealedPacks ?? 0))) {
      return `${name}: sealed packs must be a whole number`;
    }
    if (!this.uomUnavailable()) {
      const precision = item.uomConfig ? item.uomConfig.baseUnit.precision : 0;
      const entered = [item.receivedQty, item.damagedQty, item.rejectedQty].map(quantity => Number(quantity ?? 0));
      if (entered.some(quantity => !fitsPrecision(quantity, precision))) {
        return precision === 0
          ? `${name}: quantities must be whole numbers`
          : `${name}: quantities allow at most ${precision} decimal place(s)`;
      }
    }
    return this.breakdownMessage(item) ? `${name}: ${this.breakdownMessage(item)}` : '';
  }

  private toItemDto(item: GrnLineItem): CreateGrnItemDto {
    const dto: CreateGrnItemDto = { poItemId: item.id, receivedQty: item.receivedQty };
    if (Number(item.damagedQty ?? 0) > 0) dto.damagedQty = Number(item.damagedQty);
    if (Number(item.rejectedQty ?? 0) > 0) dto.rejectedQty = Number(item.rejectedQty);
    if (this.isPackTracked(item) && (isEntered(item.acceptedSealedPacks) || isEntered(item.acceptedLooseQty))) {
      dto.acceptedSealedPacks = Number(item.acceptedSealedPacks ?? 0);
      dto.acceptedLooseQty = Number(item.acceptedLooseQty ?? 0);
    }
    return dto;
  }

  getPoReference(po: PurchaseOrderForGrn): string {
    return po.orderNumber || `PO-${po.id.toString().padStart(6, '0')}`;
  }

  getVariantDetails(item: { variantName?: string; variantAttributes?: string }): string {
    const parts = [item.variantName, item.variantAttributes]
      .map(v => (v ?? '').trim())
      .filter(v => this.hasReadableText(v))
      .filter(v => v.length > 0);

    return parts.join(' - ');
  }

  private hasReadableText(value: string): boolean {
    const trimmed = value.trim();
    if (!trimmed) return false;
    if (trimmed === '{}' || trimmed === '[]' || trimmed.toLowerCase() === 'null') return false;
    if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) return false;
    return true;
  }

  updateReceivedQty(index: number, value: number): void {
    const items = [...this.lineItems()];
    items[index] = { ...items[index], receivedQty: value };
    this.lineItems.set(items);
  }

  onSubmit(): void {
    if (!this.selectedPoId()) {
      this.alertService.error('Please select a Purchase Order');
      return;
    }
    if (!this.receivedDateTime()) {
      this.alertService.error('Please select a received date and time');
      return;
    }
    if (this.lineItems().some(i => i.receivedQty < 0)) {
      this.alertService.error('Received quantities cannot be negative');
      return;
    }
    const problem = this.lineItems().map(item => this.lineProblem(item)).find(message => !!message);
    if (problem) {
      this.alertService.error(problem);
      return;
    }
    if (this.isSubmitting()) {
      return;
    }

    const dto: CreateGrnDto = {
      poId: +this.selectedPoId()!,
      receivedDate: this.toUtcIsoString(this.receivedDateTime(), this.companyDates.timeZone()),
      items: this.lineItems().map(i => this.toItemDto(i))
    };

    this.isSubmitting.set(true);
    this.grnService.create(dto).subscribe({
      next: () => {
        this.alertService.success('GRN created successfully');
        this.location.back();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDateTime(date: string): string {
    if (!date) return '-';
    return this.companyDates.format(date, true, this.companyDates.timeZone());
  }

  private toDateTimeLocalString(date: Date, timeZone: string): string {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    }).formatToParts(date);

    const year = parts.find(p => p.type === 'year')?.value ?? '1970';
    const month = parts.find(p => p.type === 'month')?.value ?? '01';
    const day = parts.find(p => p.type === 'day')?.value ?? '01';
    const hour = parts.find(p => p.type === 'hour')?.value ?? '00';
    const minute = parts.find(p => p.type === 'minute')?.value ?? '00';

    return `${year}-${month}-${day}T${hour}:${minute}`;
  }

  private toUtcIsoString(localDateTime: string, timeZone: string): string {
    const [datePart, timePart] = localDateTime.split('T');
    const [yearStr, monthStr, dayStr] = datePart.split('-');
    const [hourStr = '00', minuteStr = '00'] = (timePart ?? '').split(':');

    const year = Number(yearStr);
    const month = Number(monthStr);
    const day = Number(dayStr);
    const hour = Number(hourStr);
    const minute = Number(minuteStr);

    let utcTime = Date.UTC(year, month - 1, day, hour, minute, 0);

    for (let i = 0; i < 2; i++)
    {
      const offset = this.getTimeZoneOffsetMs(new Date(utcTime), timeZone);
      utcTime = Date.UTC(year, month - 1, day, hour, minute, 0) - offset;
    }

    return new Date(utcTime).toISOString();
  }

  private getTimeZoneOffsetMs(date: Date, timeZone: string): number {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false
    }).formatToParts(date);

    const year = Number(parts.find(p => p.type === 'year')?.value ?? '1970');
    const month = Number(parts.find(p => p.type === 'month')?.value ?? '1');
    const day = Number(parts.find(p => p.type === 'day')?.value ?? '1');
    const hour = Number(parts.find(p => p.type === 'hour')?.value ?? '0');
    const minute = Number(parts.find(p => p.type === 'minute')?.value ?? '0');
    const second = Number(parts.find(p => p.type === 'second')?.value ?? '0');

    const asUtc = Date.UTC(year, month - 1, day, hour, minute, second);
    return asUtc - date.getTime();
  }
}
