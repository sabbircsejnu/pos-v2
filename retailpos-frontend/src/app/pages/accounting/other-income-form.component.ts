import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateOtherIncomeEntryDto, UpdateOtherIncomeEntryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-other-income-form',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, ReactiveFormsModule],
  templateUrl: './other-income-form.component.html',
  styleUrls: ['./other-income-form.component.css']
})
export class OtherIncomeFormComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  incomeForm!: FormGroup;
  isEditMode = signal(false);
  incomeId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);

  constructor(
    private fb: FormBuilder,
    public accountingService: AccountingService,
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
      this.incomeId.set(+id);
      this.loadIncome(+id);
    }
  }

  private initializeForm(): void {
    this.incomeForm = this.fb.group({
      incomeCategory: ['', Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      incomeAccountId: [null],
      paymentMethod: [''],
      paymentAccountId: [null, Validators.required],
      referenceNumber: [''],
      description: [''],
      incomeDate: [this.companyToday.todayIso(), Validators.required],
      outletId: [null]
    });
  }

  private loadIncome(id: number): void {
    this.isLoading.set(true);
    this.accountingService.getOtherIncomeById(id).subscribe({
      next: (res) => {
        if (res.data) {
          this.incomeForm.patchValue({
            incomeCategory: res.data.incomeCategory,
            amount: res.data.amount,
            incomeAccountId: res.data.incomeAccountId || null,
            paymentMethod: res.data.paymentMethod || '',
            paymentAccountId: res.data.paymentAccountId || null,
            referenceNumber: res.data.referenceNumber || '',
            description: res.data.description || '',
            incomeDate: res.data.incomeDate?.split('T')[0] || res.data.incomeDate,
            outletId: res.data.outletId || null
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
    if (this.incomeForm.invalid) {
      this.markFormGroupTouched(this.incomeForm);
      this.alertService.error('Please fill in all required fields correctly');
      return;
    }

    this.isSubmitting.set(true);
    const v = this.incomeForm.value;

    if (this.isEditMode()) {
      const dto: UpdateOtherIncomeEntryDto = {
        incomeCategory: v.incomeCategory,
        amount: v.amount,
        incomeAccountId: v.incomeAccountId || undefined,
        paymentMethod: v.paymentMethod || undefined,
        paymentAccountId: v.paymentAccountId || undefined,
        referenceNumber: v.referenceNumber || undefined,
        description: v.description || undefined,
        incomeDate: v.incomeDate,
        outletId: v.outletId || undefined
      };

      this.accountingService.updateOtherIncome(this.incomeId()!, dto).subscribe({
        next: () => {
          this.alertService.success('Other income updated successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      const dto: CreateOtherIncomeEntryDto = {
        incomeCategory: v.incomeCategory,
        amount: v.amount,
        incomeAccountId: v.incomeAccountId || undefined,
        paymentMethod: v.paymentMethod || undefined,
        paymentAccountId: v.paymentAccountId || undefined,
        referenceNumber: v.referenceNumber || undefined,
        description: v.description || undefined,
        incomeDate: v.incomeDate,
        outletId: v.outletId || undefined
      };

      this.accountingService.createOtherIncome(dto).subscribe({
        next: () => {
          this.alertService.success('Other income created successfully');
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

  isFieldInvalid(fieldName: string): boolean {
    const control = this.incomeForm.get(fieldName);
    return !!(control && control.invalid && control.touched);
  }

  private markFormGroupTouched(formGroup: FormGroup): void {
    Object.keys(formGroup.controls).forEach((key) => {
      formGroup.get(key)?.markAsTouched();
    });
  }
}