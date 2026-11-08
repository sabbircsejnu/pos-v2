import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UiAlertComponent, UiControlDirective, UiFormFieldComponent } from '../../components/shared/ui';
import { ExchangeSettlementDirection, ExchangeSettlementMethod } from '../../models/exchange.model';
import { MAX_REFERENCE_LENGTH, SettlementDraft, displayChange, settlementError } from './exchange-settlement';

let nextPanelId = 0;

/**
 * Settlement entry in the look of the POS pay panel: a split collection (cash with tendered and change, card, mobile),
 * a single-method refund (card and mobile refunds are completed outside the system and recorded with a reference,
 * D-09), or nothing when no money moves. The amount always comes from the server.
 */
@Component({
  selector: 'app-exchange-settlement-panel',
  standalone: true,
  imports: [FormsModule, AppCurrencyPipe, UiAlertComponent, UiControlDirective, UiFormFieldComponent],
  styleUrls: ['./exchange.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ex-pay" [attr.aria-label]="'Settlement: ' + directionTitle()">
      <div class="ex-pay__top">
        <span>{{ directionTitle() }}</span>
        <strong data-testid="settlement-amount">{{ amount() | appCurrency }}</strong>
      </div>

      @if (direction() === 'none') {
        <p class="ex-muted">The returned credit equals the replacement value. No money moves; the exchange is still recorded.</p>
      } @else if (direction() === 'collect') {
        <table class="ex-tender">
          <thead><tr><th scope="col">Method</th><th scope="col">Amount</th></tr></thead>
          <tbody>
            <tr>
              <td>Cash</td>
              <td><input appUiControl aria-label="Cash amount" name="cashAmount" type="number" min="0" step="0.01" inputmode="decimal" placeholder="0.00"
                [disabled]="disabled()" [ngModel]="draft().cash" (ngModelChange)="patch({ cash: toNumber($event) })" /></td>
            </tr>
            <tr>
              <td>Card</td>
              <td><input appUiControl aria-label="Card amount" name="cardAmount" type="number" min="0" step="0.01" inputmode="decimal" placeholder="0.00"
                [disabled]="disabled()" [ngModel]="draft().card" (ngModelChange)="patch({ card: toNumber($event) })" /></td>
            </tr>
            <tr>
              <td>Mobile Banking</td>
              <td><input appUiControl aria-label="Mobile Banking amount" name="mobileAmount" type="number" min="0" step="0.01" inputmode="decimal" placeholder="0.00"
                [disabled]="disabled()" [ngModel]="draft().mobile" (ngModelChange)="patch({ mobile: toNumber($event) })" /></td>
            </tr>
          </tbody>
        </table>
        @if ((draft().cash ?? 0) > 0) {
          <app-ui-form-field label="Cash tendered" [required]="true">
            <input appUiControl type="number" min="0" step="0.01" inputmode="decimal" placeholder="0.00" name="tendered"
              [disabled]="disabled()" [ngModel]="draft().tendered" (ngModelChange)="patch({ tendered: toNumber($event) })" />
          </app-ui-form-field>
          <p class="ex-pay__line"><span>Change</span><strong>{{ change() | appCurrency }}</strong></p>
        }
      } @else {
        <fieldset class="ex-fieldset" [disabled]="disabled()">
          <legend>Refund method (one method)</legend>
          <div class="ex-radio-row">
            @for (method of methods; track method.value) {
              <label class="ex-radio">
                <input appUiControl type="radio" [name]="id + '-refund-method'" [value]="method.value"
                  [checked]="draft().refundMethod === method.value" (change)="patch({ refundMethod: method.value, refundConfirmed: false })" />
                {{ method.label }}
              </label>
            }
          </div>
        </fieldset>
        @if (draft().refundMethod === 'card' || draft().refundMethod === 'mobile') {
          <app-ui-alert variant="info">Card and mobile refunds are completed outside the system. Record the reference of the completed refund.</app-ui-alert>
          <app-ui-form-field label="Refund reference" [required]="true" hint="Transaction or approval reference from the terminal or wallet.">
            <input appUiControl type="text" name="refundReference" [attr.maxlength]="maxReference" autocomplete="off"
              [disabled]="disabled()" [ngModel]="draft().reference" (ngModelChange)="patch({ reference: $event ?? '' })" />
          </app-ui-form-field>
          <label class="ex-check">
            <input appUiControl type="checkbox" name="refundConfirmed" [disabled]="disabled()"
              [checked]="draft().refundConfirmed" (change)="patch({ refundConfirmed: $any($event.target).checked })" />
            The refund was completed outside the system
          </label>
        }
      }

      @if (showErrors() && error(); as problem) {
        <p class="ex-error" role="status">{{ problem }}</p>
      }
    </section>
  `
})
export class ExchangeSettlementPanelComponent {
  readonly id = `ex-pay-${++nextPanelId}`;
  readonly direction = input.required<ExchangeSettlementDirection>();
  readonly amount = input.required<number>();
  readonly disabled = input(false);
  readonly showErrors = input(true);
  readonly draft = model.required<SettlementDraft>();

  readonly maxReference = MAX_REFERENCE_LENGTH;
  readonly methods: ReadonlyArray<{ value: ExchangeSettlementMethod; label: string }> = [
    { value: 'cash', label: 'Cash' },
    { value: 'card', label: 'Card' },
    { value: 'mobile', label: 'Mobile Banking' }
  ];

  readonly change = computed(() => displayChange(this.draft()));
  readonly error = computed(() => settlementError(this.direction(), this.amount(), this.draft()));
  readonly directionTitle = computed(() => {
    switch (this.direction()) {
      case 'collect': return 'Collect from customer';
      case 'refund': return 'Refund to customer';
      default: return 'No settlement';
    }
  });

  patch(change: Partial<SettlementDraft>): void {
    this.draft.update(current => ({ ...current, ...change }));
  }

  toNumber(value: unknown): number | null {
    if (value === null || value === undefined || value === '') return null;
    const number = Number(value);
    return Number.isFinite(number) ? number : null;
  }
}
