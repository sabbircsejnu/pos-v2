import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { WarehouseService } from '../../services/warehouse.service';
import { UserService } from '../../services/user.service';
import { User } from '../../models/user.model';

@Component({
  selector: 'app-warehouse-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './warehouse-form.component.html',
  styleUrls: ['./warehouse-form.component.css']
})
export class WarehouseFormComponent implements OnInit {
  warehouseForm: FormGroup;
  isEditMode = signal(false);
  warehouseId = signal<number | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  managers = signal<User[]>([]);

  constructor(
    private fb: FormBuilder,
    private warehouseService: WarehouseService,
    private userService: UserService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute
  ) {
    this.warehouseForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      address: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(200)]],
      capacity: [null, [Validators.min(0)]],
      managerId: [null]
    });
  }

  ngOnInit(): void {
    this.loadManagers();
    
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.warehouseId.set(+id);
      this.loadWarehouse(+id);
    }
  }

  loadManagers(): void {
    this.userService.getUsers().subscribe({
      next: (response: any) => {
        this.managers.set(response.data || []);
      },
      error: (error: any) => {
        console.error('Error loading managers:', error);
      }
    });
  }

  loadWarehouse(id: number): void {
    this.isLoading.set(true);
    this.warehouseService.getWarehouseById(id).subscribe({
      next: (response: any) => {
        const warehouse = response.data;
        this.warehouseForm.patchValue({
          name: warehouse.name,
          address: warehouse.address,
          capacity: warehouse.capacity,
          managerId: warehouse.managerId
        });
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading warehouse:', error);
        this.error.set('Failed to load warehouse');
        this.isLoading.set(false);
      }
    });
  }

  onSubmit(): void {
    if (this.warehouseForm.invalid) {
      this.warehouseForm.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    const formData = this.warehouseForm.value;
    const request = this.isEditMode()
      ? this.warehouseService.updateWarehouse(this.warehouseId()!, formData)
      : this.warehouseService.createWarehouse(formData);

    request.subscribe({
      next: () => {
        this.isLoading.set(false);
        this.location.back();
      },
      error: (error) => {
        console.error('Error saving warehouse:', error);
        this.error.set(error.error?.error || 'Failed to save warehouse');
        this.isLoading.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }

  getFieldError(fieldName: string): string {
    const field = this.warehouseForm.get(fieldName);
    if (field?.hasError('required')) return `${this.getFieldLabel(fieldName)} is required`;
    if (field?.hasError('minlength')) return `${this.getFieldLabel(fieldName)} is too short`;
    if (field?.hasError('maxlength')) return `${this.getFieldLabel(fieldName)} is too long`;
    if (field?.hasError('min')) return `${this.getFieldLabel(fieldName)} must be positive`;
    return '';
  }

  getFieldLabel(fieldName: string): string {
    const labels: { [key: string]: string } = {
      name: 'Warehouse name',
      address: 'Address',
      capacity: 'Capacity',
      managerId: 'Manager'
    };
    return labels[fieldName] || fieldName;
  }
}
