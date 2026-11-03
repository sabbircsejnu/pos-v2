import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthorizedOutletsDto } from '../../models/report.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { StockCountService } from '../../services/stock-count.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { StockCountCreateComponent } from './stock-count-create.component';
import { StockCountDetailsComponent } from './stock-count-details.component';
import { StockCountListComponent } from './stock-count-list.component';

describe('Stock Count authorized locations', () => {
  let access: AuthorizedOutletsDto;
  let queryParams: Record<string, string>;
  const service = {
    create: vi.fn(() => of({ data: { id: 100 } })),
    search: vi.fn(() => of({ data: {} })),
    getById: vi.fn(() => throwError(() => ({ status: 403 }))),
    stockCounts: signal<{ id: number }[]>([]),
    totalCount: signal(0),
    isLoading: signal(false),
    error: signal(null),
  };
  const alerts = { error: vi.fn(), success: vi.fn() };
  const location = { back: vi.fn() };
  const locationAccess = { load: vi.fn(() => of({ data: access })) };

  beforeEach(async () => {
    vi.clearAllMocks();
    queryParams = {};
    access = {
      outlets: [{ id: 11, name: 'Assigned A', type: 'outlet' }],
      warehouses: [],
      destinationOutlets: [{ id: 99, name: 'Unassigned destination', type: 'outlet' }],
      isBusinessOwner: false,
      isGlobalAccess: false,
      defaultLocationId: 11,
      defaultLocationType: 'outlet',
    };
    locationAccess.load.mockImplementation(() => of({ data: access }));
    await TestBed.configureTestingModule({
      imports: [StockCountCreateComponent, StockCountListComponent, StockCountDetailsComponent],
      providers: [
        { provide: StockCountService, useValue: service },
        { provide: UserOutletAccessService, useValue: locationAccess },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'Access denied' } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: location },
        { provide: ActivatedRoute, useValue: {
          snapshot: { queryParams, paramMap: { get: () => '999' } },
        } },
      ],
    }).compileComponents();
  });

  function create(): ComponentFixture<StockCountCreateComponent> {
    const fixture = TestBed.createComponent(StockCountCreateComponent);
    fixture.detectChanges();
    return fixture;
  }

  function list(): ComponentFixture<StockCountListComponent> {
    const fixture = TestBed.createComponent(StockCountListComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('keeps one outlet selected and excludes unauthorized destinations even with ViewAll', async () => {
    const fixture = create();
    await fixture.whenStable();
    fixture.detectChanges();
    const selects = fixture.nativeElement.querySelectorAll('select') as NodeListOf<HTMLSelectElement>;
    expect(selects[1].disabled).toBe(true);
    expect(Array.from(selects[1].options, option => option.text)).toEqual(['Select location', 'Assigned A']);
    expect(Array.from(selects[0].options, option => option.text)).toEqual(['Outlet']);
    fixture.componentInstance.submit();
    expect(service.create).toHaveBeenCalledWith(expect.objectContaining({ locationId: 11, locationType: 'outlet' }));
  });

  it('allows selecting and creating at every authorized outlet without global access', () => {
    access.outlets.push({ id: 12, name: 'Assigned B', type: 'outlet' });
    const fixture = create();
    const selects = fixture.nativeElement.querySelectorAll('select') as NodeListOf<HTMLSelectElement>;
    expect(selects[1].disabled).toBe(false);
    expect(Array.from(selects[1].options, option => option.text)).toContain('Assigned B');
    fixture.componentInstance.locationId.set(12);
    fixture.componentInstance.submit();
    expect(service.create).toHaveBeenCalledWith(expect.objectContaining({ locationId: 12 }));
  });

  it('rejects a manipulated create location or location type', () => {
    const fixture = create();
    fixture.componentInstance.locationId.set(99);
    fixture.componentInstance.submit();
    fixture.componentInstance.locationId.set(11);
    fixture.componentInstance.locationType.set('warehouse');
    fixture.componentInstance.submit();
    expect(service.create).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalledTimes(2);
  });

  it('does not use a stale unauthorized default from the access response', () => {
    access.defaultLocationId = 99;
    const fixture = create();
    expect(fixture.componentInstance.locationId()).toBe(11);
  });

  it('requests the StockCountOperation policy (Step 5 assignments) for create and list', () => {
    create();
    list();
    expect(locationAccess.load).toHaveBeenCalledTimes(2);
    expect(locationAccess.load).toHaveBeenNthCalledWith(1, 'StockCountOperation');
    expect(locationAccess.load).toHaveBeenNthCalledWith(2, 'StockCountOperation');
  });

  it('fails closed when loading authorized locations fails', () => {
    locationAccess.load.mockReturnValue(throwError(() => new Error('Unavailable')));
    const fixture = create();
    fixture.componentInstance.submit();
    expect(service.create).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalled();
    const listFixture = list();
    listFixture.componentInstance.load();
    expect(service.search).not.toHaveBeenCalled();
  });

  it('clears counts from the previous access context before loading authorizations', () => {
    service.stockCounts.set([{ id: 99 }]);
    service.totalCount.set(1);
    locationAccess.load.mockReturnValue(throwError(() => new Error('Unavailable')));
    list();
    expect(service.stockCounts()).toEqual([]);
    expect(service.totalCount()).toBe(0);
    expect(service.search).not.toHaveBeenCalled();
  });

  it('lists all assigned outlets by default instead of pinning the primary outlet', () => {
    access.outlets.push({ id: 12, name: 'Assigned B', type: 'outlet' });
    list();
    expect(service.search).toHaveBeenCalledWith(expect.objectContaining({
      locationId: undefined, locationType: undefined,
    }));
  });

  it('preserves an authorized second outlet from the URL', () => {
    access.outlets.push({ id: 12, name: 'Assigned B', type: 'outlet' });
    Object.assign(queryParams, { locationId: '12', locationType: 'outlet' });
    list();
    expect(service.search).toHaveBeenCalledWith(expect.objectContaining({ locationId: 12, locationType: 'outlet' }));
    expect(alerts.error).not.toHaveBeenCalled();
  });

  it('clears an unauthorized URL filter and blocks later manipulated filters', () => {
    Object.assign(queryParams, { locationId: '99', locationType: 'outlet' });
    const fixture = list();
    expect(alerts.error).toHaveBeenCalled();
    expect(fixture.componentInstance.locationId()).toBeNull();
    expect(service.search).toHaveBeenCalledWith(expect.objectContaining({ locationId: undefined }));
    service.search.mockClear();
    fixture.componentInstance.locationType.set('outlet');
    fixture.componentInstance.locationId.set(99);
    fixture.componentInstance.load();
    expect(service.search).not.toHaveBeenCalled();
  });

  it('retains authorized global owner locations', () => {
    access.isBusinessOwner = true;
    access.isGlobalAccess = true;
    access.warehouses.push({ id: 21, name: 'Authorized warehouse', type: 'warehouse' });
    const fixture = create();
    fixture.componentInstance.onLocationTypeChange('warehouse');
    fixture.componentInstance.locationId.set(21);
    fixture.componentInstance.submit();
    expect(service.create).toHaveBeenCalledWith(expect.objectContaining({ locationId: 21, locationType: 'warehouse' }));
  });

  it('blocks a warehouse-only filter when no warehouses are authorized', () => {
    const fixture = list();
    service.search.mockClear();
    fixture.componentInstance.locationType.set('warehouse');
    fixture.componentInstance.load();
    expect(service.search).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalled();
  });

  it('does not render a count or its actions after a forbidden detail URL', () => {
    const fixture = TestBed.createComponent(StockCountDetailsComponent);
    fixture.detectChanges();
    expect(service.getById).toHaveBeenCalledWith(999);
    expect(fixture.componentInstance.stockCount()).toBeNull();
    expect(location.back).toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalled();
  });
});
