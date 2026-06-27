import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  SalesReportDto, TopProductDto, SalesByOutletDto, SalesByPaymentMethodDto,
  DailySalesTrendDto, StockLevelDto, InventoryValuationDto, SlowMovingItemDto,
  PurchaseSummaryDto, PurchaseBySupplierDto, StockTransactionReportDto,
  CurrentStockReportDto, ProductLedgerReportDto, StockMovementReportDto,
  StockValuationReportDto, OutletWiseStockReportDto,
  LowStockReportDto, OutOfStockReportDto, NegativeStockReportDto
} from '../models/report.model';

@Injectable({ providedIn: 'root' })
export class ReportService {
  private reportsUrl = `${environment.apiUrl}/reports`;

  isLoading = signal(false);

  salesSummary = signal<SalesReportDto | null>(null);
  topProducts = signal<TopProductDto[]>([]);
  salesByOutlet = signal<SalesByOutletDto[]>([]);
  salesByPaymentMethod = signal<SalesByPaymentMethodDto[]>([]);
  dailyTrend = signal<DailySalesTrendDto[]>([]);

  stockLevels = signal<StockLevelDto[]>([]);
  inventoryValuation = signal<InventoryValuationDto | null>(null);
  slowMovingItems = signal<SlowMovingItemDto[]>([]);

  purchaseSummary = signal<PurchaseSummaryDto | null>(null);
  purchaseBySupplier = signal<PurchaseBySupplierDto[]>([]);

  stockTransactionReport = signal<StockTransactionReportDto | null>(null);

  currentStockReport = signal<CurrentStockReportDto | null>(null);
  productLedgerReport = signal<ProductLedgerReportDto | null>(null);
  stockMovementReport = signal<StockMovementReportDto | null>(null);
  stockValuationReport = signal<StockValuationReportDto | null>(null);
  outletWiseStockReport  = signal<OutletWiseStockReportDto  | null>(null);
  lowStockReport         = signal<LowStockReportDto         | null>(null);
  outOfStockReport       = signal<OutOfStockReportDto       | null>(null);
  negativeStockReport    = signal<NegativeStockReportDto    | null>(null);

  constructor(private http: HttpClient) {}

  private buildDateParams(startDate?: string, endDate?: string): HttpParams {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return params;
  }

  getSalesSummary(startDate?: string, endDate?: string, outletId?: number): Observable<any> {
    let params = this.buildDateParams(startDate, endDate);
    if (outletId) params = params.set('outletId', outletId.toString());
    return this.http.get<any>(`${this.reportsUrl}/sales/summary`, { params }).pipe(
      tap({ next: (res) => this.salesSummary.set(res.data) })
    );
  }

  getTopProducts(startDate?: string, endDate?: string, limit = 10): Observable<any> {
    let params = this.buildDateParams(startDate, endDate);
    params = params.set('limit', limit.toString());
    return this.http.get<any>(`${this.reportsUrl}/sales/top-products`, { params }).pipe(
      tap({ next: (res) => this.topProducts.set(res.data || []) })
    );
  }

  getSalesByOutlet(startDate?: string, endDate?: string): Observable<any> {
    const params = this.buildDateParams(startDate, endDate);
    return this.http.get<any>(`${this.reportsUrl}/sales/by-outlet`, { params }).pipe(
      tap({ next: (res) => this.salesByOutlet.set(res.data || []) })
    );
  }

  getSalesByPaymentMethod(startDate?: string, endDate?: string): Observable<any> {
    const params = this.buildDateParams(startDate, endDate);
    return this.http.get<any>(`${this.reportsUrl}/sales/by-payment-method`, { params }).pipe(
      tap({ next: (res) => this.salesByPaymentMethod.set(res.data || []) })
    );
  }

  getDailySalesTrend(startDate?: string, endDate?: string): Observable<any> {
    const params = this.buildDateParams(startDate, endDate);
    return this.http.get<any>(`${this.reportsUrl}/sales/daily-trend`, { params }).pipe(
      tap({ next: (res) => this.dailyTrend.set(res.data || []) })
    );
  }

