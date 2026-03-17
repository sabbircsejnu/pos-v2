import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { ProductService } from '../../services/product.service';
import { InventoryService } from '../../services/inventory.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateStockAdjustmentDto, ADJUSTMENT_REASONS } from '../../models/stock-adjustment.model';

interface ProductVariantOption {
  id: number;
  sku: string;
  productName: string;
  displayName: string;
}

@Component({
  selector: 'app-adjustment-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './adjustment-create.component.html',
  styleUrls: ['./adjustment-create.component.css']
})
export class AdjustmentCreateComponent implements OnInit {
  readonly ADJUSTMENT_REASONS = ADJUSTMENT_REASONS;

  locationType = signal<string>('outlet');
  locationId = signal<number | null>(null);
  variantId = signal<number | null>(null);
  quantityChange = signal<number>(0);
  reason = signal<string>('');
  notes = signal<string>('');

  outlets = signal<any[]>([]);
  warehouses = signal<any[]>([]);
  variantOptions = signal<ProductVariantOption[]>([]);
  currentStock = signal<number | null>(null);
  isLoadingStock = signal(false);

  isLoadingData = signal(false);
  isSubmitting = signal(false);

  newStockLevel = computed(() => {
    const current = this.currentStock();
    if (current === null) return null;
    return current + this.quantityChange();
  });

  constructor(
    private adjustmentService: StockAdjustmentService,
    private outletService: OutletService,
    private warehouseService: WarehouseService,
    private productService: ProductService,
    private inventoryService: InventoryService,
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

  getLocations(): any[] {
    return this.locationType() === 'outlet' ? this.outlets() : this.warehouses();
  }

  onLocationTypeChange(): void {
    this.locationId.set(null);
    this.currentStock.set(null);
  }

  onLocationChange(): void {
    this.loadCurrentStock();
  }

  onVariantChange(): void {
    this.loadCurrentStock();
  }

  loadCurrentStock(): void {
    const vid = this.variantId();
    const lid = this.locationId();
    if (!vid || !lid) {
      this.currentStock.set(null);
      return;
    }

    this.isLoadingStock.set(true);
    const searchRequest: any = { page: 1, pageSize: 10 };
    if (this.locationType() === 'outlet') {
      searchRequest.outletId = lid;
    } else {
      searchRequest.warehouseId = lid;
    }

    this.inventoryService.search(searchRequest).subscribe({
      next: (res) => {
        const items = res.data || [];
        const match = items.find((i: any) => i.productVariantId === vid);
        this.currentStock.set(match?.quantity ?? 0);
        this.isLoadingStock.set(false);
      },
      error: () => {
        this.currentStock.set(0);
        this.isLoadingStock.set(false);
      }
    });
  }

  onSubmit(): void {
    if (!this.locationId()) {
      this.alertService.error('Please select a location');
      return;
    }
    if (!this.variantId()) {
      this.alertService.error('Please select a product variant');
      return;
    }
    if (this.quantityChange() === 0) {
      this.alertService.error('Quantity change cannot be zero');
      return;
    }
    if (!this.reason()) {
      this.alertService.error('Please select a reason');
      return;
    }

    const dto: CreateStockAdjustmentDto = {
      locationId: this.locationId()!,
      locationType: this.locationType(),
      variantId: this.variantId()!,
      quantityChange: this.quantityChange(),
      reason: this.reason(),
      notes: this.notes() || undefined
    };

    this.isSubmitting.set(true);
    this.adjustmentService.create(dto).subscribe({
      next: () => {
        this.alertService.success('Stock adjustment created successfully');
        this.router.navigate(['/stock-adjustments']);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/stock-adjustments']);
  }
}
