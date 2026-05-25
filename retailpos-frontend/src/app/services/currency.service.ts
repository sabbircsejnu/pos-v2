import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, tap, catchError, shareReplay, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CurrencySettings,
  DEFAULT_CURRENCY_SETTINGS,
  SUPPORTED_CURRENCIES,
} from '../models/settings.model';

const STORAGE_KEY = 'currency_settings_cache';

@Injectable({ providedIn: 'root' })
export class CurrencyService {
  private currencyUrl = `${environment.apiUrl}/settings/currency`;

  private settingsSignal = signal<CurrencySettings>(this.readFromStorage());
  readonly settings = this.settingsSignal.asReadonly();
  readonly symbol = computed(() => this.settingsSignal().currencySymbol);
  readonly code = computed(() => this.settingsSignal().currencyCode);

  private inflight$: Observable<CurrencySettings> | null = null;

  constructor(private http: HttpClient) {}

  /**
   * Loads currency settings from the API and caches them for the rest of the session.
   * Safe to call from APP_INITIALIZER and again after login.
   */
  load(): Observable<CurrencySettings> {
    if (this.inflight$) return this.inflight$;

    this.inflight$ = this.http.get<{ data: CurrencySettings }>(this.currencyUrl).pipe(
      map((res) => this.normalize(res?.data ?? this.settingsSignal())),
      tap((merged) => {
        this.settingsSignal.set(merged);
        this.writeToStorage(merged);
      }),
      catchError(() => of(this.settingsSignal())),
      tap({ complete: () => { this.inflight$ = null; } }),
      shareReplay(1),
    );
    return this.inflight$;
  }

  setSettings(settings: CurrencySettings): void {
    const merged = this.normalize(settings);
    this.settingsSignal.set(merged);
    this.writeToStorage(merged);
  }

  format(amount: number | null | undefined): string {
    const s = this.settingsSignal();
    const value = Number.isFinite(amount as number) ? (amount as number) : 0;
    const formattedNumber = this.formatNumber(value, s);
    return s.symbolPosition === 'after'
      ? `${formattedNumber}${s.currencySymbol}`
      : `${s.currencySymbol}${formattedNumber}`;
  }

  formatNumber(amount: number | null | undefined, settings?: CurrencySettings): string {
    const s = settings ?? this.settingsSignal();
    const value = Number.isFinite(amount as number) ? (amount as number) : 0;
    const fixed = value.toFixed(s.decimalPlaces);
    const [intPart, decPart] = fixed.split('.');
    const withThousands = s.thousandsSeparator
      ? intPart.replace(/\B(?=(\d{3})+(?!\d))/g, s.thousandsSeparator)
      : intPart;
    return decPart ? `${withThousands}${s.decimalSeparator}${decPart}` : withThousands;
  }

  private normalize(settings: CurrencySettings): CurrencySettings {
    return {
      currencyCode: settings.currencyCode || DEFAULT_CURRENCY_SETTINGS.currencyCode,
      currencySymbol: settings.currencySymbol || DEFAULT_CURRENCY_SETTINGS.currencySymbol,
      currencyName: settings.currencyName || DEFAULT_CURRENCY_SETTINGS.currencyName,
      symbolPosition: (settings.symbolPosition as 'before' | 'after') === 'after' ? 'after' : 'before',
      decimalPlaces: this.clamp(Number.isFinite(settings.decimalPlaces) ? settings.decimalPlaces : 2, 0, 4),
      thousandsSeparator: settings.thousandsSeparator ?? DEFAULT_CURRENCY_SETTINGS.thousandsSeparator,
      decimalSeparator: settings.decimalSeparator || DEFAULT_CURRENCY_SETTINGS.decimalSeparator,
    };
  }

  private clamp(n: number, min: number, max: number): number {
    return Math.min(Math.max(n, min), max);
  }

  private readFromStorage(): CurrencySettings {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return { ...DEFAULT_CURRENCY_SETTINGS };
      const parsed = JSON.parse(raw) as CurrencySettings;
      return this.normalize(parsed);
    } catch {
      return { ...DEFAULT_CURRENCY_SETTINGS };
    }
  }

  private writeToStorage(settings: CurrencySettings): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // storage quota / privacy mode — keep in-memory cache only
    }
  }

  getSupportedCurrencies() {
    return SUPPORTED_CURRENCIES;
  }
}
