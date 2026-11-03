import { HttpInterceptorFn, HttpErrorResponse, HttpContextToken } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AlertService } from '../services/alert.service';

export const FORBIDDEN_MESSAGE = 'You do not have permission to perform this action.';

/** Set on background probes whose failure must not raise a global notification. */
export const SUPPRESS_ERROR_NOTICE = new HttpContextToken<boolean>(() => false);

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
  const alertService = inject(AlertService);

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
        // 401 is handled by sessionInterceptor (silent refresh, sign-out only when unrecoverable).
        case 401:
          errorMessage = 'Your session could not be verified. Please sign in again.';
          break;
        // 403 is an authorization outcome: never sign out, clear tokens or reset Business/Outlet context.
        case 403:
          errorMessage = FORBIDDEN_MESSAGE;
          if (!req.context.get(SUPPRESS_ERROR_NOTICE) && alertService.config()?.message !== FORBIDDEN_MESSAGE) {
            alertService.error(FORBIDDEN_MESSAGE);
          }
          break;
        case 404:
          errorMessage = errorMessage || 'The requested resource was not found.';
          break;
        case 500:
          errorMessage = errorMessage || 'Server error. Please try again later.';
          break;
      }

      const sensitiveRequest = /\/(?:auth|invitation)/i.test(req.url);
      const logDetails = sensitiveRequest
        ? { status: error.status, statusText: error.statusText }
        : {
            status: error.status,
            statusText: error.statusText,
            message: errorMessage,
            validationErrors,
            originalError: error.error,
            url: error.url
          };
      console.error('HTTP Error:', logDetails);

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
