import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ApiResponse,
  BusinessSummaryDto,
  CreateBusinessRequestDto,
  CreateBusinessResponseDto,
  UpdateBusinessStatusDto
} from '../models/business.model';
import { Outlet, CreateOutletRequest, UpdateOutletRequest } from '../models/outlet.model';
import { Warehouse, CreateWarehouseRequest, UpdateWarehouseRequest } from '../models/warehouse.model';

@Injectable({
  providedIn: 'root'
})
export class BusinessService {
  private readonly apiUrl = `${environment.apiUrl}/businesses`;

  constructor(private http: HttpClient) {}

  getBusinesses(search?: string, isActive?: boolean): Observable<ApiResponse<BusinessSummaryDto[]>> {
    let params = new HttpParams();

    if (search && search.trim()) {
      params = params.set('search', search.trim());
    }

    if (typeof isActive === 'boolean') {
      params = params.set('isActive', String(isActive));
    }

    return this.http.get<ApiResponse<BusinessSummaryDto[]>>(this.apiUrl, { params });
  }

  getBusinessById(businessId: number): Observable<ApiResponse<BusinessSummaryDto>> {
    return this.http.get<ApiResponse<BusinessSummaryDto>>(`${this.apiUrl}/${businessId}`);
  }

  createBusiness(dto: CreateBusinessRequestDto): Observable<ApiResponse<CreateBusinessResponseDto>> {
    return this.http.post<ApiResponse<CreateBusinessResponseDto>>(this.apiUrl, dto);
  }

  updateBusinessStatus(businessId: number, dto: UpdateBusinessStatusDto): Observable<ApiResponse<BusinessSummaryDto>> {
    return this.http.patch<ApiResponse<BusinessSummaryDto>>(`${this.apiUrl}/${businessId}/status`, dto);
  }

  resetOwnerAccess(businessId: number): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/${businessId}/owner/reset-access`, {});
  }

  getFeatureSettings(businessId: number): Observable<ApiResponse<any[]>> {
    return this.http.get<ApiResponse<any[]>>(`${this.apiUrl}/${businessId}/features`);
  }

  upsertFeatureSettings(businessId: number, settings: any): Observable<ApiResponse<any[]>> {
    return this.http.patch<ApiResponse<any[]>>(`${this.apiUrl}/${businessId}/features`, settings);
  }

  updateSubscription(businessId: number, dto: any): Observable<ApiResponse<BusinessSummaryDto>> {
    return this.http.patch<ApiResponse<BusinessSummaryDto>>(`${this.apiUrl}/${businessId}/subscription`, dto);
  }

  // ── Business Setup: Outlets (Super Admin scoped by BusinessId) ─────────────

  getSetupOutlets(businessId: number): Observable<ApiResponse<Outlet[]>> {
    return this.http.get<ApiResponse<Outlet[]>>(`${this.apiUrl}/${businessId}/outlets`);
  }

  createSetupOutlet(businessId: number, dto: CreateOutletRequest): Observable<ApiResponse<Outlet>> {
    return this.http.post<ApiResponse<Outlet>>(`${this.apiUrl}/${businessId}/outlets`, dto);
  }

  updateSetupOutlet(businessId: number, outletId: number, dto: UpdateOutletRequest): Observable<ApiResponse<Outlet>> {
    return this.http.put<ApiResponse<Outlet>>(`${this.apiUrl}/${businessId}/outlets/${outletId}`, dto);
  }

  deleteSetupOutlet(businessId: number, outletId: number): Observable<ApiResponse<any>> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/${businessId}/outlets/${outletId}`);
  }

  // ── Business Setup: Warehouses (Super Admin scoped by BusinessId) ──────────

  getSetupWarehouses(businessId: number): Observable<ApiResponse<Warehouse[]>> {
    return this.http.get<ApiResponse<Warehouse[]>>(`${this.apiUrl}/${businessId}/warehouses`);
  }

  createSetupWarehouse(businessId: number, dto: CreateWarehouseRequest): Observable<ApiResponse<Warehouse>> {
    return this.http.post<ApiResponse<Warehouse>>(`${this.apiUrl}/${businessId}/warehouses`, dto);
  }

  updateSetupWarehouse(businessId: number, warehouseId: number, dto: UpdateWarehouseRequest): Observable<ApiResponse<Warehouse>> {
    return this.http.put<ApiResponse<Warehouse>>(`${this.apiUrl}/${businessId}/warehouses/${warehouseId}`, dto);
  }

  deleteSetupWarehouse(businessId: number, warehouseId: number): Observable<ApiResponse<any>> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/${businessId}/warehouses/${warehouseId}`);
  }
}
