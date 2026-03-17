import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  SalesReportDto, TopProductDto, SalesByOutletDto, SalesByPaymentMethodDto,
  DailySalesTrendDto, StockLevelDto, InventoryValuationDto, SlowMovingItemDto,
  PurchaseSummaryDto, PurchaseBySupplierDto
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
}
