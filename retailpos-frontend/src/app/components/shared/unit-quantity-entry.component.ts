import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VariantUomConfig } from '../../models/uom.model';
import { formatUnitQuantity, quantityStep } from '../../services/uom.service';
import { UnitEntry, entryPrecision, roundQuantity, toBaseQuantity } from '../../utils/uom-quantity.util';
import { UiControlDirective } from './ui/ui-control.directive';

/** Quantity and unit for a configured variant, with a preview of the base equivalent. The feature validates and submits. */
@Component({
  selector: 'app-unit-quantity-entry',
  standalone: true,
  imports: [FormsModule, UiControlDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="uq">
      <input appUiControl class="uq__quantity" type="number"
        [step]="step()" [attr.min]="allowNegative() ? null : 0" [placeholder]="allowNegative() ? '+/-' : ''"
        [disabled]="disabled()" [attr.aria-label]="label() + ' quantity'"
        [ngModel]="quantity()" [ngModelOptions]="{ standalone: true }" (ngModelChange)="changeQuantity($event)">
      <select appUiControl class="uq__unit" [disabled]="disabled()" [attr.aria-label]="label() + ' unit'"
        [ngModel]="unitOptionId()" [ngModelOptions]="{ standalone: true }" (ngModelChange)="changeUnit($event)">
        <option [ngValue]="null">{{ config().baseUnit.symbol }}</option>
        @for (option of config().purchaseOptions; track option.id) {
          <option [ngValue]="option.id">{{ option.label }}</option>
        }
      </select>
    </div>
    @if (preview(); as text) {
      <p class="uq__preview">= {{ text }}</p>
    }
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .uq { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .uq__quantity { flex: 1 1 5rem; min-width: 5rem; }
    .uq__unit { flex: 1 1 6rem; min-width: 6rem; }
    .uq__preview {
      margin: var(--rp-space-1) 0 0;
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
    }
  `]
})
export class UnitQuantityEntryComponent {
  readonly config = input.required<VariantUomConfig>();
  readonly unitOptionId = input<number | null>(null);
  readonly quantity = input<number | null>(null);
  readonly allowNegative = input(false);
  readonly disabled = input(false);
  readonly label = input('Item');
  readonly entryChange = output<UnitEntry>();

  readonly step = computed(() => quantityStep(entryPrecision(this.config(), this.unitOptionId())));

  /** Shown only when a conversion happens; the base unit needs no equivalent. */
  readonly preview = computed(() => {
    const quantity = this.quantity();
    if (this.unitOptionId() === null || !quantity) {
      return null;
    }
    const base = toBaseQuantity(this.config(), this.unitOptionId(), Math.abs(quantity));
    return base === null
      ? null
      : `${formatUnitQuantity(roundQuantity(quantity < 0 ? -base : base), this.config().baseUnit.code)} (preview)`;
  });

  changeQuantity(value: number | string | null): void {
    const quantity = value === null || value === '' ? null : Number(value);
    this.entryChange.emit({ unitOptionId: this.unitOptionId(), quantity });
  }

  changeUnit(unitOptionId: number | null): void {
    this.entryChange.emit({ unitOptionId, quantity: this.quantity() });
  }
}
