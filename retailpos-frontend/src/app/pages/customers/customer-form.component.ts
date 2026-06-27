import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CustomerService } from '../../services/customer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CustomerDto, CreateCustomerDto, UpdateCustomerDto } from '../../models/customer.model';

@Component({
  selector: 'app-customer-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './customer-form.component.html',
  styleUrls: ['./customer-form.component.css']
})
export class CustomerFormComponent implements OnInit {
  isEditMode = signal(false);
  customerId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);
  showPointsInput = signal(false);
  pointsToAdd = signal<number>(0);
  isAddingPoints = signal(false);

  // Form fields
  name = signal('');
  phone = signal('');
  email = signal('');
  loyaltyPoints = signal(0);

  // Validation
  nameError = signal('');
  emailError = signal('');

  constructor(
    private customerService: CustomerService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.customerId.set(+id);
      this.loadCustomer(+id);
    }
  }

  loadCustomer(id: number): void {
    this.isLoading.set(true);
    this.customerService.getById(id).subscribe({
      next: (response) => {
        const c: CustomerDto = response.data;
        if (c) {
          this.name.set(c.name);
          this.phone.set(c.phone || '');
          this.email.set(c.email || '');
          this.loyaltyPoints.set(c.loyaltyPoints);
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

  validate(): boolean {
    let valid = true;
    this.nameError.set('');
    this.emailError.set('');

    if (!this.name().trim()) {
      this.nameError.set('Customer name is required');
      valid = false;
    }
    if (this.email() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email())) {
      this.emailError.set('Please enter a valid email address');
      valid = false;
    }
    return valid;
  }

  onSubmit(): void {
    if (!this.validate()) {
      this.alertService.error('Please fix the validation errors');
      return;
    }

    this.isSubmitting.set(true);

    if (this.isEditMode()) {
      const dto: UpdateCustomerDto = {
        name: this.name().trim(),
        phone: this.phone() || undefined,
        email: this.email() || undefined
      };
      this.customerService.update(this.customerId()!, dto).subscribe({
        next: () => {
          this.alertService.success('Customer updated successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      const dto: CreateCustomerDto = {
        name: this.name().trim(),
        phone: this.phone() || undefined,
        email: this.email() || undefined
      };
      this.customerService.create(dto).subscribe({
        next: () => {
          this.alertService.success('Customer created successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  togglePointsInput(): void {
    this.showPointsInput.set(!this.showPointsInput());
    this.pointsToAdd.set(0);
  }

  onAddPoints(): void {
    const pts = this.pointsToAdd();
    if (!pts || pts <= 0) {
      this.alertService.error('Enter a valid number of points to add');
      return;
    }
    this.isAddingPoints.set(true);
    this.customerService.addPoints(this.customerId()!, pts).subscribe({
      next: (response) => {
        this.alertService.success(`${pts} points added successfully`);
        this.loyaltyPoints.set(response.data?.loyaltyPoints ?? this.loyaltyPoints() + pts);
        this.showPointsInput.set(false);
        this.isAddingPoints.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isAddingPoints.set(false);
      }
    });
  }

  onCancel(): void {
    this.location.back();
  }
}
