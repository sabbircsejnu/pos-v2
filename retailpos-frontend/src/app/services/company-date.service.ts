import { formatDate } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Injectable, Injector, inject, signal } from '@angular/core';
import { Observable, distinctUntilChanged, map, switchMap, of, catchError, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AlertService } from './alert.service';

export type CompanyDateFormat = 'dd/MM/yyyy' | 'MM/dd/yyyy' | 'yyyy-MM-dd';

export function normalizeDateFormat(value: string): CompanyDateFormat {
  switch (value.toLowerCase()) {
    case 'dd/mm/yyyy': return 'dd/MM/yyyy';
    case 'mm/dd/yyyy': return 'MM/dd/yyyy';
    case 'yyyy-mm-dd': return 'yyyy-MM-dd';
    default: throw new Error(`Unsupported company date format: ${value}`);
  }
}

export function isIsoDate(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?)?$/.test(value);
}

@Injectable({ providedIn: 'root' })
export class CompanyDateService {
  private readonly injector = inject(Injector);
  private readonly alerts = inject(AlertService);
  readonly dateFormat = signal<CompanyDateFormat>('dd/MM/yyyy');

  connect(users: Observable<{ id: number | string } | null>): void {
    users.pipe(
      map(user => user?.id ?? null),
      distinctUntilChanged(),
      switchMap(id => {
        this.dateFormat.set('dd/MM/yyyy');
        return id === null ? of(null) : this.load().pipe(catchError(() => {
          this.alerts.warning('Company date settings could not be loaded. Dates temporarily use dd/MM/yyyy.');
          return of(null);
        }));
      })
    ).subscribe();
  }

  load(): Observable<{ data: { dateFormat: string } }> {
    return this.injector.get(HttpClient).get<{ data: { dateFormat: string } }>(`${environment.apiUrl}/settings/formatting`)
      .pipe(tap(response => this.setFormat(response.data.dateFormat)));
  }

  setFormat(value: string): void {
    this.dateFormat.set(normalizeDateFormat(value));
  }

  format(value: string | number | Date | null | undefined, withTime = false, timezone?: string, locale = 'en-US'): string {
    if (value === null || value === undefined || value === '') return '';
    const pattern = this.dateFormat();
    // DateOnly values are calendar dates, not UTC instants.
    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
      const [year, month, day] = value.split('-');
      return pattern.replace('yyyy', year).replace('MM', month).replace('dd', day);
    }
    if (timezone?.includes('/')) {
      const parts = new Intl.DateTimeFormat(locale, {
        timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
      }).formatToParts(value instanceof Date ? value : new Date(value));
      const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)!.value;
      const date = pattern.replace('yyyy', part('year')).replace('MM', part('month')).replace('dd', part('day'));
      return date + (withTime ? ` ${part('hour')}:${part('minute')}:${part('second')}` : '');
    }
    return formatDate(value, `${pattern}${withTime ? ' HH:mm:ss' : ''}`, locale, timezone);
  }

  inputText(value: string, withTime: boolean): string {
    if (!value) return '';
    const date = this.format(value.slice(0, 10));
    return withTime ? `${date} ${value.slice(11)}` : date;
  }

  parse(text: string, withTime = false): string | null {
    const pattern = this.dateFormat();
    const datePattern = pattern === 'yyyy-MM-dd' ? '(\\d{4})-(\\d{2})-(\\d{2})' : '(\\d{2})/(\\d{2})/(\\d{4})';
    const match = new RegExp(`^${datePattern}${withTime ? ' (\\d{2}):(\\d{2})(?::(\\d{2}))?' : ''}$`).exec(text);
    if (!match) return null;
    const parts = pattern === 'yyyy-MM-dd' ? [match[1], match[2], match[3]]
      : pattern === 'dd/MM/yyyy' ? [match[3], match[2], match[1]] : [match[3], match[1], match[2]];
    const [year, month, day] = parts.map(Number);
    if (year < 1 || month < 1 || month > 12 || day < 1) return null;
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    if (day > days[month - 1]) return null;
    const iso = parts.join('-');
    if (!withTime) return iso;
    if (Number(match[4]) > 23 || Number(match[5]) > 59 || Number(match[6] ?? 0) > 59) return null;
    return `${iso}T${match[4]}:${match[5]}${match[6] ? `:${match[6]}` : ''}`;
  }
}
