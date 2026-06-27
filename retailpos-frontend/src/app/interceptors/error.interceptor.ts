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
  errors?: string[] | Record<string, string[]>;
}

function flattenValidationErrors(errors: unknown): string[] {
  if (!errors) {
    return [];
  }

  if (Array.isArray(errors)) {
    return errors.filter((value): value is string => typeof value === 'string' && value.trim().length > 0);
  }

  if (typeof errors === 'object') {
    return Object.values(errors as Record<string, unknown>)
      .flatMap(value => {
        if (Array.isArray(value)) {
          return value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0);
        }

        if (typeof value === 'string' && value.trim().length > 0) {
          return [value];
        }

        return [] as string[];
      });
  }

  return [];
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
      let validationErrors: string[] = [];

      // Extract error message from various possible formats
      if (error.error) {
        if (typeof error.error === 'object') {
          const apiResponse = error.error as ApiResponse;

          validationErrors = flattenValidationErrors(apiResponse.errors);
          
          // Check standardized format (new format)
          if (apiResponse.message) {
            errorMessage = apiResponse.message;
          } else if (validationErrors.length > 0) {
            errorMessage = validationErrors.join('\n');
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
        validationErrors,
        originalError: error.error,
        url: error.url
      });

      // Return enhanced error with extracted message and validation list.
      return throwError(() => ({
        status: error.status,
        message: errorMessage,
        error: {
          message: errorMessage,
          error: errorMessage,
          errors: validationErrors
        },
        errors: validationErrors,
        originalError: error
      }));
    })
  );
};

