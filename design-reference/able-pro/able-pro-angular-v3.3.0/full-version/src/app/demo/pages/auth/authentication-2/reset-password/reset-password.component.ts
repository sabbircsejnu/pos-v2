// angular import
import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { form, required, minLength } from '@angular/forms/signals';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-reset-password',
  imports: [CommonModule, SharedModule],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.scss', '../authentication-2.scss', '../../authentication.scss']
})
export class ResetPasswordComponent {
  // public props
  hide = true;
  coHide = true;

  // model holds both password fields
  resetModel = signal({ password: '', confirmPassword: '' });

  // create Signal Form with basic validators
  resetForm = form(this.resetModel, (schema) => {
    required(schema.password, { message: 'Password is required' });
    minLength(schema.password, 8, { message: 'Password must be at least 8 characters' });
    required(schema.confirmPassword, { message: 'Please confirm your password' });
  });

  // UI state
  submitted = signal(false);
  loading = signal(false);
  error = signal('');

  // helpers used by the template
  passwordErrors() {
    return this.resetForm.password().errors();
  }

  confirmErrors() {
    return this.resetForm.confirmPassword().errors();
  }

  setPassword(value: string) {
    this.resetModel.update((m) => ({ ...m, password: value }));
  }

  setConfirmPassword(value: string) {
    this.resetModel.update((m) => ({ ...m, confirmPassword: value }));
  }

  onSubmit() {
    this.submitted.set(true);

    // stop if any field has validation errors
    const passErrs = this.passwordErrors();
    const confErrs = this.confirmErrors();
    if (passErrs.length > 0 || confErrs.length > 0) {
      return;
    }

    const m = this.resetModel();
    // check password match
    if (m.password !== m.confirmPassword) {
      this.error.set('Passwords do not match');
      return;
    }

    this.error.set('');
    this.loading.set(true);

    // TODO: call API to actually reset password. Simulate a short delay here.
    setTimeout(() => {
      this.loading.set(false);
      // success handling can be added here (navigate, show toast, etc.)
    }, 800);
  }
}
