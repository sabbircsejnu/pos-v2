// angular import
import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { email, Field, form, minLength, required } from '@angular/forms/signals';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';
import { DASHBOARD_PATH } from 'src/app/app-config';

// rxjs import
import { first } from 'rxjs/operators';

interface RegisterData {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

@Component({
  selector: 'app-register',
  imports: [CommonModule, SharedModule, RouterModule, Field],
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.scss', '../authentication-1.scss', '../../authentication.scss']
})
export class RegisterComponent {
  private router = inject(Router);
  authenticationService = inject(AuthenticationService);

  // public props
  hide = true;
  coHide = true;

  // Signal-based form model
  registerModel = signal<RegisterData>({
    firstName: '',
    lastName: '',
    email: '',
    password: ''
  });

  // Create form field tree from model
  registerForm = form(this.registerModel, (schemaPath) => {
    required(schemaPath.firstName, { message: 'First name is required' });
    required(schemaPath.lastName, { message: 'Last name is required' });
    required(schemaPath.email, { message: 'Email is required' });
    email(schemaPath.email, { message: 'Please enter a valid email address' });
    required(schemaPath.password, { message: 'Password is required' });
    minLength(schemaPath.password, 8, { message: 'Password must be at least 8 characters' });
  });

  // State signals
  loading = signal(false);
  submitted = signal(false);
  error = signal('');

  constructor() {
    // Redirect to home if already logged in
    if (this.authenticationService.isLoggedIn()) {
      this.router.navigate([DASHBOARD_PATH]);
    }
  }

  onSubmit() {
    this.submitted.set(true);

    // stop here if form is invalid
    if (!this.registerForm().valid()) {
      return;
    }

    this.error.set('');
    this.loading.set(true);

    // Get form values from signals
    const formData = this.registerModel();

    // Generate a simple ID (you can use UUID if preferred)
    const userId = `user_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

    this.authenticationService
      .register(userId, formData.email, formData.password, formData.firstName, formData.lastName)
      .pipe(first())
      .subscribe({
        next: (user) => {
          // Registration successful and user is automatically logged in
          // Role is automatically set to 'User' by the API
          console.log('Registration successful, user logged in with role:', user.user?.role);
          this.router.navigate([DASHBOARD_PATH]);
        },
        error: (error) => {
          // Handle error - could be email already exists, validation error, etc.
          this.error.set(error?.error?.message || error?.message || 'Registration failed. Please try again.');
          this.loading.set(false);
        }
      });
  }
}
