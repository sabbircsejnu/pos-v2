import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthorizedOutletsDto, LocationAccessPolicy } from '../models/report.model';
import { ApiResponse } from '../interceptors/error.interceptor';

/**
 * Reads the current user's authorized outlets/warehouses from the API.
 * The list and the default-outlet resolution are owned by the backend —
 * the frontend never decides authorization on its own.
 *
 * Pass the operation policy the screen needs; without one the API applies InventoryStockView
 * (the user's Step 3 Inventory Location Access scope).
 */
@Injectable({ providedIn: 'root' })
export class UserOutletAccessService {
  private url = `${environment.apiUrl}/me/outlets`;

  authorized = signal<AuthorizedOutletsDto | null>(null);

  constructor(private http: HttpClient) {}

  load(policy?: LocationAccessPolicy): Observable<ApiResponse<AuthorizedOutletsDto> & { data: AuthorizedOutletsDto }> {
    this.authorized.set(null);
    const params = policy ? new HttpParams().set('policy', policy) : undefined;
    return this.http.get<ApiResponse<AuthorizedOutletsDto>>(this.url, { params }).pipe(
      map(response => {
        if (response?.success === false) {
          throw new Error(response.message || 'Unable to load authorized locations.');
        }
        if (!response?.data || !Array.isArray(response.data.outlets) || !Array.isArray(response.data.warehouses)) {
          throw new Error('The server returned an invalid authorized locations response.');
        }
        return { ...response, data: response.data };
      }),
      tap({ next: response => this.authorized.set(response.data) })
    );
  }
}
