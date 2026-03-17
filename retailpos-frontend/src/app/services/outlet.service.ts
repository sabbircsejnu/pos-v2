import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { Outlet, CreateOutletRequest, UpdateOutletRequest, OutletStats } from '../models/outlet.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class OutletService {
  private apiUrl = `${environment.apiUrl}/outlets`;
  
  outlets = signal<Outlet[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  getAllOutlets(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.get<any>(this.apiUrl).pipe(
      tap(response => {
        this.outlets.set(response.data);
        this.isLoading.set(false);
      })
    );
  }

  getOutletById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  createOutlet(outlet: CreateOutletRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.post<any>(this.apiUrl, outlet).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllOutlets().subscribe();
      })
    );
  }

  updateOutlet(id: number, outlet: UpdateOutletRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.put<any>(`${this.apiUrl}/${id}`, outlet).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllOutlets().subscribe();
      })
    );
  }

  deleteOutlet(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(response => {
        this.successMessage.set(response.message);
        this.isLoading.set(false);
        this.getAllOutlets().subscribe();
      })
    );
  }

  searchOutlets(query: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/search`, { params: { q: query } }).pipe(
      tap(response => {
        this.outlets.set(response.data);
      })
    );
  }

  getOutletStats(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}/stats`);
  }

  clearMessages(): void {
    this.successMessage.set(null);
    this.error.set(null);
  }
}
