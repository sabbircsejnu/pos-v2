import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AccountsListComponent } from './accounts-list.component';
import { TransactionsListComponent } from './transactions-list.component';
import { BillsListComponent } from './bills-list.component';
import { AccountingService } from '../../services/accounting.service';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

function accountingMock() {
  return {
    accounts: signal([{ id: 1, name: 'Legacy Cash', type: 'asset', balance: 25 }]),
    transactions: signal([{ id: 1, accountId: 1, accountName: 'Legacy Cash', amount: 25, type: 'credit', transactionDate: '2026-09-06' }]),
    transactionsTotalCount: signal(1),
    bills: signal([{ id: 1, supplierId: 1, supplierName: 'Supplier', amountDue: 25, dueDate: '2026-09-30', status: 'unpaid', isOverdue: false }]),
    billsTotalCount: signal(1),
    billSummary: signal({ totalUnpaid: 25, totalOverdue: 0, unpaidCount: 1, overdueCount: 0 }),
    isLoading: signal(false),
    error: signal(null),
    getAccounts: vi.fn().mockReturnValue(of({ data: { accounts: [] } })),
    getTransactions: vi.fn().mockReturnValue(of({ data: { transactions: [] } })),
    searchBills: vi.fn().mockReturnValue(of({ data: { bills: [] } })),
    getBillSummary: vi.fn().mockReturnValue(of({ data: {} })),
    createAccount: vi.fn(),
    updateAccount: vi.fn(),
    deleteAccount: vi.fn(),
    createTransaction: vi.fn(),
    createBill: vi.fn(),
    updateBillStatus: vi.fn()
  };
}

async function render<T>(component: { new (...args: any[]): T }) {
  const accounting = accountingMock();
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      { provide: AccountingService, useValue: accounting },
      { provide: SupplierService, useValue: { loadSuppliers: vi.fn(), suppliers: signal([]) } },
      { provide: AlertService, useValue: { error: vi.fn(), success: vi.fn(), confirm: vi.fn() } },
      { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
      { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } }
    ]
  }).compileComponents();

  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return { fixture, accounting };
}

describe('Legacy finance screens', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders legacy accounts as read-only historical evidence', async () => {
    const { fixture, accounting } = await render(AccountsListComponent);

    expect(fixture.nativeElement.textContent).toContain('Legacy Accounts');
    expect(fixture.nativeElement.textContent).toContain('Read-only historical records');
    expect(fixture.nativeElement.textContent).not.toContain('Add Account');
    expect(fixture.nativeElement.textContent).not.toContain('Edit');
    expect(fixture.nativeElement.textContent).not.toContain('Delete');
    expect(accounting.createAccount).not.toHaveBeenCalled();
  });

  it('renders legacy transactions without a create action', async () => {
    const { fixture, accounting } = await render(TransactionsListComponent);

    expect(fixture.nativeElement.textContent).toContain('Legacy Transactions');
    expect(fixture.nativeElement.textContent).toContain('Read-only historical records');
    expect(fixture.nativeElement.textContent).not.toContain('Add Transaction');
    expect(accounting.createTransaction).not.toHaveBeenCalled();
  });

  it('renders legacy bills without create or status mutation actions', async () => {
    const { fixture, accounting } = await render(BillsListComponent);

    expect(fixture.nativeElement.textContent).toContain('Legacy Bills Payable');
    expect(fixture.nativeElement.textContent).toContain('Read-only historical records');
    expect(fixture.nativeElement.textContent).not.toContain('Add Bill');
    const buttonText = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).map(button => button.textContent?.trim());
    expect(buttonText).not.toContain('Pay');
    expect(accounting.createBill).not.toHaveBeenCalled();
    expect(accounting.updateBillStatus).not.toHaveBeenCalled();
  });
});
