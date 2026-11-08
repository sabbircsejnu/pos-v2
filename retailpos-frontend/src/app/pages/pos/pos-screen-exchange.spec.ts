import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { PosScreenComponent } from './pos-screen.component';
import { AuthService } from '../../services/auth.service';
import { SaleService } from '../../services/sale.service';
import { PosService } from '../../services/pos.service';
import { CustomerService } from '../../services/customer.service';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { CurrencyService } from '../../services/currency.service';
import { UomService } from '../../services/uom.service';
import { ConnectivityService } from '../../services/connectivity.service';

describe('PosScreenComponent Exchange entry point', () => {
  const online = signal(true);
  let permissions: string[] = [];
  const activeSession = { id: 1, outletId: 3, terminalId: 7, businessDate: '2026-11-07', openedAt: '2026-11-07T03:00:00Z', serverNowUtc: '2026-11-07T05:00:00Z' };
  const posServiceMock = {
    heldSales: signal([]),
    isLoadingHeld: signal(false),
    loadHeldSales: vi.fn(),
    getTerminals: vi.fn(),
    getSalespersons: vi.fn(() => of({ data: { salespersons: [], defaultSalespersonUserId: null, defaultSource: 'none' } })),
    getActiveCashSession: vi.fn(),
    getPosSettings: vi.fn(() => of({ data: null })),
    cartPrices: vi.fn(() => of({ data: [] })),
    lookup: vi.fn(() => of({ data: null })),
    getCatalogue: vi.fn(() => of({ data: [] })),
    getCategories: vi.fn(() => of({ data: [] })),
    searchCustomers: vi.fn(() => of({ data: [] })),
    stockHint: vi.fn(() => of({ data: {} })),
    reportDiscountAudit: vi.fn(() => of({ success: true })),
    holdSale: vi.fn(),
    recallHeldSale: vi.fn(),
    discardHeldSale: vi.fn()
  };

  async function render(session: unknown) {
    vi.clearAllMocks();
    posServiceMock.getTerminals.mockReturnValue(of({ data: [{ id: 7, name: 'T1', code: 'T1' }] }));
    posServiceMock.getActiveCashSession.mockReturnValue(of({ data: session }));
    await TestBed.configureTestingModule({
      imports: [PosScreenComponent],
      providers: [
        { provide: AuthService, useValue: { getUserValue: () => ({ id: 11, outletId: 3, outletName: 'Main' }), hasPermission: (key: string) => permissions.includes(key) } },
        { provide: SaleService, useValue: { createSale: vi.fn() } },
        { provide: PosService, useValue: posServiceMock },
        { provide: CustomerService, useValue: { create: vi.fn() } },
        { provide: SettingsService, useValue: { getInventorySettings: () => of({ data: {} }), getReceiptSettings: () => of({ data: {} }) } },
        { provide: AlertService, useValue: { success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'error' } },
        { provide: ProductImageService, useValue: { resolveUrl: (p?: string | null) => p || '' } },
        { provide: CurrencyService, useValue: { format: (value: number) => Number(value).toFixed(2) } },
        { provide: HttpClient, useValue: { get: vi.fn(() => of({ data: [] })) } },
        { provide: UomService, useValue: { getVariantConfigs: () => of([]) } },
        { provide: ConnectivityService, useValue: { online } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PosScreenComponent);
    fixture.detectChanges();
    return fixture;
  }

  const button = (root: HTMLElement) => root.querySelector<HTMLButtonElement>('[data-testid="pos-exchange"]');

  beforeEach(() => online.set(true));
  afterEach(() => TestBed.resetTestingModule());

  it('is hidden without pos.exchange.create', async () => {
    permissions = ['sales.create'];
    const fixture = await render(activeSession);
    expect(button(fixture.nativeElement)).toBeNull();
  });

  it('opens the exchange screen with an active session', async () => {
    permissions = ['sales.create', 'pos.exchange.create'];
    const fixture = await render(activeSession);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const exchange = button(fixture.nativeElement)!;
    expect(exchange.disabled).toBe(false);
    exchange.click();
    expect(navigate).toHaveBeenCalledWith(['/pos/exchange']);
  });

  it('is disabled with a reason when no cash session is open', async () => {
    permissions = ['sales.create', 'pos.exchange.create'];
    const fixture = await render(null);
    const exchange = button(fixture.nativeElement)!;
    expect(exchange.disabled).toBe(true);
    expect(exchange.title).toContain('cash session');
  });

  it('is disabled with a reason while offline', async () => {
    permissions = ['sales.create', 'pos.exchange.create'];
    const fixture = await render(activeSession);
    online.set(false);
    fixture.detectChanges();
    const exchange = button(fixture.nativeElement)!;
    expect(exchange.disabled).toBe(true);
    expect(exchange.title).toContain('Offline');
  });
});
