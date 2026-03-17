import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { User } from '../../models/user.model';
import { Role } from '../../models/role.model';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './user-list.component.html',
  styleUrls: ['./user-list.component.css']
})
export class UserListComponent implements OnInit {
  private userService = inject(UserService);
  private roleService = inject(RoleService);
  private router = inject(Router);

  users = signal<User[]>([]);
  roles = signal<Role[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // Pagination
  currentPage = signal(1);
  pageSize = signal(10);
  totalCount = signal(0);
  totalPages = signal(0);

  // Filters
  searchQuery = signal('');
  selectedRoleId = signal<number | undefined>(undefined);
  selectedStatus = signal<boolean | undefined>(undefined);

  ngOnInit() {
    this.loadRoles();
    this.loadUsers();
  }

  loadRoles() {
    this.roleService.getAllRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error) => console.error('Error loading roles:', error)
    });
  }

  loadUsers() {
    this.isLoading.set(true);
    this.error.set(null);

    this.userService.getUsers(
      this.currentPage(),
      this.pageSize(),
      this.searchQuery() || undefined,
      this.selectedRoleId(),
      undefined,
      this.selectedStatus()
    ).subscribe({
      next: (response) => {
        this.users.set(response.users);
        this.totalCount.set(response.totalCount);
        this.totalPages.set(response.totalPages);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading users:', error);
        this.error.set('Failed to load users. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  onSearch() {
    this.currentPage.set(1);
    this.loadUsers();
  }

  onFilterChange() {
    this.currentPage.set(1);
    this.loadUsers();
  }

  clearFilters() {
    this.searchQuery.set('');
    this.selectedRoleId.set(undefined);
    this.selectedStatus.set(undefined);
    this.currentPage.set(1);
    this.loadUsers();
  }

  goToPage(page: number) {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.loadUsers();
    }
  }

  createUser() {
    this.router.navigate(['/users/create']);
  }

  editUser(userId: number, event?: Event) {
    event?.stopPropagation();
    this.router.navigate(['/users/edit', userId]);
  }

  viewUser(userId: number, event?: Event) {
    event?.stopPropagation();
    this.router.navigate(['/users', userId]);
  }

  toggleUserStatus(user: User, event?: Event) {
    event?.stopPropagation();
    const confirmMessage = user.isActive 
      ? `Are you sure you want to deactivate ${user.name}?`
      : `Are you sure you want to activate ${user.name}?`;

    if (confirm(confirmMessage)) {
      const action = user.isActive 
        ? this.userService.deactivateUser(user.id)
        : this.userService.activateUser(user.id);

      action.subscribe({
        next: (response) => {
          this.successMessage.set(response.message);
          setTimeout(() => this.successMessage.set(null), 3000);
          this.loadUsers();
        },
        error: (error) => {
          console.error('Error toggling user status:', error);
          this.error.set('Failed to update user status. Please try again.');
          setTimeout(() => this.error.set(null), 3000);
        }
      });
    }
  }

  deleteUser(user: User, event?: Event) {
    event?.stopPropagation();
    if (confirm(`Are you sure you want to delete ${user.name}? This action cannot be undone.`)) {
      this.userService.deleteUser(user.id).subscribe({
        next: (response) => {
          this.successMessage.set(response.message);
          setTimeout(() => this.successMessage.set(null), 3000);
          this.loadUsers();
        },
        error: (error) => {
          console.error('Error deleting user:', error);
          this.error.set('Failed to delete user. Please try again.');
          setTimeout(() => this.error.set(null), 3000);
        }
      });
    }
  }

  get paginationPages(): number[] {
    const pages: number[] = [];
    const maxPagesToShow = 5;
    const currentPage = this.currentPage();
    const totalPages = this.totalPages();

    let startPage = Math.max(1, currentPage - Math.floor(maxPagesToShow / 2));
    let endPage = Math.min(totalPages, startPage + maxPagesToShow - 1);

    if (endPage - startPage < maxPagesToShow - 1) {
      startPage = Math.max(1, endPage - maxPagesToShow + 1);
    }

    for (let i = startPage; i <= endPage; i++) {
      pages.push(i);
    }

    return pages;
  }

  // Helper for template
  get Math() {
    return Math;
  }
}
