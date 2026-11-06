import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CompanyDateService } from '../../services/company-date.service';
import { CurrencyService } from '../../services/currency.service';
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
  targets?: Record<string, unknown>;
  targetsFail?: boolean;
}

const VERSION_DAYS = [0, 1, 2, 3, 4, 5, 6].map(dayOfWeek => ({
  dayOfWeek,
  isOffDay: dayOfWeek === 2,
  targetAmount: dayOfWeek === 2 ? null : 100
}));
const OLD_DAYS = VERSION_DAYS.map(day => (day.dayOfWeek === 2 ? { ...day, isOffDay: false, targetAmount: 500 } : day));
const LATEST = { id: 30, versionNo: 3, effectiveFrom: '2026-10-01', isEnabled: true, days: VERSION_DAYS, createdByUserId: 1, createdAt: '2026-10-01T00:00:00Z' };

/** Tuesday is closed in the latest version and had a 500 target in the one before. */
const CONFIGURED = {
  outletId: 5,
  today: '2026-11-06',
  latest: LATEST,
  current: LATEST,
  periods: [],
  history: [LATEST, { ...LATEST, id: 20, versionNo: 2, effectiveFrom: '2026-01-01', days: OLD_DAYS }]
};

const ALL = ['outlets.create', 'outlets.edit', 'outlets.terminals.manage'];

