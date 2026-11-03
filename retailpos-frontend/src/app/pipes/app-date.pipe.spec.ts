import { TestBed } from '@angular/core/testing';
import { AppDatePipe } from './app-date.pipe';
import { CompanyDateService } from '../services/company-date.service';
import { applyClientQuery, formatCellText } from '../components/shared/ui/ui-data-table.logic';
import { UiTableColumn } from '../components/shared/ui/ui-data-table.types';

describe('shared company date display and tables', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('overrides legacy display patterns, preserves time-only displays and responds to settings changes', () => {
    const pipe = TestBed.runInInjectionContext(() => new AppDatePipe());
    expect(pipe.transform('2026-04-05', 'MM/dd/yyyy')).toBe('05/04/2026');
    expect(pipe.transform('2026-04-05T09:30:00Z', 'medium', 'UTC')).toBe('05/04/2026 09:30:00');
    expect(pipe.transform('2026-04-05T09:30:00Z', 'HH:mm', 'UTC')).toBe('09:30');
    expect(pipe.transform(null)).toBe('');
    TestBed.inject(CompanyDateService).setFormat('YYYY-MM-DD');
    expect(pipe.transform('2026-04-05')).toBe('2026-04-05');
  });

  it('searches displayed dates but sorts original dates chronologically', () => {
    const dates = TestBed.inject(CompanyDateService);
    const formatter = (value: string | number | Date, withTime: boolean) => dates.format(value, withTime);
    const columns: UiTableColumn[] = [{ key: 'date', header: 'Date', type: 'date', sortable: true }];
    const rows = [{ date: '2026-12-01' }, { date: '2026-01-31' }, { date: '2026-04-05' }];
    expect(formatCellText(columns[0], '2026-04-05', formatter)).toBe('05/04/2026');
    expect(formatCellText({ key: 'code', header: 'Code' }, '2026-04-05', formatter)).toBe('2026-04-05');
    expect(applyClientQuery(rows, columns, {
      search: '', filters: {}, sort: { columnKey: 'date', direction: 'asc' }, page: 1, pageSize: 25
    }, formatter).rows.map(row => row.date)).toEqual(['2026-01-31', '2026-04-05', '2026-12-01']);
    expect(applyClientQuery(rows, columns, {
      search: '05/04/2026', filters: {}, sort: null, page: 1, pageSize: 25
    }, formatter).rows).toEqual([{ date: '2026-04-05' }]);
  });

  it('sorts a formatted period by its raw date and does not parse date-prefixed summaries as timestamps', () => {
    const dates = TestBed.inject(CompanyDateService);
    const formatter = (value: string | number | Date, withTime: boolean) => dates.format(value, withTime);
    const column: UiTableColumn = { key: 'period', header: 'Period', sortField: 'start', sortable: true };
    const rows = [
      { start: '2026-12-01', period: '01/12/2026 - Open' },
      { start: '2026-01-31', period: '31/01/2026 - Open' }
    ];
    expect(applyClientQuery(rows, [column], {
      search: '', filters: {}, sort: { columnKey: 'period', direction: 'asc' }, page: 1, pageSize: 25
    }, formatter).rows.map(row => row.start)).toEqual(['2026-01-31', '2026-12-01']);
    expect(formatCellText(column, '2026-01-01T00:00:00Z - Open', formatter)).toBe('2026-01-01T00:00:00Z - Open');
  });
});
