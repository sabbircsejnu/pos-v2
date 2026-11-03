import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MenuItem, MenuSection } from '../../models/menu.model';
import { MenuService } from '../../services/menu.service';
import { SidebarComponent } from './sidebar.component';

const SECTIONS: MenuSection[] = [
  {
    id: 'inventory',
    label: 'INVENTORY',
    items: [
      { id: 'products', label: 'Products', icon: 'fas fa-box', route: '/products' },
      { id: 'stock', label: 'Stock Levels', icon: 'fas fa-cubes', route: '/inventory' }
    ]
  },
  {
    id: 'admin_tools',
    label: 'ADMIN_TOOLS',
    items: [{ id: 'audit', label: 'Audit Logs', icon: 'fas fa-history', route: '/audit-logs', disabled: true }]
  }
];

function createMenuStub() {
  const activeRoute = signal('/products');
  return {
    sections: signal<MenuSection[]>(SECTIONS),
    sidebarCollapsed: signal(false),
    mobileSidebarOpen: signal(false),
    sidebarDensity: signal<'comfortable' | 'compact'>('compact'),
    activeRoute,
    isItemActive: (item: MenuItem) => !!item.route && activeRoute().startsWith(item.route),
    closeMobileSidebar: vi.fn(),
    toggleSidebar: vi.fn()
  };
}

