import { Type, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  CurrentStockReportDto,
  ProductLedgerReportDto,
  StockValuationReportDto,
} from '../../models/report.model';
import { AuthService } from '../../services/auth.service';
import { CategoryService } from '../../services/category.service';
import { OutletService } from '../../services/outlet.service';
import { ProductService } from '../../services/product.service';
import { ReportService } from '../../services/report.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { WarehouseService } from '../../services/warehouse.service';
import { CurrentStockReportComponent } from './current-stock-report.component';
import { ProductLedgerReportComponent } from './product-ledger-report.component';
import { StockValuationReportComponent } from './stock-valuation-report.component';

/** U-13: inventory report cost and value need products.view_cost; without it the API sends null. */
describe('Inventory report cost visibility', () => {
  const reportService = {
    currentStockReport: signal<CurrentStockReportDto | null>(null),
    productLedgerReport: signal<ProductLedgerReportDto | null>(null),
    stockValuationReport: signal<StockValuationReportDto | null>(null),
    getCurrentStockReport: () => of(null),
    getStockValuationReport: () => of(null),
    getProductLedgerReport: () => of(null),
  };

  function setUp(permissions: string[]): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (key: string) => permissions.includes('*') || permissions.includes(key) } },
        { provide: ReportService, useValue: reportService },
        { provide: OutletService, useValue: { outlets: signal([]), getAllOutlets: () => of([]) } },
        { provide: WarehouseService, useValue: { warehouses: signal([]), getAllWarehouses: () => of([]) } },
        { provide: CategoryService, useValue: { categories: signal([]), getAllCategories: () => of([]) } },
        { provide: UserOutletAccessService, useValue: { load: () => of(null), authorized: signal(null) } },
        { provide: ProductService, useValue: {} },
        { provide: UomService, useValue: { getVariantConfigs: () => of([]) } },
      ],
    });
  }

  function render<T>(component: Type<T>): ComponentFixture<T> {
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<unknown>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function headers(fixture: ComponentFixture<unknown>): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('th')).map(th => th.textContent?.trim() ?? '');
  }

  afterEach(() => {
    reportService.currentStockReport.set(null);
    reportService.productLedgerReport.set(null);
    reportService.stockValuationReport.set(null);
    TestBed.resetTestingModule();
  });

  // ── Current stock ─────────────────────────────────────────────────────────
  function currentStock(cost: { unit: number | null; value: number | null; total: number | null }): CurrentStockReportDto {
    return {
      items: [{
        variantId: 1, productCode: 'P-1', productName: 'Attar', category: 'Perfume', locationId: 1,
        locationType: 'outlet', locationName: 'Main', availableQuantity: 488, reservedQuantity: 0, reorderLevel: 0,
        unitCost: cost.unit, stockValue: cost.value, stockStatus: 'in-stock',
      }],
      totalCount: 1,
      summary: { totalProducts: 1, totalQuantity: 488, totalStockValue: cost.total },
    };
  }

  it('current stock: hides unit cost, stock value and the value card without products.view_cost', () => {
    setUp(['reports.inventory']);
    reportService.currentStockReport.set(currentStock({ unit: null, value: null, total: null }));
    const fixture = render(CurrentStockReportComponent);

    expect(fixture.componentInstance.canViewCost()).toBe(false);
    expect(headers(fixture).some(h => h.includes('Unit Cost') || h.includes('Stock Value'))).toBe(false);
    expect(fixture.componentInstance.columns().some(c => c.key === 'unitCost' || c.key === 'stockValue')).toBe(false);
    expect(text(fixture)).not.toContain('Total Inventory Value');
    expect(text(fixture)).not.toContain('NaN');
    expect(text(fixture)).not.toContain('0.00');
    expect(text(fixture)).toContain('488');
  });

  it('current stock: shows the cost columns and total with products.view_cost', () => {
    setUp(['reports.inventory', 'products.view_cost']);
    reportService.currentStockReport.set(currentStock({ unit: 2, value: 976, total: 976 }));
    const fixture = render(CurrentStockReportComponent);

    expect(headers(fixture).some(h => h.includes('Unit Cost'))).toBe(true);
    expect(headers(fixture).some(h => h.includes('Stock Value'))).toBe(true);
    expect(text(fixture)).toContain('Total Inventory Value');
    expect(text(fixture)).toContain('976.00');
  });

  it('current stock: renders a missing cost as a dash, never as zero', () => {
    setUp(['reports.inventory', 'products.view_cost']);
    const fixture = render(CurrentStockReportComponent);

    expect(fixture.componentInstance.formatCurrency(null)).toBe('—');
    expect(fixture.componentInstance.formatCurrency(undefined)).toBe('—');
  });

  // ── Product ledger ────────────────────────────────────────────────────────
  function ledger(cost: { unit: number | null; value: number | null; total: number | null }): ProductLedgerReportDto {
    return {
      productId: 7, productName: 'Shirt',
      items: [{
        ledgerId: 1, transactionDate: '2026-11-05T10:00:00Z', transactionType: 'grn', transactionTypeLabel: 'Purchase (GRN)',
        referenceNumber: 'GRN-24', variantId: 7, productName: 'Shirt', sku: 'SHIRT-1', locationId: 1, locationName: 'Main',
        openingQuantity: 0, stockIn: 10, stockOut: 0, closingQuantity: 10, unitCost: cost.unit, transactionValue: cost.value,
      }],
      totalCount: 1,
      summary: { openingStock: 0, totalStockIn: 10, totalStockOut: 0, closingStock: 10, totalTransactionValue: cost.total },
    };
  }

  function renderLedger(): ComponentFixture<ProductLedgerReportComponent> {
    const fixture = TestBed.createComponent(ProductLedgerReportComponent);
    fixture.componentInstance.selectedProduct.set({ id: 7, name: 'Shirt', variants: [] });
    fixture.detectChanges();
    return fixture;
  }

  it('product ledger: hides unit cost, transaction value and the value totals without products.view_cost', () => {
    setUp(['reports.inventory']);
    reportService.productLedgerReport.set(ledger({ unit: null, value: null, total: null }));
    const fixture = renderLedger();

    expect(headers(fixture)).not.toContain('Unit Cost');
    expect(headers(fixture)).not.toContain('Tx Value');
    expect(text(fixture)).not.toContain('Total Transaction Value');
    expect(text(fixture)).not.toContain('NaN');
    expect(text(fixture)).not.toContain('0.00');
    expect(text(fixture)).toContain('GRN-24');
    expect(headers(fixture)).toContain('Closing Qty');
  });

  it('product ledger: shows unit cost and transaction value with products.view_cost', () => {
    setUp(['reports.inventory', 'products.view_cost']);
    reportService.productLedgerReport.set(ledger({ unit: 41.37, value: 413.7, total: 413.7 }));
    const fixture = renderLedger();

    expect(headers(fixture)).toContain('Unit Cost');
    expect(headers(fixture)).toContain('Tx Value');
    expect(text(fixture)).toContain('Total Transaction Value');
    expect(text(fixture)).toContain('41.37');
    expect(fixture.componentInstance.formatCurrency(null)).toBe('—');
  });

  // ── Stock valuation ───────────────────────────────────────────────────────
  function valuation(withCost: boolean): StockValuationReportDto {
    return {
      items: [{
        variantId: 1, productCode: 'P-1', barcode: '', sku: 'ATR-1', productName: 'Attar', categoryName: 'Perfume',
        locationId: 1, locationType: 'outlet', locationName: 'Main', quantity: 488,
        unitCost: withCost ? 2 : null, inventoryValue: withCost ? 976 : null, percentOfTotal: withCost ? 100 : null,
      }],
      totalCount: 1,
      summary: {
        totalProducts: 1, totalQuantity: 0, totalInventoryValue: withCost ? 976 : null,
        byCategory: [{ categoryName: 'Perfume', quantity: 488, inventoryValue: withCost ? 976 : null, percentOfTotal: withCost ? 100 : null }],
      },
    };
  }

  it('stock valuation: keeps quantities but hides value, unit cost, share and the value sort without products.view_cost', () => {
    setUp(['reports.inventory']);
    reportService.stockValuationReport.set(valuation(false));
    const fixture = render(StockValuationReportComponent);
    const page = fixture.componentInstance;

    expect(page.sortBy()).toBe('productCode');
    expect(headers(fixture).some(h => h.includes('Unit Cost') || h.includes('Inventory Value') || h.includes('% of Total'))).toBe(false);
    expect(text(fixture)).not.toContain('Total Inventory Value');
    expect(text(fixture)).not.toContain('NaN');
    expect(text(fixture)).not.toContain('0.00');
    expect(text(fixture)).toContain('488');

    page.showCategoryPanel.set(true);
    fixture.detectChanges();
    expect(text(fixture)).toContain('Quantity by Category');
    expect(text(fixture)).not.toContain('Value by Category');
    expect(page.valueBarWidth(null)).toBe('0%');
  });

  it('stock valuation: shows value columns, total and the value sort with products.view_cost', () => {
    setUp(['*']);
    reportService.stockValuationReport.set(valuation(true));
    const fixture = render(StockValuationReportComponent);

    expect(fixture.componentInstance.sortBy()).toBe('inventoryValue');
    expect(headers(fixture).some(h => h.includes('Unit Cost'))).toBe(true);
    expect(headers(fixture).some(h => h.includes('Inventory Value'))).toBe(true);
    expect(text(fixture)).toContain('Total Inventory Value');
    expect(text(fixture)).toContain('976.00');
  });
});
