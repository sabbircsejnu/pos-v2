import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ProductImage } from '../models/product-image.model';

const PLACEHOLDER_DATA_URI =
  // eslint-disable-next-line max-len
  'data:image/svg+xml;utf8,' + encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none"><rect width="64" height="64" rx="6" fill="#f1f5f9"/><path d="M16 44l10-12 8 10 6-6 8 8" stroke="#94a3b8" stroke-width="2" fill="none"/><circle cx="22" cy="24" r="4" fill="#cbd5e1"/></svg>`
  );

@Injectable({ providedIn: 'root' })
export class ProductImageService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/products`;
  /** Strip "/api" suffix to get origin where /uploads is served. */
  private origin = environment.apiUrl.replace(/\/api\/?$/, '');

  list(productId: number): Observable<ProductImage[]> {
    return this.http.get<ProductImage[]>(`${this.base}/${productId}/images`);
  }

  upload(productId: number, file: File, isPrimary = false): Observable<ProductImage> {
    const fd = new FormData();
    fd.append('file', file);
    fd.append('isPrimary', String(isPrimary));
    return this.http.post<ProductImage>(`${this.base}/${productId}/images`, fd);
  }

  delete(productId: number, imageId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${productId}/images/${imageId}`);
  }

  setPrimary(productId: number, imageId: number): Observable<{ primaryImageId: number }> {
    return this.http.put<{ primaryImageId: number }>(
      `${this.base}/${productId}/images/${imageId}/primary`, {});
  }

  /** Resolve a server-relative path (e.g. "/uploads/...") to an absolute URL. */
  resolveUrl(path: string | null | undefined): string {
    if (!path) return PLACEHOLDER_DATA_URI;
    if (path.startsWith('http://') || path.startsWith('https://')) return path;
    return this.origin + path;
  }

  placeholder(): string {
    return PLACEHOLDER_DATA_URI;
  }
}
