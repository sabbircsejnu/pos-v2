import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ChartAccountRow, UpdateChartAccountDto } from '../../models/accounting.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  UiAlertComponent,
  UiButtonComponent,
  UiConfirmDialogComponent,
  UiControlDirective,
  UiDataTableComponent,
  UiDialogComponent,
  UiFormFieldComponent,
  UiPageHeaderComponent,
  UiTableCellDirective,
  UiTableColumn,
  UiTableRowActionsDirective
} from '../../components/shared/ui';

const ACCOUNT_TYPES = [
  { value: 'Asset', label: 'Asset', normalBalance: 'Debit' },
  { value: 'Liability', label: 'Liability', normalBalance: 'Credit' },
  { value: 'Equity', label: 'Equity', normalBalance: 'Credit' },
  { value: 'Income', label: 'Income', normalBalance: 'Credit' },
  { value: 'CostOfGoodsSold', label: 'Cost of Goods Sold', normalBalance: 'Debit' },
  { value: 'Expense', label: 'Expense', normalBalance: 'Debit' }
] as const;

interface AccountTableRow {
  id: number;
  accountCode: string;
  accountName: string;
  accountType: string;
  typeLabel: string;
  parentLabel: string;
  origin: 'system' | 'custom';
  status: 'active' | 'inactive';
  depth: number;
  account: ChartAccountRow;
}

function byDisplayOrder(a: ChartAccountRow, b: ChartAccountRow): number {
  return a.displayOrder - b.displayOrder || a.accountCode.localeCompare(b.accountCode);
}

