import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { VariantUomConfig } from '../../models/uom.model';
import { AccountingService } from '../../services/accounting.service';
import { ReportService } from '../../services/report.service';
import { UomService } from '../../services/uom.service';
import { ValuationReportsComponent } from '../accounting/valuation-reports.component';
import { CurrentStockReportComponent } from './current-stock-report.component';
import { LowStockReportComponent } from './low-stock-report.component';
import { OutletWiseStockReportComponent } from './outlet-wise-stock-report.component';

const ml: VariantUomConfig = {
  configId: 40,
  version: 1,
  variantId: 2,
  baseUnit: { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume', precision: 2, isWhole: false },
  packStateTrackingEnabled: false,
  repackingEnabled: false,
  purchaseOptions: [],
  sellingOptions: []
};

describe('Report quantity labels', () => {
  const getVariantConfigs = vi.fn();

  beforeEach(() => {
    getVariantConfigs.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: UomService, useValue: { getVariantConfigs } }
      ]
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  function outletWise() {
    const fixture = TestBed.createComponent(OutletWiseStockReportComponent);
    const row = (variantId: number, quantity: number) => ({
      outletId: 1, outletName: 'A', variantId, sku: 'S', productName: 'P', productCode: 'C', category: 'X', quantity, unitCost: 1, stockValue: 1
    });
    TestBed.inject(ReportService).outletWiseStockReport.set({
      items: [row(1, 3), row(2, 1250.5)],
      totalCount: 2,
      summary: { totalOutlets: 1, totalSkus: 2, totalQuantity: 3, totalStockValue: 2, byOutlet: [] }
    } as never);
    return fixture;
  }

  it('keeps a pcs-only page exactly as it was', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const fixture = outletWise();
    fixture.detectChanges();
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Total Quantity');
    expect(text).not.toContain('Total (pcs)');
    expect(text).not.toContain(' pcs');
  });

  it('labels a pcs-only total and each row once other units are on the page', () => {
    getVariantConfigs.mockReturnValue(of([ml]));
    const fixture = outletWise();
    fixture.detectChanges();
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Total (pcs)');
    expect(text).toContain('3 pcs');
    expect(text).toContain('1,250.5 ml');
  });

  function currentStock() {
    const fixture = TestBed.createComponent(CurrentStockReportComponent);
    const row = (variantId: number) => ({
      variantId, productCode: 'C', productName: 'P', category: 'X', locationId: 1, locationType: 'outlet', locationName: 'A',
      availableQuantity: 5, reservedQuantity: 0, reorderLevel: 0, unitCost: 1, stockValue: 5, stockStatus: 'in-stock'
    });
    TestBed.inject(ReportService).currentStockReport.set({
      items: [row(1), row(2)],
      totalCount: 2,
      summary: { totalProducts: 2, totalQuantity: 5, totalStockValue: 10 }
    } as never);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.nativeElement.textContent as string;
  }

  function lowStock() {
    const fixture = TestBed.createComponent(LowStockReportComponent);
    const row = (variantId: number) => ({
      variantId, productName: 'P', productCode: 'C', sku: 'S', locationName: 'A', locationType: 'outlet',
      currentStock: 1, reorderLevel: 5, suggestedOrderQty: 4, urgencyLevel: 'Low'
    });
    TestBed.inject(ReportService).lowStockReport.set({
      items: [row(1), row(2)],
      totalCount: 2,
      summary: { totalSkus: 2, totalDeficitQty: 4, criticalCount: 0, lowCount: 2 }
    } as never);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.nativeElement.textContent as string;
  }

  it('labels the current stock total as pcs once other units are on the page', () => {
    getVariantConfigs.mockReturnValue(of([ml]));
    const text = currentStock();

    expect(text).toContain('Total (pcs)');
    expect(text).toContain('5 pcs');
    expect(text).toContain('5 ml');
    expect(text).not.toContain('Mixed units');
  });

  it('keeps the current stock wording for pcs', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const text = currentStock();

    expect(text).toContain('Total Quantity');
    expect(text).not.toContain('Total (pcs)');
  });

  it('labels the low stock total as pcs once other units are on the page', () => {
    getVariantConfigs.mockReturnValue(of([ml]));
    const text = lowStock();

    expect(text).toContain('Total (pcs)');
    expect(text).toContain('4 pcs');
    expect(text).not.toContain('Mixed units');
  });

  it('keeps the low stock wording for pcs', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const text = lowStock();

    expect(text).toContain('Total Deficit Quantity');
    expect(text).not.toContain('Total (pcs)');
  });
});

describe('Inventory valuation capitalized packaging', () => {
  const report = (capitalizedPackagingValue: number) => ({
    data: {
      rows: [], unvaluedMovements: [], totalQuantity: 10, totalInventoryValue: 500, generalLedgerBalance: 500,
      reconciliationDifference: 0, asOfDate: '2026-11-07', capitalizedPackagingValue
    }
  });

  function page(capitalized: number): string {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { valuationMode: 'inventory' } } } },
        { provide: AccountingService, useValue: { getInventoryValuationReport: () => of(report(capitalized)) } }
      ]
    });
    const fixture = TestBed.createComponent(ValuationReportsComponent);
    fixture.detectChanges();
    return fixture.nativeElement.textContent as string;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the value and says it is already included', () => {
    const text = page(160);

    expect(text).toContain('Capitalized packaging value');
    expect(text).toContain('160');
    expect(text).toContain('already included');
  });

  it('stays hidden when there is none', () => {
    expect(page(0)).not.toContain('Capitalized packaging value');
  });
});
