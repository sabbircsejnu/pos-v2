import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { UserService } from '../../services/user.service';
import { User } from '../../models/user.model';

@Component({
  selector: 'app-user-details',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './user-details.component.html',
  styleUrls: ['./user-details.component.css']
})
export class UserDetailsComponent implements OnInit {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private userService = inject(UserService);

  user = signal<User | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  userId = signal<number | null>(null);

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      if (params['id']) {
        this.userId.set(+params['id']);
        this.loadUser(+params['id']);
      }
    });
  }

  loadUser(id: number): void {
    this.isLoading.set(true);
    this.error.set(null);
    
    this.userService.getUserById(id).subscribe({
      next: (user) => {
        this.user.set(user);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading user:', err);
        this.error.set('Failed to load user details');
        this.isLoading.set(false);
      }
    });
  }

  editUser(): void {
    if (this.userId()) {
      this.router.navigate(['/users/edit', this.userId()]);
    }
  }

  goBack(): void {
    this.router.navigate(['/users']);
  }

  toggleStatus(): void {
    const user = this.user();
    if (!user) return;

    const action = user.isActive ? 'deactivate' : 'activate';
    const confirmMsg = `Are you sure you want to ${action} ${user.name}?`;
    
    if (!confirm(confirmMsg)) return;

    const observable = user.isActive 
      ? this.userService.deactivateUser(user.id)
      : this.userService.activateUser(user.id);

    observable.subscribe({
      next: () => {
        // Reload user to get updated data
        this.loadUser(user.id);
        alert(`User ${action}d successfully!`);
      },
      error: (err) => {
        console.error(`Error ${action}ing user:`, err);
        alert(`Failed to ${action} user`);
      }
    });
  }

  deleteUser(): void {
    const user = this.user();
    if (!user) return;

    const confirmMsg = `Are you sure you want to delete ${user.name}? This action cannot be undone.`;
    if (!confirm(confirmMsg)) return;

    this.userService.deleteUser(user.id).subscribe({
      next: () => {
        alert('User deleted successfully!');
        this.router.navigate(['/users']);
      },
      error: (err) => {
        console.error('Error deleting user:', err);
        alert('Failed to delete user');
      }
    });
  }
}
