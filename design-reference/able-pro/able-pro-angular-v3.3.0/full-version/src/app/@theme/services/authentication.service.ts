// angular import
import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { catchError, switchMap, tap } from 'rxjs/operators';

// project import
import { environment } from 'src/environments/environment';
import { User } from '../types/user';

@Injectable({ providedIn: 'root' })
export class AuthenticationService {
  private router = inject(Router);
  private http = inject(HttpClient);

  private currentUserSignal = signal<User | null>(null);
  private loadingSignal = signal<boolean>(false);
  isLogin: boolean = false;

  constructor() {
    // On initialization, check if we have a stored token and fetch user data
    const storedUser = localStorage.getItem('currentUser');
    if (storedUser) {
      try {
        const userData = JSON.parse(storedUser);
        // If we have a serviceToken, fetch the full user data from API
        if (userData.serviceToken) {
          this.fetchCurrentUser().subscribe({
            next: () => {
              this.isLogin = true;
            },
            error: () => {
              // If API call fails, clear stored data and logout
              this.logout();
            }
          });
        }
      } catch (error) {
        console.error('Error parsing stored user data:', error);
        localStorage.removeItem('currentUser');
      }
    }
  }

  /**
   * Fetches the current user data from the API using the stored serviceToken
   * This is called on app initialization and after login
   */
  fetchCurrentUser(): Observable<User> {
    this.loadingSignal.set(true);
    return this.http.get<User>(`${environment.apiUrl}/api/account/me`).pipe(
      tap((data: User) => {
        // Update the signal with the full user data (including role)
        this.currentUserSignal.set(data);
        this.loadingSignal.set(false);
      }),
      catchError((error) => {
        console.error('Error fetching current user:', error);
        this.loadingSignal.set(false);
        // Clear invalid token
        this.logout();
        throw error;
      })
    );
  }

  /**
   * Returns true if user data is currently being loaded
   */
  get isLoading(): boolean {
    return this.loadingSignal();
  }

  public get currentUserValue(): User | null {
    // Access the current user value from the signal
    return this.currentUserSignal();
  }

  public get currentUserName(): string | null {
    const currentUser = this.currentUserValue;
    return currentUser ? currentUser.user.name : 'John Doe';
  }

  login(email: string, password: string) {
    return this.http.post<User>(`${environment.apiUrl}/api/account/login`, { email, password }).pipe(
      tap((data: User) => {
        // Store only minimal data in localStorage for security (token, id, email)
        // Do NOT store role or other sensitive data in localStorage
        const userDetails = {
          id: data.user.id,
          email: data.user.email,
          serviceToken: data.serviceToken
        };
        localStorage.setItem('currentUser', JSON.stringify(userDetails));

        // Fetch full user data from API (including role) and update signal
        this.fetchCurrentUser().subscribe({
          next: () => {
            this.isLogin = true;
          },
          error: (error) => {
            console.error('Error fetching user data after login:', error);
            // Even if fetch fails, we can still set the login state
            // The user data will be fetched on next page load
            this.isLogin = true;
            this.currentUserSignal.set(data);
          }
        });
      })
    );
  }

  isLoggedIn() {
    return this.isLogin;
  }

  logout() {
    // Remove user from local storage to log user out
    localStorage.removeItem('currentUser');
    this.isLogin = false;
    // Update the signal to null
    this.currentUserSignal.set(null);
    this.loadingSignal.set(false);
    this.router.navigate(['/login']);
  }

  /**
   * Gets the serviceToken from localStorage
   * Used by HTTP interceptor to add Authorization header
   */
  getToken(): string | null {
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

  register(id: string, email: string, password: string, firstName?: string, lastName?: string): Observable<User> {
    // Register the user - API automatically sets role to 'User'

    return this.http

      .post<User[]>(`${environment.apiUrl}/api/account/register`, {
        id,
        email,
        password,
        firstName,
        lastName
      })
      .pipe(
        // After successful registration, automatically log the user in
        // Use switchMap to flatten the nested Observable from login()
        switchMap((response) => {
          // API returns array of users, verify the user was registered
          const users: User[] = Array.isArray(response) ? response : [response];
          // eslint-disable-next-line
          const registeredUser = users.find((u: any) => u.email === email);

          if (!registeredUser) {
            throw new Error('Registration successful but user not found in response');
          }

          // Automatically log the user in after successful registration
          return this.login(email, password);
        }),
        catchError((error) => {
          // Re-throw error with more context
          console.error('Registration error:', error);
          throw error;
        })
      );
  }
}
