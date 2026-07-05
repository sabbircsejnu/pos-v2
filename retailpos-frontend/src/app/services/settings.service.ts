import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CompanySettings,
  CurrencySettings,
  TaxSettings,
  ReceiptSettings,
  InvoiceNumberSettings,
  InventorySettings,
  SystemSettings,
} from '../models/settings.model';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private settingsUrl = `${environment.apiUrl}/settings`;

  isLoading = signal(false);
  isSaving = signal(false);
  allSettings = signal<SystemSettings | null>(null);

  constructor(private http: HttpClient) {}

  getAllSettings(): Observable<any> {
    this.isLoading.set(true);
    return this.http.get<any>(this.settingsUrl).pipe(
      tap({
        next: (res) => { this.allSettings.set(res.data); this.isLoading.set(false); },
        error: () => this.isLoading.set(false)
      })
    );
  }

  getCompanySettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/company`);
  }

  getCurrencySettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/currency`);
  }

  getTaxSettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/tax`);
  }

  getReceiptSettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/receipt`);
  }

  getInventorySettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/inventory`);
  }

  getInvoiceNumberSettings(): Observable<any> {
    return this.http.get<any>(`${this.settingsUrl}/invoice-number`);
  }

  updateCompanySettings(dto: CompanySettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/company`, dto);
  }

  updateCurrencySettings(dto: CurrencySettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/currency`, dto);
  }

  updateTaxSettings(dto: TaxSettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/tax`, dto);
  }

  updateReceiptSettings(dto: ReceiptSettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/receipt`, dto);
  }

  updateInventorySettings(dto: InventorySettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/inventory`, dto);
  }

  updateInvoiceNumberSettings(dto: InvoiceNumberSettings): Observable<any> {
    return this.http.put<any>(`${this.settingsUrl}/invoice-number`, dto);
  }
}
