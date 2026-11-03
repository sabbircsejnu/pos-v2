import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthorizedOutletsDto } from '../../models/report.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { SupplierService } from '../../services/supplier.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { VariationService } from '../../services/variation.service';
import { PoFormComponent } from './po-form.component';

const access = (over: Partial<AuthorizedOutletsDto> = {}): AuthorizedOutletsDto => ({
  outlets: [],
  warehouses: [],
  isBusinessOwner: false,
  accessScope: 'all_locations',
  defaultLocationId: null,
  defaultLocationType: null,
  ...over
});

interface SetupOptions {
  access: AuthorizedOutletsDto;
  routeId?: string;
  purchaseOrder?: Record<string, unknown>;
  accessError?: Error;
}

describe('PoFormComponent destination location', () => {
  let fixture: ComponentFixture<PoFormComponent>;
  let component: PoFormComponent;
  let poService: { create: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn>; getById: ReturnType<typeof vi.fn>; submit: ReturnType<typeof vi.fn>; purchaseAndReceive: ReturnType<typeof vi.fn> };
  let alerts: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn>; warning: ReturnType<typeof vi.fn>; confirm: ReturnType<typeof vi.fn> };
  let loadLocations: ReturnType<typeof vi.fn>;

  const root = () => fixture.nativeElement as HTMLElement;
  const select = (name: string) => root().querySelector(`select[name="${name}"]`) as HTMLSelectElement;
  const optionLabels = (name: string) =>
    Array.from(select(name).options).map(option => option.textContent!.replace(/\s+/g, ' ').trim());

  async function setup(options: SetupOptions): Promise<void> {
    vi.useFakeTimers();
    poService = {
      create: vi.fn().mockReturnValue(of({ data: { id: 7 } })),
      update: vi.fn().mockReturnValue(of({ data: { id: 7 } })),
      getById: vi.fn().mockReturnValue(of({ data: options.purchaseOrder })),
      submit: vi.fn().mockReturnValue(of({})),
      purchaseAndReceive: vi.fn().mockReturnValue(of({ data: { purchaseOrder: { id: 7 }, grnId: 9 } }))
    };
    alerts = {
      error: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
      confirm: vi.fn((_message: string, onConfirm: () => void) => onConfirm())
    };
    loadLocations = vi.fn().mockReturnValue(options.accessError
      ? throwError(() => options.accessError)
      : of({ data: options.access }));

    await TestBed.configureTestingModule({
      imports: [PoFormComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => options.routeId ?? null } } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: PurchaseOrderService, useValue: poService },
        { provide: SupplierService, useValue: { getAll: () => of({ data: [] }) } },
        { provide: ProductService, useValue: { searchVariants: () => of({ data: [] }) } },
        { provide: VariationService, useValue: { getAllVariations: () => of([]) } },
        { provide: UserOutletAccessService, useValue: { load: loadLocations } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: unknown) => String(error) } },
        { provide: CurrencyService, useValue: { format: (value: number) => `${value}` } },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: ProductImageService, useValue: { resolveUrl: (path?: string | null) => path ?? '' } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PoFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('defaults an Outlet-only business to its only outlet and disables the warehouse type', async () => {
    await setup({ access: access({ outlets: [{ id: 12, name: 'Main Outlet', type: 'outlet' }] }) });

    expect(component.locationType()).toBe('outlet');
    expect(component.locationId()).toBe(12);
    const warehouseOption = Array.from(select('locationType').options).find(o => o.textContent!.includes('Warehouse'))!;
    expect(warehouseOption.disabled).toBe(true);
    expect(warehouseOption.textContent).toContain('none available');
    expect(optionLabels('locationId')).toContain('Main Outlet');
    expect(root().textContent).toContain('Location Type');
  });

  it('filters the location dropdown by the selected type in a mixed business', async () => {
    await setup({
      access: access({
        outlets: [
          { id: 12, name: 'Main Outlet', type: 'outlet' },
          { id: 13, name: 'Mall Outlet', type: 'outlet' }
        ],
        warehouses: [{ id: 21, name: 'Central Warehouse', type: 'warehouse' }],
        defaultLocationType: 'warehouse',
        defaultLocationId: 21
      })
    });

    expect(component.locationType()).toBe('warehouse');
    expect(component.locationId()).toBe(21);
    expect(optionLabels('locationId')).toEqual(['Select warehouse', 'Central Warehouse']);

    component.onLocationTypeChange('outlet');
    fixture.detectChanges();

    expect(component.locationId()).toBeNull();
    expect(optionLabels('locationId')).toEqual(['Select outlet', 'Main Outlet', 'Mall Outlet']);
  });

  it('enables a Warehouse-only business without requiring an outlet assignment', async () => {
    await setup({
      access: access({
        warehouses: [{ id: 21, name: 'Central Warehouse', type: 'warehouse' }],
        isBusinessOwner: true,
        isGlobalAccess: true
      })
    });

    expect(component.locationType()).toBe('warehouse');
    expect(component.locationId()).toBe(21);
    expect(select('locationType').disabled).toBe(false);
    expect(select('locationId').disabled).toBe(false);
    expect(optionLabels('locationId')).toContain('Central Warehouse');
  });

  it('shows the no-locations hint only for a successful empty list', async () => {
    await setup({ access: access() });

    expect(select('locationType').disabled).toBe(true);
    expect(select('locationId').disabled).toBe(true);
    expect(root().textContent).toContain('No active outlets or warehouses');
    expect(root().querySelector('app-ui-state[kind="error"]')).toBeNull();
  });

  it('shows API errors instead of an empty-list hint and retries loading locations', async () => {
    await setup({ access: access(), accessError: new Error('Location service unavailable') });

    expect(root().textContent).toContain('Unable to load locations');
    expect(root().textContent).toContain('Location service unavailable');
    expect(root().textContent).not.toContain('No active outlets or warehouses');
    expect(select('locationType').disabled).toBe(true);
    expect(select('locationId').disabled).toBe(true);
    component.save(false);
    expect(alerts.error).toHaveBeenCalledWith('Error: Location service unavailable');
    expect(poService.create).not.toHaveBeenCalled();

    loadLocations.mockReturnValue(of({ data: access({ outlets: [{ id: 12, name: 'Main Outlet', type: 'outlet' }] }) }));
    const retry = Array.from(root().querySelectorAll('button'))
      .find(button => button.textContent?.includes('Retry locations'))!;
    retry.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(loadLocations).toHaveBeenCalledTimes(2);
    expect(component.locationsError()).toBe('');
    expect(select('locationType').disabled).toBe(false);
    expect(select('locationId').disabled).toBe(false);
    expect(component.locationId()).toBe(12);
    expect(root().textContent).not.toContain('Unable to load locations');
  });

  it('requires a destination before saving', async () => {
    await setup({
      access: access({
        outlets: [{ id: 12, name: 'Main Outlet', type: 'outlet' }],
        warehouses: [{ id: 21, name: 'Central Warehouse', type: 'warehouse' }]
      })
    });
    component.supplierId.set(1);
    component.items.set([{ variantId: 5, quantity: 1, unitPrice: 10, discount: 0, tax: 0 }]);

    component.save(false);

    expect(alerts.error).toHaveBeenCalledWith('Please select a location type');
    expect(poService.create).not.toHaveBeenCalled();
  });

  it('sends locationType and locationId for Purchase & Receive', async () => {
    await setup({ access: access({ outlets: [{ id: 12, name: 'Main Outlet', type: 'outlet' }] }) });
    component.supplierId.set(1);
    component.items.set([{ variantId: 5, quantity: 2, unitPrice: 10, discount: 0, tax: 0 }]);

    component.purchaseAndReceive();

    expect(alerts.confirm.mock.calls[0][0]).toContain('outlet "Main Outlet"');
    const request = poService.purchaseAndReceive.mock.calls[0][0];
    expect(request.locationType).toBe('outlet');
    expect(request.locationId).toBe(12);
    expect(request.warehouseId).toBeUndefined();
    expect(request.idempotencyKey).toBeTruthy();
  });

  it('keeps an existing order destination selected while editing', async () => {
    await setup({
      routeId: '7',
      access: access({ warehouses: [{ id: 21, name: 'Central Warehouse', type: 'warehouse' }] }),
      purchaseOrder: {
        id: 7,
        supplierId: 1,
        locationType: 'outlet',
        locationId: 44,
        locationName: 'Legacy Outlet',
        orderDate: '2026-10-04T00:00:00Z',
        items: []
      }
    });

    expect(component.locationType()).toBe('outlet');
    expect(component.locationId()).toBe(44);
    expect(optionLabels('locationId')).toContain('Legacy Outlet');
  });
});
