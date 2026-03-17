import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Inventory,
  InventoryByLocation,
  LowStock,
  InventoryValuation,
  InventorySearchRequest,
  UpdateStockThreshold
} from '../models/inventory.model';

@Injectable({
  providedIn: 'root'
})
export class InventoryService {
  private apiUrl = `${environment.apiUrl}/inventory`;
  
  isLoading = signal(false);
  inventories = signal<Inventory[]>([]);
  lowStockItems = signal<LowStock[]>([]);
  locationSummary = signal<InventoryByLocation[]>([]);

  constructor(private http: HttpClient) {}

  getAll(): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    return this.http.get<{ success: boolean; data: Inventory[]; message?: string }>(this.apiUrl);
  }

  getById(id: number): Observable<{ success: boolean; data: Inventory; message?: string }> {
    return this.http.get<{ success: boolean; data: Inventory; message?: string }>(`${this.apiUrl}/${id}`);
  }

  getByOutlet(outletId: number): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    return this.http.get<{ success: boolean; data: Inventory[]; message?: string }>(
      `${this.apiUrl}/outlet/${outletId}`
    );
  }

  getByWarehouse(warehouseId: number): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    return this.http.get<{ success: boolean; data: Inventory[]; message?: string }>(
      `${this.apiUrl}/warehouse/${warehouseId}`
    );
  }

  getLowStock(outletId?: number, warehouseId?: number): Observable<{ success: boolean; data: LowStock[]; message?: string }> {
    let params = new HttpParams();
    if (outletId) params = params.set('outletId', outletId.toString());
    if (warehouseId) params = params.set('warehouseId', warehouseId.toString());
    
    return this.http.get<{ success: boolean; data: LowStock[]; message?: string }>(
      `${this.apiUrl}/low-stock`,
      { params }
    );
  }

  getOutOfStock(outletId?: number, warehouseId?: number): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    let params = new HttpParams();
    if (outletId) params = params.set('outletId', outletId.toString());
    if (warehouseId) params = params.set('warehouseId', warehouseId.toString());
    
    return this.http.get<{ success: boolean; data: Inventory[]; message?: string }>(
      `${this.apiUrl}/out-of-stock`,
      { params }
    );
  }

  getExpiringSoon(days: number = 30, outletId?: number, warehouseId?: number): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    let params = new HttpParams().set('days', days.toString());
    if (outletId) params = params.set('outletId', outletId.toString());
    if (warehouseId) params = params.set('warehouseId', warehouseId.toString());
    
    return this.http.get<{ success: boolean; data: Inventory[]; message?: string }>(
      `${this.apiUrl}/expiring-soon`,
      { params }
    );
  }

  getTotalStockByVariant(variantId: number): Observable<{ success: boolean; data: { variantId: number; totalQuantity: number }; message?: string }> {
    return this.http.get<{ success: boolean; data: { variantId: number; totalQuantity: number }; message?: string }>(
      `${this.apiUrl}/variant/${variantId}/total-stock`
    );
  }

  getValuation(outletId?: number, warehouseId?: number): Observable<{ success: boolean; data: InventoryValuation; message?: string }> {
    let params = new HttpParams();
    if (outletId) params = params.set('outletId', outletId.toString());
    if (warehouseId) params = params.set('warehouseId', warehouseId.toString());
    
    return this.http.get<{ success: boolean; data: InventoryValuation; message?: string }>(
      `${this.apiUrl}/valuation`,
      { params }
    );
  }

  getSummary(): Observable<{ success: boolean; data: InventoryByLocation[]; message?: string }> {
    return this.http.get<{ success: boolean; data: InventoryByLocation[]; message?: string }>(
      `${this.apiUrl}/summary`
    );
  }

  search(request: InventorySearchRequest): Observable<{ success: boolean; data: Inventory[]; message?: string }> {
    return this.http.post<{ success: boolean; data: Inventory[]; message?: string }>(
      `${this.apiUrl}/search`,
      request
    );
  }

  updateStockThreshold(id: number, data: UpdateStockThreshold): Observable<{ success: boolean; data: Inventory; message?: string }> {
    return this.http.put<{ success: boolean; data: Inventory; message?: string }>(
      `${this.apiUrl}/${id}/threshold`,
      data
    );
  }
}
