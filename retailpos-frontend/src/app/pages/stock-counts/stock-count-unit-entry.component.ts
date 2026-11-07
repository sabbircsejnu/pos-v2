import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { UnitQuantityEntryComponent } from '../../components/shared/unit-quantity-entry.component';
import { UI_KIT } from '../../components/shared/ui';
import { StockCountLineDto } from '../../models/stock-count.model';
import { PackStateBalanceRow, PackStateKind, StockCountEntriesResult, StockCountEntryInput, VariantUomConfig } from '../../models/uom.model';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UomService, formatUnitQuantity } from '../../services/uom.service';
import { UnitEntry, entryError, roundQuantity, toBaseQuantity } from '../../utils/uom-quantity.util';

interface CountedPack {
  state: PackStateKind;
  packCode: string;
  baseQtyPerPack: number;
  systemPacks: number;
  counted: number | null;
}

/**
 * Counts one configured line in mixed units. Totals here are previews; the server converts the entries and returns the
 * physical stock and difference. Sealed and prepared counts must equal the system, because a variance posts as loose.
 */
@Component({
  selector: 'app-stock-count-unit-entry',
  standalone: true,
  imports: [FormsModule, UnitQuantityEntryComponent, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="scu" role="group" [attr.aria-label]="'Count ' + line().productName + ' by unit'">
      @if (packStatus() === 'loading') {
        <app-ui-state kind="loading" [compact]="true" title="Loading sealed and loose stock..." />
      } @else if (packStatus() === 'failed') {
        <app-ui-state kind="error" [compact]="true" title="Could not load sealed and loose stock">
          <button appUiButton type="button" variant="outline" (click)="loadPacks()">Retry</button>
        </app-ui-state>
      } @else {
        @for (pack of packs(); track pack.state + pack.packCode + pack.baseQtyPerPack; let p = $index) {
          <app-ui-form-field [label]="packLabel(pack)" [error]="packError(pack)">
            <input appUiControl type="number" min="0" step="1" [disabled]="saving()"
              [ngModel]="pack.counted" [ngModelOptions]="{ standalone: true }" (ngModelChange)="setPack(p, $event)">
          </app-ui-form-field>
        }

        <p class="scu__title">{{ config().packStateTrackingEnabled ? 'Loose stock counted' : 'Counted quantities' }}</p>
        @for (row of rows(); track $index; let r = $index) {
          <div class="scu__row">
            <app-unit-quantity-entry [config]="config()" [disabled]="saving()" [label]="line().productName"
              [unitOptionId]="row.unitOptionId" [quantity]="row.quantity" (entryChange)="setRow(r, $event)" />
            @if (rows().length > 1) {
              <button appUiButton type="button" variant="outline" size="sm" [disabled]="saving()"
                [attr.aria-label]="'Remove counted row ' + (r + 1)" (click)="removeRow(r)">Remove</button>
            }
          </div>
          @if (rowError(row); as message) {
            <p class="scu__error">{{ message }}</p>
          }
        }
        <div class="scu__actions">
          <button appUiButton type="button" variant="outline" size="sm" [disabled]="saving()" (click)="addRow()">Add another unit</button>
        </div>

        <dl class="scu__totals">
          <div><dt>System</dt><dd>{{ format(line().currentStock) }}</dd></div>
          <div><dt>Counted (preview)</dt><dd>{{ total() === null ? '-' : format(total()!) }}</dd></div>
          <div><dt>Variance (preview)</dt><dd>{{ total() === null ? '-' : format(variance()!) }}</dd></div>
        </dl>

        @if (apiError()) {
          <app-ui-alert variant="danger">{{ apiError() }}</app-ui-alert>
        }
        <div class="scu__actions">
          <button appUiButton type="button" [loading]="saving()" [disabled]="!canSave()" (click)="save()">Save count</button>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .scu { display: flex; flex-direction: column; gap: var(--rp-space-3); max-width: 36rem; }
    .scu__title { margin: 0; font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-semibold); color: var(--rp-color-text-primary); }
    .scu__row { display: flex; flex-wrap: wrap; align-items: flex-start; gap: var(--rp-space-2); }
    .scu__row app-unit-quantity-entry { flex: 1 1 14rem; }
    .scu__error { margin: 0; font-size: var(--rp-text-helper-size); color: var(--rp-color-status-danger-fg); }
    .scu__actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .scu__totals { display: flex; flex-wrap: wrap; gap: var(--rp-space-2) var(--rp-space-6); margin: 0; }
    .scu__totals dt { font-size: var(--rp-text-helper-size); color: var(--rp-color-text-secondary); }
    .scu__totals dd { margin: 0; font-weight: var(--rp-font-weight-semibold); color: var(--rp-color-text-primary); }
  `]
})
export class StockCountUnitEntryComponent implements OnInit {
  private readonly uom = inject(UomService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly destroyRef = inject(DestroyRef);

  readonly stockCountId = input.required<number>();
  readonly line = input.required<StockCountLineDto>();
  readonly config = input.required<VariantUomConfig>();
  readonly locationId = input.required<number>();
  readonly locationType = input.required<string>();
  readonly saved = output<StockCountEntriesResult>();

  readonly packStatus = signal<'loading' | 'ready' | 'failed'>('ready');
  readonly packs = signal<CountedPack[]>([]);
  readonly rows = signal<UnitEntry[]>([{ unitOptionId: null, quantity: null }]);
  readonly saving = signal(false);
  readonly apiError = signal<string | null>(null);

  readonly total = computed(() => {
    let sum = 0;
    for (const pack of this.packs()) {
      if (pack.counted === null) {
        return null;
      }
      sum += pack.counted * pack.baseQtyPerPack;
    }
    for (const row of this.rows()) {
      if (this.rowError(row)) {
        return null;
      }
      sum += toBaseQuantity(this.config(), row.unitOptionId, row.quantity as number) as number;
    }
    return roundQuantity(sum);
  });

  readonly variance = computed(() => {
    const total = this.total();
    return total === null ? null : roundQuantity(total - this.line().currentStock);
  });

  readonly canSave = computed(() => this.total() !== null && !this.saving());

  ngOnInit(): void {
    if (this.config().packStateTrackingEnabled) {
      this.loadPacks();
    }
  }

  loadPacks(): void {
    this.packStatus.set('loading');
    this.uom.getPackState(this.line().variantId, this.locationId(), this.locationType())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: view => {
          this.packs.set(view.balances
            .filter((row: PackStateBalanceRow) => row.state !== 'loose' && row.packCount > 0)
            .map(row => ({
              state: row.state,
              packCode: row.packCode,
              baseQtyPerPack: row.baseQtyPerPack,
              systemPacks: row.packCount,
              counted: row.packCount
            })));
          this.packStatus.set('ready');
        },
        error: () => this.packStatus.set('failed')
      });
  }

  format(quantity: number): string {
    return formatUnitQuantity(quantity, this.config().baseUnit.code);
  }

  packLabel(pack: CountedPack): string {
    const form = pack.state === 'sealed' ? 'Sealed' : 'Prepared';
    return `${form} ${pack.packCode} packs (${this.format(pack.baseQtyPerPack)} each, system ${pack.systemPacks})`;
  }

  packError(pack: CountedPack): string {
    if (pack.counted === null || pack.counted < 0 || !Number.isInteger(pack.counted)) {
      return 'Enter a whole number of packs.';
    }
    return pack.counted === pack.systemPacks ? '' : `This must equal the system (${pack.systemPacks}). Open or adjust those packs first.`;
  }

  rowError(row: UnitEntry): string | null {
    return entryError(this.config(), row.unitOptionId, row.quantity, { allowZero: true });
  }

  setPack(index: number, value: number | null): void {
    this.packs.update(packs => packs.map((pack, i) => (i === index ? { ...pack, counted: value === null ? null : Number(value) } : pack)));
  }

  setRow(index: number, entry: UnitEntry): void {
    this.rows.update(rows => rows.map((row, i) => (i === index ? entry : row)));
  }

  addRow(): void {
    this.rows.update(rows => [...rows, { unitOptionId: null, quantity: null }]);
  }

  removeRow(index: number): void {
    this.rows.update(rows => rows.filter((_, i) => i !== index));
  }

  /** Packs are sent as a base quantity with their pack identity, so a pack that is no longer a purchase unit still counts. */
  buildEntries(): StockCountEntryInput[] {
    const tracked = this.config().packStateTrackingEnabled;
    return [
      ...this.packs().map(pack => ({
        unitOptionId: null,
        enteredQuantity: roundQuantity((pack.counted as number) * pack.baseQtyPerPack),
        state: pack.state,
        packCode: pack.packCode,
        baseQtyPerPack: pack.baseQtyPerPack
      })),
      ...this.rows().map(row => ({
        unitOptionId: row.unitOptionId,
        enteredQuantity: row.quantity as number,
        ...(tracked ? { state: 'loose' as const } : {})
      }))
    ];
  }

  save(): void {
    if (!this.canSave()) {
      return;
    }
    this.saving.set(true);
    this.apiError.set(null);
    this.uom.saveCountEntries(this.stockCountId(), this.line().id, this.buildEntries())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.saving.set(false);
          this.saved.emit(result);
        },
        error: error => {
          this.saving.set(false);
          this.apiError.set(this.errors.extractErrorMessage(error));
        }
      });
  }
}