describe('SidebarComponent presentation', () => {
  let fixture: ComponentFixture<SidebarComponent>;
  let menu: ReturnType<typeof createMenuStub>;
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const aside = () => root().querySelector('aside') as HTMLElement;
  const itemButton = (label: string) =>
    root().querySelector(`button.rp-nav-item[aria-label="${label}"]`) as HTMLButtonElement;
  const caption = (sectionId: string) =>
    root().querySelector(`button.rp-nav-caption[aria-controls="nav-group-${sectionId}"]`) as HTMLButtonElement;

  beforeEach(() => {
    localStorage.clear();
    menu = createMenuStub();
    TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [provideRouter([]), { provide: MenuService, useValue: menu }]
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('renders a labelled navigation landmark containing every permitted menu item', () => {
    const nav = root().querySelector('nav[aria-label="Main navigation"]');
    expect(nav).not.toBeNull();
    expect(nav!.querySelectorAll('button.rp-nav-item')).toHaveLength(3);
    expect(itemButton('Products').textContent).toContain('Products');
  });

  it('marks only the active route as the current page', () => {
    expect(itemButton('Products').getAttribute('aria-current')).toBe('page');
    expect(itemButton('Products').classList).toContain('rp-nav-item--active');
    expect(itemButton('Stock Levels').getAttribute('aria-current')).toBeNull();
    expect(itemButton('Stock Levels').classList).not.toContain('rp-nav-item--active');
  });

  it('expands the group that owns the active route and collapses the others', () => {
    expect(caption('inventory').getAttribute('aria-expanded')).toBe('true');
    expect(caption('admin_tools').getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelector('#nav-group-admin_tools')!.classList).toContain('rp-nav-list--collapsed');
    expect(root().querySelector('#nav-group-inventory')!.classList).not.toContain('rp-nav-list--collapsed');
  });

  it('toggles a group from its caption and reports the state to assistive technology', () => {
    caption('admin_tools').click();
    fixture.detectChanges();
    expect(caption('admin_tools').getAttribute('aria-expanded')).toBe('true');
    expect(root().querySelector('#nav-group-admin_tools')!.classList).not.toContain('rp-nav-list--collapsed');

    caption('admin_tools').click();
    fixture.detectChanges();
    expect(caption('admin_tools').getAttribute('aria-expanded')).toBe('false');
  });

  it('navigates to the item route and closes the mobile drawer', () => {
    itemButton('Stock Levels').click();
    expect(router.navigate).toHaveBeenCalledWith(['/inventory']);
    expect(menu.closeMobileSidebar).toHaveBeenCalled();
  });

  it('keeps disabled items inert and labels them as coming soon', () => {
    caption('admin_tools').click();
    fixture.detectChanges();
    const disabled = itemButton('Audit Logs');
    expect(disabled.disabled).toBe(true);
    expect(disabled.textContent).toContain('Soon');
    disabled.click();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('exposes an accessible name on every item so the collapsed icon rail stays usable', () => {
    root().querySelectorAll('button.rp-nav-item').forEach(button => {
      expect(button.getAttribute('aria-label')).toBeTruthy();
      expect(button.querySelector('i')!.closest('[aria-hidden="true"]')).not.toBeNull();
    });
  });

  it('reflects the collapsed rail and density as presentation classes', () => {
    expect(aside().classList).not.toContain('rp-sidebar--collapsed');
    expect(aside().classList).toContain('sidebar-density-compact');

    menu.sidebarCollapsed.set(true);
    menu.sidebarDensity.set('comfortable');
    fixture.detectChanges();

    expect(aside().classList).toContain('rp-sidebar--collapsed');
    expect(aside().classList).toContain('sidebar-density-comfortable');
  });

  it('shows the drawer backdrop only while the mobile drawer is open and closes it on click', () => {
    expect(root().querySelector('.rp-sidebar-backdrop')).toBeNull();
    expect(aside().classList).not.toContain('rp-sidebar--open');

    menu.mobileSidebarOpen.set(true);
    fixture.detectChanges();
    expect(aside().classList).toContain('rp-sidebar--open');
    const backdrop = root().querySelector('.rp-sidebar-backdrop') as HTMLElement;
    expect(backdrop).not.toBeNull();

    backdrop.click();
    expect(menu.closeMobileSidebar).toHaveBeenCalled();
  });

  it('closes the drawer from the close button and the Escape key', () => {
    (root().querySelector('.rp-sidebar__close') as HTMLButtonElement).click();
    expect(menu.closeMobileSidebar).toHaveBeenCalledTimes(1);

    aside().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(menu.closeMobileSidebar).toHaveBeenCalledTimes(2);
  });

  it('uses the id referenced by the header toggle for aria-controls', () => {
    expect(aside().id).toBe('app-sidebar');
  });
});

const HR_SECTIONS: MenuSection[] = [
  {
    id: 'hr-payroll',
    label: 'HR AND PAYROLL',
    items: [
      { id: 'hr-dashboard', label: 'HR Dashboard', icon: 'fas fa-chart-pie', route: '/hr/dashboard' },
      {
        id: 'hr-group-employee-management', label: 'Employee Management', icon: 'fas fa-address-book',
        children: [
          { id: 'hr-employees', label: 'Employees', icon: 'fas fa-users', route: '/hr/employees' },
          { id: 'hr-positions', label: 'Positions', icon: 'fas fa-user-tie', route: '/hr/positions' }
        ]
      },
      {
        id: 'payroll-group', label: 'Payroll', icon: 'fas fa-money-bill-wave',
        children: [
          { id: 'payroll-periods', label: 'Payroll Periods', icon: 'fas fa-calendar-check', route: '/payroll/periods' }
        ]
      }
    ]
  },
  SECTIONS[0]
];

describe('SidebarComponent 3-level HR & Payroll navigation', () => {
  let fixture: ComponentFixture<SidebarComponent>;
  let menu: ReturnType<typeof createMenuStub>;
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const leafButton = (label: string) =>
    root().querySelector(`button.rp-nav-item[aria-label="${label}"]`) as HTMLButtonElement;
  const groupButton = (id: string) =>
    root().querySelector(`button.rp-nav-item--group[aria-controls="nav-subgroup-${id}"]`) as HTMLButtonElement;
  const subList = (id: string) => root().querySelector(`#nav-subgroup-${id}`) as HTMLElement;

  function create(route: string): void {
    localStorage.clear();
    menu = createMenuStub();
    menu.sections.set(HR_SECTIONS);
    menu.activeRoute.set(route);
    TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [provideRouter([]), { provide: MenuService, useValue: menu }]
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
  }

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('renders level-2 groups with their level-3 pages nested beneath them', () => {
    create('/hr/dashboard');
    expect(groupButton('hr-group-employee-management').textContent).toContain('Employee Management');
    const children = Array.from(subList('hr-group-employee-management').querySelectorAll('button.rp-nav-item'));
    expect(children.map(button => button.getAttribute('aria-label'))).toEqual(['Employees', 'Positions']);
    children.forEach(button => expect(button.classList).toContain('rp-nav-item--nested'));
    expect(leafButton('HR Dashboard').classList).not.toContain('rp-nav-item--nested');
  });

  it('keeps level-2 groups collapsed until opened and reports state to assistive technology', () => {
    create('/hr/dashboard');
    const group = groupButton('hr-group-employee-management');
    expect(group.getAttribute('aria-expanded')).toBe('false');
    expect(subList('hr-group-employee-management').classList).toContain('rp-nav-list--collapsed');

    group.click();
    fixture.detectChanges();
    expect(group.getAttribute('aria-expanded')).toBe('true');
    expect(subList('hr-group-employee-management').classList).not.toContain('rp-nav-list--collapsed');

    group.click();
    fixture.detectChanges();
    expect(group.getAttribute('aria-expanded')).toBe('false');
  });

  it('expands and highlights the group of the active page on direct load', () => {
    create('/payroll/periods');
    expect(root().querySelector('button.rp-nav-caption')!.getAttribute('aria-expanded')).toBe('true');
    expect(groupButton('payroll-group').getAttribute('aria-expanded')).toBe('true');
    expect(groupButton('payroll-group').classList).toContain('rp-nav-item--group-active');
    expect(groupButton('hr-group-employee-management').getAttribute('aria-expanded')).toBe('false');
    expect(groupButton('hr-group-employee-management').classList).not.toContain('rp-nav-item--group-active');
    expect(leafButton('Payroll Periods').getAttribute('aria-current')).toBe('page');
  });

  it('expands the owning group when navigation changes the active route', () => {
    create('/hr/dashboard');
    menu.activeRoute.set('/hr/positions');
    fixture.detectChanges();
    expect(groupButton('hr-group-employee-management').getAttribute('aria-expanded')).toBe('true');
    expect(leafButton('Positions').classList).toContain('rp-nav-item--active');
  });

  it('navigates from a nested page and closes the mobile drawer', () => {
    create('/hr/employees');
    leafButton('Employees').click();
    expect(router.navigate).toHaveBeenCalledWith(['/hr/employees']);
    expect(menu.closeMobileSidebar).toHaveBeenCalled();
  });

  it('shows every nested page in the collapsed icon rail without toggling groups', () => {
    create('/hr/dashboard');
    menu.sidebarCollapsed.set(true);
    fixture.detectChanges();
    expect(subList('payroll-group').classList).not.toContain('rp-nav-list--collapsed');
    groupButton('payroll-group').click();
    fixture.detectChanges();
    expect(subList('payroll-group').classList).not.toContain('rp-nav-list--collapsed');
  });

  it('leaves flat sections rendered exactly as before', () => {
    create('/hr/dashboard');
    const inventory = root().querySelector('#nav-group-inventory')!;
    expect(inventory.querySelectorAll('button.rp-nav-item')).toHaveLength(2);
    expect(inventory.querySelector('.rp-nav-item--group, .rp-nav-item--nested')).toBeNull();
  });
});