  getStockLevels(locationId?: number, locationType?: string, lowStockOnly = false): Observable<any> {
    let params = new HttpParams().set('lowStockOnly', lowStockOnly.toString());
    if (locationId) params = params.set('locationId', locationId.toString());
    if (locationType) params = params.set('locationType', locationType);
    return this.http.get<any>(`${this.reportsUrl}/inventory/stock-levels`, { params }).pipe(
      tap({ next: (res) => this.stockLevels.set(res.data || []) })
    );
  }

  getInventoryValuation(): Observable<any> {
    return this.http.get<any>(`${this.reportsUrl}/inventory/valuation`).pipe(
      tap({ next: (res) => this.inventoryValuation.set(res.data) })
    );
  }

  getSlowMovingItems(days = 90): Observable<any> {
    const params = new HttpParams().set('days', days.toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/slow-moving`, { params }).pipe(
      tap({ next: (res) => this.slowMovingItems.set(res.data || []) })
    );
  }

  getPurchaseSummary(startDate?: string, endDate?: string): Observable<any> {
    const params = this.buildDateParams(startDate, endDate);
    return this.http.get<any>(`${this.reportsUrl}/purchases/summary`, { params }).pipe(
      tap({ next: (res) => this.purchaseSummary.set(res.data) })
    );
  }

  getPurchaseBySupplier(startDate?: string, endDate?: string): Observable<any> {
    const params = this.buildDateParams(startDate, endDate);
    return this.http.get<any>(`${this.reportsUrl}/purchases/by-supplier`, { params }).pipe(
      tap({ next: (res) => this.purchaseBySupplier.set(res.data || []) })
    );
  }

  getStockTransactionReport(
    productId: number,
    variantId?: number,
    outletId?: number,
    locationType?: string,
    dateFrom?: string,
    dateTo?: string
  ): Observable<any> {
    let params = new HttpParams().set('productId', productId.toString());
    if (variantId) params = params.set('variantId', variantId.toString());
    if (outletId) params = params.set('outletId', outletId.toString());
    if (locationType) params = params.set('locationType', locationType);
    if (dateFrom) params = params.set('dateFrom', dateFrom);
    if (dateTo) params = params.set('dateTo', dateTo);
    return this.http.get<any>(`${this.reportsUrl}/stock-transactions`, { params }).pipe(
      tap({ next: (res) => this.stockTransactionReport.set(res.data) })
    );
  }

  getCurrentStockReport(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    productId?: number;
    stockStatus?: string;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    page?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)     params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId)  params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)   params = params.set('categoryId', filter.categoryId.toString());
    if (filter.productId)    params = params.set('productId', filter.productId.toString());
    if (filter.stockStatus && filter.stockStatus !== 'all')
                             params = params.set('stockStatus', filter.stockStatus);
    if (filter.search)       params = params.set('search', filter.search);
    if (filter.sortBy)       params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)      params = params.set('sortDir', filter.sortDir);
    params = params.set('page', (filter.page ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/current-stock`, { params }).pipe(
      tap({ next: (res) => this.currentStockReport.set(res.data) })
    );
  }

