import { Component, DestroyRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DropdownOption, SearchableDropdownComponent } from '../../components/searchable-dropdown/searchable-dropdown.component';
import { UiAlertComponent, UiBadgeComponent, UiButtonComponent, UiCardComponent, UiControlDirective, UiFormFieldComponent, UiPageHeaderComponent, UiStateComponent } from '../../components/shared/ui';
import { ExpenseCategoryDto } from '../../models/accounting.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

@Component({
  selector: 'app-expense-categories',
  standalone: true,
  imports: [
    ReactiveFormsModule, SearchableDropdownComponent, UiAlertComponent, UiBadgeComponent, UiButtonComponent,
    UiCardComponent, UiControlDirective, UiFormFieldComponent, UiPageHeaderComponent, UiStateComponent
  ],
  templateUrl: './expense-categories.component.html',
  styleUrls: ['./expense-categories.component.css']
})
export class ExpenseCategoriesComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly accounting = inject(AccountingService);
  private readonly alerts = inject(AlertService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild('accountDropdown') accountDropdown?: SearchableDropdownComponent;

  /** Business-wide configuration: separate from the permissions that enter expenses. The server enforces it too. */
  readonly canManage = this.auth.hasPermission('accounting.expense_categories.manage');
  readonly canViewChart = this.auth.hasPermission('accounting.chart_accounts.view');

  readonly categories = signal<ExpenseCategoryDto[]>([]);
  readonly categoriesState = signal<'loading' | 'ready' | 'error'>('loading');
  readonly categoriesError = signal<string | null>(null);

  readonly eligibleAccounts = signal<DropdownOption[]>([]);
  readonly accountsState = signal<'loading' | 'ready' | 'error'>('loading');
  readonly accountsError = signal<string | null>(null);

  readonly editing = signal<ExpenseCategoryDto | null>(null);
  readonly selectedAccountId = signal<number | null>(null);
  readonly isActiveValue = signal(true);
  readonly isSaving = signal(false);
  readonly formError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    expenseAccountId: this.fb.control<number | null>(null, Validators.required),
    isActive: [true]
  });

  /** A saved mapping the lookup no longer offers: it stays usable for metadata edits, but cannot be newly chosen. */
  readonly legacyAccount = computed<DropdownOption | null>(() => {
    const category = this.editing();
    if (!category || this.accountsState() !== 'ready') return null;
    if (this.eligibleAccounts().some(account => account.id === category.expenseAccountId)) return null;
    return { id: category.expenseAccountId, name: this.accountLabel(category) };
  });

  readonly isLegacySelection = computed(() => {
    const legacy = this.legacyAccount();
    return !!legacy && this.selectedAccountId() === legacy.id;
  });

  readonly isRemapping = computed(() => {
    const category = this.editing();
    return !!category && this.selectedAccountId() !== null && this.selectedAccountId() !== category.expenseAccountId;
  });

  readonly isDeactivating = computed(() => !!this.editing()?.isActive && !this.isActiveValue());

  ngOnInit(): void {
    this.form.controls.expenseAccountId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => this.selectedAccountId.set(value));
    this.form.controls.isActive.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => this.isActiveValue.set(value));
    this.loadCategories();
    if (this.canManage) {
      this.loadAccounts();
    }
  }

  loadCategories(): void {
    this.categoriesState.set('loading');
    this.categoriesError.set(null);
    this.accounting.getExpenseCategories(true).subscribe({
      next: res => {
        this.categories.set(res.data ?? []);
        this.categoriesState.set('ready');
      },
      error: err => {
        this.categoriesError.set(this.errors.extractErrorMessage(err));
        this.categoriesState.set('error');
      }
    });
  }

  loadAccounts(): void {
    this.accountsState.set('loading');
    this.accountsError.set(null);
    this.accounting.getEligibleExpenseAccounts().subscribe({
      next: res => {
        this.eligibleAccounts.set((res.data ?? []).map(account => ({
          id: account.id,
          name: `${account.accountCode} — ${account.accountName}`
        })));
        this.accountsState.set('ready');
        this.syncDropdownSoon();
      },
      error: err => {
        this.accountsError.set(this.errors.extractErrorMessage(err));
        this.accountsState.set('error');
      }
    });
  }

  accountLabel(category: ExpenseCategoryDto): string {
    return category.expenseAccountCode
      ? `${category.expenseAccountCode} — ${category.expenseAccountName ?? ''}`
      : `Account ${category.expenseAccountId}`;
  }

  startCreate(): void {
    this.editing.set(null);
    this.formError.set(null);
    this.form.reset({ name: '', expenseAccountId: null, isActive: true });
    this.syncDropdownSoon();
  }

  startEdit(category: ExpenseCategoryDto): void {
    this.editing.set(category);
    this.formError.set(null);
    this.form.reset({ name: category.name, expenseAccountId: category.expenseAccountId, isActive: category.isActive });
    this.syncDropdownSoon();
  }

  onAccountSearch(event: { query: string }): void {
    const query = (event.query || '').toLowerCase();
    this.accountDropdown?.setOptions(this.dropdownOptions().filter(option => !query || option.name.toLowerCase().includes(query)));
    this.accountDropdown?.setHasMore(false);
  }

  onAccountSelect(option: DropdownOption): void {
    this.form.patchValue({ expenseAccountId: option.id });
    this.form.controls.expenseAccountId.markAsTouched();
  }

  onAccountCleared(): void {
    this.form.patchValue({ expenseAccountId: null });
    this.form.controls.expenseAccountId.markAsTouched();
    this.onAccountSearch({ query: '' });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.isSaving()) return;

    const value = this.form.getRawValue();
    // The status comes from the form, so editing an inactive category never reactivates it by accident.
    const dto = { name: value.name.trim(), expenseAccountId: value.expenseAccountId!, isActive: value.isActive };
    const editing = this.editing();
    const request = editing
      ? this.accounting.updateExpenseCategory(editing.id, dto)
      : this.accounting.createExpenseCategory(dto);

    this.isSaving.set(true);
    this.formError.set(null);
    request.subscribe({
      next: () => {
        this.alerts.success(editing ? 'Category updated' : 'Category created');
        this.isSaving.set(false);
        this.startCreate();
        this.loadCategories();
      },
      error: err => {
        // Keep what was entered; the server's message says what to fix.
        this.formError.set(this.errors.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  private dropdownOptions(): DropdownOption[] {
    const legacy = this.legacyAccount();
    return legacy ? [legacy, ...this.eligibleAccounts()] : this.eligibleAccounts();
  }

  /** The selector may only render on this change (no eligible accounts until a legacy mapping is opened), so wait a tick. */
  private syncDropdownSoon(): void {
    setTimeout(() => this.syncDropdown());
  }

  /** The selector owns its text, so put the chosen account's label back whenever the selection changes from outside. */
  private syncDropdown(): void {
    const dropdown = this.accountDropdown;
    if (!dropdown) return;

    const selected = this.form.controls.expenseAccountId.value;
    const options = this.dropdownOptions();
    dropdown.setOptions(options);
    dropdown.setHasMore(false);
    dropdown.selectedId = selected;
    dropdown.searchQuery = options.find(option => option.id === selected)?.name ?? '';
  }
}
