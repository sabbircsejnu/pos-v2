import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RoleService } from '../../services/role.service';
import { RoleFormComponent } from './role-form.component';

const financePermissions = [
  'accounting.expenses.view', 'accounting.expenses.create', 'accounting.expenses.edit',
  'accounting.reconciliation.view', 'accounting.close.audit.view', 'accounting.statement_import.view',
  'payroll.period.create', 'payroll.final_settlement.payment.hold'
];

describe('RoleFormComponent finance permission assignment', () => {
  let roleService: { getAllPermissions: ReturnType<typeof vi.fn>; getRoleById: ReturnType<typeof vi.fn>; updateRole: ReturnType<typeof vi.fn>; createRole: ReturnType<typeof vi.fn> };

  const create = () => {
    const fixture = TestBed.createComponent(RoleFormComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  beforeEach(() => {
    roleService = {
      getAllPermissions: vi.fn(() => of(financePermissions)),
      getRoleById: vi.fn(() => of({ id: 3, name: 'Accounts', permissions: ['accounting.expenses.view'] })),
      updateRole: vi.fn(() => of({})),
      createRole: vi.fn(() => of({}))
    };
    TestBed.configureTestingModule({
      imports: [RoleFormComponent],
      providers: [provideRouter([]), { provide: RoleService, useValue: roleService }]
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('groups the expense, expense category, bank reconciliation and close permissions under Finance', () => {
    const groups = new Map(create().visiblePermissionCategories.map(category => [category.name, category.permissions]));

    expect(groups.get('Finance - Expenses and Expense Categories')).toEqual([
      'accounting.expenses.view', 'accounting.expenses.create', 'accounting.expenses.edit'
    ]);
    expect(groups.get('Finance - Bank Reconciliation and Adjustments')).toEqual(['accounting.reconciliation.view']);
    expect(groups.get('Finance - Period Close')).toEqual(['accounting.close.audit.view']);
    expect(groups.get('Finance - Bank Statements, Settlements and Transfers')).toEqual(['accounting.statement_import.view']);
  });

  it('offers the payroll period and final-settlement payment permissions that the API enforces', () => {
    const offered = create().visiblePermissionCategories.flatMap(category => category.permissions);

    expect(offered).toContain('payroll.period.create');
    expect(offered).toContain('payroll.final_settlement.payment.hold');
  });

  it('persists a newly assigned Finance permission together with the role\'s existing ones', () => {
    const component = create();
    component.isEditMode.set(true);
    component.roleId.set(3);
    component.loadRole(3);
    component.togglePermission('accounting.expenses.edit');

    component.onSubmit();

    expect(roleService.updateRole).toHaveBeenCalledWith(3, {
      name: 'Accounts',
      permissions: ['accounting.expenses.view', 'accounting.expenses.edit']
    });
  });

  it('lists the discount permissions under Sales Management and terminal management under Outlet Management', () => {
    const all = [
      'sales.view', 'sales.refund', 'sales.discount.apply', 'sales.discount.override_limit', 'sales.discount.configure',
      'outlets.view', 'outlets.edit', 'outlets.terminals.manage'
    ];
    roleService.getAllPermissions.mockReturnValue(of(all));
    const groups = new Map(create().visiblePermissionCategories.map(category => [category.name, category.permissions]));

    expect(groups.get('Sales Management')).toEqual([
      'sales.view', 'sales.refund', 'sales.discount.apply', 'sales.discount.override_limit', 'sales.discount.configure'
    ]);
    expect(groups.get('Outlet Management')).toEqual(['outlets.view', 'outlets.edit', 'outlets.terminals.manage']);
  });

  it('offers every new Phase 2 permission before the permission list loads', () => {
    roleService.getAllPermissions.mockReturnValue(of([]));
    const offered = create().visiblePermissionCategories.flatMap(category => category.permissions);

    for (const permission of ['sales.discount.apply', 'sales.discount.override_limit', 'sales.discount.configure', 'outlets.terminals.manage']) {
      expect(offered).toContain(permission);
    }
  });

  it('shows readable labels and descriptions for the new permissions and renders them', () => {
    roleService.getAllPermissions.mockReturnValue(of(['sales.discount.apply', 'outlets.terminals.manage']));
    const fixture = TestBed.createComponent(RoleFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    expect(component.getPermissionDisplayName('sales.discount.apply')).toBe('Apply Manual Discounts');
    expect(component.getPermissionDisplayName('sales.discount.override_limit')).toBe('Override Discount Limit');
    expect(component.getPermissionDisplayName('sales.discount.configure')).toBe('Configure Discount Limit');
    expect(component.getPermissionDisplayName('outlets.terminals.manage')).toBe('Manage POS Terminals');
    expect(component.getPermissionDescription('outlets.terminals.manage')).toContain('POS terminals');
    expect(component.getPermissionDisplayName('sales.view')).toBe('sales.view');
    expect(component.getPermissionDescription('sales.view')).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Apply Manual Discounts');
    expect(fixture.nativeElement.textContent).toContain('Manage POS Terminals');
  });

  it('persists a newly granted discount permission', () => {
    const component = create();
    component.isEditMode.set(true);
    component.roleId.set(3);
    component.loadRole(3);
    component.togglePermission('sales.discount.apply');

    component.onSubmit();

    expect(roleService.updateRole).toHaveBeenCalledWith(3, {
      name: 'Accounts',
      permissions: ['accounting.expenses.view', 'sales.discount.apply']
    });
  });
});