/** Business Chart of Accounts: the hierarchy, its maintenance and status. Accounts are never deleted. */
@Component({
  selector: 'app-chart-of-accounts',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    UiAlertComponent,
    UiButtonComponent,
    UiConfirmDialogComponent,
    UiControlDirective,
    UiDataTableComponent,
    UiDialogComponent,
    UiFormFieldComponent,
    UiPageHeaderComponent,
    UiTableCellDirective,
    UiTableRowActionsDirective
  ],
  templateUrl: './chart-of-accounts.component.html'
})
export class ChartOfAccountsComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly accounting = inject(AccountingService);
  private readonly alerts = inject(AlertService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly auth = inject(AuthService);

  readonly accountTypes = ACCOUNT_TYPES;
  readonly accounts = signal<ChartAccountRow[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly canCreate = this.auth.hasPermission('accounting.chart_accounts.create');
  readonly canEdit = this.auth.hasPermission('accounting.chart_accounts.edit');

  readonly showForm = signal(false);
  readonly editing = signal<ChartAccountRow | null>(null);
  readonly isSaving = signal(false);
  readonly formError = signal<string | null>(null);

  readonly statusTarget = signal<ChartAccountRow | null>(null);
  readonly showDeactivate = signal(false);
  readonly isChangingStatus = signal(false);
  readonly statusError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    accountCode: ['', [Validators.required, Validators.maxLength(30)]],
    accountName: ['', [Validators.required, Validators.maxLength(150)]],
    accountType: ['Expense', Validators.required],
    normalBalanceType: ['Debit', Validators.required],
    parentAccountId: this.fb.control<number | null>(null),
    isControlAccount: [false],
    allowManualPosting: [true],
    displayOrder: [0, [Validators.required, Validators.min(0)]]
  });

  /** Drives the parent list, which must follow the type chosen in the form. */
  private readonly formType = signal('Expense');

  readonly columns: UiTableColumn[] = [
    { key: 'accountCode', header: 'Code', type: 'custom', searchable: true, width: '12rem' },
    { key: 'accountName', header: 'Account name' },
    {
      key: 'typeLabel', header: 'Type',
      filter: { type: 'select', options: ACCOUNT_TYPES.map(type => ({ value: type.label, label: type.label })), placeholder: 'All types' }
    },
    { key: 'parentLabel', header: 'Parent', emptyText: 'Top level' },
    {
      key: 'origin', header: 'Source', type: 'status', searchable: false,
      statusMap: { system: { label: 'System', variant: 'info' }, custom: { label: 'Custom', variant: 'neutral' } }
    },
    {
      key: 'status', header: 'Status', type: 'status', searchable: false,
      filter: { type: 'select', options: [{ value: 'active', label: 'Active' }, { value: 'inactive', label: 'Inactive' }], placeholder: 'All statuses' },
      statusMap: { active: { label: 'Active', variant: 'success' }, inactive: { label: 'Inactive', variant: 'danger' } }
    }
  ];

  /** Parents first, each followed by its children, so the table reads as the hierarchy. */
  readonly rows = computed<AccountTableRow[]>(() => {
    const accounts = this.accounts();
    const byId = new Map(accounts.map(account => [account.id, account]));
    const children = new Map<number | null, ChartAccountRow[]>();
    for (const account of accounts) {
      const parentId = account.parentAccountId != null && byId.has(account.parentAccountId) ? account.parentAccountId : null;
      children.set(parentId, [...(children.get(parentId) ?? []), account]);
    }

    const ordered: AccountTableRow[] = [];
    const visit = (parentId: number | null, depth: number) => {
      for (const account of [...(children.get(parentId) ?? [])].sort(byDisplayOrder)) {
        const parent = account.parentAccountId != null ? byId.get(account.parentAccountId) : undefined;
        ordered.push({
          id: account.id,
          accountCode: account.accountCode,
          accountName: account.accountName,
          accountType: account.accountType,
          typeLabel: ACCOUNT_TYPES.find(type => type.value === account.accountType)?.label ?? account.accountType,
          parentLabel: parent ? this.label(parent) : '',
          origin: account.isSystemAccount ? 'system' : 'custom',
          status: account.isActive ? 'active' : 'inactive',
          depth,
          account
        });
        visit(account.id, depth + 1);
      }
    };
    visit(null, 0);
    return ordered;
  });

  /** Same-type active accounts, never the account itself or anything below it, which would form a cycle. */
  readonly parentOptions = computed(() => {
    const editing = this.editing();
    const excluded = editing ? this.withDescendants(editing.id) : new Set<number>();
    return this.accounts()
      .filter(account => account.accountType === this.formType() && account.isActive && !excluded.has(account.id))
      .sort((a, b) => a.accountCode.localeCompare(b.accountCode));
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.accounting.getAllChartAccounts().subscribe({
      next: res => {
        this.accounts.set(res.data ?? []);
        this.isLoading.set(false);
      },
      error: err => {
        this.loadError.set(this.errors.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  label(account: ChartAccountRow): string {
    return `${account.accountCode} — ${account.accountName}`;
  }

  typeLabel(value: string): string {
    return ACCOUNT_TYPES.find(type => type.value === value)?.label ?? value;
  }

  openCreate(): void {
    this.editing.set(null);
    this.formError.set(null);
    this.formType.set('Expense');
    this.form.reset({
      accountCode: '', accountName: '', accountType: 'Expense', normalBalanceType: 'Debit',
      parentAccountId: null, isControlAccount: false, allowManualPosting: true, displayOrder: 0
    });
    this.form.controls.accountCode.enable();
    this.form.controls.accountType.enable();
    this.form.controls.normalBalanceType.enable();
    this.setSystemRestrictions(false);
    this.showForm.set(true);
  }

  openEdit(account: ChartAccountRow): void {
    this.editing.set(account);
    this.formError.set(null);
    this.formType.set(account.accountType);
    this.form.reset({
      accountCode: account.accountCode,
      accountName: account.accountName,
      accountType: account.accountType,
      normalBalanceType: account.normalBalanceType ?? 'Debit',
      parentAccountId: account.parentAccountId ?? null,
      isControlAccount: account.isControlAccount,
      allowManualPosting: account.allowManualPosting,
      displayOrder: account.displayOrder
    });
    // Fixed once created: the update API does not accept them.
    this.form.controls.accountCode.disable();
    this.form.controls.accountType.disable();
    this.form.controls.normalBalanceType.disable();
    this.setSystemRestrictions(!!account.isSystemAccount);
    this.showForm.set(true);
  }

  onTypeChange(): void {
    const value = this.form.controls.accountType.value;
    this.formType.set(value);
    const type = ACCOUNT_TYPES.find(option => option.value === value);
    this.form.patchValue({ normalBalanceType: type?.normalBalance ?? 'Debit', parentAccountId: null });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.isSaving()) return;

    this.isSaving.set(true);
    this.formError.set(null);
    const value = this.form.getRawValue();
    const parentAccountId = value.parentAccountId ? Number(value.parentAccountId) : null;
    const editing = this.editing();
    const shared = {
      accountName: value.accountName.trim(),
      parentAccountId,
      isControlAccount: value.isControlAccount,
      allowManualPosting: value.allowManualPosting,
      displayOrder: Number(value.displayOrder)
    };
    const request = editing
      ? this.accounting.updateChartAccount(editing.id, { ...shared, isActive: editing.isActive })
      : this.accounting.createChartAccount({
          ...shared,
          accountCode: value.accountCode.trim(),
          accountType: value.accountType,
          normalBalanceType: value.normalBalanceType,
          isActive: true
        });

    request.subscribe({
      next: () => {
        this.alerts.success(editing ? 'Account updated' : 'Account created');
        this.isSaving.set(false);
        this.showForm.set(false);
        this.load();
      },
      error: err => {
        // Keep the dialog and what was typed; the server's message says what to fix.
        this.formError.set(this.errors.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  askDeactivate(account: ChartAccountRow): void {
    this.statusTarget.set(account);
    this.statusError.set(null);
    this.showDeactivate.set(true);
  }

  confirmDeactivate(): void {
    const account = this.statusTarget();
    if (!account || this.isChangingStatus()) return;

    this.isChangingStatus.set(true);
    this.statusError.set(null);
    this.accounting.deactivateChartAccount(account.id).subscribe({
      next: () => {
        this.alerts.success('Account deactivated');
        this.isChangingStatus.set(false);
        this.showDeactivate.set(false);
        this.load();
      },
      error: err => {
        // The dependency message stays on screen so the user can act on it.
        this.statusError.set(this.errors.extractErrorMessage(err));
        this.isChangingStatus.set(false);
      }
    });
  }

  activate(account: ChartAccountRow): void {
    if (this.isChangingStatus()) return;

    this.isChangingStatus.set(true);
    const dto: UpdateChartAccountDto = {
      accountName: account.accountName,
      parentAccountId: account.parentAccountId ?? null,
      isControlAccount: account.isControlAccount,
      allowManualPosting: account.allowManualPosting,
      isActive: true,
      displayOrder: account.displayOrder
    };
    this.accounting.updateChartAccount(account.id, dto).subscribe({
      next: () => {
        this.alerts.success('Account activated');
        this.isChangingStatus.set(false);
        this.load();
      },
      error: err => {
        this.alerts.error(this.errors.extractErrorMessage(err));
        this.isChangingStatus.set(false);
      }
    });
  }

  /** The backend refuses to change these on a system account, so the form does not offer them. */
  private setSystemRestrictions(isSystem: boolean): void {
    for (const control of [this.form.controls.parentAccountId, this.form.controls.isControlAccount, this.form.controls.allowManualPosting]) {
      if (isSystem) {
        control.disable();
      } else {
        control.enable();
      }
    }
  }

  private withDescendants(accountId: number): Set<number> {
    const found = new Set<number>([accountId]);
    let grew = true;
    while (grew) {
      grew = false;
      for (const account of this.accounts()) {
        if (account.parentAccountId != null && found.has(account.parentAccountId) && !found.has(account.id)) {
          found.add(account.id);
          grew = true;
        }
      }
    }
    return found;
  }
}
