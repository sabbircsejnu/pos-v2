import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { ExpenseFormComponent } from './expense-form.component';
import { AuthorizedOutletsDto } from '../../models/report.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';

const outlet = (id: number, name = `Outlet ${id}`) => ({ id, name, type: 'outlet' as const });
const warehouse = (id: number, name = `Warehouse ${id}`) => ({ id, name, type: 'warehouse' as const });
const access = (overrides: Partial<AuthorizedOutletsDto>): AuthorizedOutletsDto =>
  ({ outlets: [], warehouses: [], isBusinessOwner: false, isGlobalAccess: false, ...overrides });

describe('ExpenseFormComponent', () => {
  let fixture: ComponentFixture<ExpenseFormComponent>;
  let component: ExpenseFormComponent;
  let accounting: Record<string, ReturnType<typeof vi.fn>>;
  let outletAccess: { load: ReturnType<typeof vi.fn> };
  let alerts: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn>; confirm: ReturnType<typeof vi.fn> };
  let router: { navigate: ReturnType<typeof vi.fn> };
  let location: { back: ReturnType<typeof vi.fn> };

  async function setup(authorized: AuthorizedOutletsDto, routeId: string | null = null): Promise<void> {
    outletAccess.load.mockReturnValue(of({ data: authorized }));
    await TestBed.configureTestingModule({
      imports: [ExpenseFormComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: UserOutletAccessService, useValue: outletAccess },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'Server said no' } },
        { provide: Router, useValue: router },
        { provide: Location, useValue: location },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => routeId } } } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExpenseFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function fillRequired(): void {
    component.expenseForm.patchValue({ expenseCategoryId: 1, amount: 25, paymentAccountId: 1021 });
  }

  function chooseLocation(type: 'outlet' | 'warehouse' | 'business', id: number | null): void {
    component.expenseForm.patchValue({ locationType: type });
    component.onLocationTypeChange();
    component.expenseForm.patchValue({ locationId: id });
  }

  beforeEach(() => {
    accounting = {
      getExpenseCategories: vi.fn().mockReturnValue(of({ data: [{ id: 1, name: 'Utilities', expenseAccountId: 7001, isActive: true }] })),
      getExpensePaymentAccounts: vi.fn().mockReturnValue(of({ data: [{ id: 1021, accountCode: '1020', accountName: 'Bank', accountType: 'Asset' }] })),
      getChartAccounts: vi.fn(),
      createExpense: vi.fn().mockReturnValue(of({ data: { id: 55 } })),
      updateExpense: vi.fn().mockReturnValue(of({})),
      getExpenseById: vi.fn(),
      getExpenseAttachments: vi.fn().mockReturnValue(of({ data: [] })),
      uploadExpenseAttachment: vi.fn()
    };
    outletAccess = { load: vi.fn() };
    alerts = { error: vi.fn(), success: vi.fn(), confirm: vi.fn() };
    router = { navigate: vi.fn() };
    location = { back: vi.fn() };
  });

  describe('payment account', () => {
    it('uses the expense lookup and never the Chart of Accounts', async () => {
      await setup(access({ outlets: [outlet(11)] }));

      expect(accounting['getExpensePaymentAccounts']).toHaveBeenCalled();
      expect(accounting['getChartAccounts']).not.toHaveBeenCalled();
    });

    it('shows a retryable error and no manual account ID input when the lookup fails', async () => {
      accounting['getExpensePaymentAccounts'].mockReturnValueOnce(throwError(() => new Error('nope')));
      await setup(access({ outlets: [outlet(11)] }));
      const host: HTMLElement = fixture.nativeElement;

      expect(host.textContent).toContain('Could not load payment accounts');
      expect(host.querySelector('input#paymentAccountId')).toBeNull();

      Array.from(host.querySelectorAll('button')).find(button => button.textContent?.includes('Retry'))!.click();
      expect(component.accountsState()).toBe('ready');
    });

    it('drops a saved account that is no longer enabled and says so', async () => {
      accounting['getExpenseById'].mockReturnValue(of({ data: {
        id: 9, expenseCategoryId: 1, amount: 5, paymentAccountId: 1500, expenseDate: '2026-03-01', outletId: 11, outletName: 'Dhaka', status: 'Draft'
      } }));
      await setup(access({ outlets: [outlet(11)] }), '9');

      expect(component.expenseForm.value.paymentAccountId).toBeNull();
      expect(component.paymentAccountNotice()).toContain('cannot be used with this payment source');
    });

    it('asks only for the accounts the selected source may credit at the location', async () => {
      await setup(access({ outlets: [outlet(11)] }));

      expect(accounting['getExpensePaymentAccounts']).toHaveBeenLastCalledWith(expect.objectContaining({ paymentSource: 'bank', outletId: 11 }));

      component.onSourceSelect({ id: 2, code: 'petty_cash', name: 'Petty cash', description: '' });

      expect(accounting['getExpensePaymentAccounts']).toHaveBeenLastCalledWith(expect.objectContaining({ paymentSource: 'petty_cash', outletId: 11 }));
    });

    it('reloads for a new location', async () => {
      await setup(access({ outlets: [outlet(11), outlet(12)], warehouses: [warehouse(21)] }));

      chooseLocation('outlet', 12);
      component.onLocationChange();
      expect(accounting['getExpensePaymentAccounts']).toHaveBeenLastCalledWith(expect.objectContaining({ outletId: 12, warehouseId: undefined }));

      chooseLocation('warehouse', 21);
      component.onLocationChange();
      expect(accounting['getExpensePaymentAccounts']).toHaveBeenLastCalledWith(expect.objectContaining({ outletId: undefined, warehouseId: 21 }));
    });

    it('clears an incompatible account when the source changes, and says so', async () => {
      await setup(access({ outlets: [outlet(11)] }));
      component.expenseForm.patchValue({ paymentAccountId: 1021 });
      accounting['getExpensePaymentAccounts'].mockReturnValue(of({ data: [{ id: 1001, accountCode: '1000', accountName: 'Cash', accountType: 'Asset' }, { id: 1011, accountCode: '1010', accountName: 'Petty', accountType: 'Asset' }] }));

      component.onSourceSelect({ id: 2, code: 'petty_cash', name: 'Petty cash', description: '' });

      expect(component.expenseForm.value.paymentAccountId).toBeNull();
      expect(component.paymentAccountNotice()).toContain('Choose another one');
    });

    it('keeps a compatible account without a notice', async () => {
      await setup(access({ outlets: [outlet(11)] }));
      component.expenseForm.patchValue({ paymentAccountId: 1021 });

      component.onLocationChange();

      expect(component.expenseForm.value.paymentAccountId).toBe(1021);
      expect(component.paymentAccountNotice()).toBeNull();
    });

    it('uses the single configured cash account for a POS drawer expense and fixes the selection', async () => {
      await setup(access({ outlets: [outlet(11)] }));
      accounting['getExpensePaymentAccounts'].mockReturnValue(of({ data: [{ id: 1001, accountCode: '1000', accountName: 'Cash', accountType: 'Asset' }] }));

      component.onSourceSelect({ id: 3, code: 'pos_drawer', name: 'POS cash drawer', description: '' });

      expect(component.expenseForm.value.paymentAccountId).toBe(1001);
      expect(component.isDrawerAccountFixed()).toBe(true);
    });

    it('tells the user to pick an outlet when a POS drawer expense has none yet', async () => {
      await setup(access({ outlets: [outlet(11), outlet(12)] }));
      accounting['getExpensePaymentAccounts'].mockReturnValue(of({ data: [] }));

      component.onSourceSelect({ id: 3, code: 'pos_drawer', name: 'POS cash drawer', description: '' });
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Select the outlet whose cash drawer pays this expense');
    });

    it('ignores an answer that arrives after a newer request', async () => {
      await setup(access({ outlets: [outlet(11)] }));
      const slow = new Subject<{ data: unknown[] }>();
      accounting['getExpensePaymentAccounts'].mockReturnValueOnce(slow);
      component.onSourceSelect({ id: 2, code: 'petty_cash', name: 'Petty cash', description: '' });

      component.onSourceSelect({ id: 1, code: 'bank', name: 'Bank', description: '' });
      slow.next({ data: [] });

      expect(component.paymentAccounts().map(option => option.id)).toEqual([1021]);
    });
  });

  describe('location', () => {
    it('loads locations under the expense policy', async () => {
      await setup(access({ outlets: [outlet(11)] }));

      expect(outletAccess.load).toHaveBeenCalledWith('ExpenseOperation');
    });

    it('auto-selects and locks the only assigned outlet', async () => {
      await setup(access({ outlets: [outlet(11)] }));

      expect(component.isLocationLocked()).toBe(true);
      expect(component.expenseForm.get('locationId')?.disabled).toBe(true);

      fillRequired();
      component.onSubmit();

      expect(accounting['createExpense']).toHaveBeenCalledWith(expect.objectContaining({ outletId: 11, warehouseId: undefined }));
    });

    it('auto-selects and locks the only assigned warehouse', async () => {
      await setup(access({ warehouses: [warehouse(21)] }));

      fillRequired();
      component.onSubmit();

      expect(component.isLocationLocked()).toBe(true);
      expect(accounting['createExpense']).toHaveBeenCalledWith(expect.objectContaining({ outletId: undefined, warehouseId: 21 }));
    });

    it('never preselects the default outlet when several locations are assigned', async () => {
      await setup(access({ outlets: [outlet(11), outlet(12)], warehouses: [warehouse(21)], defaultOutletId: 12 }));

      expect(component.isLocationLocked()).toBe(false);
      expect(component.expenseForm.getRawValue().locationId).toBeNull();

      fillRequired();
      component.onSubmit();

      expect(accounting['createExpense']).not.toHaveBeenCalled();
      expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('outlet or warehouse'));
    });

    it('offers business-level only to business-wide users', async () => {
      await setup(access({ outlets: [outlet(11), outlet(12)] }));
      expect(component.locationTypeOptions().map(option => option.value)).toEqual(['outlet']);

      TestBed.resetTestingModule();
      await setup(access({ outlets: [outlet(11)], warehouses: [warehouse(21)], isGlobalAccess: true }));
      expect(component.locationTypeOptions().map(option => option.value)).toEqual(['outlet', 'warehouse', 'business']);

      fillRequired();
      component.onSubmit();
      expect(accounting['createExpense']).toHaveBeenCalledWith(expect.objectContaining({ outletId: undefined, warehouseId: undefined }));
    });

    it('does not lock a business-wide user who has one outlet', async () => {
      await setup(access({ outlets: [outlet(11)], isGlobalAccess: true }));

      expect(component.isLocationLocked()).toBe(false);
    });

    it('blocks entry for a user with no assignments, whatever their default outlet', async () => {
      await setup(access({ defaultOutletId: 11 }));
      const host: HTMLElement = fixture.nativeElement;

      expect(host.textContent).toContain('No location available');
      expect(host.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    });

    it('requires an outlet for a POS drawer expense', async () => {
      await setup(access({ warehouses: [warehouse(21), warehouse(22)] }));
      fillRequired();
      chooseLocation('warehouse', 21);
      component.selectedSource.set({ id: 3, code: 'pos_drawer', name: 'POS', description: '' });

      component.onSubmit();

      expect(accounting['createExpense']).not.toHaveBeenCalled();
      expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('POS cash drawer'));
    });

    it('fixes the stored location and shows the reference when editing', async () => {
      accounting['getExpenseById'].mockReturnValue(of({ data: {
        id: 9, expenseNumber: '20261105-WH21-0001', expenseCategoryId: 1, amount: 5, paymentAccountId: 1021,
        expenseDate: '2026-03-01', warehouseId: 21, warehouseName: 'Central', status: 'Draft'
      } }));
      await setup(access({}), '9');

      expect(component.expenseNumber()).toBe('20261105-WH21-0001');
      expect(component.isLocationLocked()).toBe(true);
      expect(outletAccess.load).not.toHaveBeenCalled();

      component.onSubmit();

      expect(accounting['updateExpense']).toHaveBeenCalledWith(9, expect.objectContaining({ warehouseId: 21, outletId: undefined }));
    });
  });

  describe('attachments on create', () => {
    const file = new File(['%PDF-'], 'receipt.pdf', { type: 'application/pdf' });

    it('uploads queued files after creating, then returns', async () => {
      accounting['uploadExpenseAttachment'].mockReturnValue(of({ data: { id: 1, fileName: 'receipt.pdf', contentType: 'application/pdf', fileSizeBytes: 5, createdAt: '' } }));
      await setup(access({ outlets: [outlet(11)] }));
      component.attachments!.addFiles([file]);
      fillRequired();

      component.onSubmit();

      expect(accounting['uploadExpenseAttachment']).toHaveBeenCalledWith(55, file);
      expect(location.back).toHaveBeenCalled();
    });

    it('opens the saved expense for editing when a file is refused', async () => {
      accounting['uploadExpenseAttachment'].mockReturnValue(throwError(() => new Error('too big')));
      await setup(access({ outlets: [outlet(11)] }));
      component.attachments!.addFiles([file]);
      fillRequired();

      component.onSubmit();

      expect(router.navigate).toHaveBeenCalledWith(['/expenses/edit', 55]);
      expect(location.back).not.toHaveBeenCalled();
      expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('The expense was saved'));
    });
  });
});
