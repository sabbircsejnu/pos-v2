import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { StockTransferDto } from '../../models/stock-transfer.model';
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
