import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UnitQuantityEntryComponent } from '../../components/shared/unit-quantity-entry.component';
import { UiControlDirective } from '../../components/shared/ui/ui-control.directive';
import { VariantUomConfig } from '../../models/uom.model';
import { UnitEntry } from '../../utils/uom-quantity.util';
import { AdjustmentUomEntry, LOOSE_CHOICE, adjustmentEntryError, holdsPreparedStock, sealedChoices } from './adjustment-uom.logic';

/** Unit, signed quantity and, for a pack-tracked item, the stock form an adjustment changes. */
@Component({
  selector: 'app-adjustment-uom-fields',
  standalone: true,
  imports: [FormsModule, UnitQuantityEntryComponent, UiControlDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-unit-quantity-entry [config]="config()" [allowNegative]="true" [disabled]="disabled()" [label]="label()"
      [unitOptionId]="line().unitOptionId ?? null" [quantity]="line().enteredQuantity ?? null"
      (entryChange)="entryChange.emit($event)" />
    @if (config().packStateTrackingEnabled) {
      <select appUiControl class="auf__form" [disabled]="disabled()" [attr.aria-label]="label() + ' stock form'"
        [ngModel]="line().packChoice ?? null" [ngModelOptions]="{ standalone: true }" (ngModelChange)="packChoiceChange.emit($event)">
        <option [ngValue]="null" disabled>Stock form...</option>
        <option [ngValue]="looseChoice">Loose</option>
        @for (choice of choices(); track choice.key) {
          <option [ngValue]="choice.key">{{ choice.label }}</option>
        }
      </select>
      @if (holdsPrepared()) {
        <p class="auf__note">Prepared packs cannot be adjusted here. Reverse the repack job if it is still unsold.</p>
      }
    }
    @if (error(); as message) {
      <p class="auf__error">{{ message }}</p>
    }
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .auf__form { margin-top: var(--rp-space-2); }
    .auf__note, .auf__error { margin: var(--rp-space-1) 0 0; font-size: var(--rp-text-helper-size); }
    .auf__note { color: var(--rp-color-text-secondary); }
    .auf__error { color: var(--rp-color-status-danger-fg); }
  `]
})
export class AdjustmentUomFieldsComponent {
  readonly line = input.required<AdjustmentUomEntry & { uomConfig?: VariantUomConfig | null }>();
  readonly disabled = input(false);
  readonly label = input('Item');
  readonly entryChange = output<UnitEntry>();
  readonly packChoiceChange = output<string | null>();

  readonly looseChoice = LOOSE_CHOICE;
  readonly config = computed(() => this.line().uomConfig as VariantUomConfig);
  readonly choices = computed(() => sealedChoices(this.config(), this.line().packView, this.line().storedPack));
  readonly holdsPrepared = computed(() => holdsPreparedStock(this.line().packView));
  readonly error = computed(() => adjustmentEntryError(this.line()));
}
