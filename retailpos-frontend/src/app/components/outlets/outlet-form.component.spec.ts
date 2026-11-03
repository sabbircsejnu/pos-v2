import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { UserService } from '../../services/user.service';
import { OutletFormComponent } from './outlet-form.component';

const OUTLET = {
  id: 5,
  name: 'Dhaka Main',
  address: '12 Road, Dhaka',
  contactNumber: '01700000000',
  managerId: null,
  invoicePrefix: 'DHK01',
  invoicePrefixLocked: false,
  timeZoneId: null,
  terminals: [
    { id: 11, name: 'Counter 1', code: 'T-001', isActive: true, hasHistory: false, hasOpenSession: false },
    { id: 12, name: 'Counter 2', code: 'T-002', isActive: true, hasHistory: true, hasOpenSession: true },
    { id: 13, name: 'Counter 3', code: 'T-003', isActive: false, hasHistory: true, hasOpenSession: false }
  ]
};

interface SetupOptions {
  permissions?: string[];
  outlet?: Record<string, unknown>;
  edit?: boolean;
}

const ALL = ['outlets.create', 'outlets.edit', 'outlets.terminals.manage'];

describe('OutletFormComponent (POS setup)', () => {
  let fixture: ComponentFixture<OutletFormComponent>;
  let cmp: OutletFormComponent;
  let outletService: {
    getOutletById: ReturnType<typeof vi.fn>;
    createOutlet: ReturnType<typeof vi.fn>;
    updateOutlet: ReturnType<typeof vi.fn>;
    deleteTerminal: ReturnType<typeof vi.fn>;
  };
  let location: { back: ReturnType<typeof vi.fn> };
  let alerts: AlertService;

  const root = () => fixture.nativeElement as HTMLElement;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string, scope: ParentNode = root()) => Array.from(scope.querySelectorAll<E>(selector));
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const field = (label: string) => qa('app-ui-form-field').find(f => f.querySelector('label')?.textContent?.replace(/\s*\*?\s*\(required\)/, '').trim().startsWith(label))!;
  const control = <E extends HTMLElement = HTMLInputElement>(label: string) => field(label).querySelector('input, select, textarea') as E;
  const type = async (label: string, value: string) => {
    const el = control<HTMLInputElement>(label);
    el.value = value;
    el.dispatchEvent(new Event('input'));
    await settle();
  };
  const rows = () => qa('.of-terminal');
  const rowInput = (index: number) => rows()[index].querySelector<HTMLInputElement>('input[type="text"]')!;
  const rowToggle = (index: number) => rows()[index].querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  const buttonByLabel = (label: string) => qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === label || b.textContent!.trim() === label);
  const fillOutlet = async () => {
    await type('Outlet Name', 'Dhaka Main');
    await type('Address', '12 Road, Dhaka');
  };
  const submit = async () => {
    q<HTMLFormElement>('form').dispatchEvent(new Event('submit'));
    await settle();
  };
  const apiError = (errorCode: string, message = 'refused') => ({
    status: 409,
    message,
    error: { message, error: message, errors: [] },
    originalError: { status: 409, error: { error: message, errorCode } }
  });

  async function setup(options: SetupOptions = {}) {
    const permissions = options.permissions ?? ALL;
    outletService = {
      getOutletById: vi.fn(() => of({ data: { ...OUTLET, ...options.outlet } })),
      createOutlet: vi.fn(() => of({ message: 'ok' })),
      updateOutlet: vi.fn(() => of({ message: 'ok' })),
      deleteTerminal: vi.fn(() => of({ message: 'deleted' }))
    };
    location = { back: vi.fn() };
    TestBed.configureTestingModule({
      imports: [OutletFormComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(options.edit ? { id: '5' } : {}) } } },
        { provide: Location, useValue: location },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
        { provide: OutletService, useValue: outletService },
        { provide: UserService, useValue: { getUsers: () => of({ data: [{ id: 3, name: 'Rafi', roleName: 'Manager' }] }) } }
      ]
    });
    alerts = TestBed.inject(AlertService);
    fixture = TestBed.createComponent(OutletFormComponent);
    cmp = fixture.componentInstance;
    await settle();
    await settle();
  }

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  describe('new outlet', () => {
    beforeEach(() => setup());

    it('pre-fills one editable "Counter 1" row that is not persisted until the outlet is saved', () => {
      expect(rows().length).toBe(1);
      expect(rowInput(0).value).toBe('Counter 1');
      expect(rows()[0].textContent).toContain('assigned when saved');
      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(outletService.updateOutlet).not.toHaveBeenCalled();
    });

    it('explains that checkout needs an active terminal', () => {
      expect(root().textContent).toContain('Checkout is blocked at an outlet until it has at least one active terminal');
    });

    it('sends the invoice prefix in upper case, the terminals and no time zone when blank', async () => {
      await fillOutlet();
      await type('Invoice Prefix', 'dhk01');
      expect(control('Invoice Prefix').value).toBe('DHK01');
      await submit();

      expect(outletService.createOutlet).toHaveBeenCalledTimes(1);
      const payload = outletService.createOutlet.mock.calls[0][0];
      expect(payload).toEqual({
        name: 'Dhaka Main', address: '12 Road, Dhaka', contactNumber: '', managerId: null,
        invoicePrefix: 'DHK01',
        terminals: [{ name: 'Counter 1', isActive: true }]
      });
      expect(payload).not.toHaveProperty('timeZoneId');
      expect(location.back).toHaveBeenCalled();
    });

    it('lets the user rename the suggested row and add more rows', async () => {
      rowInput(0).value = 'Front desk';
      rowInput(0).dispatchEvent(new Event('input'));
      buttonByLabel('Add terminal')!.click();
      await settle();
      expect(rows().length).toBe(2);
      rowInput(1).value = 'Back desk';
      rowInput(1).dispatchEvent(new Event('input'));
      rowToggle(1).click();
      await settle();
      await fillOutlet();
      await submit();

      expect(outletService.createOutlet.mock.calls[0][0].terminals).toEqual([
        { name: 'Front desk', isActive: true },
        { name: 'Back desk', isActive: false }
      ]);
    });

    it('allows removing the suggested row, warns that sales are blocked and still saves', async () => {
      buttonByLabel('Remove unsaved terminal 1')!.click();
      await settle();
      expect(rows().length).toBe(0);
      expect(root().textContent).toContain('sales are blocked until an active terminal exists');
      await fillOutlet();
      await submit();

      expect(outletService.createOutlet.mock.calls[0][0].terminals).toEqual([]);
      expect(alerts.config()).toMatchObject({ type: 'warning' });
      expect(alerts.config()!.message).toContain('Sales are blocked');
      expect(location.back).toHaveBeenCalled();
    });

    it('rejects duplicate terminal names case-insensitively and does not call the API', async () => {
      buttonByLabel('Add terminal')!.click();
      await settle();
      rowInput(1).value = ' counter 1 ';
      rowInput(1).dispatchEvent(new Event('input'));
      await fillOutlet();
      await submit();

      expect(rows()[0].textContent).toContain('Another terminal at this outlet already has this name.');
      expect(rows()[1].textContent).toContain('Another terminal at this outlet already has this name.');
      expect(outletService.createOutlet).not.toHaveBeenCalled();

      rowInput(1).value = 'Counter 2';
      rowInput(1).dispatchEvent(new Event('input'));
      await submit();
      expect(outletService.createOutlet).toHaveBeenCalledTimes(1);
    });

    it('requires a terminal name and an outlet name', async () => {
      rowInput(0).value = '   ';
      rowInput(0).dispatchEvent(new Event('input'));
      await submit();
      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(rows()[0].textContent).toContain('Enter a terminal name.');
      expect(field('Outlet Name').textContent).toContain('Outlet name is required');
    });

    it('rejects an invalid invoice prefix client-side', async () => {
      await fillOutlet();
      await type('Invoice Prefix', '1ABC');
      await submit();
      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(field('Invoice Prefix').textContent).toContain('Use 1–10 letters or digits, starting with a letter.');
    });

    it('sends a chosen time zone override', async () => {
      await fillOutlet();
      await type('Time Zone', 'Asia/Dhaka');
      await submit();
      expect(outletService.createOutlet.mock.calls[0][0].timeZoneId).toBe('Asia/Dhaka');
    });
  });

  describe('without outlets.terminals.manage', () => {
    it('does not pre-fill or offer terminal editing and never sends terminals (create)', async () => {
      await setup({ permissions: ['outlets.create'] });
      expect(rows().length).toBe(0);
      expect(buttonByLabel('Add terminal')).toBeUndefined();
      expect(root().textContent).toContain('outlets.terminals.manage');
      await fillOutlet();
      await submit();
      expect(outletService.createOutlet.mock.calls[0][0]).not.toHaveProperty('terminals');
      expect(alerts.config()).toBeNull();
    });

    it('shows existing terminals read-only and omits terminals on update', async () => {
      await setup({ permissions: ['outlets.edit'], edit: true });
      expect(rows().length).toBe(3);
      for (let i = 0; i < 3; i++) {
        expect(rowInput(i).disabled).toBe(true);
        expect(rowToggle(i).disabled).toBe(true);
      }
      expect(qa('button').some(b => (b.getAttribute('aria-label') ?? '').startsWith('Delete terminal'))).toBe(false);
      await submit();
      expect(outletService.updateOutlet).toHaveBeenCalledTimes(1);
      expect(outletService.updateOutlet.mock.calls[0][1]).not.toHaveProperty('terminals');
    });
  });

  describe('editing an outlet', () => {
    beforeEach(() => setup({ edit: true }));

    it('loads the outlet, its prefix, and terminals with read-only codes and status badges', () => {
      expect(outletService.getOutletById).toHaveBeenCalledWith(5);
      expect(control('Outlet Name').value).toBe('Dhaka Main');
      expect(control('Invoice Prefix').value).toBe('DHK01');
      expect(rows().length).toBe(3);
      expect(rowInput(1).value).toBe('Counter 2');
      expect(rows()[0].textContent).toContain('T-001');
      expect(rows()[1].querySelectorAll('input[type="text"]').length).toBe(1);
      expect(rows()[1].textContent).toContain('Open session');
      expect(rows()[1].textContent).toContain('Has history');
      expect(rows()[0].textContent).not.toContain('Has history');
      expect(rows()[2].textContent).toContain('Inactive');
    });

    it('disables the active toggle for a terminal with an open session and explains why', () => {
      expect(rowToggle(1).disabled).toBe(true);
      expect(rowToggle(0).disabled).toBe(false);
      expect(rows()[1].textContent).toContain('Close the session before deactivating it');
    });

    it('offers delete only for persisted terminals without history', () => {
      expect(buttonByLabel('Delete terminal Counter 1')).toBeDefined();
      expect(buttonByLabel('Delete terminal Counter 2')).toBeUndefined();
      expect(buttonByLabel('Delete terminal Counter 3')).toBeUndefined();
      expect(rows()[2].textContent).toContain('Deactivate it instead');
    });

    it('offers remove only for unsaved rows', async () => {
      expect(qa('button').some(b => (b.getAttribute('aria-label') ?? '').startsWith('Remove unsaved'))).toBe(false);
      buttonByLabel('Add terminal')!.click();
      await settle();
      expect(rows()[3].textContent).toContain('Unsaved');
      expect(buttonByLabel('Remove unsaved terminal 4')).toBeDefined();
    });

    it('sends persisted rows by id, new rows without id and an empty time zone to clear the override', async () => {
      rowInput(0).value = 'Main counter';
      rowInput(0).dispatchEvent(new Event('input'));
      rowToggle(2).click();
      buttonByLabel('Add terminal')!.click();
      await settle();
      rowInput(3).value = 'Counter 4';
      rowInput(3).dispatchEvent(new Event('input'));
      await submit();

      expect(outletService.updateOutlet).toHaveBeenCalledWith(5, {
        name: 'Dhaka Main', address: '12 Road, Dhaka', contactNumber: '01700000000', managerId: null,
        invoicePrefix: 'DHK01',
        timeZoneId: '',
        terminals: [
          { id: 11, name: 'Main counter', isActive: true },
          { id: 12, name: 'Counter 2', isActive: true },
          { id: 13, name: 'Counter 3', isActive: true },
          { name: 'Counter 4', isActive: true }
        ]
      });
    });

    it('deletes a terminal without history only after confirmation', async () => {
      buttonByLabel('Delete terminal Counter 1')!.click();
      await settle();
      expect(outletService.deleteTerminal).not.toHaveBeenCalled();
      const confirm = qa<HTMLButtonElement>('button', document.body).find(b => b.textContent!.trim() === 'Delete terminal')!;
      expect(confirm).toBeDefined();
      confirm.click();
      await settle();

      expect(outletService.deleteTerminal).toHaveBeenCalledWith(5, 11);
      expect(rows().length).toBe(2);
      expect(rowInput(0).value).toBe('Counter 2');
    });

    it('keeps the row and explains when deleting is refused', async () => {
      outletService.deleteTerminal.mockReturnValue(throwError(() => apiError('TERMINAL_HAS_HISTORY')));
      cmp.requestDeleteTerminal(cmp.terminalRows()[0]);
      cmp.confirmDeleteTerminal();
      await settle();
      expect(rows().length).toBe(3);
      expect(root().textContent).toContain('has sales or cash-session history, so it cannot be deleted');
    });
  });

  describe('locked invoice prefix', () => {
    beforeEach(() => setup({ edit: true, outlet: { invoicePrefixLocked: true } }));

    it('is read-only with a locked hint and is not sent', async () => {
      expect(control('Invoice Prefix').disabled).toBe(true);
      expect(root().textContent).toContain('Locked: invoices were already issued with this prefix');
      await submit();
      expect(outletService.updateOutlet.mock.calls[0][1]).not.toHaveProperty('invoicePrefix');
    });
  });

  describe('API error mapping', () => {
    beforeEach(() => setup());

    const failWith = async (code: string, message = 'server message') => {
      outletService.createOutlet.mockReturnValue(throwError(() => apiError(code, message)));
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await fillOutlet();
      await submit();
      await settle();
    };

    it.each([
      ['OUTLET_INVOICE_PREFIX_INVALID', 'Use 1–10 letters or digits, starting with a letter.'],
      ['OUTLET_INVOICE_PREFIX_EXISTS', 'Another outlet in this business already uses this prefix.'],
      ['OUTLET_INVOICE_PREFIX_LOCKED', 'The prefix cannot change because invoices were already issued for this outlet.'],
      ['OUTLET_INVOICE_PREFIX_AMBIGUOUS', 'one prefix cannot extend another using only digits']
    ])('shows %s next to the invoice prefix', async (code, text) => {
      await failWith(code);
      expect(field('Invoice Prefix').textContent).toContain(text);
      expect(field('Invoice Prefix').querySelector('input')!.getAttribute('aria-invalid')).toBe('true');
      expect(q('app-ui-alert[data-variant="danger"]')).toBeNull();
    });

    it.each([
      ['TERMINAL_NAME_EXISTS', 'Terminal names must be unique'],
      ['TERMINAL_INVALID', 'Every terminal needs a name'],
      ['TERMINAL_HAS_OPEN_SESSION', 'open or overdue cash session cannot be deactivated']
    ])('shows %s in the terminals section', async (code, text) => {
      await failWith(code);
      expect(q('app-ui-card:has(.of-terminals)').textContent).toContain(text);
    });

    it('locks the prefix after OUTLET_INVOICE_PREFIX_LOCKED', async () => {
      await failWith('OUTLET_INVOICE_PREFIX_LOCKED');
      expect(cmp.invoicePrefixLocked()).toBe(true);
      expect(control('Invoice Prefix').disabled).toBe(true);
    });

    it('shows other errors, including 403 forbidden, as a form-level alert and allows retrying', async () => {
      await failWith('forbidden', 'ignored');
      expect(q('app-ui-alert[data-variant="danger"]').textContent).toContain('You do not have permission');
      await failWith('SOMETHING_ELSE', 'Boom');
      expect(q('app-ui-alert[data-variant="danger"]').textContent).toContain('Boom');
      expect(outletService.createOutlet).toHaveBeenCalledTimes(2);
    });
  });
});
