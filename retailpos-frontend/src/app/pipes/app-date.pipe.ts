import { formatDate } from '@angular/common';
import { Pipe, PipeTransform, inject } from '@angular/core';
import { CompanyDateService } from '../services/company-date.service';

@Pipe({ name: 'appDate', standalone: true, pure: false })
export class AppDatePipe implements PipeTransform {
  private readonly dates = inject(CompanyDateService);

  transform(value: string | number | Date | null | undefined, style?: string, timezone?: string, locale?: string): string {
    if (value === null || value === undefined || value === '') return '';
    if (style && (/Time$/.test(style) || /^[HhmsSa: .]+$/.test(style))) {
      return formatDate(value, style, locale ?? 'en-US', timezone);
    }
    const withTime = !!style && (['short', 'medium', 'long', 'full'].includes(style) || /[Hhms]/.test(style.replace(/medium|short|long|full|Date/g, '')));
    return this.dates.format(value, withTime, timezone, locale);
  }
}
