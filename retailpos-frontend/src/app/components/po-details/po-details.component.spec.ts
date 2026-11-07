import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AlertService } from '../../services/alert.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { UomService } from '../../services/uom.service';
import { PoDetailsComponent } from './po-details.component';

const order = (items: unknown[]) => ({
  id: 7, poNumber: 'PO-7', status: 'draft', supplierName: 'S', locationType: 'outlet', locationName: 'Main', orderDate: '2026-01-01T00:00:00Z',
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', totalAmount: 600, totalItems: items.length, totalQuantity: 300, items,
});

describe('PoDetailsComponent units', () => {
  const uom = { getVariantConfigs: vi.fn() };

  async function render(items: unknown[]): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [PoDetailsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '7' } } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: PurchaseOrderService, useValue: { getById: () => of({ data: order(items) }) } },
        { provide: AlertService, useValue: { error: vi.fn(), success: vi.fn(), confirm: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'error' } },
        { provide: CurrencyService, useValue: { format: (value: number) => `$${value}` } },
        { provide: UomService, useValue: uom },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PoDetailsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    uom.getVariantConfigs.mockReset();
    uom.getVariantConfigs.mockImplementation((ids: number[]) => of(ids.length ? [{ variantId: 5, baseUnit: { symbol: 'ml' } }] : []));
  });

  afterEach(() => TestBed.resetTestingModule());

  it('shows the entered unit, its price and the base equivalent', async () => {
    const root = await render([{
      id: 1, variantId: 5, productName: 'Attar', variantName: '', quantity: 300, unitPrice: 2, discount: 0, tax: 0, totalPrice: 600,
      uomUnitCode: 'BOX', uomLabel: 'Box of 100', uomEnteredQuantity: 3, uomEnteredUnitPrice: 200, uomOptionId: 30,
    }]);

    const row = root.querySelector('tbody tr')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(row).toContain('3 = 300 ml');
    expect(row).toContain('Box of 100');
    expect(row).toContain('$200 per Box of 100');
    expect(row).toContain('$600');
  });

  it('leaves a pcs line as it was and asks for no units', async () => {
    const root = await render([{
      id: 1, variantId: 6, productName: 'Shirt', variantName: '', quantity: 3, unit: 'pcs', unitPrice: 50, discount: 0, tax: 0, totalPrice: 150,
    }]);

    const row = root.querySelector('tbody tr')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(row).toContain('3 pcs $50 $150');
    expect(uom.getVariantConfigs).toHaveBeenCalledWith([]);
  });
});
