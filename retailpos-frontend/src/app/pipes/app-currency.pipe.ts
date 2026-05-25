import { Pipe, PipeTransform, inject } from '@angular/core';
import { CurrencyService } from '../services/currency.service';

/**
 * Formats a numeric amount using the company-level currency settings.
 *
 * Pure pipe — Angular re-runs it whenever the bound value changes. Currency
 * settings are pulled from the cached signal in {@link CurrencyService}; on
 * settings update, change detection on any view bound to a currency-formatted
 * value will pick up the new format.
 *
 * Usage:
 *   {{ amount | appCurrency }}
 *   {{ amount | appCurrency:'plain' }}  // number only, no symbol
 */
@Pipe({ name: 'appCurrency', standalone: true, pure: false })
export class AppCurrencyPipe implements PipeTransform {
  private currency = inject(CurrencyService);

  transform(value: number | null | undefined, mode: 'symbol' | 'plain' = 'symbol'): string {
    if (mode === 'plain') return this.currency.formatNumber(value);
    return this.currency.format(value);
  }
}
