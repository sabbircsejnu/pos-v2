import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Params, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { GrnSearchRequest } from '../../models/grn.model';
import { AuthorizedOutletsDto } from '../../models/report.model';
import { GrnService } from '../../services/grn.service';
import { ListStateService } from '../../services/list-state.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { GrnListComponent } from './grn-list.component';

const LOCATIONS: Pick<AuthorizedOutletsDto, 'outlets' | 'warehouses'> = {
  outlets: [{ id: 1, name: 'Outlet A', type: 'outlet' }],
  warehouses: [{ id: 2, name: 'Warehouse B', type: 'warehouse' }],
};

describe('GrnListComponent filters', () => {
  const grnService = {
    isLoading: signal(false),
    error: signal<string | null>(null),
    grns: signal([]),
    totalCount: signal(0),
    search: vi.fn(() => of({})),
  };
  const locationAccess = { load: vi.fn() };
  const route = { snapshot: { queryParams: {} as Params } };
  let listState: ListStateService;
  let fixture: ComponentFixture<GrnListComponent>;
  let page: GrnListComponent;

  const lastRequest = () => grnService.search.mock.calls.at(-1)?.at(0) as unknown as GrnSearchRequest;

  const render = async (queryParams: Params = {}, locations = LOCATIONS) => {
    locationAccess.load.mockReturnValue(of({ data: locations }));
    route.snapshot.queryParams = queryParams;
    fixture = TestBed.createComponent(GrnListComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const optionLabels = (controlIndex: number) =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('select')[controlIndex].options)
      .map(option => option.text.trim());

  const changeFilter = async (change: () => void) => {
    change();
    await vi.advanceTimersByTimeAsync(400);
  };

  beforeEach(() => {
    vi.clearAllMocks();
    grnService.search.mockImplementation(() => of({}));
    grnService.grns.set([]);
    route.snapshot.queryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: GrnService, useValue: grnService },
        { provide: UserOutletAccessService, useValue: locationAccess },
        { provide: ActivatedRoute, useValue: route },
      ],
    });
    listState = TestBed.inject(ListStateService);
    vi.spyOn(listState, 'update').mockImplementation(() => undefined);
    vi.spyOn(listState, 'clear').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('loads location options through the GRN access policy before the first search', async () => {
    const options = new Subject<{ data: typeof LOCATIONS }>();
    locationAccess.load.mockReturnValue(options);
    fixture = TestBed.createComponent(GrnListComponent);
    fixture.detectChanges();

    expect(locationAccess.load).toHaveBeenCalledWith('GrnView');
    expect(grnService.search).not.toHaveBeenCalled();

    options.next({ data: LOCATIONS });
    expect(grnService.search).toHaveBeenCalledTimes(1);
  });

  it('sends all five filters together to the server', async () => {
    await render({
      status: 'partial',
      locationType: 'warehouse',
      locationId: '2',
      fromDate: '2026-03-10',
      toDate: '2026-03-12',
    });

    expect(lastRequest()).toEqual({
      status: 'partial',
      locationType: 'warehouse',
      locationId: 2,
      fromDate: '2026-03-10',
      toDate: '2026-03-12',
      pageNumber: 1,
      pageSize: 10,
    });
  });

  it('omits unset filters, so each date is independently optional', async () => {
    await render({ toDate: '2026-03-12' });

    expect(lastRequest()).toEqual({
      status: undefined,
      locationType: undefined,
      locationId: undefined,
      fromDate: undefined,
      toDate: '2026-03-12',
      pageNumber: 1,
      pageSize: 10,
    });
  });

  it('offers only the locations of the selected type', async () => {
    await render();
    expect(optionLabels(1)).toEqual(['All', 'Outlet', 'Warehouse']);
    expect(optionLabels(2)).toEqual(['All Locations']);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('select')[2].disabled).toBe(true);

    page.locationTypeFilter.set('warehouse');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(optionLabels(2)).toEqual(['All Warehouses', 'Warehouse B']);
  });

  it('works for a warehouse-only user, who gets no outlet choice', async () => {
    await render({}, { outlets: [], warehouses: LOCATIONS.warehouses });

    expect(optionLabels(1)).toEqual(['All', 'Warehouse']);
    expect(grnService.search).toHaveBeenCalledTimes(1);
  });

  it('drops a location from an old link that the user can no longer access', async () => {
    await render({ locationType: 'outlet', locationId: '99' });

    expect(page.locationTypeFilter()).toBe('outlet');
    expect(page.locationFilter()).toBeNull();
    expect(lastRequest().locationId).toBeUndefined();
  });

  it('drops a location type the user holds no location for', async () => {
    await render({ locationType: 'outlet', locationId: '1' }, { outlets: [], warehouses: LOCATIONS.warehouses });

    expect(page.locationTypeFilter()).toBe('');
    expect(lastRequest().locationType).toBeUndefined();
    expect(lastRequest().locationId).toBeUndefined();
  });

  it('still searches when the options fail, without any location filter', async () => {
    locationAccess.load.mockReturnValue(throwError(() => new Error('nope')));
    route.snapshot.queryParams = { locationType: 'outlet', locationId: '1' };
    fixture = TestBed.createComponent(GrnListComponent);
    fixture.detectChanges();

    expect(lastRequest().locationType).toBeUndefined();
    expect(lastRequest().locationId).toBeUndefined();
  });

  it('resets the location when the location type changes', async () => {
    await render({ locationType: 'outlet', locationId: '1' });
    vi.useFakeTimers();

    await changeFilter(() => {
      page.locationTypeFilter.set('warehouse');
      page.onLocationTypeChange();
    });

    expect(page.locationFilter()).toBeNull();
    expect(lastRequest()).toMatchObject({ locationType: 'warehouse', locationId: undefined });
  });

  it('returns to page 1 whenever a filter changes', async () => {
    await render({ page: '3' });
    expect(lastRequest().pageNumber).toBe(3);
    vi.useFakeTimers();

    await changeFilter(() => {
      page.fromDate.set('2026-03-10');
      page.onFilterChange();
    });

    expect(page.pageNumber()).toBe(1);
    expect(lastRequest()).toMatchObject({ fromDate: '2026-03-10', pageNumber: 1 });
  });

  it('rejects From Date after To Date with a clear message and no request', async () => {
    await render();
    grnService.search.mockClear();
    vi.useFakeTimers();

    await changeFilter(() => {
      page.fromDate.set('2026-03-12');
      page.toDate.set('2026-03-10');
      page.onFilterChange();
    });
    page.changePage(2);
    fixture.detectChanges();

    expect(grnService.search).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('From Date cannot be later than To Date.');
    expect(page.pageNumber()).toBe(1);
  });

  it('accepts equal From and To dates', async () => {
    await render({ fromDate: '2026-03-10', toDate: '2026-03-10' });

    expect(page.dateRangeError()).toBe('');
    expect(lastRequest()).toMatchObject({ fromDate: '2026-03-10', toDate: '2026-03-10' });
  });

  it('clears all five filters and returns to page 1', async () => {
    await render({
      status: 'full',
      locationType: 'outlet',
      locationId: '1',
      fromDate: '2026-03-10',
      toDate: '2026-03-12',
      page: '2',
    });

    page.clearFilters();

    expect(listState.clear).toHaveBeenCalled();
    expect(lastRequest()).toEqual({
      status: undefined,
      locationType: undefined,
      locationId: undefined,
      fromDate: undefined,
      toDate: undefined,
      pageNumber: 1,
      pageSize: 10,
    });
  });

  it('keeps the filters in the URL', async () => {
    await render();
    vi.useFakeTimers();

    await changeFilter(() => {
      page.locationTypeFilter.set('warehouse');
      page.locationFilter.set(2);
      page.toDate.set('2026-03-12');
      page.onFilterChange();
    });

    expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({
      locationType: 'warehouse',
      locationId: 2,
      toDate: '2026-03-12',
      page: 1,
    }));
  });

  it('ignores a slower response from earlier filters', async () => {
    await render();
    const earlier = new Subject<unknown>();
    grnService.search.mockReturnValueOnce(earlier as never);
    page.search();

    page.statusFilter.set('full');
    page.search();

    expect(earlier.observed).toBe(false);
  });

  it('explains an empty filtered result instead of prompting to create a GRN', async () => {
    await render({ status: 'full' });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No GRNs match the selected filters.');

    page.clearFilters();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Create a GRN after receiving goods');
  });
});
