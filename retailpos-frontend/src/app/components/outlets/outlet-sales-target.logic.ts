import { OutletSalesTargetDay, SalesTargetPeriodType } from '../../models/outlet.model';

/** JavaScript and .NET number weekdays the same way (0 = Sunday). Shown in business-week order. */
export const WEEKDAYS: readonly { value: number; label: string }[] = [
  { value: 6, label: 'Saturday' },
  { value: 0, label: 'Sunday' },
  { value: 1, label: 'Monday' },
  { value: 2, label: 'Tuesday' },
  { value: 3, label: 'Wednesday' },
  { value: 4, label: 'Thursday' },
  { value: 5, label: 'Friday' }
];

export const TARGET_PERIODS: readonly { type: SalesTargetPeriodType; label: string }[] = [
  { type: 'weekly', label: 'Weekly (Saturday to Friday)' },
  { type: 'first_half', label: 'First Half (1st to 15th)' },
  { type: 'second_half', label: 'Second Half (16th to month end)' },
  { type: 'monthly', label: 'Monthly' }
];

export interface TargetVersionLike {
  versionNo: number;
  effectiveFrom: string;
  isEnabled: boolean;
  days: OutletSalesTargetDay[];
}

export interface TargetPeriodPreview {
  /** Null when no date of the period is configured: N/A, not zero. */
  total: number | null;
  workingDays: number;
  offDays: number;
  notConfiguredDays: number;
}

const DAY_MS = 86_400_000;

function toUtc(iso: string): number {
  const [year, month, day] = iso.split('-').map(Number);
  return Date.UTC(year, month - 1, day);
}

function toIso(utc: number): string {
  return new Date(utc).toISOString().slice(0, 10);
}

/** The calendar range of a period type that contains a date. Mirrors SalesTargetCalculator on the server. */
export function periodRange(type: SalesTargetPeriodType, iso: string): { from: string; to: string } {
  const utc = toUtc(iso);
  const date = new Date(utc);
  const year = date.getUTCFullYear();
  const month = date.getUTCMonth();
  switch (type) {
    case 'weekly': {
      const from = utc - ((date.getUTCDay() + 1) % 7) * DAY_MS;
      return { from: toIso(from), to: toIso(from + 6 * DAY_MS) };
    }
    case 'first_half':
      return { from: toIso(Date.UTC(year, month, 1)), to: toIso(Date.UTC(year, month, 15)) };
    case 'second_half':
      return { from: toIso(Date.UTC(year, month, 16)), to: toIso(Date.UTC(year, month + 1, 0)) };
    case 'monthly':
      return { from: toIso(Date.UTC(year, month, 1)), to: toIso(Date.UTC(year, month + 1, 0)) };
  }
}

/** The version in force on a date: the latest start on or before it, then the latest save. */
export function versionOn<T extends TargetVersionLike>(versions: readonly T[], iso: string): T | null {
  let best: T | null = null;
  for (const version of versions) {
    if (version.effectiveFrom > iso) continue;
    if (!best
      || version.effectiveFrom > best.effectiveFrom
      || (version.effectiveFrom === best.effectiveFrom && version.versionNo > best.versionNo)) {
      best = version;
    }
  }
  return best;
}

/**
 * On-screen preview from the values in the form. It walks real calendar dates, as the server does; the server's figures
 * are the ones that count. Summed in paisa so the preview does not show floating point noise.
 */
export function previewPeriod(versions: readonly TargetVersionLike[], from: string, to: string): TargetPeriodPreview {
  let paisa = 0;
  let workingDays = 0;
  let offDays = 0;
  let notConfiguredDays = 0;
  for (let utc = toUtc(from); utc <= toUtc(to); utc += DAY_MS) {
    const version = versionOn(versions, toIso(utc));
    const day = version?.isEnabled
      ? version.days.find(candidate => candidate.dayOfWeek === new Date(utc).getUTCDay())
      : undefined;
    if (day?.isOffDay) {
      offDays++;
    } else if (day && isAmount(day.targetAmount)) {
      workingDays++;
      paisa += Math.round(day.targetAmount! * 100);
    } else {
      notConfiguredDays++;
    }
  }
  return {
    total: workingDays + offDays > 0 ? paisa / 100 : null,
    workingDays,
    offDays,
    notConfiguredDays
  };
}

/** A usable money amount: not blank, not negative, at most two decimals. */
export function isAmount(value: unknown): boolean {
  if (value === null || value === undefined || value === '') return false;
  const amount = Number(value);
  return Number.isFinite(amount) && amount >= 0 && Math.abs(amount * 100 - Math.round(amount * 100)) < 1e-6;
}
