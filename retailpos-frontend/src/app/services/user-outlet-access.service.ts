import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthorizedOutletsDto } from '../models/report.model';

/**
 * Reads the current user's authorized outlets/warehouses from the API.
 * The list and the default-outlet resolution are owned by the backend —
 * the frontend never decides authorization on its own.
 */
@Injectable({ providedIn: 'root' })
export class UserOutletAccessService {
  private url = `${environment.apiUrl}/me/outlets`;

  authorized = signal<AuthorizedOutletsDto | null>(null);

  constructor(private http: HttpClient) {}

  load(): Observable<any> {
    return this.http.get<any>(this.url).pipe(
      tap({ next: (res) => this.authorized.set(res.data) })
    );
  }
}
