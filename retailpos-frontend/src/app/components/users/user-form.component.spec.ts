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

describe('UserFormComponent default location rules', () => {
  const userService = {
    createUser: vi.fn(() => of({})),
    updateUser: vi.fn(() => of({})),
    getUserById: vi.fn()
  };

  function setup(routeParams: Record<string, string> = {}): UserFormComponent {
    TestBed.configureTestingModule({
      imports: [UserFormComponent],
      providers: [
        { provide: UserService, useValue: userService },
        { provide: RoleService, useValue: { getAllRoles: () => of([]), getAllPermissions: () => of([]) } },
        { provide: OutletService, useValue: { getAllOutlets: () => of({ data: [] }) } },
        { provide: WarehouseService, useValue: { getAllWarehouses: () => of({ data: [] }) } },
        { provide: ErrorHandlerService, useValue: { getUserFriendlyMessage: () => 'failed' } },
        { provide: AuthService, useValue: { hasPermission: () => false, isSuperAdmin: () => false } },
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
      ...values
    });
  }

  beforeEach(() => {
    userService.createUser.mockClear();
    userService.updateUser.mockClear();
    userService.getUserById.mockReset();
  });

  it('creates a warehouse-only user with no default location and no outlet', () => {
    const component = setup();
    fill(component, { warehouseIds: [7] });

    component.onSubmit();

    expect(component.error()).toBeNull();
    expect(userService.createUser).toHaveBeenCalledTimes(1);
    const dto = userService.createUser.mock.calls[0][0 as never] as Record<string, unknown>;
    expect(dto['outletId']).toBeUndefined();
    expect(dto['defaultLocationId']).toBeUndefined();
    expect(dto['outletIds']).toEqual([]);
    expect(dto['warehouseIds']).toEqual([7]);
  });

  it('sends a warehouse default without any default outlet', () => {
    const component = setup();
    fill(component, { warehouseIds: [7, 8], defaultLocationType: 'warehouse', defaultLocationId: 8 });

    component.onSubmit();

    const dto = userService.createUser.mock.calls[0][0 as never] as Record<string, unknown>;
    expect(dto['outletId']).toBeUndefined();
    expect(dto['defaultLocationType']).toBe('warehouse');
    expect(dto['defaultLocationId']).toBe(8);
  });

  it('allows several assigned outlets under specific scope without choosing a first outlet', () => {
    const component = setup();
    fill(component, { outletIds: [1, 2], inventoryLocationAccessScope: 'specific_locations' });

    component.onSubmit();

    const dto = userService.createUser.mock.calls[0][0 as never] as Record<string, unknown>;
    expect(dto['outletId']).toBeUndefined();
    expect(dto['defaultLocationId']).toBeUndefined();
    expect(dto['outletIds']).toEqual([1, 2]);
  });

  it('keeps a default outlet with a matching assignment', () => {
    const component = setup();
    fill(component, { outletIds: [1], defaultLocationType: 'outlet', defaultLocationId: 1 });

    component.onSubmit();

    const dto = userService.createUser.mock.calls[0][0 as never] as Record<string, unknown>;
    expect(dto['outletId']).toBe(1);
    expect(dto['defaultLocationId']).toBe(1);
  });

  it.each(['assigned_only', 'specific_locations'])(
    'rejects a default outlet outside the selected outlets under %s',
    (scope) => {
      const component = setup();
      fill(component, {
        outletIds: [1],
        defaultLocationType: 'outlet',
        defaultLocationId: 2,
        inventoryLocationAccessScope: scope
      });

      component.onSubmit();

      expect(component.error()).toContain('assigned locations');
      expect(userService.createUser).not.toHaveBeenCalled();
    }
  );

  it('allows all-locations users to default to an outlet outside the selected outlets', () => {
    const component = setup();
    fill(component, {
      outletIds: [1],
      defaultLocationType: 'outlet',
      defaultLocationId: 2,
      inventoryLocationAccessScope: 'all_locations'
    });

    component.onSubmit();

    expect(component.error()).toBeNull();
    expect(userService.createUser).toHaveBeenCalledTimes(1);
  });

  it('requires a default warehouse to be selected in assignments so it is not lost on save', () => {
    const component = setup();
    fill(component, {
      defaultLocationType: 'warehouse',
      defaultLocationId: 8,
      inventoryLocationAccessScope: 'all_locations'
    });

    component.onSubmit();

    expect(component.error()).toContain('warehouse');
    expect(userService.createUser).not.toHaveBeenCalled();
  });

  it('rejects assigned-only users with neither a default outlet nor an assigned warehouse', () => {
    const component = setup();
    fill(component, { outletIds: [1] });

    component.onSubmit();

    expect(component.error()).toContain('Assigned Location Only');
    expect(userService.createUser).not.toHaveBeenCalled();
  });

  it('clears an existing default outlet on edit without selecting a replacement', () => {
    userService.getUserById.mockReturnValue(of({
      id: 3,
      name: 'Existing',
      email: 'existing@example.test',
      roleId: 5,
      businessId: 9,
      outletId: 1,
      defaultLocationType: 'outlet',
      defaultLocationId: 1,
      inventoryLocationAccessScope: 'specific_locations',
      outletAssignments: [{ outletId: 1 }, { outletId: 2 }],
      warehouseAssignments: [],
      isActive: true
    }));
    const component = setup({ id: '3' });
    expect(component.userForm.get('defaultLocationId')?.value).toBe(1);

    component.userForm.patchValue({ defaultLocationId: null });
    component.onSubmit();

    expect(userService.updateUser).toHaveBeenCalledTimes(1);
    const dto = userService.updateUser.mock.calls[0][1 as never] as Record<string, unknown>;
    expect(dto['outletId']).toBeUndefined();
    expect(dto['defaultLocationId']).toBeUndefined();
    expect(dto['outletIds']).toEqual([1, 2]);
  });

  it('loads a user without any default as having no default location', () => {
    userService.getUserById.mockReturnValue(of({
      id: 4,
      name: 'Existing',
      email: 'existing@example.test',
      roleId: 5,
      businessId: 9,
      outletId: null,
      defaultLocationType: null,
      defaultLocationId: null,
      inventoryLocationAccessScope: 'specific_locations',
      outletAssignments: [{ outletId: 1 }],
      warehouseAssignments: [],
      isActive: true
    }));

    const component = setup({ id: '4' });

    expect(component.userForm.get('defaultLocationId')?.value).toBeNull();
  });
});
