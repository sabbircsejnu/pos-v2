import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { CreateSupplierAdvanceDto, CreateSupplierPaymentDto, SupplierAdvanceDto, SupplierPaymentDto } from '../../models/accounting.model';
import { Supplier } from '../../models/supplier.model';

@Component({
  selector: 'app-supplier-payments',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, ReactiveFormsModule],
  templateUrl: './supplier-payments.component.html',
  styleUrls: ['./supplier-payments.component.css']
})
export class SupplierPaymentsComponent implements OnInit {
  paymentForm!: FormGroup;
  advanceForm!: FormGroup;
  suppliers = signal<Supplier[]>([]);
  payments = signal<SupplierPaymentDto[]>([]);
  advances = signal<SupplierAdvanceDto[]>([]);
  isSubmitting = signal(false);

  constructor(
    private fb: FormBuilder,
    public accountingService: AccountingService,
    private supplierService: SupplierService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {
    this.initializeForms();
  }

  ngOnInit(): void {
    this.supplierService.loadSuppliers();
    this.suppliers.set(this.supplierService.suppliers());
    this.loadSampleData();
  }

  private initializeForms(): void {
    this.paymentForm = this.fb.group({
      supplierId: [null, Validators.required],
      paymentDate: [new Date().toISOString().split('T')[0], Validators.required],
      paymentMethod: ['Cash', Validators.required],
      paymentAccountId: [1, Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      referenceNumber: [''],
      notes: ['']
    });

    this.advanceForm = this.fb.group({
      supplierId: [null, Validators.required],
      paymentDate: [new Date().toISOString().split('T')[0], Validators.required],
      paymentMethod: ['Cash', Validators.required],
      paymentAccountId: [1, Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      referenceNumber: [''],
      notes: ['']
    });
  }

  private loadSampleData(): void {
    this.payments.set([
      {
        id: 1,
        supplierId: 1,
        supplierName: 'Sample Supplier',
        paymentDate: new Date().toISOString(),
        paymentMethod: 'Cash',
        paymentAccountId: 1,
        amount: 1500,
        allocatedAmount: 1500,
        advanceAmount: 0,
        status: 'Draft'
      }
    ]);

    this.advances.set([
      {
        id: 1,
        supplierId: 1,
        supplierName: 'Sample Supplier',
        paymentDate: new Date().toISOString(),
        paymentMethod: 'Cash',
        paymentAccountId: 1,
        amount: 250,
        remainingAmount: 250,
        appliedAmount: 0,
        status: 'Draft'
      }
    ]);
  }

  onCreatePayment(): void {
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      this.alertService.error('Please complete the payment form correctly.');
      return;
    }

    this.isSubmitting.set(true);
    const dto: CreateSupplierPaymentDto = {
      supplierId: Number(this.paymentForm.value.supplierId),
      paymentDate: this.paymentForm.value.paymentDate,
      paymentMethod: this.paymentForm.value.paymentMethod,
      paymentAccountId: Number(this.paymentForm.value.paymentAccountId),
      amount: Number(this.paymentForm.value.amount),
      referenceNumber: this.paymentForm.value.referenceNumber || undefined,
      notes: this.paymentForm.value.notes || undefined,
      allocations: []
    };

    this.accountingService.createSupplierPayment(dto).subscribe({
      next: () => {
        this.alertService.success('Supplier payment created successfully.');
        this.paymentForm.reset({
          supplierId: null,
          paymentDate: new Date().toISOString().split('T')[0],
          paymentMethod: 'Cash',
          paymentAccountId: 1,
          amount: null,
          referenceNumber: '',
          notes: ''
        });
        this.isSubmitting.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  onCreateAdvance(): void {
    if (this.advanceForm.invalid) {
      this.advanceForm.markAllAsTouched();
      this.alertService.error('Please complete the advance form correctly.');
      return;
    }

    this.isSubmitting.set(true);
    const dto: CreateSupplierAdvanceDto = {
      supplierId: Number(this.advanceForm.value.supplierId),
      paymentDate: this.advanceForm.value.paymentDate,
      paymentMethod: this.advanceForm.value.paymentMethod,
      paymentAccountId: Number(this.advanceForm.value.paymentAccountId),
      amount: Number(this.advanceForm.value.amount),
      referenceNumber: this.advanceForm.value.referenceNumber || undefined,
      notes: this.advanceForm.value.notes || undefined
    };

    this.accountingService.createSupplierAdvance(dto).subscribe({
      next: () => {
        this.alertService.success('Supplier advance created successfully.');
        this.advanceForm.reset({
          supplierId: null,
          paymentDate: new Date().toISOString().split('T')[0],
          paymentMethod: 'Cash',
          paymentAccountId: 1,
          amount: null,
          referenceNumber: '',
          notes: ''
        });
        this.isSubmitting.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  onSubmitPayment(payment: SupplierPaymentDto): void {
    this.accountingService.submitSupplierPayment(payment.id).subscribe({
      next: () => {
        this.alertService.success('Supplier payment submitted.');
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  onApprovePayment(payment: SupplierPaymentDto): void {
    this.accountingService.approveSupplierPayment(payment.id).subscribe({
      next: () => {
        this.alertService.success('Supplier payment approved and posted.');
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  onSubmitAdvance(advance: SupplierAdvanceDto): void {
    this.accountingService.submitSupplierAdvance(advance.id).subscribe({
      next: () => {
        this.alertService.success('Supplier advance submitted.');
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  onApproveAdvance(advance: SupplierAdvanceDto): void {
    this.accountingService.approveSupplierAdvance(advance.id).subscribe({
      next: () => {
        this.alertService.success('Supplier advance approved and posted.');
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }
}