  buildCurrentStockExportUrl(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    productId?: number;
    stockStatus?: string;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)     params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId)  params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)   params = params.set('categoryId', filter.categoryId.toString());
    if (filter.productId)    params = params.set('productId', filter.productId.toString());
    if (filter.stockStatus && filter.stockStatus !== 'all')
                             params = params.set('stockStatus', filter.stockStatus);
    if (filter.search)       params = params.set('search', filter.search);
    if (filter.sortBy)       params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)      params = params.set('sortDir', filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/current-stock/export?${params.toString()}`;
  }

  getProductLedgerReport(filter: {
    productId: number;
    variantId?: number;
    outletId?: number;
    warehouseId?: number;
    dateFrom?: string;
    dateTo?: string;
    transactionType?: string;
    page?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams().set('productId', filter.productId.toString());
    if (filter.variantId)       params = params.set('variantId', filter.variantId.toString());
    if (filter.outletId)        params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId)     params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.dateFrom)        params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo)          params = params.set('dateTo', filter.dateTo);
    if (filter.transactionType && filter.transactionType !== 'all')
                                params = params.set('transactionType', filter.transactionType);
    params = params.set('page',     (filter.page ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/product-ledger`, { params }).pipe(
      tap({ next: (res) => this.productLedgerReport.set(res.data) })
    );
  }

  buildProductLedgerExportUrl(filter: {
    productId: number;
    variantId?: number;
    outletId?: number;
    warehouseId?: number;
    dateFrom?: string;
    dateTo?: string;
    transactionType?: string;
    format?: string;
  }): string {
    let params = new HttpParams().set('productId', filter.productId.toString());
    if (filter.variantId)       params = params.set('variantId', filter.variantId.toString());
    if (filter.outletId)        params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId)     params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.dateFrom)        params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo)          params = params.set('dateTo', filter.dateTo);
    if (filter.transactionType && filter.transactionType !== 'all')
                                params = params.set('transactionType', filter.transactionType);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/product-ledger/export?${params.toString()}`;
  }

  getStockMovementReport(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    search?: string;
    dateFrom?: string;
    dateTo?: string;
    sortBy?: string;
    sortDir?: string;
    page?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)      params = params.set('search', filter.search);
    if (filter.dateFrom)    params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo)      params = params.set('dateTo', filter.dateTo);
    if (filter.sortBy)      params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir', filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/stock-movement`, { params }).pipe(
      tap({ next: (res) => this.stockMovementReport.set(res.data) })
    );
  }

  buildStockMovementExportUrl(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    search?: string;
    dateFrom?: string;
    dateTo?: string;
    sortBy?: string;
    sortDir?: string;
    format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)      params = params.set('search', filter.search);
    if (filter.dateFrom)    params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo)      params = params.set('dateTo', filter.dateTo);
    if (filter.sortBy)      params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir', filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/stock-movement/export?${params.toString()}`;
  }

  getStockValuationReport(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    page?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)      params = params.set('search', filter.search);
    if (filter.sortBy)      params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir', filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/stock-valuation`, { params }).pipe(
      tap({ next: (res) => this.stockValuationReport.set(res.data) })
    );
  }

  buildStockValuationExportUrl(filter: {
    outletId?: number;
    warehouseId?: number;
    categoryId?: number;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId', filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)      params = params.set('search', filter.search);
    if (filter.sortBy)      params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir', filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/stock-valuation/export?${params.toString()}`;
  }

  getOutletWiseStockReport(filter: {
    outletId?: number;
    categoryId?: number;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    page?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)   params = params.set('outletId', filter.outletId.toString());
    if (filter.categoryId) params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)     params = params.set('search', filter.search);
    if (filter.sortBy)     params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)    params = params.set('sortDir', filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/outlet-wise-stock`, { params }).pipe(
      tap({ next: (res) => this.outletWiseStockReport.set(res.data) })
    );
  }

  buildOutletWiseStockExportUrl(filter: {
    outletId?: number;
    categoryId?: number;
    search?: string;
    sortBy?: string;
    sortDir?: string;
    format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)   params = params.set('outletId', filter.outletId.toString());
    if (filter.categoryId) params = params.set('categoryId', filter.categoryId.toString());
    if (filter.search)     params = params.set('search', filter.search);
    if (filter.sortBy)     params = params.set('sortBy', filter.sortBy);
    if (filter.sortDir)    params = params.set('sortDir', filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/outlet-wise-stock/export?${params.toString()}`;
  }

  // ── Report #6: Low Stock ─────────────────────────────────────────────────
  getLowStockReport(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; page?: number; pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/low-stock`, { params }).pipe(
      tap({ next: (res) => this.lowStockReport.set(res.data) })
    );
  }

  buildLowStockExportUrl(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId!.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId!.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId!.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/low-stock/export?${params.toString()}`;
  }

  // ── Report #7: Out Of Stock ──────────────────────────────────────────────
  getOutOfStockReport(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; page?: number; pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/out-of-stock`, { params }).pipe(
      tap({ next: (res) => this.outOfStockReport.set(res.data) })
    );
  }

  buildOutOfStockExportUrl(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId!.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId!.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId!.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/out-of-stock/export?${params.toString()}`;
  }

  // ── Report #8: Negative Stock ────────────────────────────────────────────
  getNegativeStockReport(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; page?: number; pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/negative-stock`, { params }).pipe(
      tap({ next: (res) => this.negativeStockReport.set(res.data) })
    );
  }

  buildNegativeStockExportUrl(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; sortBy?: string; sortDir?: string; format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId!.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId!.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId!.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/negative-stock/export?${params.toString()}`;
  }

  // ── Report #9: Stock Adjustment ───────────────────────────────────────────
  stockAdjustmentReport = signal<any | null>(null);

  getStockAdjustmentReport(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; dateFrom?: string; dateTo?: string;
    sortBy?: string; sortDir?: string; page?: number; pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.dateFrom)    params = params.set('dateFrom',    filter.dateFrom);
    if (filter.dateTo)      params = params.set('dateTo',      filter.dateTo);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/stock-adjustments`, { params }).pipe(
      tap({ next: (res) => this.stockAdjustmentReport.set(res.data) })
    );
  }

  buildStockAdjustmentExportUrl(filter: {
    outletId?: number | null; warehouseId?: number | null; categoryId?: number | null;
    search?: string; dateFrom?: string; dateTo?: string;
    sortBy?: string; sortDir?: string; format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.outletId)    params = params.set('outletId',    filter.outletId!.toString());
    if (filter.warehouseId) params = params.set('warehouseId', filter.warehouseId!.toString());
    if (filter.categoryId)  params = params.set('categoryId',  filter.categoryId!.toString());
    if (filter.search)      params = params.set('search',      filter.search);
    if (filter.dateFrom)    params = params.set('dateFrom',    filter.dateFrom);
    if (filter.dateTo)      params = params.set('dateTo',      filter.dateTo);
    if (filter.sortBy)      params = params.set('sortBy',      filter.sortBy);
    if (filter.sortDir)     params = params.set('sortDir',     filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/stock-adjustments/export?${params.toString()}`;
  }

  // ── Report #10: Stock Transfer ────────────────────────────────────────────
  stockTransferReport = signal<any | null>(null);

  getStockTransferReport(filter: {
    fromOutletId?: number | null; toOutletId?: number | null; categoryId?: number | null;
    status?: string; search?: string; dateFrom?: string; dateTo?: string;
    sortBy?: string; sortDir?: string; page?: number; pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filter.fromOutletId) params = params.set('fromOutletId', filter.fromOutletId.toString());
    if (filter.toOutletId)   params = params.set('toOutletId',   filter.toOutletId.toString());
    if (filter.categoryId)   params = params.set('categoryId',   filter.categoryId.toString());
    if (filter.status)       params = params.set('status',       filter.status);
    if (filter.search)       params = params.set('search',       filter.search);
    if (filter.dateFrom)     params = params.set('dateFrom',     filter.dateFrom);
    if (filter.dateTo)       params = params.set('dateTo',       filter.dateTo);
    if (filter.sortBy)       params = params.set('sortBy',       filter.sortBy);
    if (filter.sortDir)      params = params.set('sortDir',      filter.sortDir);
    params = params.set('page',     (filter.page     ?? 1).toString());
    params = params.set('pageSize', (filter.pageSize ?? 50).toString());
    return this.http.get<any>(`${this.reportsUrl}/inventory/stock-transfers-report`, { params }).pipe(
      tap({ next: (res) => this.stockTransferReport.set(res.data) })
    );
  }

  buildStockTransferReportExportUrl(filter: {
    fromOutletId?: number | null; toOutletId?: number | null; categoryId?: number | null;
    status?: string; search?: string; dateFrom?: string; dateTo?: string;
    sortBy?: string; sortDir?: string; format?: string;
  }): string {
    let params = new HttpParams();
    if (filter.fromOutletId) params = params.set('fromOutletId', filter.fromOutletId!.toString());
    if (filter.toOutletId)   params = params.set('toOutletId',   filter.toOutletId!.toString());
    if (filter.categoryId)   params = params.set('categoryId',   filter.categoryId!.toString());
    if (filter.status)       params = params.set('status',       filter.status);
    if (filter.search)       params = params.set('search',       filter.search);
    if (filter.dateFrom)     params = params.set('dateFrom',     filter.dateFrom);
    if (filter.dateTo)       params = params.set('dateTo',       filter.dateTo);
    if (filter.sortBy)       params = params.set('sortBy',       filter.sortBy);
    if (filter.sortDir)      params = params.set('sortDir',      filter.sortDir);
    params = params.set('format', filter.format ?? 'csv');
    return `${this.reportsUrl}/inventory/stock-transfers-report/export?${params.toString()}`;
  }
}
