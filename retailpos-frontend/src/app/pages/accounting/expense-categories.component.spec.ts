import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ExpenseCategoriesComponent } from './expense-categories.component';
import { ExpenseCategoryDto } from '../../models/accounting.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

const MANAGE = 'accounting.expense_categories.manage';

const category = (overrides: Partial<ExpenseCategoryDto> = {}): ExpenseCategoryDto => ({
  id: 1,
  name: 'Internet Bill',
  expenseAccountId: 7400,
  expenseAccountCode: '7400',
  expenseAccountName: 'Internet Expense',
  isActive: true,
  ...overrides
});

describe('ExpenseCategoriesComponent', () => {
  let fixture: ComponentFixture<ExpenseCategoriesComponent>;
  let component: ExpenseCategoriesComponent;
  let accounting: Record<string, ReturnType<typeof vi.fn>>;
  let alerts: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  async function setup(permissions: string[] = [MANAGE]): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ExpenseCategoriesComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (err: Error) => err.message } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExpenseCategoriesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();
  }

  const host = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    accounting = {
      getExpenseCategories: vi.fn().mockReturnValue(of({ data: [category()] })),
      getEligibleExpenseAccounts: vi.fn().mockReturnValue(of({ data: [
        { id: 7400, accountCode: '7400', accountName: 'Internet Expense' },
        { id: 7310, accountCode: '6310', accountName: 'Bank Fee Expense' }
      ] })),
      createExpenseCategory: vi.fn().mockReturnValue(of({ data: category() })),
      updateExpenseCategory: vi.fn().mockReturnValue(of({ data: category() }))
    };
    alerts = { success: vi.fn(), error: vi.fn() };
  });

  describe('account selector', () => {
    it('replaces the typed account ID with code and name choices from the dedicated lookup', async () => {
      await setup();

      expect(host().querySelector('input[type="number"]')).toBeNull();
      expect(host().querySelector('input[role="combobox"]')).not.toBeNull();
      expect(accounting['getEligibleExpenseAccounts']).toHaveBeenCalled();
      expect(component.eligibleAccounts().map(option => option.name)).toEqual(['7400 — Internet Expense', '6310 — Bank Fee Expense']);
    });

    it('filters by code or name as the user searches', async () => {
      await setup();

      component.onAccountSearch({ query: 'bank' });
      expect(component.accountDropdown!.options().map(option => option.id)).toEqual([7310]);

      component.onAccountSearch({ query: '7400' });
      expect(component.accountDropdown!.options().map(option => option.id)).toEqual([7400]);
    });

    it('shows a loading state while the lookup runs', async () => {
      await setup();
      component.accountsState.set('loading');
      fixture.detectChanges();

      expect(host().textContent).toContain('Loading expense accounts');
    });

    it('shows a retryable error when the lookup fails', async () => {
      accounting['getEligibleExpenseAccounts'].mockReturnValueOnce(throwError(() => new Error('down')));
      await setup();

      expect(host().textContent).toContain('Could not load expense accounts');
      Array.from(host().querySelectorAll('button')).find(button => button.textContent?.includes('Retry'))!.click();
      expect(component.accountsState()).toBe('ready');
    });

    it('explains the next step when no account is eligible', async () => {
      accounting['getEligibleExpenseAccounts'].mockReturnValue(of({ data: [] }));
      accounting['getExpenseCategories'].mockReturnValue(of({ data: [] }));
      await setup([MANAGE, 'accounting.chart_accounts.view']);

      expect(host().textContent).toContain('No eligible expense accounts');
      expect(host().textContent).toContain('Create an operating expense account in Chart of Accounts');
    });

    it('requires an account before saving', async () => {
      await setup();
      component.form.patchValue({ name: 'Rent' });

      component.save();
      fixture.detectChanges();

      expect(accounting['createExpenseCategory']).not.toHaveBeenCalled();
      expect(host().textContent).toContain('Choose the expense account');
    });
  });

  describe('create and edit', () => {
    it('stores the internal identifier of the chosen account', async () => {
      await setup();
      component.form.patchValue({ name: ' Bank Charges ' });
      component.onAccountSelect({ id: 7310, name: '6310 — Bank Fee Expense' });

      component.save();

      expect(accounting['createExpenseCategory']).toHaveBeenCalledWith({ name: 'Bank Charges', expenseAccountId: 7310, isActive: true });
      expect(alerts.success).toHaveBeenCalled();
    });

    it('does not reactivate an inactive category when it is edited', async () => {
      await setup();
      component.startEdit(category({ isActive: false }));
      component.form.patchValue({ name: 'Renamed' });

      component.save();

      expect(accounting['updateExpenseCategory']).toHaveBeenCalledWith(1, { name: 'Renamed', expenseAccountId: 7400, isActive: false });
    });

    it('can deactivate and reactivate a category through its status control', async () => {
      await setup();
      component.startEdit(category());
      component.form.patchValue({ isActive: false });
      fixture.detectChanges();
      expect(host().textContent).toContain('cannot be approved while it is inactive');

      component.save();
      expect(accounting['updateExpenseCategory']).toHaveBeenLastCalledWith(1, expect.objectContaining({ isActive: false }));

      component.startEdit(category({ isActive: false }));
      component.form.patchValue({ isActive: true });
      component.save();
      expect(accounting['updateExpenseCategory']).toHaveBeenLastCalledWith(1, expect.objectContaining({ isActive: true }));
    });

    it('keeps what was entered and shows the reason when the server refuses', async () => {
      accounting['createExpenseCategory'].mockReturnValue(throwError(() => new Error("Expense category 'Rent' already exists.")));
      await setup();
      component.form.patchValue({ name: 'Rent' });
      component.onAccountSelect({ id: 7400, name: '7400 — Internet Expense' });

      component.save();
      fixture.detectChanges();

      expect(host().textContent).toContain('already exists');
      expect(component.form.getRawValue().name).toBe('Rent');
    });
  });

  describe('remapping warning', () => {
    it('warns only when an edit changes the account, without implying posted journals change', async () => {
      await setup();
      component.startEdit(category());
      fixture.detectChanges();
      expect(host().textContent).not.toContain('may post to the new account');

      component.onAccountSelect({ id: 7310, name: '6310 — Bank Fee Expense' });
      fixture.detectChanges();

      expect(host().textContent).toContain('may post to the new account');
      expect(host().textContent).toContain('Journals already posted do not change');
    });

    it('does not warn on create', async () => {
      await setup();
      component.onAccountSelect({ id: 7310, name: '6310 — Bank Fee Expense' });
      fixture.detectChanges();

      expect(host().textContent).not.toContain('may post to the new account');
    });
  });

  describe('legacy mapping', () => {
    const legacy = category({ id: 2, name: 'Old', expenseAccountId: 4010, expenseAccountCode: '4010', expenseAccountName: 'Sales Discounts' });

    it('marks a saved mapping the lookup no longer offers, and keeps it on a metadata edit', async () => {
      await setup();

      component.startEdit(legacy);
      fixture.detectChanges();

      expect(host().textContent).toContain('no longer selectable');
      component.save();
      expect(accounting['updateExpenseCategory']).toHaveBeenCalledWith(2, expect.objectContaining({ expenseAccountId: 4010 }));
    });

    it('shows the account as code and name rather than an ID', async () => {
      await setup();

      expect(host().textContent).toContain('7400 — Internet Expense');
      expect(host().textContent).not.toContain('Expense account ID');
    });
  });

  describe('restricted user', () => {
    it('reads categories with no form, edit action or account lookup', async () => {
      await setup(['accounting.expenses.view', 'accounting.expenses.edit']);

      expect(host().textContent).toContain('Internet Bill');
      expect(host().querySelector('form')).toBeNull();
      expect(Array.from(host().querySelectorAll('button')).some(button => button.textContent?.includes('Edit'))).toBe(false);
      expect(accounting['getEligibleExpenseAccounts']).not.toHaveBeenCalled();
    });
  });
});
