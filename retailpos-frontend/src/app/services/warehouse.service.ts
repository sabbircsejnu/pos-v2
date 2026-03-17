import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { Warehouse, CreateWarehouseRequest, UpdateWarehouseRequest, WarehouseStats } from '../models/warehouse.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class WarehouseService {
  private apiUrl = `${environment.apiUrl}/warehouses`;
  
  warehouses = signal<Warehouse[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  getAllWarehouses(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.get<any>(this.apiUrl).pipe(
      tap(response => {
        this.warehouses.set(response.data);
        this.isLoading.set(false);
      })
    );
  }

  getWarehouseById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  createWarehouse(warehouse: CreateWarehouseRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.post<any>(this.apiUrl, warehouse).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllWarehouses().subscribe();
      })
    );
  }

  updateWarehouse(id: number, warehouse: UpdateWarehouseRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.put<any>(`${this.apiUrl}/${id}`, warehouse).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllWarehouses().subscribe();
      })
    );
  }

  deleteWarehouse(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllWarehouses().subscribe();
      })
    );
  }

  searchWarehouses(query: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/search`, { params: { q: query } }).pipe(
      tap(response => {
        this.warehouses.set(response.data);
      })
    );
  }

  getWarehouseStats(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}/stats`);
  }

  clearMessages(): void {
    this.successMessage.set(null);
    this.error.set(null);
  }
}
