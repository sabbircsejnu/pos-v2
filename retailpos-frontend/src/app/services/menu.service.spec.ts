import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { MenuService } from './menu.service';
import { AuthService } from './auth.service';
import { MenuItem } from '../models/menu.model';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a parent menu item with the supplied children. */
function parent(id: string, children: MenuItem[]): MenuItem {
  return { id, label: id, icon: '', children };
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
});
