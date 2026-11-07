import { CommonModule, Location } from '@angular/common';
import { Component, DestroyRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { UomService } from '../../services/uom.service';
import { UnitEntry } from '../../utils/uom-quantity.util';
import { AdjustmentUomFieldsComponent } from './adjustment-uom-fields.component';
import { AdjustmentUomEntry, adjustmentEntryError, adjustmentUomPayload, restoreDraftEntry, signedBaseChange, storedDraftPayload } from './adjustment-uom.logic';

interface EditableAdjustmentLine extends AdjustmentUomEntry {
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
  imports: [CommonModule, FormsModule, AdjustmentUomFieldsComponent],
  templateUrl: './adjustment-edit.component.html',
  styleUrls: ['./adjustment-edit.component.css']
})
export class AdjustmentEditComponent implements OnInit {
  readonly reasons = ADJUSTMENT_REASONS;
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  isLoading = signal(true);
  adjustment = signal<StockAdjustmentDto | null>(null);
  outlets = signal<any[]>([]);
  warehouses = signal<any[]>([]);

  locationType = signal('outlet');
  locationId = signal<number | null>(null);
  reason = signal('');
  notes = signal('');
  items = signal<EditableAdjustmentLine[]>([]);
  isSaving = signal(false);

  /** Plain pcs lines are not edited on this screen. */
  configuredLines = computed(() => this.items()
    .map((line, index) => ({ line, index }))
    .filter(row => !!row.line.uomConfig));
  isCheckingUnits = computed(() => this.items().some(line => line.uomStatus === 'loading'));
  unitsFailed = computed(() => this.items().some(line => line.uomStatus === 'failed'));
  canSave = computed(() => !this.isSaving() && !this.isCheckingUnits());

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
          uomStatus: 'loading',
        })));
        this.loadLineConfigs(adj);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.location.back();
      }
    });
  }

  /** Each configured line reopens as it was entered, with its stock form. */
  loadLineConfigs(adjustment: StockAdjustmentDto): void {
    const uom = this.injector.get(UomService);
    this.items.update(items => items.map(line => ({ ...line, uomStatus: 'loading' })));
    uom.getVariantConfigs(adjustment.lines.map(line => line.variantId))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: configs => {
          const byVariant = new Map(configs.map(config => [config.variantId, config]));
          const saved = new Map(adjustment.lines.map(line => [line.variantId, line]));
          this.items.update(items => items.map(line => {
            const config = byVariant.get(line.variantId);
            const savedLine = saved.get(line.variantId);
            return {
              ...line,
              uomStatus: 'ready',
              uomConfig: config ?? null,
              ...(config && savedLine ? restoreDraftEntry(config, savedLine) : {})
            };
          }));
          configs.filter(config => config.packStateTrackingEnabled).forEach(config => {
            uom.getPackState(config.variantId, adjustment.locationId, adjustment.locationType)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                next: view => this.items.update(items => items.map(line => (line.variantId === config.variantId ? { ...line, packView: view } : line))),
                error: () => undefined
              });
          });
        },
        error: () => this.items.update(items => items.map(line => ({ ...line, uomStatus: 'failed' })))
      });
  }

  updateLineEntry(index: number, entry: UnitEntry): void {
    this.items.update(items => {
      const next = [...items];
      const config = next[index].uomConfig;
      if (!config) {
        return items;
      }
      next[index] = {
        ...next[index],
        unitOptionId: entry.unitOptionId,
        enteredQuantity: entry.quantity,
        quantityChange: signedBaseChange(config, entry)
      };
      return next;
    });
  }

  updateLinePackChoice(index: number, packChoice: string | null): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], packChoice };
      return next;
    });
  }

  /** A line whose configuration could not be read resends what it already holds, unchanged. */
  private linePayload(line: EditableAdjustmentLine, adjustment: StockAdjustmentDto) {
    if (line.uomStatus === 'failed') {
      const saved = adjustment.lines.find(candidate => candidate.variantId === line.variantId);
      return saved ? storedDraftPayload(saved) : {};
    }
    return adjustmentUomPayload(line);
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
    if (this.isSaving()) {
      return;
    }
    if (!adj || !this.locationId() || this.items().length === 0) {
      this.alertService.error('Please complete all required fields');
      return;
    }
    if (this.isCheckingUnits()) {
      this.alertService.warning('Still checking the units of these items. Try again in a moment.');
      return;
    }
    if (this.items().some(line => adjustmentEntryError(line))) {
      this.alertService.error('Please fix the invalid adjustment lines');
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
        ...this.linePayload(line, adj)
      })),
    };

    this.isSaving.set(true);
    this.adjustmentService.update(adj.id, dto).subscribe({
      next: () => {
        this.alertService.success('Stock adjustment draft updated successfully');
        this.router.navigate(['/stock-adjustments', adj.id]);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
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