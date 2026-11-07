import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StockTransferDto } from '../../models/stock-transfer.model';
import { VariantUomConfig } from '../../models/uom.model';
import { UomService } from '../../services/uom.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { TransferDetailsComponent } from './transfer-details.component';

function transfer(overrides: Partial<StockTransferDto> = {}): StockTransferDto {
  return {
    id: 7,
    transferType: 'direct',
    fromLocationId: 1,
    fromLocationType: 'warehouse',
    fromLocationName: 'Central',
    toLocationId: 3,
    toLocationType: 'outlet',
    toLocationName: 'Basundhara 1',
    transferDate: '2026-11-05',
    status: 'in_transit',
    createdAt: '2026-11-05T10:00:00Z',
    items: [{
      id: 1,
      variantId: 25,
      variantSku: 'SKU-25',
      productName: 'Borka',
      quantity: 10,
      requestedQuantity: 10,
      transferQuantity: 10,
      acceptedQuantity: 0,
      rejectedQuantity: 0,
      missingQuantity: 0,
      damagedQuantity: 0,
      inTransitQuantity: 10,
      returnTransitQuantity: 0,
      unitCost: 5
    }],
    ...overrides
  };
}

describe('Receiving a stock transfer', () => {
  const alerts = { success: vi.fn(), error: vi.fn(), confirm: vi.fn() };
  const service = { getById: vi.fn(), receive: vi.fn(), reject: vi.fn() };

  function createPage(dto: StockTransferDto): TransferDetailsComponent {
    service.getById.mockReturnValue(of({ data: dto }));
    service.receive.mockReturnValue(of({ data: dto }));
    const page = TestBed.createComponent(TransferDetailsComponent).componentInstance;
    page.ngOnInit();
    page.receive();
    return page;
  }

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: StockTransferService, useValue: service },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: null }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '7' } } } }
      ]
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('keeps every row reconciled while quantities are edited', () => {
    const page = createPage(transfer());

    page.updateReceiveQuantity(0, 'acceptedQuantity', 6);
    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 6, rejectedQuantity: 4 });

    page.updateReceiveQuantity(0, 'missingQuantity', 1);
    page.updateReceiveQuantity(0, 'damagedQuantity', 1);
    expect(page.receiveItems()[0]).toMatchObject({
      acceptedQuantity: 4, rejectedQuantity: 4, missingQuantity: 1, damagedQuantity: 1
    });
  });

  it('never lets a quantity exceed what was dispatched', () => {
    const page = createPage(transfer());

    page.updateReceiveQuantity(0, 'rejectedQuantity', 4);
    page.updateReceiveQuantity(0, 'missingQuantity', 99);

    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 0, rejectedQuantity: 4, missingQuantity: 6 });
  });

  it('requires a reason for missing or damaged units before submitting', () => {
    const page = createPage(transfer());
    page.updateReceiveQuantity(0, 'damagedQuantity', 1);

    page.submitReceive();

    expect(alerts.error).toHaveBeenCalled();
    expect(service.receive).not.toHaveBeenCalled();
  });

  it('sends every quantity to the server', () => {
    const page = createPage(transfer());
    page.updateReceiveQuantity(0, 'acceptedQuantity', 7);
    page.updateReceiveQuantity(0, 'missingQuantity', 1);
    page.updateReceiveRemarks(0, 'Lost in transit');

    page.submitReceive();

    expect(service.receive).toHaveBeenCalledWith(7, expect.objectContaining({
      items: [expect.objectContaining({
        variantId: 25,
        acceptedQuantity: 6,
        rejectedQuantity: 3,
        missingQuantity: 1,
        damagedQuantity: 0,
        remarks: 'Lost in transit'
      })]
    }));
  });

  it('records units short on a system return as missing, because it cannot be rejected again', () => {
    const page = createPage(transfer({ originalTransferId: 3, transferType: 'return' }));

    page.updateReceiveQuantity(0, 'acceptedQuantity', 8);

    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 8, rejectedQuantity: 0, missingQuantity: 2 });
    expect(page.canRejectAction()).toBe(false);
  });
});

