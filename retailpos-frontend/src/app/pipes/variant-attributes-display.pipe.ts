import { Pipe, PipeTransform } from '@angular/core';

type VariantAttributeRow = {
  attributeName?: string;
  name?: string;
  key?: string;
  displayName?: string;
  value?: unknown;
  displayValue?: unknown;
  sortOrder?: number;
  displayOrder?: number;
};

@Pipe({
  name: 'variantAttributesDisplay',
  standalone: true,
  pure: true,
})
export class VariantAttributesDisplayPipe implements PipeTransform {
  transform(value?: string | null): string {
    if (!value) {
      return '';
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return '';
    }

    try {
      const parsed = JSON.parse(trimmed);

      if (Array.isArray(parsed)) {
        return this.formatArray(parsed as VariantAttributeRow[]);
      }

      if (parsed && typeof parsed === 'object') {
        return this.formatObject(parsed as Record<string, unknown>);
      }
    } catch {
      return this.cleanFormattedText(trimmed);
    }

    return this.cleanFormattedText(trimmed);
  }

  private formatArray(rows: VariantAttributeRow[]): string {
    return rows
      .filter(row => !!this.resolveLabel(row) && this.isDisplayableValue(this.resolveValue(row)))
      .sort((left, right) => (left.displayOrder ?? left.sortOrder ?? Number.MAX_SAFE_INTEGER) - (right.displayOrder ?? right.sortOrder ?? Number.MAX_SAFE_INTEGER))
      .map(row => `${this.resolveLabel(row)} ${String(this.resolveValue(row)).trim()}`)
      .join(' / ');
  }

  private formatObject(obj: Record<string, unknown>): string {
    return Object.entries(obj)
      .filter(([key, rawValue]) => this.isDisplayableEntry(key, rawValue))
      .map(([key, rawValue]) => `${this.humanizeKey(key)} ${String(rawValue).trim()}`)
      .join(' / ');
  }

  private cleanFormattedText(value: string): string {
    return value
      .replace(/[{}"]+/g, '')
      .replace(/\s*,\s*/g, ' / ')
      .replace(/\s*\|\s*/g, ' / ')
      .replace(/\s*\/\s*/g, ' / ')
      .trim();
  }

  private resolveLabel(row: VariantAttributeRow): string {
    const label = row.displayName || row.attributeName || row.name || row.key;
    return label ? this.humanizeKey(String(label)) : '';
  }

  private resolveValue(row: VariantAttributeRow): unknown {
    return row.displayValue ?? row.value;
  }

  private isDisplayableEntry(key: string, rawValue: unknown): boolean {
    if (!this.isDisplayableValue(rawValue)) {
      return false;
    }

    const normalizedKey = key.trim().toLowerCase();
    if (['deleted', 'isdeleted', 'active', 'isactive', 'status'].includes(normalizedKey)) {
      return false;
    }

    return true;
  }

  private isDisplayableValue(rawValue: unknown): boolean {
    if (rawValue === null || rawValue === undefined) {
      return false;
    }

    const normalized = String(rawValue).trim();
    if (!normalized) {
      return false;
    }

    return !['null', 'undefined', 'deleted', 'inactive', 'false'].includes(normalized.toLowerCase());
  }

  private humanizeKey(key: string): string {
    return key
      .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^./, c => c.toUpperCase());
  }
}