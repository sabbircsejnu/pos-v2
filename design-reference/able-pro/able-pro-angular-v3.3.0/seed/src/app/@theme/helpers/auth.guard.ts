import { Injectable, inject } from '@angular/core';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot, CanActivateChild } from '@angular/router';
import { Observable, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

import { AuthenticationService } from '../services/authentication.service';
import { User } from '../types/user';

@Injectable({ providedIn: 'root' })
export class AuthGuardChild implements CanActivateChild {
  private router = inject(Router);
  private authenticationService = inject(AuthenticationService);

  /**
   * Determines whether a child route can be activated based on user authentication and authorization.
   *
   * @param route - The activated route snapshot that contains the route configuration and parameters.
   * @param state - The router state snapshot that contains the current router state.
   * @returns A boolean or Observable<boolean> indicating whether the route can be activated. Redirects to an appropriate page if not.
   *
   * If the user is logged in and their role is authorized for the route, returns true.
   * If the user is logged in but not authorized, redirects to the unauthorized page and returns false.
   * If the user is not logged in, redirects to the login page with the return URL and returns false.
   * If user data is being loaded, waits for it to complete before making a decision.
   */
  canActivateChild(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | Observable<boolean> {
    const currentUser = this.authenticationService.currentUserValue;
    const hasToken = !!this.authenticationService.getToken();

    // If we have a token but no user data, fetch it first
    if (hasToken && !currentUser && !this.authenticationService.isLoading) {
      return this.authenticationService.fetchCurrentUser().pipe(
        map((user) => {
          this.authenticationService.isLogin = true;
          return this.checkAuthorization(route, state, user);
        }),
        catchError(() => {
          // If fetch fails, redirect to login
          this.router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
          return of(false);
        })
      );
    }

    // If user data is currently loading, wait for it to complete
    if (this.authenticationService.isLoading) {
      // Return an observable that waits for loading to complete
      return new Observable<boolean>((observer) => {
        let attempts = 0;
        const maxAttempts = 50; // 5 seconds max (50 * 100ms)

        const checkInterval = setInterval(() => {
          attempts++;
          if (!this.authenticationService.isLoading || attempts >= maxAttempts) {
            clearInterval(checkInterval);
            const user = this.authenticationService.currentUserValue;
            if (user && this.authenticationService.isLoggedIn()) {
              observer.next(this.checkAuthorization(route, state, user));
            } else {
              this.router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
              observer.next(false);
            }
            observer.complete();
          }
        }, 100);
      });
    }

    // If we have user data, check authorization
    if (currentUser && this.authenticationService.isLoggedIn()) {
      return this.checkAuthorization(route, state, currentUser);
    }

    // User not logged in, redirect to login page
    this.router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  /**
   * Checks if the user is authorized for the route based on their role
   */
  private checkAuthorization(route: ActivatedRouteSnapshot, state: RouterStateSnapshot, currentUser: User): boolean {
    const { roles } = route.data;
    if (roles && !roles.includes(currentUser.user.role)) {
      // User not authorized, redirect to unauthorized page
      this.router.navigate(['/unauthorized']);
      return false;
    }
    // User is logged in and authorized for child routes
    return true;
  }
}
