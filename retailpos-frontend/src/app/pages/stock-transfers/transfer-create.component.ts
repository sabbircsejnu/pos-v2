import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StockTransferService } from '../../services/stock-transfer.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { ProductService } from '../../services/product.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateStockTransferDto, CreateStockTransferItemDto } from '../../models/stock-transfer.model';

interface TransferLineItem {
  variantId: number;
  variantSku: string;
  productName: string;
  quantity: number;
}

interface ProductVariantOption {
  id: number;
  sku: string;
  productName: string;
  displayName: string;
}

@Component({
  selector: 'app-transfer-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transfer-create.component.html',
  styleUrls: ['./transfer-create.component.css']
})
export class TransferCreateComponent implements OnInit {
  fromLocationType = signal<string>('outlet');
  fromLocationId = signal<number | null>(null);
  toLocationType = signal<string>('outlet');
  toLocationId = signal<number | null>(null);
  transferDate = signal<string>(new Date().toISOString().substring(0, 10));

  lineItems = signal<TransferLineItem[]>([]);
  variantOptions = signal<ProductVariantOption[]>([]);

  outlets = signal<any[]>([]);
  warehouses = signal<any[]>([]);

  isLoadingData = signal(false);
  isSubmitting = signal(false);

  constructor(
    private transferService: StockTransferService,
    private outletService: OutletService,
    private warehouseService: WarehouseService,
    private productService: ProductService,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadSupportingData();
  }

  loadSupportingData(): void {
    this.isLoadingData.set(true);

    this.outletService.getAllOutlets().subscribe({
      next: () => this.outlets.set(this.outletService.outlets()),
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });

    this.warehouseService.getAllWarehouses().subscribe({
      next: () => this.warehouses.set(this.warehouseService.warehouses()),
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });

    this.productService.search({ pageSize: 200, pageNumber: 1, sortBy: 'name', sortOrder: 'asc' }).subscribe({
      next: () => {
        const products = this.productService.products();
        const options: ProductVariantOption[] = [];
        for (const product of products) {
          if ((product as any).variants?.length) {
            for (const variant of (product as any).variants) {
              options.push({
                id: variant.id,
                sku: variant.sku,
                productName: product.name,
                displayName: `${product.name} - ${variant.sku}`
              });
            }
          }
        }
        this.variantOptions.set(options);
        this.isLoadingData.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoadingData.set(false);
      }
    });
  }

  getFromLocations(): any[] {
    return this.fromLocationType() === 'outlet' ? this.outlets() : this.warehouses();
  }

  getToLocations(): any[] {
    return this.toLocationType() === 'outlet' ? this.outlets() : this.warehouses();
  }

  onFromTypeChange(): void {
    this.fromLocationId.set(null);
  }

  onToTypeChange(): void {
    this.toLocationId.set(null);
  }

  addItem(): void {
    this.lineItems.update(items => [
      ...items,
      { variantId: 0, variantSku: '', productName: '', quantity: 1 }
    ]);
  }

  removeItem(index: number): void {
    this.lineItems.update(items => items.filter((_, i) => i !== index));
  }

  onVariantSelected(index: number, variantId: number): void {
    const variant = this.variantOptions().find(v => v.id === +variantId);
    if (variant) {
      this.lineItems.update(items => {
        const updated = [...items];
        updated[index] = {
          ...updated[index],
          variantId: variant.id,
          variantSku: variant.sku,
          productName: variant.productName
        };
        return updated;
      });
    }
  }

  updateQuantity(index: number, qty: number): void {
    this.lineItems.update(items => {
      const updated = [...items];
      updated[index] = { ...updated[index], quantity: qty };
      return updated;
    });
  }

  onSubmit(): void {
    if (!this.fromLocationId()) {
      this.alertService.error('Please select a from location');
      return;
    }
    if (!this.toLocationId()) {
      this.alertService.error('Please select a to location');
      return;
    }
    if (
      this.fromLocationType() === this.toLocationType() &&
      this.fromLocationId() === this.toLocationId()
    ) {
      this.alertService.error('From and to locations cannot be the same');
      return;
    }
    if (this.lineItems().length === 0) {
      this.alertService.error('Please add at least one item');
      return;
    }
    if (this.lineItems().some(i => !i.variantId)) {
      this.alertService.error('Please select a product variant for all items');
      return;
    }
    if (this.lineItems().some(i => i.quantity <= 0)) {
      this.alertService.error('All quantities must be greater than 0');
      return;
    }

    const dto: CreateStockTransferDto = {
      fromLocationId: this.fromLocationId()!,
      fromLocationType: this.fromLocationType(),
      toLocationId: this.toLocationId()!,
      toLocationType: this.toLocationType(),
      transferDate: this.transferDate(),
      items: this.lineItems().map(i => ({
        variantId: i.variantId,
        quantity: i.quantity
      }))
    };

    this.isSubmitting.set(true);
    this.transferService.create(dto).subscribe({
      next: () => {
        this.alertService.success('Stock transfer created successfully');
        this.router.navigate(['/stock-transfers']);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/stock-transfers']);
  }
}
