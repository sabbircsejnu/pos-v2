import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { User, CreateUserDto, UpdateUserDto, ChangePasswordDto, UserListResponse } from '../models/user.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class UserService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/users`;

  users = signal<User[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);

  /**
   * Get paginated list of users with optional filters
   */
  getUsers(
    pageNumber: number = 1,
    pageSize: number = 10,
    search?: string,
    roleId?: number,
    outletId?: number,
    isActive?: boolean
  ): Observable<UserListResponse> {
    let params = new HttpParams()
      .set('pageNumber', pageNumber.toString())
      .set('pageSize', pageSize.toString());

    if (search) params = params.set('search', search);
    if (roleId) params = params.set('roleId', roleId.toString());
    if (outletId !== undefined) params = params.set('outletId', outletId.toString());
    if (isActive !== undefined) params = params.set('isActive', isActive.toString());

    return this.http.get<UserListResponse>(this.apiUrl, { params });
  }

  /**
   * Get user by ID
   */
  getUserById(id: number): Observable<User> {
    return this.http.get<User>(`${this.apiUrl}/${id}`);
  }

  /**
   * Create new user
   */
  createUser(dto: CreateUserDto): Observable<User> {
    return this.http.post<User>(this.apiUrl, dto);
  }

  /**
   * Update existing user
   */
  updateUser(id: number, dto: UpdateUserDto): Observable<User> {
    return this.http.put<User>(`${this.apiUrl}/${id}`, dto);
  }

  /**
   * Delete user (soft delete)
   */
  deleteUser(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  /**
   * Activate user
   */
  activateUser(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/activate`, {});
  }

  /**
   * Deactivate user
   */
  deactivateUser(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/deactivate`, {});
  }

  /**
   * Change user password
   */
  changePassword(id: number, dto: ChangePasswordDto): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${id}/change-password`, dto);
  }

  /**
   * Get users by outlet
   */
  getUsersByOutlet(outletId: number): Observable<User[]> {
    return this.http.get<User[]>(`${this.apiUrl}/outlet/${outletId}`);
  }

  /**
   * Get users by role
   */
  getUsersByRole(roleId: number): Observable<User[]> {
    return this.http.get<User[]>(`${this.apiUrl}/role/${roleId}`);
  }
}
