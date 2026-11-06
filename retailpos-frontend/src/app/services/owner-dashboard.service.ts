import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from '../interceptors/error.interceptor';
import {
  BestSellerRow,
  CashFlow,
  InventorySummary,
  OwnerDashboardMeta,
  PagedResult,
  ProductInsights,
  SalesFinanceSummary,
  SalesTrend,
  ShowroomPerformance,
  SlowMovers,
  StockValueByLocation,
  TransferOpportunities,
  TransferOpportunity
} from '../models/owner-dashboard.model';

export interface SalesQuery {
  from: string;
  to: string;
  outletId: number | null;
}

export interface InventoryQuery {
  from: string;
  to: string;
  locationId: number | null;
  locationType: 'outlet' | 'warehouse' | null;
  categoryId: number | null;
}

export interface TableQuery {
  search?: string;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

export interface SlowMoversQuery extends TableQuery {
  classification?: string;
  minStock?: number | null;
  maxStock?: number | null;
}

export interface TransferQuery extends TableQuery {
  sourceLocationId?: number | null;
  destinationLocationId?: number | null;
  priority?: string;
}

/** Read-only client of the Owner Dashboard API. Totals and authorization are always the server's. */
@Injectable({ providedIn: 'root' })
export class OwnerDashboardService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/v1/owner-dashboard`;

  meta(): Observable<OwnerDashboardMeta> {
    return this.get('meta', {});
  }

  salesSummary(query: SalesQuery): Observable<SalesFinanceSummary> {
    return this.get('sales/summary', { ...query });
  }

  showrooms(query: SalesQuery): Observable<ShowroomPerformance> {
    return this.get('sales/showrooms', { ...query });
  }

  cashFlow(query: SalesQuery): Observable<CashFlow> {
    return this.get('sales/cash-flow', { ...query });
  }

  salesTrend(query: SalesQuery): Observable<SalesTrend> {
    return this.get('sales/trend', { ...query });
  }

  inventorySummary(query: InventoryQuery): Observable<InventorySummary> {
    return this.get('inventory/summary', { ...query });
  }

  stockByLocation(query: InventoryQuery): Observable<StockValueByLocation> {
    return this.get('inventory/locations', { ...query });
  }

  bestSellers(query: InventoryQuery, table: TableQuery): Observable<PagedResult<BestSellerRow>> {
    return this.get('inventory/best-sellers', { ...query, ...table });
  }

  slowMovers(query: InventoryQuery, table: SlowMoversQuery): Observable<SlowMovers> {
    return this.get('inventory/slow-movers', { ...query, ...table });
  }

  transferOpportunities(query: InventoryQuery, table: TransferQuery): Observable<TransferOpportunities> {
    return this.get('inventory/transfer-opportunities', { ...query, ...table });
  }

  transferOpportunity(key: string, from: string, to: string): Observable<TransferOpportunity> {
    return this.get(`inventory/transfer-opportunities/${encodeURIComponent(key)}`, { from, to });
  }

  productInsights(
    productId: number,
    variantId: number | null,
    query: InventoryQuery
  ): Observable<ProductInsights> {
    return this.get(`inventory/products/${productId}/insights`, {
      from: query.from,
      to: query.to,
      variantId,
      locationId: query.locationId,
      locationType: query.locationType
    });
  }

  private get<T>(path: string, query: Record<string, string | number | null | undefined>): Observable<T> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }

    return this.http.get<ApiResponse<T>>(`${this.url}/${path}`, { params }).pipe(
      map(response => {
        if (response?.success === false || response?.data === undefined || response.data === null) {
          throw new Error(response?.message || 'The server returned an invalid dashboard response.');
        }
        return response.data;
      })
    );
  }
}
