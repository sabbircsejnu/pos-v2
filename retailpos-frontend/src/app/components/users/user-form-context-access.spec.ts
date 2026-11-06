import { TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UserFormComponent } from './user-form.component';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';

const MANAGE = 'users.context_access.manage';
const GRANT_ALL = 'users.context_access.grant_all';

describe('UserFormComponent context switching', () => {
  const userService = {
    createUser: vi.fn(() => of({})),
    updateUser: vi.fn(() => of({})),
    getUserById: vi.fn(),
    getContextRoleOptions: vi.fn(() => of([{ id: 8, name: 'Cashier' }, { id: 9, name: 'OutletManager' }]))
  };
  const roles = [
    { id: 5, name: 'Warehouse Clerk' },
    { id: 6, name: 'BusinessOwner' },
    { id: 7, name: 'Super Admin' }
  ];

  function setup(permissions: string[], routeParams: Record<string, string> = {}): UserFormComponent {
    TestBed.configureTestingModule({
      imports: [UserFormComponent],
      providers: [
        { provide: UserService, useValue: userService },
        { provide: RoleService, useValue: { getAllRoles: () => of(roles), getAllPermissions: () => of([]) } },
        { provide: OutletService, useValue: { getAllOutlets: () => of({ data: [] }) } },
        { provide: WarehouseService, useValue: { getAllWarehouses: () => of({ data: [] }) } },
        { provide: ErrorHandlerService, useValue: { getUserFriendlyMessage: () => 'failed' } },
        {
          provide: AuthService,
          useValue: { hasPermission: (key: string) => permissions.includes(key), isSuperAdmin: () => false }
        },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: ActivatedRoute, useValue: { params: of(routeParams) } }
      ]
    });
    const component = TestBed.createComponent(UserFormComponent).componentInstance;
    component.ngOnInit();
    return component;
  }

  function fill(component: UserFormComponent, values: Record<string, unknown>): void {
    component.userForm.patchValue({
      name: 'Test User',
      email: 'test@example.test',
      password: 'secret123',
      confirmPassword: 'secret123',
      roleId: 5,
      warehouseIds: [7],
      ...values
    });
  }

  function sentDto(): Record<string, unknown> {
    return userService.createUser.mock.calls[0][0 as never] as Record<string, unknown>;
  }

  beforeEach(() => {
    userService.createUser.mockClear();
    userService.updateUser.mockClear();
    userService.getUserById.mockReset();
    userService.getContextRoleOptions.mockClear();
  });

  it('hides the step and leaves context access out of the payload without the manage permission', () => {
    const component = setup(['users.edit']);
    fill(component, {});

    expect(component.wizardSteps).toHaveLength(5);
    expect(userService.getContextRoleOptions).not.toHaveBeenCalled();

    component.onSubmit();
    expect(sentDto()['contextAccess']).toBeUndefined();
  });

  it('adds the step after the existing five, which stay in place', () => {
    const component = setup([MANAGE]);
    fill(component, {});

    expect(component.wizardSteps.map(step => step.title)).toEqual([
      'Basic Information',
      'Permissions',
      'Inventory Location Access',
      'Default Inventory Location',
      'Outlet/Warehouse Assignments',
      'Context Switching'
    ]);
  });

  it.each([6, 7])('hides the step for the owner and platform role %i', (roleId) => {
    const component = setup([MANAGE, GRANT_ALL]);
    fill(component, { roleId });

    expect(component.wizardSteps).toHaveLength(5);
  });

  it('defaults to disabled with selected locations and mapped roles', () => {
    const component = setup([MANAGE]);
    fill(component, {});

    component.onSubmit();

    expect(sentDto()['contextAccess']).toEqual({
      enabled: false,
      locationScope: 'selected',
      roleScope: 'mapped',
      roleIds: []
    });
  });

  it('toggles and sends mapped roles', () => {
    const component = setup([MANAGE]);
    fill(component, { contextSwitchEnabled: true });

    component.toggleContextRole(8);
    component.toggleContextRole(9);
    component.toggleContextRole(8);
    expect(component.isContextRoleSelected(9)).toBe(true);
    component.onSubmit();

    expect(sentDto()['contextAccess']).toMatchObject({ enabled: true, roleIds: [9] });
  });

  it('drops mapped roles once All Business Roles is selected', () => {
    const component = setup([MANAGE, GRANT_ALL]);
    fill(component, { contextSwitchEnabled: true, contextRoleScope: 'all', contextRoleIds: [8] });

    component.onSubmit();

    expect(sentDto()['contextAccess']).toMatchObject({ roleScope: 'all', roleIds: [] });
  });

  it('never treats an empty selection as all locations', () => {
    const component = setup([MANAGE]);
    fill(component, { contextSwitchEnabled: true, warehouseIds: [], inventoryLocationAccessScope: 'all_locations' });

    component.onSubmit();

    expect(component.error()).toContain('at least one assigned outlet or warehouse');
    expect(userService.createUser).not.toHaveBeenCalled();
  });

  it('allows All Business Locations without any assignment', () => {
    const component = setup([MANAGE, GRANT_ALL]);
    fill(component, {
      contextSwitchEnabled: true,
      contextLocationScope: 'all',
      warehouseIds: [],
      inventoryLocationAccessScope: 'all_locations'
    });

    component.onSubmit();

    expect(component.error()).toBeNull();
    expect(sentDto()['contextAccess']).toMatchObject({ locationScope: 'all' });
  });

  it('loads and preserves the stored configuration when editing', () => {
    userService.getUserById.mockReturnValue(of({
      id: 3,
      name: 'Existing',
      email: 'existing@example.test',
      roleId: 5,
      outletId: null,
      isActive: true,
      warehouseAssignments: [{ warehouseId: 7 }],
      contextAccess: { enabled: true, locationScope: 'all', roleScope: 'mapped', roleIds: [9] }
    }));

    const component = setup([MANAGE], { id: '3' });

    expect(component.userForm.value).toMatchObject({
      contextSwitchEnabled: true,
      contextLocationScope: 'all',
      contextRoleScope: 'mapped',
      contextRoleIds: [9]
    });
    expect(component.isContextRoleSelected(9)).toBe(true);

    component.onSubmit();
    const dto = userService.updateUser.mock.calls[0][1 as never] as Record<string, unknown>;
    expect(dto['contextAccess']).toEqual({ enabled: true, locationScope: 'all', roleScope: 'mapped', roleIds: [9] });
  });
});
