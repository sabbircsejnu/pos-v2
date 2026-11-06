import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CompanyDateService } from '../../services/company-date.service';
import { GrnService } from '../../services/grn.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { GrnCreateComponent } from './grn-create.component';
import { GrnDetailsComponent } from './grn-details.component';
import { GrnListComponent } from './grn-list.component';

describe('GRN pages and the business time zone', () => {
  const grnService = {
    isLoading: signal(false),
    error: signal<string | null>(null),
    grns: signal([]),
    totalCount: signal(0),
    search: vi.fn(() => of({})),
    getById: vi.fn(() => of({ data: null })),
    getPendingPOs: vi.fn(() => of({ data: [] })),
    create: vi.fn(() => of({ data: { id: 1 } })),
  };

  let http: HttpTestingController;
  let dates: CompanyDateService;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: GrnService, useValue: grnService },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: { outlets: [], warehouses: [] } }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    dates = TestBed.inject(CompanyDateService);
  });

  afterEach(() => {
    // Nothing else is expected, so this fails on any request, including GET /settings/company.
    http.verify();
    TestBed.resetTestingModule();
  });

  describe.each<[string, Type<{ formatDateTime(date: string): string }>]>([
    ['list', GrnListComponent],
    ['details', GrnDetailsComponent],
    ['create', GrnCreateComponent],
  ])('%s page', (_name, page) => {
    it('makes no company settings request, so a user without settings.view gets no 403', () => {
      const fixture = TestBed.createComponent(page);
      fixture.detectChanges();
      http.expectNone(request => request.url.includes('/settings/company'));
    });

    it('shows dates in the business time zone from the formatting settings', () => {
      dates.setTimeZone('America/New_York');
      const fixture = TestBed.createComponent(page);
      fixture.detectChanges();
      expect(fixture.componentInstance.formatDateTime('2026-01-15T15:00:00Z')).toBe('15/01/2026 10:00:00');
    });

    it.each([
      ['2026-03-08T06:59:59Z', '08/03/2026 01:59:59'],
      ['2026-03-08T07:00:00Z', '08/03/2026 03:00:00'],
      ['2026-11-01T05:59:59Z', '01/11/2026 01:59:59'],
      ['2026-11-01T06:00:00Z', '01/11/2026 01:00:00'],
    ])('shows %s across a daylight saving change as %s', (instant, shown) => {
      dates.setTimeZone('America/New_York');
      const fixture = TestBed.createComponent(page);
      fixture.detectChanges();
      expect(fixture.componentInstance.formatDateTime(instant)).toBe(shown);
    });
  });

  describe('create page received date', () => {
    const createPage = () => {
      const fixture = TestBed.createComponent(GrnCreateComponent);
      fixture.detectChanges();
      return { fixture, page: fixture.componentInstance };
    };

    const submit = (page: GrnCreateComponent, localDateTime: string) => {
      page.selectedPoId.set(5);
      page.lineItems.set([{ id: 9, orderedQty: 1, receivedQty: 1 } as never]);
      page.receivedDateTime.set(localDateTime);
      page.onSubmit();
      return (grnService.create.mock.calls.at(-1) as unknown[])[0] as { receivedDate: string };
    };

    it('converts the entered time to UTC using the business time zone', () => {
      dates.setTimeZone('America/New_York');
      const { page } = createPage();
      expect(submit(page, '2026-01-15T10:00').receivedDate).toBe('2026-01-15T15:00:00.000Z');
    });

    it('re-seeds the default time when the zone arrives after the page has rendered', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-01-15T15:00:00Z'));
      try {
        const { fixture, page } = createPage();
        expect(page.receivedDateTime()).toBe('2026-01-15T21:00');

        dates.setTimeZone('America/New_York');
        fixture.detectChanges();
        await fixture.whenStable();

        expect(page.receivedDateTime()).toBe('2026-01-15T10:00');
      } finally {
        vi.useRealTimers();
      }
    });

    it('keeps a time the user already entered when the zone arrives late', async () => {
      const { fixture, page } = createPage();
      page.receivedDateTime.set('2026-01-10T08:30');

      dates.setTimeZone('America/New_York');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(page.receivedDateTime()).toBe('2026-01-10T08:30');
    });
  });
});
