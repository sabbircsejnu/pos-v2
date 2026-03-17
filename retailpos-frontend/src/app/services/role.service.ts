import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Role, CreateRoleDto, UpdateRoleDto } from '../models/role.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class RoleService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/roles`;

  roles = signal<Role[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);

  /**
   * Get all roles
   */
  getAllRoles(): Observable<Role[]> {
    return this.http.get<Role[]>(this.apiUrl);
  }

  /**
   * Get role by ID
   */
  getRoleById(id: number): Observable<Role> {
    return this.http.get<Role>(`${this.apiUrl}/${id}`);
  }

  /**
   * Create new role
   */
  createRole(dto: CreateRoleDto): Observable<Role> {
    return this.http.post<Role>(this.apiUrl, dto);
  }

  /**
   * Update existing role
   */
  updateRole(id: number, dto: UpdateRoleDto): Observable<Role> {
    return this.http.put<Role>(`${this.apiUrl}/${id}`, dto);
  }

  /**
   * Delete role
   */
  deleteRole(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  /**
   * Get all available permissions
   */
  getAllPermissions(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/permissions`);
  }
}
