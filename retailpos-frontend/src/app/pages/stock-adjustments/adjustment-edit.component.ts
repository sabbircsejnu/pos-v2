import { CommonModule, Location } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ADJUSTMENT_REASONS, StockAdjustmentDto, UpdateStockAdjustmentDto } from '../../models/stock-adjustment.model';
import { findVariantIndexById } from '../../utils/variant-selection.util';
import { ProductImageService } from '../../services/product-image.service';
import { VariantSearchItem } from '../../components/variant-search-picker/variant-search-picker.component';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';

interface EditableAdjustmentLine {
  id?: number;
  variantId: number;
  productName: string;
  variantSku: string;
  productCode?: string;
  currentStock: number;
  quantityChange: number;
  reason: string;
  notes?: string;
}

@Component({
  selector: 'app-adjustment-edit',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './adjustment-edit.component.html',
  styleUrls: ['./adjustment-edit.component.css']
})
export class AdjustmentEditComponent implements OnInit {
  readonly reasons = ADJUSTMENT_REASONS;
  isLoading = signal(true);
  adjustment = signal<StockAdjustmentDto | null>(null);
  outlets = signal<any[]>([]);
  warehouses = signal<any[]>([]);

  locationType = signal('outlet');
  locationId = signal<number | null>(null);
  reason = signal('');
  notes = signal('');
  items = signal<EditableAdjustmentLine[]>([]);

  constructor(
    private adjustmentService: StockAdjustmentService,
    private userOutletAccess: UserOutletAccessService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private imageService: ProductImageService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.location.back();
      return;
    }

    this.userOutletAccess.load().subscribe({
      next: (res) => {
        this.outlets.set(res.data?.outlets || []);
        this.warehouses.set(res.data?.warehouses || []);
        this.loadAdjustment(+id);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.location.back();
      }
    });
  }

  loadAdjustment(id: number): void {
    this.adjustmentService.getById(id).subscribe({
      next: (res) => {
        const adj = res.data as StockAdjustmentDto;
        if (adj.status !== 'Draft') {
          this.alertService.error('Only draft adjustments can be edited');
          this.router.navigate(['/stock-adjustments', id]);
          return;
        }

        this.adjustment.set(adj);
        this.locationType.set(adj.locationType);
        this.locationId.set(adj.locationId);
        this.reason.set(adj.reason || '');
        this.notes.set(adj.notes || '');
        this.items.set(adj.lines.map(line => ({
          id: line.id,
          variantId: line.variantId,
          productName: line.productName,
          variantSku: line.variantSku,
          productCode: line.productCode,
          currentStock: line.previousQuantity,
          quantityChange: line.quantityChange,
          reason: line.reason,
          notes: line.notes,
        })));
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.location.back();
      }
    });
  }

  getLocations(): any[] {
    return this.locationType() === 'warehouse' ? this.warehouses() : this.outlets();
  }

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
  }

  onVariantSelected(variant: VariantSearchItem): void {
    const existingIndex = findVariantIndexById(this.items(), variant.id);
    if (existingIndex !== -1) {
      this.alertService.warning(`${variant.productName} - ${variant.name} is already added`);
      return;
    }

    this.items.update(items => ([
      ...items,
      {
        variantId: variant.id,
        productName: variant.productName,
        variantSku: variant.sku || variant.name,
        productCode: variant.productCode,
        currentStock: variant.stockQuantity ?? 0,
        quantityChange: 0,
        reason: this.reason() || 'Other',
      }
    ]));
  }

  removeLine(index: number): void {
    this.items.update(items => items.filter((_, i) => i !== index));
  }

  updateLineQuantity(index: number, value: number): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], quantityChange: Number.isFinite(value) ? value : 0 };
      return next;
    });
  }

  updateLineReason(index: number, value: string): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], reason: value };
      return next;
    });
  }

  save(): void {
    const adj = this.adjustment();
    if (!adj || !this.locationId() || this.items().length === 0) {
      this.alertService.error('Please complete all required fields');
      return;
    }

    const dto: UpdateStockAdjustmentDto = {
      locationId: this.locationId()!,
      locationType: this.locationType(),
      items: this.items().map(line => ({
        variantId: line.variantId,
        quantityChange: line.quantityChange,
        reason: line.reason,
        notes: line.notes || this.notes() || undefined,
      })),
    };

    this.adjustmentService.update(adj.id, dto).subscribe({
      next: () => {
        this.alertService.success('Stock adjustment draft updated successfully');
        this.router.navigate(['/stock-adjustments', adj.id]);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  cancel(): void {
    const adj = this.adjustment();
    if (adj) {
      this.router.navigate(['/stock-adjustments', adj.id]);
      return;
    }

    this.location.back();
  }
}