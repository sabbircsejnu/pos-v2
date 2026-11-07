import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PurchaseOrder } from '../../models/purchase-order.model';
import { VariantUomConfig } from '../../models/uom.model';
import { UomService } from '../../services/uom.service';
import { PoListComponent } from './po-list.component';

const config = (variantId: number, code: string): VariantUomConfig => ({
  configId: variantId * 10,
  version: 1,
  variantId,
  baseUnit: { id: 1, code, symbol: code, dimension: 'volume', precision: 2, isWhole: false },
  packStateTrackingEnabled: false,
  repackingEnabled: false,
  purchaseOptions: [],
  sellingOptions: []
});

const order = (variantIds: number[], totalQuantity: number): PurchaseOrder => ({
  items: variantIds.map(variantId => ({ variantId })),
  totalItems: variantIds.length,
  totalQuantity
} as unknown as PurchaseOrder);

describe('Purchase order list quantities', () => {
  const getVariantConfigs = vi.fn();

  function page(variantIds: number[]): PoListComponent {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: UomService, useValue: { getVariantConfigs } }]
    });
    const component = TestBed.createComponent(PoListComponent).componentInstance;
    component.units.load(variantIds);
    return component;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('keeps the wording for pcs', () => {
    getVariantConfigs.mockReturnValue(of([]));

    expect(page([1, 2]).itemsSummary(order([1, 2], 12))).toBe('2 (12 units)');
  });

  it('names the unit when every line shares one', () => {
    getVariantConfigs.mockReturnValue(of([config(1, 'ml'), config(2, 'ml')]));

    expect(page([1, 2]).itemsSummary(order([1, 2], 1500))).toBe('2 (1,500 ml)');
  });

  it('shows only the line count when the units differ', () => {
    getVariantConfigs.mockReturnValue(of([config(2, 'ml')]));

    expect(page([1, 2]).itemsSummary(order([1, 2], 1503))).toBe('2 lines');
  });

  it('shows only the line count when the units are unknown', () => {
    getVariantConfigs.mockReturnValue(throwError(() => new Error('403')));

    expect(page([1]).itemsSummary(order([1], 3))).toBe('1 line');
  });
});
