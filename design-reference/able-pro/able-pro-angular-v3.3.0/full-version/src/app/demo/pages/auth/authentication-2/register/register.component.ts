// angular import
import { ChangeDetectorRef, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { email, Field, form, minLength, required } from '@angular/forms/signals';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';

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
  styleUrls: ['./register.component.scss', '../authentication-2.scss', '../../authentication.scss']
})
export class RegisterComponent {
  authenticationService = inject(AuthenticationService);
  private cd = inject(ChangeDetectorRef);

  // public props
  hide = true;
  coHide = true;
  submitted = signal(false);
  error = signal('');
  showPassword = signal(false);

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

  onSubmit(event: Event) {
    this.submitted.set(true);
    this.error.set('');
    event.preventDefault();
    const credentials = this.registerModel();
    console.log('register user logged in with:', credentials);
    this.cd.detectChanges();
  }

  togglePasswordVisibility() {
    this.showPassword.set(!this.showPassword());
  }
}
