import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { RoleService } from '../../services/role.service';
import { Role } from '../../models/role.model';

@Component({
  selector: 'app-role-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './role-list.component.html',
  styleUrls: ['./role-list.component.css']
})
export class RoleListComponent implements OnInit {
  private roleService = inject(RoleService);
  private router = inject(Router);

  roles = signal<Role[]>([]);
  permissions = signal<string[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  ngOnInit() {
    this.loadRoles();
    this.loadPermissions();
  }

  loadRoles() {
    this.isLoading.set(true);
    this.error.set(null);

    this.roleService.getAllRoles().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading roles:', error);
        this.error.set('Failed to load roles. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  loadPermissions() {
    this.roleService.getAllPermissions().subscribe({
      next: (permissions) => this.permissions.set(permissions),
      error: (error) => console.error('Error loading permissions:', error)
    });
  }

  createRole() {
    this.router.navigate(['/roles/create']);
  }

  editRole(roleId: number, event?: Event) {
    event?.stopPropagation();
    this.router.navigate(['/roles/edit', roleId]);
  }

  deleteRole(role: Role, event?: Event) {
    event?.stopPropagation();
    if (role.userCount > 0) {
      alert(`Cannot delete role '${role.name}' because it is assigned to ${role.userCount} user(s).`);
      return;
    }

    if (confirm(`Are you sure you want to delete the role '${role.name}'? This action cannot be undone.`)) {
      this.roleService.deleteRole(role.id).subscribe({
        next: (response) => {
          this.successMessage.set(response.message);
          setTimeout(() => this.successMessage.set(null), 3000);
          this.loadRoles();
        },
        error: (error) => {
          console.error('Error deleting role:', error);
          this.error.set(error.error?.error || 'Failed to delete role. Please try again.');
          setTimeout(() => this.error.set(null), 3000);
        }
      });
    }
  }

  getPermissionCount(role: Role): number {
    return role.permissions.includes('*') ? this.permissions().length : role.permissions.length;
  }
}
