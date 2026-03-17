import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import {
  Variation,
  CreateVariationDto,
  UpdateVariationDto,
  CreateVariationOptionDto,
  UpdateVariationOptionDto,
  VariationOption,
} from '../models/variation.model';

// API Response wrapper interface
export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data?: T;
  errors?: string[];
}

@Injectable({
  providedIn: 'root'
})
export class VariationService {
  private apiUrl = `${environment.apiUrl}/variations`;
  
  variations = signal<Variation[]>([]);
  selectedVariation = signal<Variation | null>(null);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  // Get all variations
  getAllVariations(): Observable<Variation[]> {
    return this.http.get<ApiResponse<Variation[]>>(this.apiUrl)
      .pipe(map(response => response.data || []));
  }

  // Get variation by ID
  getVariationById(id: number): Observable<Variation> {
    return this.http.get<ApiResponse<Variation>>(`${this.apiUrl}/${id}`)
      .pipe(map(response => response.data!));
  }

  // Create variation with options
  createVariation(dto: CreateVariationDto): Observable<Variation> {
    return this.http.post<ApiResponse<Variation>>(this.apiUrl, dto)
      .pipe(map(response => response.data!));
  }

  // Update variation
  updateVariation(id: number, dto: UpdateVariationDto): Observable<Variation> {
    return this.http.put<ApiResponse<Variation>>(`${this.apiUrl}/${id}`, dto)
      .pipe(map(response => response.data!));
  }

  // Delete variation
  deleteVariation(id: number): Observable<string> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/${id}`)
      .pipe(map(response => response.message || 'Variation deleted successfully'));
  }

  // Create option for a variation
  createOption(variationId: number, dto: CreateVariationOptionDto): Observable<VariationOption> {
    return this.http.post<ApiResponse<VariationOption>>(`${this.apiUrl}/${variationId}/options`, dto)
      .pipe(map(response => response.data!));
  }

  // Update option
  updateOption(optionId: number, dto: UpdateVariationOptionDto): Observable<VariationOption> {
    return this.http.put<ApiResponse<VariationOption>>(`${this.apiUrl}/options/${optionId}`, dto)
      .pipe(map(response => response.data!));
  }

  // Delete option
  deleteOption(optionId: number): Observable<string> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/options/${optionId}`)
      .pipe(map(response => response.message || 'Option deleted successfully'));
  }

  // Load all variations and update signal
  loadVariations(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    
    this.getAllVariations().subscribe({
      next: (variations) => {
        this.variations.set(variations);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading variations:', error);
        this.errorMessage.set('Failed to load variations');
        this.isLoading.set(false);
      }
    });
  }

  // Clear error message
  clearError(): void {
    this.errorMessage.set(null);
  }
}
