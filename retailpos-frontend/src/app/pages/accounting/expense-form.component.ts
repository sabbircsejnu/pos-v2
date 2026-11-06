import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AfterViewInit, Component, OnInit, ViewChild, computed, signal, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateExpenseDto, ExpenseCategoryDto, UpdateExpenseDto } from '../../models/accounting.model';
import { DropdownOption, SearchableDropdownComponent } from '../../components/searchable-dropdown/searchable-dropdown.component';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthorizedLocationDto, AuthorizedOutletsDto } from '../../models/report.model';
import { UiAlertComponent, UiButtonComponent, UiControlDirective, UiFormFieldComponent, UiStateComponent } from '../../components/shared/ui';
import { ExpenseAttachmentsComponent } from './expense-attachments.component';
import { EXPENSE_PAYMENT_SOURCES, ExpensePaymentSourceOption, paymentSourceOption } from './expense-payment-sources';

export type ExpenseLocationType = 'outlet' | 'warehouse' | 'business';

@Component({
  selector: 'app-expense-form',
  standalone: true,
  imports: [
    UiDateInputDirective, CommonModule, ReactiveFormsModule, SearchableDropdownComponent,
    UiAlertComponent, UiButtonComponent, UiControlDirective, UiFormFieldComponent, UiStateComponent,
    ExpenseAttachmentsComponent
  ],
  templateUrl: './expense-form.component.html',
  styleUrls: ['./expense-form.component.css']
})
export class ExpenseFormComponent implements OnInit, AfterViewInit {
  private readonly companyToday = inject(CompanyDateService);
  expenseForm!: FormGroup;
  isEditMode = signal(false);
  expenseId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);
  categories = signal<ExpenseCategoryDto[]>([]);

  @ViewChild('sourceDropdown') sourceDropdown?: SearchableDropdownComponent;
  @ViewChild('accountDropdown') accountDropdown?: SearchableDropdownComponent;
  @ViewChild('attachments') attachments?: ExpenseAttachmentsComponent;

  readonly paymentSources = EXPENSE_PAYMENT_SOURCES;
  selectedSource = signal<ExpensePaymentSourceOption>(EXPENSE_PAYMENT_SOURCES[0]);
  /** Accounts accounting has enabled for expenses; the server validates the chosen one again. */
  paymentAccounts = signal<DropdownOption[]>([]);
  accountsState = signal<'loading' | 'ready' | 'error'>('loading');
  accountsError = signal<string | null>(null);
  paymentAccountNotice = signal<string | null>(null);
  private accountsRequest = 0;

  expenseNumber = signal<string | null>(null);

  locationAccess = signal<AuthorizedOutletsDto | null>(null);
  locationsState = signal<'loading' | 'ready' | 'error'>('loading');
  locationsError = signal<string | null>(null);
  locationType = signal<ExpenseLocationType | ''>('');
  isLocationLocked = signal(false);
  /** The one location of a locked selector; on edit it may not be selectable for a new expense. */
  lockedLocation = signal<AuthorizedLocationDto | null>(null);

  locationTypeOptions = computed(() => {
    const options: { value: ExpenseLocationType; label: string }[] = [];
    const access = this.locationAccess();
    const locked = this.isLocationLocked() ? this.locationType() : null;
    if (locked === 'outlet' || (!locked && access?.outlets.length)) options.push({ value: 'outlet', label: 'Outlet' });
    if (locked === 'warehouse' || (!locked && access?.warehouses.length)) options.push({ value: 'warehouse', label: 'Warehouse' });
    if (locked === 'business' || (!locked && access?.isGlobalAccess)) options.push({ value: 'business', label: 'Business-level (no location)' });
    return options;
  });

  locationOptions = computed<AuthorizedLocationDto[]>(() => {
    const locked = this.lockedLocation();
    if (locked) return [locked];
    const access = this.locationAccess();
    if (this.locationType() === 'outlet') return access?.outlets ?? [];
    if (this.locationType() === 'warehouse') return access?.warehouses ?? [];
    return [];
  });

  hasNoEligibleLocation = computed(() => {
    const access = this.locationAccess();
    return !!access && !this.isEditMode() && !access.isGlobalAccess
      && access.outlets.length === 0 && access.warehouses.length === 0;
  });

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

    // The payment account list needs the location, so it loads once that is known (see below).
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.expenseId.set(+id);
      this.loadExpense(+id);
    } else {
      this.loadLocations();
    }
  }

  private initializeForm(): void {
    this.expenseForm = this.fb.group({
      expenseCategoryId: [null, Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      paymentAccountId: [null, Validators.required],
      referenceNumber: [''],
      description: [''],
      expenseDate: [this.companyToday.todayIso(), Validators.required],
      locationType: [''],
      locationId: [null],
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
    this.loadPaymentAccounts();
  }

  /** True when the POS drawer's one configured account is already chosen, so there is nothing to select. */
  isDrawerAccountFixed(): boolean {
    return this.isDrawerSource() && this.paymentAccounts().length === 1
      && this.paymentAccounts()[0].id === Number(this.expenseForm.value.paymentAccountId);
  }

  emptyAccountsMessage(): string {
    if (!this.isDrawerSource()) {
      return `Ask accounting to map and enable an account for ${this.selectedSource().name} in Accounting Configuration.`;
    }
    return this.locationType() === 'outlet' && this.expenseForm.getRawValue().locationId
      ? 'Ask accounting to map a cash account for this outlet in Accounting Configuration.'
      : 'Select the outlet whose cash drawer pays this expense.';
  }

  onAccountSearch(event: { query: string }): void {
    const q = (event.query || '').toLowerCase();
    const accounts = this.paymentAccounts();
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

  /** The list depends on the source and the location, so a change to either reloads it; only the latest answer counts. */
  loadPaymentAccounts(): void {
    const request = ++this.accountsRequest;
    const type = this.locationType();
    const locationId = this.expenseForm.getRawValue().locationId;
    this.accountsState.set('loading');
    this.accountsError.set(null);
    this.accountingService.getExpensePaymentAccounts({
      paymentSource: this.selectedSource().code,
      outletId: type === 'outlet' && locationId ? Number(locationId) : undefined,
      warehouseId: type === 'warehouse' && locationId ? Number(locationId) : undefined
    }).subscribe({
      next: (res) => {
        if (request !== this.accountsRequest) return;
        this.paymentAccounts.set((res.data || []).map(account => ({
          id: account.id,
          name: `${account.accountCode} · ${account.accountName}`
        })));
        this.accountsState.set('ready');
        this.reconcileSelectedPaymentAccount();
        // The account selector renders once the list exists; seed it on the next tick.
        setTimeout(() => this.onAccountSearch({ query: '' }));
      },
      error: (err) => {
        if (request !== this.accountsRequest) return;
        this.accountsError.set(this.errorHandler.extractErrorMessage(err));
        this.accountsState.set('error');
      }
    });
  }

  /**
   * Never keeps an account the server would refuse, and says so rather than silently changing it. The POS drawer has
   * exactly one possible account, so that one is chosen for the user.
   */
  private reconcileSelectedPaymentAccount(): void {
    this.paymentAccountNotice.set(null);
    const selected = this.expenseForm.value.paymentAccountId;
    const accounts = this.paymentAccounts();
    if (selected && !accounts.some(account => account.id === Number(selected))) {
      this.expenseForm.patchValue({ paymentAccountId: null });
      this.accountDropdown?.clear();
      this.paymentAccountNotice.set('The selected payment account cannot be used with this payment source at this location. Choose another one.');
    }
    if (this.isDrawerSource() && accounts.length === 1 && !this.expenseForm.value.paymentAccountId) {
      this.expenseForm.patchValue({ paymentAccountId: accounts[0].id });
    }
  }

  loadLocations(): void {
    this.locationsState.set('loading');
    this.locationsError.set(null);
    this.outletAccess.load('ExpenseOperation').subscribe({
      next: (res) => {
        this.applyLocationAccess(res.data);
        this.locationsState.set('ready');
        this.loadPaymentAccounts();
      },
      error: (err) => {
        this.locationsError.set(this.errorHandler.extractErrorMessage(err));
        this.locationsState.set('error');
        this.loadPaymentAccounts();
      }
    });
  }

  private applyLocationAccess(access: AuthorizedOutletsDto): void {
    this.locationAccess.set(access);
    const eligible = [...access.outlets, ...access.warehouses];

    if (!access.isGlobalAccess && eligible.length === 1) {
      const type = access.outlets.length === 1 ? 'outlet' : 'warehouse';
      this.lockLocation(type, eligible[0]);
    } else if (access.isGlobalAccess) {
      this.setLocationType('business');
    } else if (access.outlets.length === 0 || access.warehouses.length === 0) {
      this.setLocationType(access.outlets.length > 0 ? 'outlet' : 'warehouse');
    }
  }

  private lockLocation(type: ExpenseLocationType, location: AuthorizedLocationDto | null): void {
    this.locationType.set(type);
    this.lockedLocation.set(location);
    this.isLocationLocked.set(true);
    this.expenseForm.patchValue({ locationType: type, locationId: location?.id ?? null });
    this.expenseForm.get('locationType')?.disable();
    this.expenseForm.get('locationId')?.disable();
  }

  private setLocationType(type: ExpenseLocationType): void {
    this.locationType.set(type);
    this.expenseForm.patchValue({ locationType: type, locationId: null });
  }

  onLocationTypeChange(): void {
    this.setLocationType(this.expenseForm.get('locationType')?.value);
    this.loadPaymentAccounts();
  }

  onLocationChange(): void {
    this.loadPaymentAccounts();
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
            paymentSource: res.data.paymentSource || 'bank'
          });
          this.selectedSource.set(paymentSourceOption(res.data.paymentSource));
          this.expenseNumber.set(res.data.expenseNumber ?? null);

          // The reference carries the location, so it is fixed once the expense exists.
          if (res.data.outletId) {
            this.lockLocation('outlet', { id: res.data.outletId, name: res.data.outletName ?? '', type: 'outlet' });
          } else if (res.data.warehouseId) {
            this.lockLocation('warehouse', { id: res.data.warehouseId, name: res.data.warehouseName ?? '', type: 'warehouse' });
          } else {
            this.lockLocation('business', null);
          }
          this.locationsState.set('ready');
          this.loadPaymentAccounts();
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

    const v = this.expenseForm.getRawValue();
    const type = this.locationType();
    if (this.isDrawerSource() && (type !== 'outlet' || !v.locationId)) {
      this.alertService.error('A POS cash drawer expense needs the outlet whose drawer pays it.');
      return;
    }
    if (!type || (type !== 'business' && !v.locationId)) {
      this.alertService.error('Select the outlet or warehouse this expense belongs to.');
      return;
    }
    if (this.isSubmitting()) return;

    this.isSubmitting.set(true);
    const location = {
      outletId: type === 'outlet' ? Number(v.locationId) : undefined,
      warehouseId: type === 'warehouse' ? Number(v.locationId) : undefined
    };

    if (this.isEditMode()) {
      const dto: UpdateExpenseDto = {
        expenseCategoryId: Number(v.expenseCategoryId),
        amount: v.amount,
        paymentAccountId: Number(v.paymentAccountId),
        referenceNumber: v.referenceNumber || undefined,
        description: v.description || undefined,
        expenseDate: v.expenseDate,
        ...location,
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
        ...location,
        paymentSource: v.paymentSource || undefined
      };
      this.accountingService.createExpense(dto).subscribe({
        next: (res) => this.afterCreate(res.data.id),
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  /** The expense is saved at this point, so a refused file sends the user to its edit page, never back to a blank form. */
  private afterCreate(expenseId: number): void {
    const uploads = this.attachments?.uploadPending(expenseId) ?? of<string[]>([]);
    uploads.subscribe(failures => {
      if (failures.length > 0) {
        this.alertService.error(`The expense was saved, but some files were not attached. ${failures.join(' ')}`);
        this.router.navigate(['/expenses/edit', expenseId]);
        return;
      }
      this.alertService.success('Expense created successfully');
      this.location.back();
    });
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
