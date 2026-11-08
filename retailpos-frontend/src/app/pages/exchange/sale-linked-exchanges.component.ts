import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { UiAlertComponent, UiCardComponent } from '../../components/shared/ui';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { ExchangeListItem, settlementDirectionLabel } from '../../models/exchange.model';
import { ExchangeService } from '../../services/exchange.service';
import { readExchangeError } from './exchange-errors';

/** Sale Details card listing every exchange that used the sale (FR-EX-152). Hidden when there are none. */
@Component({
  selector: 'app-sale-linked-exchanges',
  standalone: true,
  imports: [RouterLink, AppCurrencyPipe, UiCardComponent, UiAlertComponent],
  styleUrls: ['./exchange.css'],
  template: `
    @if (error()) {
      <app-ui-alert variant="warning" title="Linked exchanges">{{ error() }}</app-ui-alert>
    } @else if (items().length > 0) {
      <app-ui-card title="Linked exchanges" [headingLevel]="2">
        <ul class="ex-list">
          @for (item of items(); track item.id) {
            <li>
              <span>
                <a [routerLink]="['/exchanges', item.id]">{{ item.exchangeNumber }}</a>
                <span class="ex-sub">{{ item.businessDate }} · {{ item.receivingOutletName || '' }} · {{ item.status }}</span>
              </span>
              <span>{{ directionLabel(item.settlementDirection) }} {{ item.settlementAmount | appCurrency }}</span>
            </li>
          }
        </ul>
      </app-ui-card>
    }
  `
})
export class SaleLinkedExchangesComponent {
  private readonly exchanges = inject(ExchangeService);
  private readonly destroyRef = inject(DestroyRef);
  readonly saleId = input.required<number>();
  readonly items = signal<ExchangeListItem[]>([]);
  readonly error = signal('');
  readonly directionLabel = settlementDirectionLabel;

  constructor() {
    effect(() => {
      const saleId = this.saleId();
      if (saleId > 0) this.load(saleId);
    });
  }

  private load(saleId: number): void {
    this.error.set('');
    this.exchanges.listBySale(saleId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => this.items.set(res?.data ?? []),
      error: (err: unknown) => {
        const info = readExchangeError(err);
        // An unconfigured business simply has no exchanges to link.
        if (info.code === 'EXCHANGE_NOT_CONFIGURED' || info.status === 404) {
          this.items.set([]);
          return;
        }
        this.error.set(info.message);
      }
    });
  }
}