describe('Receiving a stock transfer with units', () => {
  const alerts = { success: vi.fn(), error: vi.fn(), confirm: vi.fn() };
  const service = { getById: vi.fn(), receive: vi.fn(), reject: vi.fn() };
  const getVariantConfigs = vi.fn();

  const config = (tracked: boolean): VariantUomConfig => ({
    configId: 40,
    version: 1,
    variantId: 25,
    baseUnit: { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume', precision: 2, isWhole: false },
    packStateTrackingEnabled: tracked,
    repackingEnabled: false,
    purchaseOptions: [],
    sellingOptions: []
  });

  function rendered(dto: StockTransferDto) {
    service.getById.mockReturnValue(of({ data: dto }));
    service.receive.mockReturnValue(of({ data: dto }));
    const fixture = TestBed.createComponent(TransferDetailsComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    fixture.componentInstance.receive();
    fixture.detectChanges();
    return fixture;
  }

  const fractional = (overrides: Partial<StockTransferDto['items'][number]> = {}) => {
    const base = transfer();
    return { ...base, items: [{ ...base.items[0], quantity: 2.5, requestedQuantity: 2.5, transferQuantity: 2.5, inTransitQuantity: 2.5, ...overrides }] };
  };

  beforeEach(() => {
    vi.clearAllMocks();
    getVariantConfigs.mockReturnValue(of([config(false)]));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: StockTransferService, useValue: service },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: null }) } },
        { provide: UomService, useValue: { getVariantConfigs } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '7' } } } }
      ]
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('splits a fractional line to the precision of its unit and keeps it reconciled', () => {
    const page = rendered(fractional()).componentInstance;

    page.updateReceiveQuantity(0, 'acceptedQuantity', 1.1);
    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 1.1, rejectedQuantity: 1.4 });

    page.updateReceiveQuantity(0, 'acceptedQuantity', 1.239);
    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 1.23, rejectedQuantity: 1.27 });

    page.submitReceive();
    expect(alerts.error).not.toHaveBeenCalled();
    expect(service.receive).toHaveBeenCalled();
  });

  it('keeps plain pcs whole', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const page = rendered(transfer()).componentInstance;

    page.updateReceiveQuantity(0, 'acceptedQuantity', 6.7);

    expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 6, rejectedQuantity: 4 });
    expect(page.receiveStep(page.receiveItems()[0])).toBe('1');
  });

  it('labels quantities and never adds unlike units', () => {
    const base = transfer();
    getVariantConfigs.mockReturnValue(of([config(false)]));
    const fixture = rendered({ ...base, items: [base.items[0], { ...base.items[0], id: 2, variantId: 26, quantity: 3, transferQuantity: 3 }] });

    expect(fixture.componentInstance.transferTotalLabel()).toBe('10 ml · 3 pcs');
    expect(fixture.nativeElement.textContent).toContain('10 ml');
  });

  it('keeps the totals as plain numbers for pcs', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const page = rendered(transfer()).componentInstance;

    expect(page.transferTotalLabel()).toBe('10');
    expect(page.getSummaryFields().find(field => field.label === 'Total Transfer Quantity')?.value).toBe('10');
  });

  describe('a line with sealed or prepared packs', () => {
    const sealed = { state: 'sealed' as const, packCode: 'JAR', baseQtyPerPack: 5, packCount: 2, baseQuantity: 10 };

    it('can only be accepted whole or rejected whole', () => {
      const page = rendered(transfer({ items: [{ ...transfer().items[0], allocations: [sealed] }] })).componentInstance;
      expect(page.receiveItems()[0].wholeLineOnly).toBe(true);

      page.updateReceiveQuantity(0, 'acceptedQuantity', 5);
      page.submitReceive();
      expect(alerts.error).toHaveBeenCalledWith('A line with sealed or prepared packs must be accepted whole or rejected whole');
      expect(service.receive).not.toHaveBeenCalled();

      page.setWholeLine(0, 'reject');
      expect(page.receiveItems()[0]).toMatchObject({ acceptedQuantity: 0, rejectedQuantity: 10, missingQuantity: 0, damagedQuantity: 0 });
      page.submitReceive();
      expect(service.receive).toHaveBeenCalledTimes(1);
    });

    it('disables partial quantities', async () => {
      const fixture = rendered(transfer({ items: [{ ...transfer().items[0], allocations: [sealed] }] }));
      await fixture.whenStable();
      fixture.detectChanges();
      const inputs: HTMLInputElement[] = Array.from(fixture.nativeElement.querySelectorAll('.max-h-\\[58vh\\] input[type="number"]'));

      expect(inputs.length).toBeGreaterThan(0);
      expect(inputs.every(input => input.disabled)).toBe(true);
    });

    it('offers only acceptance on a system return, which cannot be rejected again', () => {
      const fixture = rendered(transfer({ originalTransferId: 3, transferType: 'return', items: [{ ...transfer().items[0], allocations: [sealed] }] }));
      const text = fixture.nativeElement.textContent as string;

      expect(text).toContain('Accept whole line');
      expect(text).not.toContain('Reject whole line');
    });
  });

  it('lets a loose-only line of a pack-tracked item be received in part', () => {
    getVariantConfigs.mockReturnValue(of([config(true)]));
    const loose = { state: 'loose' as const, packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 10 };
    const fixture = rendered(transfer({ items: [{ ...transfer().items[0], allocations: [loose] }] }));
    const page = fixture.componentInstance;

    expect(page.receiveItems()[0].wholeLineOnly).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain('Accept whole line');

    page.updateReceiveQuantity(0, 'acceptedQuantity', 4);
    page.submitReceive();

    expect(service.receive).toHaveBeenCalled();
  });

  it('shows the stock forms and the entered unit of a line', () => {
    const allocations = [
      { state: 'sealed' as const, packCode: 'JAR', baseQtyPerPack: 4, packCount: 2, baseQuantity: 8 },
      { state: 'loose' as const, packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 2 }
    ];
    const fixture = rendered(transfer({ items: [{ ...transfer().items[0], allocations, uomLabel: 'Litre', uomUnitCode: 'L', uomEnteredQuantity: 0.01 }] }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Sealed JAR: 2 packs (8 ml) · Loose: 2 ml');
    expect(text).toContain('Entered 0.01 Litre');
  });

  it('adds nothing to a plain pcs line', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const text = rendered(transfer()).nativeElement.textContent as string;

    expect(text).not.toContain('Entered');
    expect(text).not.toContain('Loose');
  });

  it('keeps the input and shows the API message when the server refuses', () => {
    const fixture = rendered(fractional());
    const page = fixture.componentInstance;
    service.receive.mockReturnValue(throwError(() => new Error('Accept or reject the whole line.')));
    page.updateReceiveQuantity(0, 'acceptedQuantity', 1);

    page.submitReceive();

    expect(alerts.error).toHaveBeenCalled();
    expect(page.showReceiveModal()).toBe(true);
    expect(page.isReceiving()).toBe(false);
    expect(page.receiveItems()[0].acceptedQuantity).toBe(1);
  });

  it('does not submit twice', () => {
    const page = rendered(fractional()).componentInstance;
    page.isReceiving.set(true);

    page.submitReceive();

    expect(service.receive).not.toHaveBeenCalled();
  });
});
