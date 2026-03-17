import { Component, Input, Output, EventEmitter, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { UserService } from '../../services/user.service';
import { ChangePasswordDto } from '../../models/user.model';

@Component({
  selector: 'app-change-password-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './change-password-modal.component.html',
  styleUrls: ['./change-password-modal.component.css']
})
export class ChangePasswordModalComponent {
  private fb = inject(FormBuilder);
  private userService = inject(UserService);

  @Input() userId!: number;
  @Input() userName!: string;
  @Output() close = new EventEmitter<void>();
  @Output() success = new EventEmitter<void>();

  passwordForm!: FormGroup;
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  showCurrentPassword = signal(false);
  showNewPassword = signal(false);
  showConfirmPassword = signal(false);

  ngOnInit(): void {
    this.initializeForm();
  }

  initializeForm(): void {
    this.passwordForm = this.fb.group({
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', Validators.required]
    });
  }

  onSubmit(): void {
    if (this.passwordForm.invalid) {
      Object.keys(this.passwordForm.controls).forEach(key => {
        this.passwordForm.get(key)?.markAsTouched();
      });
      return;
    }

    // Validate passwords match
    const newPassword = this.passwordForm.get('newPassword')?.value;
    const confirmPassword = this.passwordForm.get('confirmPassword')?.value;
    
    if (newPassword !== confirmPassword) {
      this.error.set('New password and confirm password do not match');
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    const dto: ChangePasswordDto = {
      currentPassword: this.passwordForm.get('currentPassword')?.value,
      newPassword: newPassword,
      confirmPassword: confirmPassword
    };

    this.userService.changePassword(this.userId, dto).subscribe({
      next: () => {
        this.successMessage.set('Password changed successfully!');
        setTimeout(() => {
          this.success.emit();
          this.closeModal();
        }, 1500);
      },
      error: (err) => {
        console.error('Error changing password:', err);
        this.error.set(err.error?.message || 'Failed to change password. Please check your current password.');
        this.isLoading.set(false);
      }
    });
  }

  closeModal(): void {
    this.close.emit();
  }

  toggleCurrentPasswordVisibility(): void {
    this.showCurrentPassword.set(!this.showCurrentPassword());
  }

  toggleNewPasswordVisibility(): void {
    this.showNewPassword.set(!this.showNewPassword());
  }

  toggleConfirmPasswordVisibility(): void {
    this.showConfirmPassword.set(!this.showConfirmPassword());
  }

  getFieldError(fieldName: string): string {
    const field = this.passwordForm.get(fieldName);
    if (field?.hasError('required')) return `${fieldName} is required`;
    if (field?.hasError('minlength')) {
      const minLength = field.errors?.['minlength'].requiredLength;
      return `Minimum ${minLength} characters required`;
    }
    return '';
  }
}
