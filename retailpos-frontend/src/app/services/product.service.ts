import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap, catchError, of } from 'rxjs';
import {
  Product,
  CreateProductRequest,
  UpdateProductRequest,
  ProductSearchRequest,
  ProductListResponse
} from '../models/product.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  private apiUrl = `${environment.apiUrl}/products`;

  // Signals
  products = signal<Product[]>([]);
  selectedProduct = signal<Product | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);

  // Pagination
  totalCount = signal(0);
  pageNumber = signal(1);
  pageSize = signal(10);

  constructor(private http: HttpClient) {}

  /**
   * Get all products
   */
  getAll(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.apiUrl).pipe(
      tap(response => {
        this.products.set(response.data || []);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to load products');
        this.isLoading.set(false);
        return of({ data: [] });
      })
    );
  }

  /**
   * Search products with filters and pagination
   */
  search(searchRequest: ProductSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/search`, searchRequest).pipe(
      tap(response => {
        const data = response.data as ProductListResponse;
        this.products.set(data.products || []);
        this.totalCount.set(data.totalCount);
        this.pageNumber.set(data.pageNumber);
        this.pageSize.set(data.pageSize);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to search products');
        this.isLoading.set(false);
        return of({ data: { products: [], totalCount: 0 } });
      })
    );
  }

  /**
   * Get product by ID
   */
  getById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap(response => {
        this.selectedProduct.set(response.data);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to load product');
        this.isLoading.set(false);
        return of({ data: null });
      })
    );
  }

  /**
   * Get product by SKU
   */
  getBySku(sku: string): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/sku/${sku}`).pipe(
      tap(response => {
        this.selectedProduct.set(response.data);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to load product');
        this.isLoading.set(false);
        return of({ data: null });
      })
    );
  }

  /**
   * Get product by Barcode
   */
  getByBarcode(barcode: string): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/barcode/${barcode}`).pipe(
      tap(response => {
        this.selectedProduct.set(response.data);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to load product');
        this.isLoading.set(false);
        return of({ data: null });
      })
    );
  }

  /**
   * Get products by category
   */
  getByCategory(categoryId: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/category/${categoryId}`).pipe(
      tap(response => {
        this.products.set(response.data || []);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to load products');
        this.isLoading.set(false);
        return of({ data: [] });
      })
    );
  }

  /**
   * Create a new product
   */
  create(product: CreateProductRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(this.apiUrl, product).pipe(
      tap(response => {
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to create product');
        this.isLoading.set(false);
        throw error;
      })
    );
  }

  /**
   * Update an existing product
   */
  update(id: number, product: UpdateProductRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.put<any>(`${this.apiUrl}/${id}`, product).pipe(
      tap(response => {
        this.selectedProduct.set(response.data);
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to update product');
        this.isLoading.set(false);
        throw error;
      })
    );
  }

  /**
   * Delete a product
   */
  delete(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(response => {
        // Remove from local array
        this.products.update(products => products.filter(p => p.id !== id));
        this.isLoading.set(false);
      }),
      catchError(error => {
        this.error.set(error.error?.error || 'Failed to delete product');
        this.isLoading.set(false);
        throw error;
      })
    );
  }

  /**
   * Generate SKU for a product name
   */
  generateSku(productName: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/generate-sku`, JSON.stringify(productName), {
      headers: { 'Content-Type': 'application/json' }
    });
  }

  /**
   * Clear selected product
   */
  clearSelection(): void {
    this.selectedProduct.set(null);
  }

  /**
   * Search product variants (for PO form)
   */
  searchVariants(query: string, page: number = 1, pageSize: number = 20): Observable<any> {
    const params = new HttpParams()
      .set('query', query)
      .set('pageNumber', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<any>(`${this.apiUrl}/variants/search`, { params }).pipe(
      catchError(error => {
        console.error('Failed to search variants:', error);
        return of({ data: [] });
      })
    );
  }

  /**
   * Clear error
   */
  clearError(): void {
    this.error.set(null);
  }
}
