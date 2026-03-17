import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { OutletService } from '../../services/outlet.service';
import { UserService } from '../../services/user.service';
import { User } from '../../models/user.model';

@Component({
  selector: 'app-outlet-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './outlet-form.component.html',
  styleUrls: ['./outlet-form.component.css']
})
export class OutletFormComponent implements OnInit {
  outletForm: FormGroup;
  isEditMode = signal(false);
  outletId = signal<number | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  managers = signal<User[]>([]);

  constructor(
    private fb: FormBuilder,
    private outletService: OutletService,
    private userService: UserService,
    private router: Router,
    private route: ActivatedRoute
  ) {
    this.outletForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      address: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(200)]],
      contactNumber: ['', [Validators.pattern(/^[\d\s\-\+\(\)]+$/)]],
      managerId: [null]
    });
  }

  ngOnInit(): void {
    this.loadManagers();
    
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.outletId.set(+id);
      this.loadOutlet(+id);
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

  loadOutlet(id: number): void {
    this.isLoading.set(true);
    this.outletService.getOutletById(id).subscribe({
      next: (response: any) => {
        const outlet = response.data;
        this.outletForm.patchValue({
          name: outlet.name,
          address: outlet.address,
          contactNumber: outlet.contactNumber,
          managerId: outlet.managerId
        });
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading outlet:', error);
        this.error.set('Failed to load outlet');
        this.isLoading.set(false);
      }
    });
  }

  onSubmit(): void {
    if (this.outletForm.invalid) {
      this.outletForm.markAllAsTouched();
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    const formData = this.outletForm.value;
    const request = this.isEditMode()
      ? this.outletService.updateOutlet(this.outletId()!, formData)
      : this.outletService.createOutlet(formData);

    request.subscribe({
      next: () => {
        this.isLoading.set(false);
        this.router.navigate(['/outlets']);
      },
      error: (error) => {
        console.error('Error saving outlet:', error);
        this.error.set(error.error?.error || 'Failed to save outlet');
        this.isLoading.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/outlets']);
  }

  getFieldError(fieldName: string): string {
    const field = this.outletForm.get(fieldName);
    if (field?.hasError('required')) return `${this.getFieldLabel(fieldName)} is required`;
    if (field?.hasError('minlength')) return `${this.getFieldLabel(fieldName)} is too short`;
    if (field?.hasError('maxlength')) return `${this.getFieldLabel(fieldName)} is too long`;
    if (field?.hasError('pattern')) return `${this.getFieldLabel(fieldName)} format is invalid`;
    return '';
  }

  getFieldLabel(fieldName: string): string {
    const labels: { [key: string]: string } = {
      name: 'Outlet name',
      address: 'Address',
      contactNumber: 'Contact number',
      managerId: 'Manager'
    };
    return labels[fieldName] || fieldName;
  }
}
