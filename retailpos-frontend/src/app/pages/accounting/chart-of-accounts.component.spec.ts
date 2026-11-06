import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ChartOfAccountsComponent } from './chart-of-accounts.component';
import { ChartAccountRow } from '../../models/accounting.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

const account = (id: number, overrides: Partial<ChartAccountRow> = {}): ChartAccountRow => ({
  id,
  accountCode: String(id),
  accountName: `Account ${id}`,
  accountType: 'Expense',
  normalBalanceType: 'Debit',
  parentAccountId: null,
  isControlAccount: false,
  allowManualPosting: true,
  isActive: true,
  displayOrder: 0,
  isExpensePaymentAccount: false,
  isSystemAccount: false,
  ...overrides
});

describe('ChartOfAccountsComponent', () => {
  let fixture: ComponentFixture<ChartOfAccountsComponent>;
  let component: ChartOfAccountsComponent;
  let accounting: Record<string, ReturnType<typeof vi.fn>>;
  let alerts: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  async function setup(permissions: string[], rows: ChartAccountRow[] = []): Promise<void> {
    accounting['getAllChartAccounts'].mockReturnValue(of({ data: rows }));
    await TestBed.configureTestingModule({
      imports: [ChartOfAccountsComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (err: Error) => err.message } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ChartOfAccountsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const buttons = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(button => button.textContent?.trim());

  beforeEach(() => {
    accounting = {
      getAllChartAccounts: vi.fn(),
      createChartAccount: vi.fn().mockReturnValue(of({ data: account(1) })),
      updateChartAccount: vi.fn().mockReturnValue(of({ data: account(1) })),
      deactivateChartAccount: vi.fn().mockReturnValue(of({}))
    };
    alerts = { success: vi.fn(), error: vi.fn() };
  });

  it('lists each child straight after its parent, with parent, source and status', async () => {
    await setup(['accounting.chart_accounts.view'], [
      account(30, { displayOrder: 3 }),
      account(21, { parentAccountId: 20, accountName: 'Child' }),
      account(20, { displayOrder: 2, accountName: 'Parent', isSystemAccount: true, isActive: false })
    ]);

    expect(component.rows().map(row => [row.id, row.depth])).toEqual([[20, 0], [21, 1], [30, 0]]);
    const child = component.rows()[1];
    expect(child.parentLabel).toBe('20 — Parent');
    expect(component.rows()[0].origin).toBe('system');
    expect(component.rows()[0].status).toBe('inactive');
    expect(text()).toContain('Child');
  });

  it('shows a retryable error when the list fails', async () => {
    accounting['getAllChartAccounts'].mockReturnValueOnce(throwError(() => new Error('down')));
    await TestBed.configureTestingModule({
      imports: [ChartOfAccountsComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (err: Error) => err.message } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ChartOfAccountsComponent);
    fixture.detectChanges();

    expect(text()).toContain('Could not load the chart of accounts');
  });

  describe('permissions', () => {
    it('offers no management action to a view-only user', async () => {
      await setup(['accounting.chart_accounts.view'], [account(1)]);

      expect(buttons()).not.toContain('Add account');
      expect(buttons()).not.toContain('Edit');
      expect(buttons()).not.toContain('Deactivate');
    });

    it('offers create and edit actions only to their own permissions', async () => {
      await setup(['accounting.chart_accounts.view', 'accounting.chart_accounts.create'], [account(1)]);
      expect(buttons()).toContain('Add account');
      expect(buttons()).not.toContain('Edit');

      TestBed.resetTestingModule();
      await setup(['accounting.chart_accounts.view', 'accounting.chart_accounts.edit'], [account(1)]);
      expect(buttons()).not.toContain('Add account');
      expect(buttons()).toContain('Edit');
      expect(buttons()).toContain('Deactivate');
    });

    it('never offers a delete action, nor status changes on a system account', async () => {
      await setup(['accounting.chart_accounts.view', 'accounting.chart_accounts.edit'], [account(1, { isSystemAccount: true })]);

      expect(buttons().some(label => label?.toLowerCase().includes('delete'))).toBe(false);
      expect(buttons()).toContain('Edit');
      expect(buttons()).not.toContain('Deactivate');
    });
  });

  describe('create and edit', () => {
    const managing = ['accounting.chart_accounts.view', 'accounting.chart_accounts.create', 'accounting.chart_accounts.edit'];

    it('creates an operating expense account with the matching normal balance', async () => {
      await setup(managing);
      component.openCreate();
      component.form.patchValue({ accountCode: ' 7400 ', accountName: ' Internet Expense ' });

      component.save();

      expect(accounting['createChartAccount']).toHaveBeenCalledWith(expect.objectContaining({
        accountCode: '7400', accountName: 'Internet Expense', accountType: 'Expense', normalBalanceType: 'Debit', isActive: true
      }));
      expect(component.showForm()).toBe(false);
    });

    it('follows the account type with its normal balance and clears a parent of the old type', async () => {
      await setup(managing, [account(5, { accountType: 'Expense' }), account(6, { accountType: 'Income' })]);
      component.openCreate();
      component.form.patchValue({ parentAccountId: 5, accountType: 'Income' });

      component.onTypeChange();

      expect(component.form.getRawValue().normalBalanceType).toBe('Credit');
      expect(component.form.getRawValue().parentAccountId).toBeNull();
      expect(component.parentOptions().map(option => option.id)).toEqual([6]);
    });

    it('requires a code and a name', async () => {
      await setup(managing);
      component.openCreate();

      component.save();

      expect(accounting['createChartAccount']).not.toHaveBeenCalled();
    });

    it('keeps the dialog and the input open when the server refuses', async () => {
      accounting['createChartAccount'].mockReturnValue(throwError(() => new Error("An account with code '7400' already exists.")));
      await setup(managing);
      component.openCreate();
      component.form.patchValue({ accountCode: '7400', accountName: 'Duplicate' });

      component.save();

      expect(component.showForm()).toBe(true);
      expect(component.formError()).toContain('already exists');
      expect(component.form.getRawValue().accountCode).toBe('7400');
    });

    it('fixes code, type and normal balance on edit and sends only what the API accepts', async () => {
      await setup(managing, [account(7, { isActive: false })]);
      component.openEdit(account(7, { isActive: false }));

      expect(component.form.controls.accountCode.disabled).toBe(true);
      expect(component.form.controls.accountType.disabled).toBe(true);
      expect(component.form.controls.normalBalanceType.disabled).toBe(true);

      component.form.patchValue({ accountName: 'Renamed' });
      component.save();

      const [id, dto] = accounting['updateChartAccount'].mock.calls[0];
      expect(id).toBe(7);
      expect(dto.accountName).toBe('Renamed');
      expect(dto.isActive).toBe(false);
      expect(dto.accountCode).toBeUndefined();
      expect(dto.accountType).toBeUndefined();
    });

    it('does not offer hierarchy or posting changes the backend refuses on a system account', async () => {
      await setup(managing, [account(7, { isSystemAccount: true })]);

      component.openEdit(account(7, { isSystemAccount: true }));

      expect(component.form.controls.parentAccountId.disabled).toBe(true);
      expect(component.form.controls.isControlAccount.disabled).toBe(true);
      expect(component.form.controls.allowManualPosting.disabled).toBe(true);
      expect(component.form.controls.accountName.enabled).toBe(true);
    });

    it('never offers the account or its descendants as a parent', async () => {
      const rows = [account(1), account(2, { parentAccountId: 1 }), account(3, { parentAccountId: 2 }), account(4), account(5, { isActive: false })];
      await setup(managing, rows);

      component.openEdit(rows[0]);

      expect(component.parentOptions().map(option => option.id)).toEqual([4]);
    });
  });

  describe('status', () => {
    const editing = ['accounting.chart_accounts.view', 'accounting.chart_accounts.edit'];

    it('shows the dependency message and stays open when deactivation is refused', async () => {
      const message = 'Cannot deactivate this account because it is used by 2 active expense categories.';
      accounting['deactivateChartAccount'].mockReturnValue(throwError(() => new Error(message)));
      await setup(editing, [account(7)]);
      component.askDeactivate(account(7));

      component.confirmDeactivate();

      expect(component.statusError()).toBe(message);
      expect(component.showDeactivate()).toBe(true);
    });

    it('deactivates through the dedicated endpoint and reloads', async () => {
      await setup(editing, [account(7)]);
      accounting['getAllChartAccounts'].mockClear();
      component.askDeactivate(account(7));

      component.confirmDeactivate();

      expect(accounting['deactivateChartAccount']).toHaveBeenCalledWith(7);
      expect(accounting['getAllChartAccounts']).toHaveBeenCalled();
      expect(component.showDeactivate()).toBe(false);
    });

    it('reactivates without changing anything else about the account', async () => {
      const inactive = account(7, { isActive: false, displayOrder: 9, parentAccountId: 3, accountName: 'Old' });
      await setup(editing, [inactive]);

      component.activate(inactive);

      expect(accounting['updateChartAccount']).toHaveBeenCalledWith(7, {
        accountName: 'Old', parentAccountId: 3, isControlAccount: false, allowManualPosting: true, isActive: true, displayOrder: 9
      });
    });
  });
});
