import { Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class DocumentPdfService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  downloadPurchaseOrder(id: number): Observable<HttpResponse<Blob>> {
    return this.downloadFromPath(`/purchase-orders/${id}/pdf`);
  }

  downloadDocument(documentType: string, id: number): Observable<HttpResponse<Blob>> {
    return this.downloadFromPath(`/documents/${documentType}/${id}/pdf`);
  }

  triggerBrowserDownload(response: HttpResponse<Blob>, fallbackFileName: string): void {
    const blob = response.body;
    if (!blob) {
      throw new Error('Empty file response');
    }

    const fileName = this.extractFileName(response) || fallbackFileName;
    const blobUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = blobUrl;
    anchor.download = fileName;
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(blobUrl);
  }

  private downloadFromPath(path: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}${path}`, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  private extractFileName(response: HttpResponse<Blob>): string | null {
    const contentDisposition = response.headers.get('content-disposition');
    if (!contentDisposition) {
      return null;
    }

    const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
    if (utf8Match?.[1]) {
      return decodeURIComponent(utf8Match[1]);
    }

    const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
    return plainMatch?.[1] ?? null;
  }
}