import { TestBed } from '@angular/core/testing';
import { NEVER, Subject } from 'rxjs';
import { MenuService } from './menu.service';
import { AuthService } from './auth.service';
import { MenuItem, NAV_SECTIONS } from '../models/menu.model';
import { HrPayrollCapabilityStore } from './hr-payroll-capability.store';
import { HrPayrollCapabilityService } from './hr-payroll-capability.service';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a parent menu item with the supplied children. */
function parent(id: string, children: MenuItem[]): MenuItem {
  return { id, label: id, icon: '', children };
}

/** Flatten a menu tree into its routable leaf pages. */
function leavesOf(items: MenuItem[]): MenuItem[] {
  return items.flatMap(item => (item.children?.length ? leavesOf(item.children) : [item]));
}

/** Build a leaf menu item gated by a single permission string. */
function leaf(id: string, permission?: string, role?: string): MenuItem {
  return { id, label: id, icon: '', route: `/${id}`, permission, role };
}

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('MenuService – filterMenuItems', () => {
  let service: MenuService;

  // Use `any` cast so vitest 4.x mock types don't cause TS2345 errors on
  // mockReturnValue / mockImplementation call sites.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const mockAuthService: any = {
    currentUser$: new Subject<void>().asObservable(),
    hasPermission: vi.fn().mockReturnValue(false),
    hasAnyPermission: vi.fn().mockReturnValue(false),
    hasRole: vi.fn().mockReturnValue(false),
    getUserValue: vi.fn().mockReturnValue(null),
    isAuthenticated: vi.fn().mockReturnValue(false),
  };

  beforeEach(() => {
    // Reset all mocks to "deny everything" before every test.
    mockAuthService.hasPermission.mockReturnValue(false);
    mockAuthService.hasAnyPermission.mockReturnValue(false);
    mockAuthService.hasRole.mockReturnValue(false);

    TestBed.configureTestingModule({
      providers: [
        MenuService,
        { provide: AuthService, useValue: mockAuthService },
        { provide: HrPayrollCapabilityService, useValue: { load: () => NEVER } },
      ],
    });

    service = TestBed.inject(MenuService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  /** Direct access to the private filtering method for unit testing. */
  function filter(items: MenuItem[]): MenuItem[] {
    return (service as any).filterMenuItems(items);
  }

  /** Visible HR & Payroll pages (level-3 leaves) for the static NAV_SECTIONS. */
  function visibleNavLeaves(): MenuItem[] {
    return leavesOf(filter(NAV_SECTIONS.flatMap(section => section.items)));
  }

  it('makes opening import discoverable without granting employee or payroll navigation', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockImplementation(capability => capability === 'hr.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'hr.import.view');
    const routes = visibleNavLeaves().map(item => item.route);
    expect(routes).toContain('/hr/opening-imports');
    expect(routes).not.toContain('/hr/employees');
    expect(routes).not.toContain('/payroll');
  });

  it('hides opening import when HR is disabled even with import permission', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled').mockReturnValue(false);
    mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'hr.import.view');
    expect(visibleNavLeaves().map(item => item.route)).not.toContain('/hr/opening-imports');
  });

  it('shows Payroll navigation only with Payroll capability and both confidential dashboard permissions', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockImplementation(capability => capability === 'hr.enabled' || capability === 'payroll.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.dashboard.view' || permission === 'payroll.salary.view');

    const payroll = visibleNavLeaves().find(item => item.id === 'payroll-dashboard');

    expect(payroll?.route).toBe('/payroll/dashboard');

    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.dashboard.view');
    expect(visibleNavLeaves().some(item => item.id === 'payroll-dashboard')).toBe(false);
  });

  it('hides Payroll navigation when capability or permission is missing', () => {
    const capabilityStore = TestBed.inject(HrPayrollCapabilityStore);
    vi.spyOn(capabilityStore, 'isEnabled')
      .mockImplementation(capability => capability === 'hr.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.dashboard.view' || permission === 'payroll.salary.view');
    expect(visibleNavLeaves().some(item => item.id === 'payroll-dashboard')).toBe(false);

    vi.spyOn(capabilityStore, 'isEnabled')
      .mockImplementation(capability => capability === 'hr.enabled' || capability === 'payroll.enabled');
    mockAuthService.hasPermission.mockReturnValue(false);
    expect(visibleNavLeaves().some(item => item.id === 'payroll-dashboard')).toBe(false);
  });

  it('shows Payroll reports only with Payroll capability and both report permissions', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockImplementation(capability => capability === 'hr.enabled' || capability === 'payroll.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.report.view' || permission === 'payroll.salary.view');

    const report = visibleNavLeaves().find(item => item.id === 'payroll-reports');
    expect(report?.route).toBe('/payroll/reports');

    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.report.view');
    expect(visibleNavLeaves().some(item => item.id === 'payroll-reports')).toBe(false);
  });

  it('shows salary setup only when Payroll capability and salary view permission are available', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockImplementation(capability => capability === 'payroll.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.salary.view');

    const salarySetup = visibleNavLeaves().find(item => item.id === 'payroll-salary-setup');

    expect(salarySetup?.route).toBe('/payroll/salary-setup');

    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockReturnValue(false);
    expect(visibleNavLeaves().some(item => item.id === 'payroll-salary-setup')).toBe(false);
  });

  it('shows payroll validation only when Payroll capability and salary view permission are available', () => {
    vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
      .mockImplementation(capability => capability === 'payroll.enabled');
    mockAuthService.hasPermission.mockImplementation((permission: string) =>
      permission === 'payroll.salary.view');

    const validation = visibleNavLeaves().find(item => item.id === 'payroll-validation');

    expect(validation?.route).toBe('/payroll/validation');
  });

  describe('HR & Payroll 3-level hierarchy', () => {
    const hrSection = () => NAV_SECTIONS.find(section => section.id === 'hr-payroll')!;
    const group = (items: MenuItem[], id: string) => items.find(item => item.id === id);
    const visibleHr = () => filter(hrSection().items);
    const allowAll = () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled').mockReturnValue(true);
      mockAuthService.hasPermission.mockReturnValue(true);
      mockAuthService.hasAnyPermission.mockReturnValue(true);
    };

    it('declares the target level-2 groups and their child pages in order', () => {
      const shape = hrSection().items.map(item => [item.label, (item.children ?? []).map(child => child.label)]);
      expect(shape).toEqual([
        ['HR Dashboard', []],
        ['Employee Management', ['Employees', 'Organisation', 'Positions', 'Operational Locations']],
        ['Attendance', ['Shifts', 'Holidays', 'Weekly Off', 'Rosters', 'Attendance Devices']],
        ['Leave & Overtime', ['Leave Requests & Policies', 'Overtime']],
        ['Employee Exit', ['Exit & Clearance']],
        ['Payroll', [
          'Payroll Dashboard', 'Salary Setup', 'Loans & Advances', 'Payroll Validation',
          'Payroll Periods', 'Payments & Bank Files'
        ]],
        ['Imports', ['Opening Employee Import', 'Opening Payroll Import']],
        ['Reports', ['Core HR Reports', 'Attendance Reports', 'Payroll Reports']]
      ]);
    });

    it('never lists the same route twice', () => {
      const routes = leavesOf(hrSection().items).map(item => item.route);
      expect(new Set(routes).size).toBe(routes.length);
    });

    it('exposes every group when all capabilities and permissions are granted', () => {
      allowAll();
      expect(visibleHr().map(item => item.id)).toEqual(hrSection().items.map(item => item.id));
    });

    it('hides the Payroll group and Payroll imports/reports when Payroll is disabled', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
        .mockImplementation(capability => capability !== 'payroll.enabled');
      mockAuthService.hasPermission.mockReturnValue(true);
      mockAuthService.hasAnyPermission.mockReturnValue(true);

      const visible = visibleHr();
      expect(group(visible, 'payroll-group')).toBeUndefined();
      expect(group(visible, 'hr-group-imports')!.children!.map(child => child.id)).toEqual(['hr-opening-imports']);
      expect(group(visible, 'hr-group-reports')!.children!.map(child => child.id))
        .toEqual(['hr-core-reports', 'hr-attendance-reports']);
    });

    it('shows an HR-only user the HR groups and none of the Payroll pages', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
        .mockImplementation(capability => capability === 'hr.enabled');
      mockAuthService.hasPermission.mockImplementation((permission: string) => permission.startsWith('hr.'));
      mockAuthService.hasAnyPermission.mockImplementation((permissions: string[]) =>
        permissions.some(permission => permission.startsWith('hr.')));

      const visible = visibleHr();
      expect(group(visible, 'hr-group-employee-management')!.children!.map(child => child.id))
        .toEqual(['hr-employees', 'hr-organisation', 'hr-positions', 'hr-locations']);
      expect(group(visible, 'hr-group-leave-overtime')!.children!.map(child => child.id)).toEqual(['hr-leave']);
      expect(group(visible, 'hr-group-attendance')).toBeUndefined();
      expect(group(visible, 'payroll-group')).toBeUndefined();
      expect(leavesOf(visible).some(item => item.route?.startsWith('/payroll'))).toBe(false);
    });

    it('hides unauthorised children but keeps the group while one child remains', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled').mockReturnValue(true);
      mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'hr.position.view');

      const management = group(visibleHr(), 'hr-group-employee-management');
      expect(management!.children!.map(child => child.id)).toEqual(['hr-positions']);
    });

    it('hides a group when none of its children are visible', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled').mockReturnValue(true);
      mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'hr.position.view');

      expect(visibleHr().map(item => item.id)).toEqual(['hr-group-employee-management']);
    });

    it('hides the Attendance group when the Attendance capability is off', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
        .mockImplementation(capability => capability !== 'attendance.enabled' && capability !== 'deviceintegration.enabled');
      mockAuthService.hasPermission.mockReturnValue(true);
      mockAuthService.hasAnyPermission.mockReturnValue(true);

      expect(group(visibleHr(), 'hr-group-attendance')).toBeUndefined();
    });

    it('keeps the Payments route behind the Payroll capability and its payment permissions', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
        .mockImplementation(capability => capability === 'payroll.enabled');
      mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'payroll.payment.view');
      mockAuthService.hasAnyPermission.mockImplementation((permissions: string[]) =>
        permissions.includes('payroll.payment.view'));

      expect(leavesOf(visibleHr()).map(item => item.route)).toEqual(['/payroll/payments']);
    });

    it('leaves every non-HR section flat so their navigation is unchanged', () => {
      const others = NAV_SECTIONS.filter(section => section.id !== 'hr-payroll');
      expect(others.length).toBeGreaterThan(0);
      expect(others.flatMap(section => section.items).some(item => item.children?.length)).toBe(false);
    });

    it('requires Payroll capability for Opening Payroll Import', () => {
      vi.spyOn(TestBed.inject(HrPayrollCapabilityStore), 'isEnabled')
        .mockImplementation(capability => capability === 'hr.enabled');
      mockAuthService.hasPermission.mockImplementation((permission: string) => permission === 'payroll.import.view');

      expect(leavesOf(visibleHr()).map(item => item.route)).not.toContain('/payroll/opening-imports');
    });
  });

  // -------------------------------------------------------------------------
  // Rule 1 – Parent menu with children
  // -------------------------------------------------------------------------

  describe('parent menu with submenus', () => {
    const stockMovement = parent('stock-movement', [
      leaf('stock-transfers', 'stock_transfers.view'),
      leaf('stock-adjustments', 'stock_adjustments.view'),
    ]);

    it('is visible when at least one submenu is permitted', () => {
      mockAuthService.hasPermission.mockImplementation((p: string) => p === 'stock_transfers.view');

      const result = filter([stockMovement]);

      expect(result).toHaveLength(1);
      expect(result[0].id).toBe('stock-movement');
    });

    it('is hidden when no submenu is permitted', () => {
      // hasPermission already returns false (default).
      const result = filter([stockMovement]);

      expect(result).toHaveLength(0);
    });

    it('exposes only the permitted submenus in the filtered children', () => {
      mockAuthService.hasPermission.mockImplementation((p: string) => p === 'stock_adjustments.view');

      const result = filter([stockMovement]);

      expect(result[0].children).toHaveLength(1);
      expect(result[0].children![0].id).toBe('stock-adjustments');
    });

    it('exposes all submenus when user has all required permissions', () => {
      mockAuthService.hasPermission.mockReturnValue(true);

      const result = filter([stockMovement]);

      expect(result[0].children).toHaveLength(2);
    });

    it('never produces an empty dropdown – parent is fully removed when all children are denied', () => {
      // Both stock_transfers.view and stock_adjustments.view denied → parent must not appear.
      const result = filter([stockMovement]);

      const ids = result.map((m) => m.id);
      expect(ids).not.toContain('stock-movement');
    });
  });

  // -------------------------------------------------------------------------
  // Rule 2 – Direct (leaf) menu item
  // -------------------------------------------------------------------------

  describe('direct menu item (no children)', () => {
    it('is visible when the user has the required permission', () => {
      mockAuthService.hasPermission.mockImplementation((p: string) => p === 'users.view');

      expect(filter([leaf('users', 'users.view')])).toHaveLength(1);
    });

    it('is hidden when the user lacks the required permission', () => {
      expect(filter([leaf('users', 'users.view')])).toHaveLength(0);
    });

    it('is always visible when no permission is required (e.g. Dashboard)', () => {
      // hasPermission returns false but item has no permission → must still show.
      expect(filter([leaf('dashboard')])).toHaveLength(1);
    });

    it('is hidden when the user does not have the required role', () => {
      mockAuthService.hasRole.mockReturnValue(false);

      expect(filter([leaf('businesses', undefined, 'Super Admin')])).toHaveLength(0);
    });

    it('is visible when the user has the required role', () => {
      mockAuthService.hasRole.mockImplementation((r: string) => r === 'Super Admin');

      expect(filter([leaf('businesses', undefined, 'Super Admin')])).toHaveLength(1);
    });
  });

  // -------------------------------------------------------------------------
  // Rule 3 – Mixed menu (parent + sibling leaf items)
  // -------------------------------------------------------------------------

  describe('mixed menu list', () => {
    it('filters each item independently', () => {
      // Dashboard (no permission) + Stock Movement (no permissions granted) + Users (granted).
      mockAuthService.hasPermission.mockImplementation((p: string) => p === 'users.view');

      const items: MenuItem[] = [
        leaf('dashboard'),
        parent('stock-movement', [
          leaf('stock-transfers', 'stock_transfers.view'),
          leaf('stock-adjustments', 'stock_adjustments.view'),
        ]),
        leaf('users', 'users.view'),
      ];

      const result = filter(items);
      const ids = result.map((m) => m.id);

      expect(ids).toContain('dashboard');      // no permission needed
      expect(ids).not.toContain('stock-movement'); // all children denied
      expect(ids).toContain('users');              // permitted
    });

    it('refreshes the menuItems signal after login (currentUser$ emission)', () => {
      // Initially no permissions → Administration hidden.
      const adminMenu = parent('admin', [
        leaf('users', 'users.view'),
        leaf('roles', 'roles.view'),
      ]);
      mockAuthService.hasPermission.mockReturnValue(false);
      const before = (service as any).filterMenuItems([adminMenu]);
      expect(before).toHaveLength(0);

      // Grant permission and re-filter.
      mockAuthService.hasPermission.mockReturnValue(true);
      const after = (service as any).filterMenuItems([adminMenu]);
      expect(after).toHaveLength(1);
    });
  });

  describe('P3-08 legacy finance controls', () => {
    it('does not expose legacy Account, Transaction, or Bill workflows in active navigation', () => {
      const finance = NAV_SECTIONS.find(section => section.id === 'finance');
      const routes = finance?.items.map(item => item.route) ?? [];

      expect(routes).not.toContain('/accounts');
      expect(routes).not.toContain('/transactions');
      expect(routes).not.toContain('/bills');
    });
  });

  describe('Accounting Manager role menu visibility', () => {
    it('shows intended accounting/reporting menus without administration or legacy finance write-workflow entries', () => {
      const accountingManagerPermissions = new Set([
        'accounting.configuration.view', 'accounting.financial_years.view', 'accounting.periods.view',
        'accounting.expenses.view', 'accounting.supplier_payments.view', 'accounting.supplier_advances.view',
        'accounting.other_income.view', 'accounting.manual_journals.view', 'accounting.reconciliation.view',
        'accounting.close.audit.view', 'accounting.reports.general_ledger', 'accounting.reports.trial_balance',
        'accounting.reports.profit_loss', 'accounting.reports.balance_sheet', 'accounting.reports.cash_book',
        'accounting.reports.cash_flow', 'accounting.reports.profitability', 'accounting.reports.cogs',
        'accounting.reports.inventory_valuation', 'accounting.reports.ap_subledger', 'accounting.reports.ap_aging',
        'accounting.reports.supplier_statement', 'accounting.reports.ap_reconciliation', 'accounting.reports.vat'
      ]);
      mockAuthService.hasPermission.mockImplementation((p: string) => accountingManagerPermissions.has(p));
      mockAuthService.hasAnyPermission.mockImplementation((permissions: string[]) => permissions.some(p => accountingManagerPermissions.has(p)));

      const result = filter(NAV_SECTIONS.flatMap(section => section.items));
      const routes = result.map(item => item.route);

      expect(routes).toContain('/settings/accounting');
      expect(routes).toContain('/manual-journals');
      expect(routes).toContain('/accounting/reconciliation');
      expect(routes).toContain('/reports/cash-flow');
      expect(routes).toContain('/reports/profitability');
      expect(routes).not.toContain('/users');
      expect(routes).not.toContain('/roles');
      expect(routes).not.toContain('/accounts');
      expect(routes).not.toContain('/transactions');
      expect(routes).not.toContain('/bills');
    });
  });
});
