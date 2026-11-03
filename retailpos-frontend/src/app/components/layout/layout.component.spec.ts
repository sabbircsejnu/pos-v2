import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterModule, provideRouter } from '@angular/router';
import { MenuService } from '../../services/menu.service';
import { LayoutComponent } from './layout.component';

@Component({ selector: 'app-navbar', standalone: true, template: '<div data-stub="navbar"></div>' })
class NavbarStubComponent {}

@Component({ selector: 'app-sidebar', standalone: true, template: '<div data-stub="sidebar"></div>' })
class SidebarStubComponent {}

@Component({ selector: 'app-business-context-selector', standalone: true, template: '<div data-stub="selector"></div>' })
class SelectorStubComponent {}

@Component({ standalone: true, template: '<p data-stub="page">page</p>' })
class PageStubComponent {}

describe('LayoutComponent shell', () => {
  let fixture: ComponentFixture<LayoutComponent>;
  let menu: { sidebarCollapsed: ReturnType<typeof signal<boolean>>; setActiveRouteByUrl: ReturnType<typeof vi.fn> };

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement>(selector: string) => root().querySelector(selector) as T;

  beforeEach(async () => {
    menu = { sidebarCollapsed: signal(false), setActiveRouteByUrl: vi.fn() };
    TestBed.configureTestingModule({
      imports: [LayoutComponent],
      providers: [provideRouter([{ path: 'products', component: PageStubComponent }]), { provide: MenuService, useValue: menu }]
    });
    TestBed.overrideComponent(LayoutComponent, {
      set: { imports: [RouterModule, NavbarStubComponent, SidebarStubComponent, SelectorStubComponent] }
    });
    fixture = TestBed.createComponent(LayoutComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('scopes the light theme to the shell root without touching the document', () => {
    const shell = q('.rp-shell');
    expect(shell.getAttribute('data-theme')).toBe('light');
    expect(document.body.getAttribute('data-theme')).toBeNull();
  });

  it('renders the navbar, sidebar and a focusable main landmark', () => {
    expect(q('[data-stub="navbar"]')).not.toBeNull();
    expect(q('[data-stub="sidebar"]')).not.toBeNull();
    const main = q('main');
    expect(main.id).toBe('main-content');
    expect(main.getAttribute('tabindex')).toBe('-1');
  });

  it('places the Business context selector before the routed page, inside main', () => {
    const content = q('main .rp-shell-content');
    const children = Array.from(content.children).map(c => c.tagName.toLowerCase());
    expect(children.indexOf('app-business-context-selector')).toBe(0);
    expect(children).toContain('router-outlet');
  });

  it('reflects the collapsed sidebar as a shell class used for content offsets', () => {
    expect(q('.rp-shell').classList).not.toContain('rp-shell--collapsed');
    menu.sidebarCollapsed.set(true);
    fixture.detectChanges();
    expect(q('.rp-shell').classList).toContain('rp-shell--collapsed');
  });

  it('moves focus to main from the skip link without changing the route', async () => {
    const router = TestBed.inject(Router);
    const before = router.url;
    const link = q<HTMLAnchorElement>('a.rp-skip-link');
    expect(link.textContent).toContain('Skip to main content');

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    link.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(q('main'));
    expect(router.url).toBe(before);
  });

  it('keeps the active menu route in sync with navigation', async () => {
    const router = TestBed.inject(Router);
    menu.setActiveRouteByUrl.mockClear();
    await router.navigateByUrl('/products');
    expect(menu.setActiveRouteByUrl).toHaveBeenCalledWith('/products');
  });
});
