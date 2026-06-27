import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  BarcodePrintHistory,
  BarcodePrintHistoryList,
  BarcodePrintHistorySearchRequest,
  BarcodeTemplate,
  BarcodeVariantSearchItem,
  RecordBarcodePrintHistoryRequest,
  UpsertBarcodeTemplateRequest,
} from '../models/barcode.model';

interface ApiResponse<T> {
  data: T;
  message?: string;
}

@Injectable({
  providedIn: 'root'
})
export class BarcodeService {
  private readonly barcodeUrl = `${environment.apiUrl}/barcodes`;
  private readonly templateUrl = `${environment.apiUrl}/barcode-templates`;

  constructor(private http: HttpClient) {}

  searchVariants(
    query: string,
    pageNumber: number = 1,
    pageSize: number = 20,
    locationId?: number | null,
    locationType?: string | null
  ): Observable<BarcodeVariantSearchItem[]> {
    let params = new HttpParams()
      .set('query', query)
      .set('pageNumber', pageNumber.toString())
      .set('pageSize', pageSize.toString());

    if (locationId && locationType) {
      params = params
        .set('locationId', locationId.toString())
        .set('locationType', locationType);
    }

    return this.http.get<ApiResponse<BarcodeVariantSearchItem[]>>(`${this.barcodeUrl}/variants/search`, { params })
      .pipe(map(response => response.data || []));
  }

  getTemplates(includeInactive: boolean = false): Observable<BarcodeTemplate[]> {
    const params = new HttpParams().set('includeInactive', includeInactive.toString());
    return this.http.get<ApiResponse<BarcodeTemplate[]>>(this.templateUrl, { params })
      .pipe(map(response => response.data || []));
  }

  getTemplateById(id: number): Observable<BarcodeTemplate> {
    return this.http.get<ApiResponse<BarcodeTemplate>>(`${this.templateUrl}/${id}`)
      .pipe(map(response => response.data));
  }

  createTemplate(payload: UpsertBarcodeTemplateRequest): Observable<BarcodeTemplate> {
    return this.http.post<ApiResponse<BarcodeTemplate>>(this.templateUrl, payload)
      .pipe(map(response => response.data));
  }

  updateTemplate(id: number, payload: UpsertBarcodeTemplateRequest): Observable<BarcodeTemplate> {
    return this.http.put<ApiResponse<BarcodeTemplate>>(`${this.templateUrl}/${id}`, payload)
      .pipe(map(response => response.data));
  }

  deleteTemplate(id: number): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${this.templateUrl}/${id}`)
      .pipe(map(() => void 0));
  }

  setDefaultTemplate(id: number): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.templateUrl}/${id}/set-default`, {})
      .pipe(map(() => void 0));
  }

  recordPrintHistory(payload: RecordBarcodePrintHistoryRequest, isBulk: boolean): Observable<BarcodePrintHistory> {
    const endpoint = isBulk ? 'bulk' : 'single';
    return this.http.post<ApiResponse<BarcodePrintHistory>>(`${this.barcodeUrl}/print-history/${endpoint}`, payload)
      .pipe(map(response => response.data));
  }

  searchPrintHistory(payload: BarcodePrintHistorySearchRequest): Observable<BarcodePrintHistoryList> {
    return this.http.post<ApiResponse<BarcodePrintHistoryList>>(`${this.barcodeUrl}/print-history/search`, payload)
      .pipe(map(response => response.data));
  }

  getPrintHistoryById(id: number): Observable<BarcodePrintHistory> {
    return this.http.get<ApiResponse<BarcodePrintHistory>>(`${this.barcodeUrl}/print-history/${id}`)
      .pipe(map(response => response.data));
  }

  generatePdf(payload: RecordBarcodePrintHistoryRequest): Observable<HttpResponse<Blob>> {
    return this.http.post(`${this.barcodeUrl}/pdf`, payload, {
      observe: 'response',
      responseType: 'blob'
    });
  }
}