describe('OutletFormComponent (POS setup)', () => {
  let fixture: ComponentFixture<OutletFormComponent>;
  let cmp: OutletFormComponent;
  let outletService: {
    getOutletById: ReturnType<typeof vi.fn>;
    getSalesTargets: ReturnType<typeof vi.fn>;
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
      getSalesTargets: vi.fn(() => options.targetsFail ? throwError(() => new Error('down')) : of({ data: options.targets ?? null })),
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
        { provide: CurrencyService, useValue: { format: (value: number) => `৳${value}` } },
        { provide: CompanyDateService, useValue: { todayIso: () => '2026-11-06', format: (value: string) => value, formatWithPattern: (value: string) => value } },
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

    it('no longer offers or sends an outlet time zone', async () => {
      await fillOutlet();
      expect(Array.from(document.querySelectorAll('label')).some(label => label.textContent?.includes('Time Zone'))).toBe(false);
      await submit();
      expect(outletService.createOutlet.mock.calls[0][0]).not.toHaveProperty('timeZoneId');
    });
  });

  describe('invoice printing settings', () => {
    it('omits empty print settings on create and sends the ones that were filled in', async () => {
      await setup();
      await fillOutlet();
      await type('Invoice Print Branch Name', ' IRANI MART-1 ');
      await type('BIN Number', '0029807530203');
      await type('Mushak', '6.3');
      await type('Return / Exchange Policy', 'No refunds.\nExchange in 15 days.');
      await submit();

      const payload = outletService.createOutlet.mock.calls[0][0];
      expect(payload.invoicePrintBranchName).toBe('IRANI MART-1');
      expect(payload.binNumber).toBe('0029807530203');
      expect(payload.mushak).toBe('6.3');
      expect(payload.returnExchangePolicy).toBe('No refunds.\nExchange in 15 days.');
      expect(payload).not.toHaveProperty('vatLawNotification');
    });

    it('loads saved values and sends an empty value on edit so a setting can be cleared', async () => {
      await setup({ edit: true, outlet: { invoicePrintBranchName: 'IRANI MART-1', vatLawNotification: 'S.R.O. No. 19\nVAT applies.' } });
      expect(control('Invoice Print Branch Name').value).toBe('IRANI MART-1');
      expect(control<HTMLTextAreaElement>('VAT Law Notification').value).toBe('S.R.O. No. 19\nVAT applies.');

      await type('Invoice Print Branch Name', '');
      await submit();

      const payload = outletService.updateOutlet.mock.calls[0][1];
      expect(payload.invoicePrintBranchName).toBe('');
      expect(payload.vatLawNotification).toBe('S.R.O. No. 19\nVAT applies.');
    });

    it('defaults the branch name placeholder to the outlet name and refuses over-long text', async () => {
      await setup();
      await type('Outlet Name', 'Dhaka Main');
      expect(control('Invoice Print Branch Name').placeholder).toBe('Dhaka Main');

      await fillOutlet();
      await type('BIN Number', 'x'.repeat(51));
      await submit();
      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(field('BIN Number').textContent).toContain('at most 50 characters');
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
        invoicePrintBranchName: '',
        binNumber: '',
        mushak: '',
        returnExchangePolicy: '',
        vatLawNotification: '',
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

  describe('tabs', () => {
    const tabs = () => qa<HTMLButtonElement>('[role="tab"]');
    const panel = (id: string) => q<HTMLElement>(`#of-panel-${id}`);
    const openTab = async (index: number) => {
      tabs()[index].click();
      await settle();
    };

    it('renders exactly the three tabs, with Outlet Details open', async () => {
      await setup();
      expect(tabs().map(tab => tab.textContent!.trim())).toEqual(['Outlet Details', 'Invoice Settings', 'Sales Target']);
      expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
      expect(panel('details').hidden).toBe(false);
      expect(panel('invoice').hidden).toBe(true);
      expect(panel('target').hidden).toBe(true);
    });

    it('places each section on its tab', async () => {
      await setup({ edit: true });
      expect(panel('details').textContent).toContain('POS terminals');
      expect(panel('details').textContent).toContain('Weekly Off Days');
      expect(panel('invoice').textContent).toContain('Invoice Prefix');
      expect(panel('invoice').textContent).toContain('BIN Number');
      expect(panel('invoice').textContent).toContain('VAT Law Notification');
      expect(panel('target').textContent).toContain('Enable Sales Target');
    });

    it('keeps typed values when switching tabs', async () => {
      await setup();
      await type('Outlet Name', 'Banani');
      await openTab(1);
      await type('BIN Number', '12345');
      await openTab(2);
      await openTab(0);

      expect(control('Outlet Name').value).toBe('Banani');
      expect(control('BIN Number').value).toBe('12345');
    });

    it('moves between tabs with the arrow keys', async () => {
      await setup();
      tabs()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      await settle();
      expect(cmp.activeTab()).toBe('invoice');
      tabs()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
      await settle();
      expect(cmp.activeTab()).toBe('details');
    });

    it('marks the tab with an invalid field and opens it on a failed save', async () => {
      await setup();
      await fillOutlet();
      await openTab(1);
      await type('Invoice Prefix', '1BAD');
      await openTab(0);
      await submit();

      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(cmp.activeTab()).toBe('invoice');
      expect(tabs()[1].querySelector('.of-tab__error')).not.toBeNull();
      expect(tabs()[0].querySelector('.of-tab__error')).toBeNull();
    });

    it('opens the tab a server error belongs to', async () => {
      await setup();
      await fillOutlet();
      outletService.createOutlet.mockReturnValueOnce(throwError(() => apiError('OUTLET_INVOICE_PREFIX_EXISTS')));
      await submit();

      expect(cmp.activeTab()).toBe('invoice');
      expect(tabs()[1].querySelector('.of-tab__error')).not.toBeNull();
    });

    it('asks before leaving only when something changed, and not after saving', async () => {
      await setup();
      expect(cmp.hasUnsavedChanges()).toBe(false);
      await fillOutlet();
      cmp.outletForm.markAsDirty();
      expect(cmp.hasUnsavedChanges()).toBe(true);
      await submit();
      expect(cmp.hasUnsavedChanges()).toBe(false);
    });
  });

  describe('weekly off days', () => {
    const chips = () => qa('.wms__chip').map(chip => chip.textContent!.trim());
    const openDropdown = async () => {
      q<HTMLButtonElement>('.wms__trigger').click();
      await settle();
    };
    const dayOption = (label: string) =>
      qa<HTMLLabelElement>('.wms__menu label').find(item => item.textContent!.includes(label))!.querySelector('input')!;
    const pick = async (...labels: string[]) => {
      await openDropdown();
      for (const label of labels) {
        dayOption(label).click();
        await settle();
      }
    };

    it('starts with none selected and offers the seven days from Saturday', async () => {
      await setup();
      expect(q('.wms__trigger').textContent).toContain('No off days');
      await openDropdown();
      expect(qa('.wms__menu label').map(item => item.textContent!.trim())).toEqual(
        ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']);
    });

    it('sends nothing for targets when no off day or target was set', async () => {
      await setup();
      await fillOutlet();
      await submit();
      expect(outletService.createOutlet.mock.calls[0][0].salesTargets).toBeUndefined();
    });

    it('saves several off days on a new outlet, with chips that can be removed', async () => {
      await setup();
      await fillOutlet();
      await pick('Tuesday', 'Friday');
      expect(chips()).toEqual(['Tuesday', 'Friday']);

      q<HTMLButtonElement>('[aria-label="Remove Friday"]').click();
      await settle();
      expect(chips()).toEqual(['Tuesday']);

      await submit();
      const body = outletService.createOutlet.mock.calls[0][0].salesTargets;
      expect(body.isEnabled).toBe(false);
      expect(body.expectedVersionNo).toBeNull();
      expect(body.days.filter((day: any) => day.isOffDay).map((day: any) => day.dayOfWeek)).toEqual([2]);
    });

    it('refuses all seven days with a clear message', async () => {
      await setup();
      await fillOutlet();
      await pick('Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday');
      await submit();

      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(q('.wms__error').textContent).toContain('cannot be closed on all seven days');
      expect(cmp.activeTab()).toBe('details');
    });

    it('loads saved off days on edit', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      expect(chips()).toEqual(['Tuesday']);
    });

    it('sends nothing for targets when another tab is the only change', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      await type('Outlet Name', 'Renamed outlet');
      await submit();
      expect(outletService.updateOutlet.mock.calls[0][1].salesTargets).toBeUndefined();
    });
  });

  describe('sales targets', () => {
    const dayInput = (label: string) => control<HTMLInputElement>(label);
    const setDay = async (label: string, value: string) => type(label, value);
    const enable = async () => {
      const box = qa<HTMLLabelElement>('label.ui-check').find(item => item.textContent!.includes('Enable Sales Target'))!.querySelector('input')!;
      box.click();
      await settle();
    };
    const fillDays = async (amount = '100') => {
      for (const day of ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']) {
        if (dayInput(day)) await setDay(day, amount);
      }
    };
    const offDay = async (day: number) => {
      cmp.outletForm.get('weeklyOffDays')!.setValue(day === -1 ? [] : [day]);
      await settle();
    };
    const fullWeek = () => q('.of-full-week').textContent!;
    const periodTotal = (label: string) =>
      qa('.of-period').find(item => item.textContent!.includes(label))!.querySelector('.of-day__amount')!.textContent!.trim();

    it('is off by default and hides the amounts', async () => {
      await setup();
      expect(root().textContent).toContain('Sales targets are off for this outlet');
      expect(qa('.of-days').length).toBe(0);
    });

    it('requires every open day once enabled', async () => {
      await setup();
      await fillOutlet();
      await enable();
      await submit();

      expect(outletService.createOutlet).not.toHaveBeenCalled();
      expect(cmp.activeTab()).toBe('target');
    });

    it('rejects a negative amount and more than two decimals', async () => {
      await setup();
      await enable();
      await setDay('Monday', '-5');
      expect(cmp.dayControl(1).hasError('min')).toBe(true);
      await setDay('Monday', '1.005');
      expect(cmp.dayControl(1).hasError('money')).toBe(true);
      await setDay('Monday', '1.05');
      expect(cmp.dayControl(1).valid).toBe(true);
    });

    it('replaces a closed day input with Off Day at zero and recalculates', async () => {
      await setup();
      await enable();
      await fillDays();
      expect(fullWeek()).toContain('৳700');

      await offDay(2);

      expect(field('Tuesday')).toBeUndefined();
      expect(qa('.of-day__closed')[0].textContent).toContain('Tuesday');
      expect(qa('.of-day__closed')[0].textContent).toContain('Off Day');
      expect(cmp.dayControl(2).disabled).toBe(true);
      expect(fullWeek()).toContain('৳600');
    });

    it('sends the exact weekly-example amounts', async () => {
      await setup();
      await fillOutlet();
      await enable();
      await offDay(2);
      for (const [day, amount] of Object.entries({ Saturday: '15000', Sunday: '12000', Monday: '12000', Wednesday: '12000', Thursday: '16000', Friday: '20000' })) {
        await setDay(day, amount);
      }
      expect(fullWeek()).toContain('৳87000');
      await submit();

      const body = outletService.createOutlet.mock.calls[0][0].salesTargets;
      expect(body.isEnabled).toBe(true);
      expect(body.days.find((day: any) => day.dayOfWeek === 2)).toEqual({ dayOfWeek: 2, isOffDay: true, targetAmount: null });
      expect(body.days.find((day: any) => day.dayOfWeek === 5).targetAmount).toBe(20000);
    });

    it('previews only dates on or after the effective date, and a whole later week in full', async () => {
      await setup();
      await enable();
      await fillDays();
      // Today is the last day (Friday) of its Saturday-to-Friday week.
      expect(periodTotal('Weekly')).toBe('৳100');
      expect(root().textContent).toContain('6 day(s) of this period have no target');

      cmp.onReferenceDateChange('2026-11-10');
      await settle();
      expect(periodTotal('Weekly')).toBe('৳700');
      expect(periodTotal('First Half')).toBe('৳1000'); // 6th to 15th
      expect(periodTotal('Second Half')).toBe('৳1500');
      expect(periodTotal('Monthly')).toBe('৳2500');
    });

    it('does not bring back an old amount when a day reopens, but shows it', async () => {
      await setup();
      await enable();
      await fillDays('250');
      await offDay(2);
      await offDay(-1);

      expect(dayInput('Tuesday').value).toBe('');
      expect(cmp.dayControl(2).hasError('required')).toBe(true);
      expect(root().textContent).toContain('Previously ৳250');
    });

    it('explains the effect of closing a day that has a saved target', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      await offDay(1);
      expect(root().textContent).toContain('stops counting from the effective date');
    });

    it('shows a reopened day from saved history, not silently restored', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      await offDay(-1);
      expect(dayInput('Tuesday').value).toBe('');
      expect(root().textContent).toContain('Previously ৳500');
    });

    it('loads saved amounts and sends the loaded version number with a change', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      expect(dayInput('Monday').value).toBe('100');
      await setDay('Monday', '150');
      await submit();

      const body = outletService.updateOutlet.mock.calls[0][1].salesTargets;
      expect(body.expectedVersionNo).toBe(3);
      expect(body.effectiveFrom).toBe('2026-11-06');
    });

    it('refuses an effective date in the past', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      const date = control<HTMLInputElement>('Effective From');
      date.value = '2026-11-01';
      date.dispatchEvent(new Event('input'));
      await settle();

      await submit();
      expect(outletService.updateOutlet).not.toHaveBeenCalled();
      expect(cmp.activeTab()).toBe('target');
    });

    it('marks a period overridden, sends it, and offers Reset to Auto', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      const period = () => qa('.of-period').find(item => item.textContent!.includes('Monthly'))!;
      expect(period().textContent).toContain('Auto Calculated');

      const amount = period().querySelector<HTMLInputElement>('input[type="number"]')!;
      amount.value = '9000';
      amount.dispatchEvent(new Event('input'));
      await settle();
      expect(period().textContent).toContain('Manually Overridden');
      expect(period().querySelector('.of-day__amount')!.textContent).toContain('৳9000');

      await submit();
      const sent = outletService.updateOutlet.mock.calls[0][1].salesTargets.overrides;
      expect(sent).toEqual([{ periodType: 'monthly', periodFrom: '2026-11-01', overrideAmount: 9000, reason: null }]);
    });

    it('sends a null amount to reset a saved override', async () => {
      const periods = [{ periodType: 'monthly', from: '2026-11-01', to: '2026-11-30', overrideAmount: 5000, isOverridden: true, reason: 'Eid', calculated: 2600, target: 5000, updatedByUserId: 1, updatedAt: null, workingDays: 26, offDays: 4, notConfiguredDays: 0 }];
      await setup({ edit: true, targets: { ...CONFIGURED, periods } });
      const period = qa('.of-period').find(item => item.textContent!.includes('Monthly'))!;
      expect(period.textContent).toContain('Manually Overridden');

      Array.from(period.querySelectorAll('button')).find(button => button.textContent!.includes('Reset to Auto'))!.click();
      await settle();
      await submit();

      const sent = outletService.updateOutlet.mock.calls[0][1].salesTargets.overrides;
      expect(sent).toEqual([{ periodType: 'monthly', periodFrom: '2026-11-01', overrideAmount: null, reason: null }]);
    });

    it('keeps an override edit while looking at another month', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      const monthly = () => qa('.of-period').find(item => item.textContent!.includes('Monthly'))!;
      const amount = monthly().querySelector<HTMLInputElement>('input[type="number"]')!;
      amount.value = '9000';
      amount.dispatchEvent(new Event('input'));
      await settle();

      cmp.onReferenceDateChange('2026-12-10');
      await settle();
      expect(outletService.getSalesTargets).toHaveBeenLastCalledWith(5, '2026-12-10');
      expect(monthly().querySelector<HTMLInputElement>('input[type="number"]')!.value).toBe('');

      await submit();
      expect(outletService.updateOutlet.mock.calls[0][1].salesTargets.overrides[0].periodFrom).toBe('2026-11-01');
    });

    it('shows a conflict from the server on the Sales Target tab', async () => {
      await setup({ edit: true, targets: CONFIGURED });
      await setDay('Monday', '150');
      outletService.updateOutlet.mockReturnValueOnce(throwError(() => apiError('OUTLET_TARGET_CONFLICT', 'Reload the page')));
      await submit();

      expect(cmp.activeTab()).toBe('target');
      expect(root().textContent).toContain('Reload the page');
    });

    it('leaves targets out of the save when they could not be loaded', async () => {
      await setup({ edit: true, targetsFail: true });
      expect(root().textContent).toContain('Sales targets not loaded');
      await type('Outlet Name', 'Renamed outlet');
      await submit();

      expect(outletService.updateOutlet).toHaveBeenCalledTimes(1);
      expect(outletService.updateOutlet.mock.calls[0][1].salesTargets).toBeUndefined();
    });

    it('keeps invoice and terminal behaviour when targets change', async () => {
      await setup({ edit: true, outlet: { invoicePrefixLocked: true }, targets: CONFIGURED });
      expect(control('Invoice Prefix').disabled).toBe(true);
      await setDay('Monday', '150');
      await submit();

      const payload = outletService.updateOutlet.mock.calls[0][1];
      expect(payload.invoicePrefix).toBeUndefined();
      expect(payload.terminals.length).toBe(3);
    });
  });
});
