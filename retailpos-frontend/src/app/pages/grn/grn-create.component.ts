import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
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
  receivedDate = signal<string>(new Date().toISOString().substring(0, 10));
  isLoadingPOs = signal(false);
  isSubmitting = signal(false);

  constructor(
    private grnService: GrnService,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

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
        receivedQty: item.orderedQty
      }));
      this.lineItems.set(items);
    }
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
    if (!this.receivedDate()) {
      this.alertService.error('Please select a received date');
      return;
    }
    if (this.lineItems().some(i => i.receivedQty < 0)) {
      this.alertService.error('Received quantities cannot be negative');
      return;
    }

    const dto: CreateGrnDto = {
      poId: +this.selectedPoId()!,
      receivedDate: this.receivedDate(),
      items: this.lineItems().map(i => ({
        poItemId: i.id,
        receivedQty: i.receivedQty
      }))
    };

    this.isSubmitting.set(true);
    this.grnService.create(dto).subscribe({
      next: () => {
        this.alertService.success('GRN created successfully');
        this.router.navigate(['/grn']);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/grn']);
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }
}
