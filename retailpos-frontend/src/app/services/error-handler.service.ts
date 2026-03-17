import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

/**
 * Standardized error response from HTTP interceptor
 */
export interface ErrorResponse {
  status: number;
  message: string;
  originalError: HttpErrorResponse;
}

/**
 * Utility service for consistent error handling across the application
 */
@Injectable({
  providedIn: 'root'
})
export class ErrorHandlerService {
  /**
   * Extract error message from various error formats
   * This handles errors that may have already been processed by the interceptor
   * or raw errors that bypassed the interceptor
   */
  extractErrorMessage(error: any): string {
    // Check if it's already processed by interceptor
    if (error?.message && typeof error.message === 'string') {
      return error.message;
    }

    // Handle HttpErrorResponse
    if (error instanceof HttpErrorResponse) {
      if (error.error) {
        if (typeof error.error === 'object') {
          return error.error.message 
            || (error.error.errors && error.error.errors.join(', '))
            || error.error.error
            || error.message;
        } else if (typeof error.error === 'string') {
          return error.error;
        }
      }
      return error.message || 'An error occurred';
    }

    // Handle Error object
    if (error instanceof Error) {
      return error.message;
    }

    // Handle string
    if (typeof error === 'string') {
      return error;
    }

    // Fallback
    return 'An unexpected error occurred';
  }

  /**
   * Get user-friendly error message based on error type
   */
  getUserFriendlyMessage(error: any): string {
    const message = this.extractErrorMessage(error);

    // Customize messages based on common error patterns
    if (message.includes('duplicate') || message.includes('already exists')) {
      return message; // Keep as is for duplicate errors
    }

    if (message.includes('not found') || message.includes('does not exist')) {
      return message; // Keep as is for not found errors
    }

    if (message.includes('unauthorized') || message.includes('401')) {
      return 'You need to login to perform this action';
    }

    if (message.includes('forbidden') || message.includes('403')) {
      return 'You do not have permission to perform this action';
    }

    if (message.includes('network') || message.includes('connection')) {
      return 'Unable to connect to server. Please check your internet connection';
    }

    return message;
  }

  /**
   * Log error to console with details (useful for debugging)
   */
  logError(error: any, context?: string): void {
    const message = this.extractErrorMessage(error);
    const prefix = context ? `[${context}]` : '[Error]';
    
    console.error(`${prefix} ${message}`, error);
  }

  /**
   * Check if error is a specific type
   */
  isNotFoundError(error: any): boolean {
    return error?.status === 404 || 
           this.extractErrorMessage(error).toLowerCase().includes('not found');
  }

  isUnauthorizedError(error: any): boolean {
    return error?.status === 401;
  }

  isForbiddenError(error: any): boolean {
    return error?.status === 403;
  }

  isNetworkError(error: any): boolean {
    return error?.status === 0;
  }

  isServerError(error: any): boolean {
    return error?.status >= 500;
  }
}
