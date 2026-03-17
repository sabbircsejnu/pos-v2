import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

// API Response wrapper interface
export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data?: T;
  errors?: string[];
}

export interface ProductVariationDto {
  variationId: number;
  variationName: string;
  isRequired: boolean;
  options: VariationOptionInfo[];
}

export interface VariationOptionInfo {
  id: number;
  name: string;
  priceAdjustment: number;
}

export interface CombinationDto {
  id?: number;
  sku: string;
  barcode?: string;
  priceAdjustment: number;
  costAdjustment: number;
  optionIds: number[];
  options: CombinationOptionInfo[];
  combinationName: string;
  finalPrice: number;
}

export interface CombinationOptionInfo {
  id: number;
  variationName: string;
  optionName: string;
  priceAdjustment: number;
}

export interface AssignVariationsRequest {
  variationIds: number[];
}

export interface GenerateCombinationsRequest {
  variationIds?: number[];
}

export interface CreateCombinationRequest {
  sku?: string;
  barcode?: string;
  priceAdjustment: number;
  costAdjustment: number;
  optionIds: number[];
}

export interface UpdateCombinationRequest {
  sku?: string;
  barcode?: string;
  priceAdjustment: number;
  costAdjustment: number;
}

@Injectable({
  providedIn: 'root'
})
export class ProductVariationService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Get variations assigned to product
  getProductVariations(productId: number): Observable<ProductVariationDto[]> {
    return this.http.get<ApiResponse<ProductVariationDto[]>>(`${this.apiUrl}/products/${productId}/variations`)
      .pipe(map(response => response.data || []));
  }

  // Assign variations to product
  assignVariations(productId: number, variationIds: number[]): Observable<string> {
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/products/${productId}/variations/assign`, { variationIds })
      .pipe(map(response => response.message || 'Success'));
  }

  // Get combinations for product
  getCombinations(productId: number): Observable<CombinationDto[]> {
    return this.http.get<ApiResponse<CombinationDto[]>>(`${this.apiUrl}/products/${productId}/variations/combinations`)
      .pipe(map(response => response.data || []));
  }

  // Generate all combinations
  generateAllCombinations(productId: number): Observable<{ data: CombinationDto[], message: string }> {
    return this.http.post<ApiResponse<CombinationDto[]>>(
      `${this.apiUrl}/products/${productId}/variations/combinations/generate-all`,
      {}
    ).pipe(map(response => ({ data: response.data || [], message: response.message || 'Success' })));
  }

  // Generate selected combinations
  generateSelectedCombinations(productId: number, variationIds: number[]): Observable<{ data: CombinationDto[], message: string }> {
    return this.http.post<ApiResponse<CombinationDto[]>>(
      `${this.apiUrl}/products/${productId}/variations/combinations/generate-selected`,
      { variationIds }
    ).pipe(map(response => ({ data: response.data || [], message: response.message || 'Success' })));
  }

  // Create manual combination
  createManualCombination(productId: number, request: CreateCombinationRequest): Observable<CombinationDto> {
    return this.http.post<ApiResponse<CombinationDto>>(`${this.apiUrl}/products/${productId}/variations/combinations`, request)
      .pipe(map(response => response.data!));
  }

  // Update combination
  updateCombination(productId: number, variantId: number, request: UpdateCombinationRequest): Observable<string> {
    return this.http.put<ApiResponse<any>>(`${this.apiUrl}/products/${productId}/variations/combinations/${variantId}`, request)
      .pipe(map(response => response.message || 'Success'));
  }

  // Delete combination
  deleteCombination(productId: number, variantId: number): Observable<string> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/products/${productId}/variations/combinations/${variantId}`)
      .pipe(map(response => response.message || 'Success'));
  }
}
