import { OutletSalesTargetDay } from '../../models/outlet.model';
import { TargetVersionLike, isAmount, periodRange, previewPeriod, versionOn } from './outlet-sales-target.logic';

const days = (amount: number, ...off: number[]): OutletSalesTargetDay[] =>
  [0, 1, 2, 3, 4, 5, 6].map(dayOfWeek => ({
    dayOfWeek,
    isOffDay: off.includes(dayOfWeek),
    targetAmount: off.includes(dayOfWeek) ? null : amount
  }));

const version = (versionNo: number, effectiveFrom: string, list: OutletSalesTargetDay[], isEnabled = true): TargetVersionLike =>
  ({ versionNo, effectiveFrom, isEnabled, days: list });

describe('outlet sales target preview', () => {
  it('runs the week from Saturday to Friday', () => {
    expect(periodRange('weekly', '2026-11-10')).toEqual({ from: '2026-11-07', to: '2026-11-13' });
    expect(periodRange('weekly', '2026-11-07')).toEqual({ from: '2026-11-07', to: '2026-11-13' });
    expect(periodRange('weekly', '2026-11-13')).toEqual({ from: '2026-11-07', to: '2026-11-13' });
  });

  it('splits months at the 15th, not every 14 days', () => {
    expect(periodRange('first_half', '2026-11-20')).toEqual({ from: '2026-11-01', to: '2026-11-15' });
    expect(periodRange('second_half', '2026-01-02')).toEqual({ from: '2026-01-16', to: '2026-01-31' });
  });

  it('handles a leap February', () => {
    expect(periodRange('monthly', '2028-02-10')).toEqual({ from: '2028-02-01', to: '2028-02-29' });
    expect(periodRange('monthly', '2027-02-10')).toEqual({ from: '2027-02-01', to: '2027-02-28' });
  });

  it('counts actual weekdays in a month', () => {
    const versions = [version(1, '2026-01-01', days(100, 2))];

    const november = previewPeriod(versions, '2026-11-01', '2026-11-30');
    expect(november).toEqual({ total: 2600, workingDays: 26, offDays: 4, notConfiguredDays: 0 });

    const december = previewPeriod(versions, '2026-12-01', '2026-12-31');
    expect(december.offDays).toBe(5);
    expect(december.total).toBe(2600);
  });

  it('is N/A, not zero, without an enabled version', () => {
    expect(previewPeriod([], '2026-11-01', '2026-11-07').total).toBeNull();
    expect(previewPeriod([version(1, '2026-01-01', days(100), false)], '2026-11-01', '2026-11-07').total).toBeNull();
  });

  it('uses each version only from its effective date', () => {
    const versions = [version(1, '2026-01-01', days(100)), version(2, '2026-11-10', days(200))];

    expect(previewPeriod(versions, '2026-11-08', '2026-11-11').total).toBe(600);
  });

  it('reports days before the first version as not configured', () => {
    const preview = previewPeriod([version(1, '2026-11-10', days(100))], '2026-11-07', '2026-11-13');

    expect(preview.total).toBe(400);
    expect(preview.notConfiguredDays).toBe(3);
  });

  it('does not show floating point noise', () => {
    expect(previewPeriod([version(1, '2026-01-01', days(0.1))], '2026-11-07', '2026-11-09').total).toBe(0.3);
  });

  it('lets the later save win on one date', () => {
    const versions = [version(2, '2026-11-08', days(200)), version(1, '2026-11-08', days(100))];

    expect(versionOn(versions, '2026-11-08')?.versionNo).toBe(2);
  });

  it('recognises usable amounts', () => {
    expect(isAmount(0)).toBe(true);
    expect(isAmount(100.25)).toBe(true);
    expect(isAmount(null)).toBe(false);
    expect(isAmount('')).toBe(false);
    expect(isAmount(-1)).toBe(false);
    expect(isAmount(1.005)).toBe(false);
  });
});
