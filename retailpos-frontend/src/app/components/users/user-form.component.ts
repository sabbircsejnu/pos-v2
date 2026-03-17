import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { CreateUserDto, UpdateUserDto } from '../../models/user.model';
import { Role } from '../../models/role.model';

@Component({
  selector: 'app-user-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './user-form.component.html',
  styleUrls: ['./user-form.component.css']
})
export class UserFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private userService = inject(UserService);
  private roleService = inject(RoleService);

  userForm!: FormGroup;
  isEditMode = signal(false);
  userId = signal<number | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  roles = signal<Role[]>([]);
  showPassword = signal(false);
  showConfirmPassword = signal(false);

  ngOnInit(): void {
    this.initializeForm();
    this.loadRoles();
    
    // Check if edit mode
    this.route.params.subscribe(params => {
      if (params['id']) {
        this.isEditMode.set(true);
        this.userId.set(+params['id']);
        this.loadUser(+params['id']);
      }
    });
  }

  initializeForm(): void {
    this.userForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]],
      roleId: [null, Validators.required],
      outletId: [null],
      isActive: [true]
    });
  }

  loadRoles(): void {
    this.roleService.getAllRoles().subscribe({
      next: (roles) => {
        this.roles.set(roles);
      },
      error: (err) => {
        console.error('Error loading roles:', err);
        this.error.set('Failed to load roles');
      }
    });
  }

  loadUser(id: number): void {
    this.isLoading.set(true);
    this.userService.getUserById(id).subscribe({
      next: (user) => {
        // Remove password validators in edit mode
        this.userForm.get('password')?.clearValidators();
        this.userForm.get('confirmPassword')?.clearValidators();
        this.userForm.get('password')?.updateValueAndValidity();
        this.userForm.get('confirmPassword')?.updateValueAndValidity();
        
        this.userForm.patchValue({
          name: user.name,
          email: user.email,
          roleId: user.roleId,
          outletId: user.outletId,
          isActive: user.isActive
        });
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading user:', err);
        this.error.set('Failed to load user');
        this.isLoading.set(false);
      }
    });
  }

  onSubmit(): void {
    if (this.userForm.invalid) {
      Object.keys(this.userForm.controls).forEach(key => {
        this.userForm.get(key)?.markAsTouched();
      });
      return;
    }

    // Validate passwords match in create mode
    if (!this.isEditMode()) {
      const password = this.userForm.get('password')?.value;
      const confirmPassword = this.userForm.get('confirmPassword')?.value;
      if (password !== confirmPassword) {
        this.error.set('Passwords do not match');
        return;
      }
    }

    this.isLoading.set(true);
    this.error.set(null);

    if (this.isEditMode() && this.userId()) {
      this.updateUser();
    } else {
      this.createUser();
    }
  }

  createUser(): void {
    const formValue = this.userForm.value;
    const dto: CreateUserDto = {
      name: formValue.name,
      email: formValue.email,
      password: formValue.password,
      roleId: formValue.roleId,
      outletId: formValue.outletId,
      isActive: formValue.isActive
    };

    this.userService.createUser(dto).subscribe({
      next: (user) => {
        this.successMessage.set('User created successfully!');
        setTimeout(() => {
          this.router.navigate(['/users']);
        }, 1500);
      },
      error: (err) => {
        console.error('Error creating user:', err);
        this.error.set(err.error?.message || 'Failed to create user');
        this.isLoading.set(false);
      }
    });
  }

  updateUser(): void {
    const formValue = this.userForm.value;
    const dto: UpdateUserDto = {
      name: formValue.name,
      email: formValue.email,
      roleId: formValue.roleId,
      outletId: formValue.outletId,
      isActive: formValue.isActive
    };

    this.userService.updateUser(this.userId()!, dto).subscribe({
      next: (user) => {
        this.successMessage.set('User updated successfully!');
        setTimeout(() => {
          this.router.navigate(['/users']);
        }, 1500);
      },
      error: (err) => {
        console.error('Error updating user:', err);
        this.error.set(err.error?.message || 'Failed to update user');
        this.isLoading.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/users']);
  }

  togglePasswordVisibility(): void {
    this.showPassword.set(!this.showPassword());
  }

  toggleConfirmPasswordVisibility(): void {
    this.showConfirmPassword.set(!this.showConfirmPassword());
  }

  getFieldError(fieldName: string): string {
    const field = this.userForm.get(fieldName);
    if (field?.hasError('required')) return `${fieldName} is required`;
    if (field?.hasError('email')) return 'Invalid email format';
    if (field?.hasError('minlength')) {
      const minLength = field.errors?.['minlength'].requiredLength;
      return `Minimum ${minLength} characters required`;
    }
    return '';
  }
}
