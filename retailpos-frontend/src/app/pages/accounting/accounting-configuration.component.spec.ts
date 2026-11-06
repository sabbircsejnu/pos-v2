import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AccountingConfigurationComponent } from './accounting-configuration.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

const row = (id: number, code: string, flagged: boolean) => ({
  id,
  accountCode: code,
  accountName: `Account ${code}`,
  accountType: 'Asset',
  isControlAccount: false,
  allowManualPosting: true,
  isActive: true,
  displayOrder: 0,
  isExpensePaymentAccount: flagged
});

describe('AccountingConfigurationComponent expense payment accounts', () => {
  async function setup(canEdit: boolean, accounting: Partial<Record<string, ReturnType<typeof vi.fn>>> = {}) {
    await TestBed.configureTestingModule({
      imports: [AccountingConfigurationComponent],
      providers: [
        {
          provide: AccountingService,
          useValue: {
            getAccountingConfiguration: vi.fn().mockReturnValue(of({ data: { supplierPayableAccountId: 2000 } })),
            getActiveEquityChartAccounts: vi.fn().mockReturnValue(of({ data: [] })),
            getChartAccounts: vi.fn((type: string) => of({ data: type === 'Asset' ? [row(1000, '1000', true)] : [row(2000, '2000', false), row(2100, '2100', false)] })),
            setChartAccountExpensePaymentFlag: vi.fn(),
            ...accounting
          }
        },
        { provide: AlertService, useValue: { error: vi.fn(), success: vi.fn() } },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => canEdit && permission === 'accounting.chart_accounts.edit' } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'Server said no' } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(AccountingConfigurationComponent);
    fixture.detectChanges();
    const checkboxes = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
    return { fixture, component: fixture.componentInstance, checkboxes };
  }

  it('lists funding candidates without supplier payable', async () => {
    const { component, checkboxes } = await setup(true);

    expect(component.paymentCandidateAccounts().map(account => account.id)).toEqual([1000, 2100]);
    expect(checkboxes().map(box => box.checked)).toEqual([true, false]);
  });

  it('is read-only without the chart edit permission', async () => {
    const { checkboxes } = await setup(false);

    expect(checkboxes().every(box => box.disabled)).toBe(true);
  });

  it('saves the flag and keeps the returned account', async () => {
    const save = vi.fn().mockReturnValue(of({ data: row(2100, '2100', true) }));
    const { component, fixture, checkboxes } = await setup(true, { setChartAccountExpensePaymentFlag: save });
    const box = checkboxes()[1];
    box.checked = true;

    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(save).toHaveBeenCalledWith(expect.objectContaining({ id: 2100 }), true);
    expect(component.liabilityAccounts().find(account => account.id === 2100).isExpensePaymentAccount).toBe(true);
  });

  it('puts the checkbox back and shows the error when saving fails', async () => {
    const save = vi.fn().mockReturnValue(throwError(() => new Error('refused')));
    const { component, checkboxes } = await setup(true, { setChartAccountExpensePaymentFlag: save });
    const box = checkboxes()[1];
    box.checked = true;

    box.dispatchEvent(new Event('change'));

    expect(box.checked).toBe(false);
    expect(component.paymentFlagError()).toBe('Server said no');
  });
});
