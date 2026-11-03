import { Injectable } from '@angular/core';
import { HttpRequest, HttpHandler, HttpEvent, HttpInterceptor } from '@angular/common/http';
import { Observable } from 'rxjs';

import { environment } from 'src/environments/environment';

@Injectable()
export class BasicAuthInterceptor implements HttpInterceptor {
  /**
   * Intercepts HTTP requests to add JWT token in Authorization header
   *
   * NOTE: We read the token directly from localStorage to avoid circular dependency.
   * The AuthenticationService uses HttpClient, which requires this interceptor,
   * so injecting AuthenticationService here would create a circular dependency.
   */
  intercept(request: HttpRequest<string>, next: HttpHandler): Observable<HttpEvent<string>> {
    // Add auth header with JWT token if user is logged in and request is to the api url
    // Get token directly from localStorage to avoid circular dependency with AuthenticationService
    const token = this.getTokenFromStorage();
    const isApiUrl = request.url.startsWith(environment.apiUrl);

    if (token && isApiUrl) {
      request = request.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }

    return next.handle(request);
  }

  /**
   * Gets the serviceToken from localStorage
   * This method mirrors the logic in AuthenticationService.getToken()
   * but is kept here to avoid circular dependency
   */
  private getTokenFromStorage(): string | null {
    const storedUser = localStorage.getItem('currentUser');
    if (storedUser) {
      try {
        const userData = JSON.parse(storedUser);
        return userData.serviceToken || null;
      } catch {
        return null;
      }
    }
    return null;
  }
}
