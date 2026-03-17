import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Standardized API response format from backend
 */
export interface ApiResponse<T = any> {
  success: boolean;
  message?: string;
  data?: T;
  errors?: string[];
}

/**
 * HTTP Interceptor that handles all HTTP errors globally
 * and extracts error messages from standardized API responses
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      let errorMessage = 'An unexpected error occurred';

      // Extract error message from various possible formats
      if (error.error) {
        if (typeof error.error === 'object') {
          const apiResponse = error.error as ApiResponse;
          
          // Check standardized format (new format)
          if (apiResponse.message) {
            errorMessage = apiResponse.message;
          } else if (apiResponse.errors && apiResponse.errors.length > 0) {
            errorMessage = apiResponse.errors.join(', ');
          } 
          // Check legacy format (old format)
          else if ((apiResponse as any).error) {
            errorMessage = (apiResponse as any).error;
          }
        } else if (typeof error.error === 'string') {
          errorMessage = error.error;
        }
      } else if (error.message) {
        errorMessage = error.message;
      }

      // Handle specific HTTP status codes
      switch (error.status) {
        case 0:
          errorMessage = 'Unable to connect to server. Please check your connection.';
          break;
        case 401:
          errorMessage = 'Unauthorized. Please login again.';
          authService.logout();
          router.navigate(['/login']);
          break;
        case 403:
          errorMessage = errorMessage || 'You do not have permission to perform this action.';
          break;
        case 404:
          errorMessage = errorMessage || 'The requested resource was not found.';
          break;
        case 500:
          errorMessage = errorMessage || 'Server error. Please try again later.';
          break;
      }

      console.error('HTTP Error:', {
        status: error.status,
        statusText: error.statusText,
        message: errorMessage,
        originalError: error.error,
        url: error.url
      });

      // Return enhanced error with extracted message
      return throwError(() => ({
        status: error.status,
        message: errorMessage,
        originalError: error
      }));
    })
  );
};

