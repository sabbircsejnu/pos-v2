import { Injectable, Injector, inject } from '@angular/core';
import { HttpRequest, HttpHandler, HttpEvent, HttpInterceptor } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { AuthenticationService } from '../services/authentication.service';

@Injectable()
export class ErrorInterceptor implements HttpInterceptor {
  private injector = inject(Injector);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(request).pipe(
      catchError((err) => {
        if ([401, 403].includes(err.status)) {
          const authService = this.injector.get(AuthenticationService); // Resolve only when needed
          authService.logout();
        }

        const error = err.error.message || err.statusText;
        return throwError(() => error);
      })
    );
  }
}
