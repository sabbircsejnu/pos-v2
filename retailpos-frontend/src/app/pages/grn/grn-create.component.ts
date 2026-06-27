import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { CurrencyService } from '../../services/currency.service';
import { SettingsService } from '../../services/settings.service';
import {
  PurchaseOrderForGrn,
  PurchaseOrderItemForGrn,
  CreateGrnDto,
  CreateGrnItemDto
} from '../../models/grn.model';

interface GrnLineItem extends PurchaseOrderItemForGrn {
  receivedQty: number;
}

@Component({
  selector: 'app-grn-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './grn-create.component.html',
  styleUrls: ['./grn-create.component.css']
})
export class GrnCreateComponent implements OnInit {
  private imageSvc = inject(ProductImageService);
  resolveImage(path?: string | null): string { return this.imageSvc.resolveUrl(path); }

  pendingPOs = signal<PurchaseOrderForGrn[]>([]);
  selectedPO = signal<PurchaseOrderForGrn | null>(null);
  lineItems = signal<GrnLineItem[]>([]);
  selectedPoId = signal<number | null>(null);
  receivedDateTime = signal<string>('');
  businessTimeZone = signal<string>('Asia/Dhaka');
  isLoadingPOs = signal(false);
  isSubmitting = signal(false);

  constructor(
    private grnService: GrnService,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService,
    private settingsService: SettingsService
  ) {}

  ngOnInit(): void {
    this.loadBusinessTimeZone();
    this.receivedDateTime.set(this.toDateTimeLocalString(new Date(), this.businessTimeZone()));
    this.loadPendingPOs();
  }

  private loadBusinessTimeZone(): void {
    this.settingsService.getCompanySettings().subscribe({
      next: (res) => {
        const tz = res?.data?.timeZone;
        if (typeof tz === 'string' && tz.trim()) {
          this.businessTimeZone.set(tz.trim());
        }

        this.receivedDateTime.set(this.toDateTimeLocalString(new Date(), this.businessTimeZone()));
      },
      error: () => {
        this.receivedDateTime.set(this.toDateTimeLocalString(new Date(), this.businessTimeZone()));
      }
    });
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
        receivedQty: item.orderedQty
      }));
      this.lineItems.set(items);
    }
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

    const dto: CreateGrnDto = {
      poId: +this.selectedPoId()!,
      receivedDate: this.toUtcIsoString(this.receivedDateTime(), this.businessTimeZone()),
      items: this.lineItems().map(i => ({
        poItemId: i.id,
        receivedQty: i.receivedQty
      }))
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

    const value = new Date(date);
    if (Number.isNaN(value.getTime())) return '-';

    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: this.businessTimeZone(),
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    }).formatToParts(value);

    const day = parts.find(p => p.type === 'day')?.value ?? '--';
    const month = parts.find(p => p.type === 'month')?.value ?? '---';
    const year = parts.find(p => p.type === 'year')?.value ?? '----';
    const hour = parts.find(p => p.type === 'hour')?.value ?? '--';
    const minute = parts.find(p => p.type === 'minute')?.value ?? '--';
    const dayPeriod = (parts.find(p => p.type === 'dayPeriod')?.value ?? '').toUpperCase();

    return `${day} ${month}, ${year} ${hour}:${minute} ${dayPeriod}`.trim();
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
