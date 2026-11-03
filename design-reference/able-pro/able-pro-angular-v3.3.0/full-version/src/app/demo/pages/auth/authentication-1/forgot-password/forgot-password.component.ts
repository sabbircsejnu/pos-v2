// angular import
import { ChangeDetectorRef, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { email, Field, form, required } from '@angular/forms/signals';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';

@Component({
  selector: 'app-forgot-password',
  imports: [CommonModule, SharedModule, RouterModule, Field],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.scss', '../authentication-1.scss', '../../authentication.scss']
})
export class ForgotPasswordComponent {
  authenticationService = inject(AuthenticationService);
  private cd = inject(ChangeDetectorRef);

  submitted = signal(false);
  error = signal('');

  forgotModel = signal<{ email: string }>({
    email: ''
  });

  forgotForm = form(this.forgotModel, (schemaPath) => {
    required(schemaPath.email, { message: 'Email is required' });
    email(schemaPath.email, { message: 'Enter a valid email address' });
  });

  onSubmit(event: Event) {
    this.submitted.set(true);
    this.error.set('');
    event.preventDefault();
    const credentials = this.forgotModel();
    console.log('forgot password user logged in with:', credentials);
    this.cd.detectChanges();
  }
}
