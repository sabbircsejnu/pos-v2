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
  /** All globally available options for this variation type. */
  options: VariationOptionInfo[];
  /** Which option IDs are currently selected for this specific product. */
  selectedOptionIds: number[];
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
  /**
   * Key = variationId, Value = array of selected optionIds for that variation.
   * When a variation is assigned but its entry is absent/empty, the backend
   * defaults to all active options for that variation.
   */
  selectedOptionsByVariation: { [variationId: number]: number[] };
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

  // Get variations assigned to product (includes selectedOptionIds per variation)
  getProductVariations(productId: number): Observable<ProductVariationDto[]> {
    return this.http.get<ApiResponse<ProductVariationDto[]>>(`${this.apiUrl}/products/${productId}/variations`)
      .pipe(map(response => response.data || []));
  }

  // Assign variations to product with selected options per variation
  assignVariations(
    productId: number,
    variationIds: number[],
    selectedOptionsByVariation: { [variationId: number]: number[] }
  ): Observable<string> {
    const body: AssignVariationsRequest = { variationIds, selectedOptionsByVariation };
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/products/${productId}/variations/assign`, body)
      .pipe(map(response => response.message || 'Success'));
  }

  // Get combinations for product
  getCombinations(productId: number): Observable<CombinationDto[]> {
    return this.http.get<ApiResponse<CombinationDto[]>>(`${this.apiUrl}/products/${productId}/variations/combinations`)
      .pipe(map(response => response.data || []));
  }

  // Generate all combinations (uses product-saved selected options)
  generateAllCombinations(productId: number): Observable<{ data: CombinationDto[], message: string }> {
    return this.http.post<ApiResponse<CombinationDto[]>>(
      `${this.apiUrl}/products/${productId}/variations/combinations/generate-all`,
      {}
    ).pipe(map(response => ({ data: response.data || [], message: response.message || 'Success' })));
  }

  // Generate combinations for selected variation types (uses product-saved selected options)
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
