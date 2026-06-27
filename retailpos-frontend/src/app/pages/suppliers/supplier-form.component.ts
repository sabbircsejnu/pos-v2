import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateSupplierDto, UpdateSupplierDto } from '../../models/supplier.model';

@Component({
  selector: 'app-supplier-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './supplier-form.component.html',
  styleUrls: ['./supplier-form.component.css']
})
export class SupplierFormComponent implements OnInit {
  supplierForm!: FormGroup;
  isEditMode = signal(false);
  supplierId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);

  constructor(
    private fb: FormBuilder,
    private supplierService: SupplierService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {
    this.initializeForm();
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.supplierId.set(+id);
      this.loadSupplier(+id);
    }
  }

  initializeForm(): void {
    this.supplierForm = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      contact: ['', Validators.maxLength(100)],
      address: ['', Validators.maxLength(500)],
      creditLimit: [0, [Validators.required, Validators.min(0)]]
    });
  }

  loadSupplier(id: number): void {
    this.isLoading.set(true);
    this.supplierService.getById(id).subscribe({
      next: (response) => {
        if (response.data) {
          this.supplierForm.patchValue({
            name: response.data.name,
            contact: response.data.contact || '',
            address: response.data.address || '',
            creditLimit: response.data.creditLimit
          });
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.location.back();
      }
    });
  }

  onSubmit(): void {
    if (this.supplierForm.invalid) {
      this.markFormGroupTouched(this.supplierForm);
      this.alertService.error('Please fill in all required fields correctly');
      return;
    }

    this.isSubmitting.set(true);
    const formValue = this.supplierForm.value;

    if (this.isEditMode()) {
      const updateDto: UpdateSupplierDto = {
        name: formValue.name,
        contact: formValue.contact || undefined,
        address: formValue.address || undefined,
        creditLimit: formValue.creditLimit
      };

      this.supplierService.update(this.supplierId()!, updateDto).subscribe({
        next: () => {
          this.alertService.success('Supplier updated successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      const createDto: CreateSupplierDto = {
        name: formValue.name,
        contact: formValue.contact || undefined,
        address: formValue.address || undefined,
        creditLimit: formValue.creditLimit
      };

      this.supplierService.create(createDto).subscribe({
        next: () => {
          this.alertService.success('Supplier created successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  onCancel(): void {
    this.location.back();
  }

  private markFormGroupTouched(formGroup: FormGroup): void {
    Object.keys(formGroup.controls).forEach((key) => {
      const control = formGroup.get(key);
      control?.markAsTouched();
    });
  }

  getErrorMessage(fieldName: string): string {
    const control = this.supplierForm.get(fieldName);
    if (control?.hasError('required')) {
      return `${this.getFieldLabel(fieldName)} is required`;
    }
    if (control?.hasError('maxLength')) {
      return `${this.getFieldLabel(fieldName)} is too long`;
    }
    if (control?.hasError('min')) {
      return `${this.getFieldLabel(fieldName)} must be at least 0`;
    }
    return '';
  }

  private getFieldLabel(fieldName: string): string {
    const labels: { [key: string]: string } = {
      name: 'Supplier name',
      contact: 'Contact',
      address: 'Address',
      creditLimit: 'Credit limit'
    };
    return labels[fieldName] || fieldName;
  }

  isFieldInvalid(fieldName: string): boolean {
    const control = this.supplierForm.get(fieldName);
    return !!(control && control.invalid && control.touched);
  }
}
