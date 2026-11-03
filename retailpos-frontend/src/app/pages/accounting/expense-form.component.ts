import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AfterViewInit, Component, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateExpenseDto, ExpenseCategoryDto, UpdateExpenseDto } from '../../models/accounting.model';
import { DropdownOption, SearchableDropdownComponent } from '../../components/searchable-dropdown/searchable-dropdown.component';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthorizedLocationDto } from '../../models/report.model';
import { EXPENSE_PAYMENT_SOURCES, ExpensePaymentSourceOption, paymentSourceOption } from './expense-payment-sources';

@Component({
  selector: 'app-expense-form',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, ReactiveFormsModule, SearchableDropdownComponent],
  templateUrl: './expense-form.component.html',
  styleUrls: ['./expense-form.component.css']
})
export class ExpenseFormComponent implements OnInit, AfterViewInit {
  expenseForm!: FormGroup;
  isEditMode = signal(false);
  expenseId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);
  categories = signal<ExpenseCategoryDto[]>([]);

  @ViewChild('sourceDropdown') sourceDropdown?: SearchableDropdownComponent;
  @ViewChild('accountDropdown') accountDropdown?: SearchableDropdownComponent;

  readonly paymentSources = EXPENSE_PAYMENT_SOURCES;
  selectedSource = signal<ExpensePaymentSourceOption>(EXPENSE_PAYMENT_SOURCES[0]);
  /** Active chart accounts for the payment account selector; null when they could not be loaded (manual ID fallback). */
  paymentAccounts = signal<DropdownOption[] | null>(null);
  outlets = signal<AuthorizedLocationDto[]>([]);

  constructor(
    private fb: FormBuilder,
    public accountingService: AccountingService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private outletAccess: UserOutletAccessService
  ) {
    this.initializeForm();
  }

  ngOnInit(): void {
    this.loadCategories();
    this.loadPaymentAccounts();
    this.loadOutlets();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.expenseId.set(+id);
      this.loadExpense(+id);
    }
  }

  private initializeForm(): void {
    this.expenseForm = this.fb.group({
      expenseCategoryId: [null, Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      paymentAccountId: [null, Validators.required],
      referenceNumber: [''],
      description: [''],
      expenseDate: [new Date().toISOString().split('T')[0], Validators.required],
      outletId: [null],
      paymentSource: ['bank', Validators.required]
    });
  }

  /** The selectors are parent-driven: give them their full option lists so focusing shows every choice. */
  ngAfterViewInit(): void {
    setTimeout(() => this.onSourceSearch({ query: '' }));
  }

  isDrawerSource(): boolean {
    return this.selectedSource().code === 'pos_drawer';
  }

  onSourceSearch(event: { query: string }): void {
    const q = (event.query || '').toLowerCase();
    this.sourceDropdown?.setOptions(this.paymentSources.filter(option => !q || option.name.toLowerCase().includes(q)));
    this.sourceDropdown?.setHasMore(false);
  }

  onSourceSelect(option: DropdownOption): void {
    const selected = paymentSourceOption((option as ExpensePaymentSourceOption).code);
    this.selectedSource.set(selected);
    this.expenseForm.patchValue({ paymentSource: selected.code });
  }

  onAccountSearch(event: { query: string }): void {
    const q = (event.query || '').toLowerCase();
    const accounts = this.paymentAccounts() ?? [];
    this.accountDropdown?.setOptions(accounts.filter(option => !q || option.name.toLowerCase().includes(q)));
    this.accountDropdown?.setHasMore(false);
  }

  onAccountSelect(option: DropdownOption): void {
    this.expenseForm.patchValue({ paymentAccountId: option.id });
    this.expenseForm.get('paymentAccountId')?.markAsTouched();
  }

  onAccountCleared(): void {
    this.expenseForm.patchValue({ paymentAccountId: null });
  }

  /** Cash, bank and liability accounts the expense can be paid from; the server validates the account again. */
  private loadPaymentAccounts(): void {
    this.accountingService.getChartAccounts().subscribe({
      next: (res) => {
        this.paymentAccounts.set((res.data || []).map(account => ({
          id: account.id,
          name: `${account.accountCode} · ${account.accountName}`
        })));
        // The account selector renders once the list exists; seed it on the next tick.
        setTimeout(() => this.onAccountSearch({ query: '' }));
      },
      error: () => this.paymentAccounts.set(null)
    });
  }

  private loadOutlets(): void {
    this.outletAccess.load().subscribe({
      next: (res) => this.outlets.set(res.data.outlets),
      error: () => this.outlets.set([])
    });
  }

  private loadCategories(): void {
    this.accountingService.getExpenseCategories().subscribe({
      next: (res) => {
        this.categories.set(res.data || []);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  private loadExpense(id: number): void {
    this.isLoading.set(true);
    this.accountingService.getExpenseById(id).subscribe({
      next: (res) => {
        if (res.data) {
          this.expenseForm.patchValue({
            expenseCategoryId: res.data.expenseCategoryId,
            amount: res.data.amount,
            paymentAccountId: res.data.paymentAccountId || null,
            referenceNumber: res.data.referenceNumber || '',
            description: res.data.description || '',
            expenseDate: res.data.expenseDate?.split('T')[0] || res.data.expenseDate,
            outletId: res.data.outletId || null,
            paymentSource: res.data.paymentSource || 'bank'
          });
          this.selectedSource.set(paymentSourceOption(res.data.paymentSource));
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
    if (this.expenseForm.invalid) {
      this.markFormGroupTouched(this.expenseForm);
      this.alertService.error('Please fill in all required fields correctly');
      return;
    }

    if (this.isDrawerSource() && !this.expenseForm.value.outletId) {
      this.expenseForm.get('outletId')?.markAsTouched();
      this.alertService.error('A POS cash drawer expense needs the outlet whose drawer pays it.');
      return;
    }
    if (this.isSubmitting()) return;

    this.isSubmitting.set(true);
    const v = this.expenseForm.value;

    if (this.isEditMode()) {
      const dto: UpdateExpenseDto = {
        expenseCategoryId: Number(v.expenseCategoryId),
        amount: v.amount,
        paymentAccountId: Number(v.paymentAccountId),
        referenceNumber: v.referenceNumber || undefined,
        description: v.description || undefined,
        expenseDate: v.expenseDate,
        outletId: v.outletId || undefined,
        paymentSource: v.paymentSource || undefined
      };
      this.accountingService.updateExpense(this.expenseId()!, dto).subscribe({
        next: () => {
          this.alertService.success('Expense updated successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      const dto: CreateExpenseDto = {
        expenseCategoryId: Number(v.expenseCategoryId),
        amount: v.amount,
        paymentAccountId: Number(v.paymentAccountId),
        referenceNumber: v.referenceNumber || undefined,
        description: v.description || undefined,
        expenseDate: v.expenseDate,
        outletId: v.outletId || undefined,
        paymentSource: v.paymentSource || undefined
      };
      this.accountingService.createExpense(dto).subscribe({
        next: () => {
          this.alertService.success('Expense created successfully');
          this.location.back();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  getCategoryLabel(category: ExpenseCategoryDto): string {
    return category.expenseAccountCode
      ? `${category.name} (${category.expenseAccountCode})`
      : category.name;
  }

  onCancel(): void {
    this.location.back();
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
