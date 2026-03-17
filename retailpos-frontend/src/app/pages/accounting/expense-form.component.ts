import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateExpenseDto, UpdateExpenseDto } from '../../models/accounting.model';

@Component({
  selector: 'app-expense-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './expense-form.component.html',
  styleUrls: ['./expense-form.component.css']
})
export class ExpenseFormComponent implements OnInit {
  expenseForm!: FormGroup;
  isEditMode = signal(false);
  expenseId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);

  readonly categories = ['Rent', 'Utilities', 'Salaries', 'Marketing', 'Supplies', 'Transport', 'Maintenance', 'Other'];

  constructor(
    private fb: FormBuilder,
    public accountingService: AccountingService,
    private router: Router,
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
      this.expenseId.set(+id);
      this.loadExpense(+id);
    }
  }

  private initializeForm(): void {
    this.expenseForm = this.fb.group({
      category: ['', Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      description: [''],
      expenseDate: [new Date().toISOString().split('T')[0], Validators.required],
      outletId: [null]
    });
  }

  private loadExpense(id: number): void {
    this.isLoading.set(true);
    this.accountingService.getExpenseById(id).subscribe({
      next: (res) => {
        if (res.data) {
          this.expenseForm.patchValue({
            category: res.data.category,
            amount: res.data.amount,
            description: res.data.description || '',
            expenseDate: res.data.expenseDate?.split('T')[0] || res.data.expenseDate,
            outletId: res.data.outletId || null
          });
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.router.navigate(['/expenses']);
      }
    });
  }

  onSubmit(): void {
    if (this.expenseForm.invalid) {
      this.markFormGroupTouched(this.expenseForm);
      this.alertService.error('Please fill in all required fields correctly');
      return;
    }

    this.isSubmitting.set(true);
    const v = this.expenseForm.value;

    if (this.isEditMode()) {
      const dto: UpdateExpenseDto = {
        category: v.category,
        amount: v.amount,
        description: v.description || undefined,
        expenseDate: v.expenseDate,
        outletId: v.outletId || undefined
      };
      this.accountingService.updateExpense(this.expenseId()!, dto).subscribe({
        next: () => {
          this.alertService.success('Expense updated successfully');
          this.router.navigate(['/expenses']);
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      const dto: CreateExpenseDto = {
        category: v.category,
        amount: v.amount,
        description: v.description || undefined,
        expenseDate: v.expenseDate,
        outletId: v.outletId || undefined
      };
      this.accountingService.createExpense(dto).subscribe({
        next: () => {
          this.alertService.success('Expense created successfully');
          this.router.navigate(['/expenses']);
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/expenses']);
  }

  isFieldInvalid(fieldName: string): boolean {
    const control = this.expenseForm.get(fieldName);
    return !!(control && control.invalid && control.touched);
  }

  private markFormGroupTouched(formGroup: FormGroup): void {
    Object.keys(formGroup.controls).forEach((key) => {
      formGroup.get(key)?.markAsTouched();
    });
  }
}
