import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { UiBadgeComponent, UiBadgeVariant } from './ui-badge.component';
import { UiCardComponent } from './ui-card.component';
import { UiBreadcrumb, UiPageHeaderComponent } from './ui-page-header.component';
import { UiStateComponent, UiStateKind } from './ui-state.component';

@Component({
  standalone: true,
  imports: [UiCardComponent],
  template: `
    <app-ui-card [title]="title" [subtitle]="subtitle" [headingLevel]="level" [loading]="loading" [padded]="padded">
      <button uiCardActions type="button" id="card-action">Export</button>
      <p id="card-content">Body content</p>
      <div uiCardFooter id="card-footer">Footer</div>
    </app-ui-card>
  `
})
class CardHostComponent {
  title = 'Stock summary';
  subtitle = 'Updated hourly';
  level: 2 | 3 | 4 = 2;
  loading = false;
  padded = true;
}

@Component({
  standalone: true,
  imports: [UiCardComponent],
  template: `<app-ui-card><p id="bare">No header</p></app-ui-card>`
})
class BareCardHostComponent {}

@Component({
  standalone: true,
  imports: [UiBadgeComponent],
  template: `<app-ui-badge [variant]="variant" [showIcon]="showIcon">{{ text }}</app-ui-badge>`
})
class BadgeHostComponent {
  variant: UiBadgeVariant = 'neutral';
  showIcon = true;
  text = 'Pending';
}

@Component({
  standalone: true,
  imports: [UiPageHeaderComponent],
  template: `
    <app-ui-page-header [title]="title" [description]="description" [breadcrumbs]="breadcrumbs" [headingLevel]="level">
      <button uiPageSecondaryActions type="button" id="secondary">Export</button>
      <a uiPagePrimaryActions href="/new" id="primary">Add product</a>
    </app-ui-page-header>
  `
})
class HeaderHostComponent {
  title = 'Products';
  description = 'Manage the catalogue';
  breadcrumbs: UiBreadcrumb[] = [{ label: 'Inventory', link: '/inventory' }, { label: 'Catalogue', link: '/catalogue' }, { label: 'Products' }];
  level: 1 | 2 = 1;
}

@Component({
  standalone: true,
  imports: [UiStateComponent],
  template: `
    <app-ui-state [kind]="kind" [title]="title" [message]="message" [compact]="compact">
      <button type="button" id="retry" (click)="retries = retries + 1">Retry</button>
    </app-ui-state>
  `
})
class StateHostComponent {
  kind: UiStateKind = 'empty';
  title = '';
  message = '';
  compact = false;
  retries = 0;
}

async function render<T>(type: new () => T, configure?: (host: T) => void, providers: unknown[] = []): Promise<ComponentFixture<T>> {
  TestBed.configureTestingModule({ imports: [type], providers: providers as never[] });
  const fixture = TestBed.createComponent(type);
  configure?.(fixture.componentInstance);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

const q = <E extends HTMLElement = HTMLElement>(fixture: ComponentFixture<unknown>, selector: string) =>
  fixture.nativeElement.querySelector(selector) as E;

afterEach(() => TestBed.resetTestingModule());

describe('UiCardComponent', () => {
  it('renders title, subtitle, projected actions, content and footer', async () => {
    const fixture = await render(CardHostComponent);
    expect(q(fixture, 'h2.ui-card__title').textContent).toBe('Stock summary');
    expect(q(fixture, '.ui-card__subtitle').textContent).toBe('Updated hourly');
    expect(q(fixture, '.ui-card__actions #card-action')).not.toBeNull();
    expect(q(fixture, '.ui-card__body #card-content').textContent).toBe('Body content');
    expect(q(fixture, '.ui-card__footer #card-footer').textContent).toBe('Footer');
  });

  it('adapts the heading level to the page outline', async () => {
    const fixture = await render(CardHostComponent, host => (host.level = 3));
    expect(q(fixture, 'h3.ui-card__title')).not.toBeNull();
    expect(q(fixture, 'h2')).toBeNull();
  });

  it('omits the header when there is no title and keeps the content', async () => {
    const fixture = await render(BareCardHostComponent);
    expect(q(fixture, '.ui-card__header')).toBeNull();
    expect(q(fixture, '#bare').textContent).toBe('No header');
  });

  it('supports edge-to-edge content', async () => {
    const fixture = await render(CardHostComponent, host => (host.padded = false));
    expect(q(fixture, '.ui-card__body').classList).toContain('ui-card__body--flush');
  });

  it('shows the standard loading state, reports busy and hides but keeps the content', async () => {
    const fixture = await render(CardHostComponent, host => (host.loading = true));
    expect(q(fixture, 'app-ui-card').getAttribute('aria-busy')).toBe('true');
    expect(q(fixture, 'app-ui-state[data-kind="loading"]').getAttribute('role')).toBe('status');
    expect(q(fixture, '.ui-card__body').hidden).toBe(true);
    expect(q(fixture, '#card-content')).not.toBeNull();
  });

  it('does not show a loading state or busy flag by default', async () => {
    const fixture = await render(CardHostComponent);
    expect(q(fixture, 'app-ui-state')).toBeNull();
    expect(q(fixture, 'app-ui-card').hasAttribute('aria-busy')).toBe(false);
    expect(q(fixture, '.ui-card__body').hidden).toBe(false);
  });
});

describe('UiBadgeComponent', () => {
  it.each([
    ['success', 'fa-circle-check'],
    ['warning', 'fa-triangle-exclamation'],
    ['danger', 'fa-circle-xmark'],
    ['info', 'fa-circle-info']
  ] as [UiBadgeVariant, string][])('renders the %s variant with text and a distinct icon', async (variant, icon) => {
    const fixture = await render(BadgeHostComponent, host => { host.variant = variant; host.text = 'Status text'; });
    const badge = q(fixture, 'app-ui-badge');
    expect(badge.getAttribute('data-variant')).toBe(variant);
    expect(badge.textContent!.trim()).toBe('Status text');
    const glyph = badge.querySelector('i')!;
    expect(glyph.classList).toContain(icon);
    expect(glyph.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders the neutral variant by default, with text and no icon', async () => {
    const fixture = await render(BadgeHostComponent);
    const badge = q(fixture, 'app-ui-badge');
    expect(badge.getAttribute('data-variant')).toBe('neutral');
    expect(badge.textContent!.trim()).toBe('Pending');
    expect(badge.querySelector('i')).toBeNull();
  });

  it('can hide the icon without removing the status text', async () => {
    const fixture = await render(BadgeHostComponent, host => { host.variant = 'danger'; host.showIcon = false; host.text = 'Overdue'; });
    expect(q(fixture, 'app-ui-badge i')).toBeNull();
    expect(q(fixture, 'app-ui-badge').textContent!.trim()).toBe('Overdue');
  });

  it('uses a different icon for every non-neutral variant so colour is not the only signal', async () => {
    const icons = new Set<string>();
    for (const variant of ['success', 'warning', 'danger', 'info'] as UiBadgeVariant[]) {
      const fixture = await render(BadgeHostComponent, host => (host.variant = variant));
      icons.add(q(fixture, 'app-ui-badge i').className);
      TestBed.resetTestingModule();
    }
    expect(icons.size).toBe(4);
  });
});

describe('UiPageHeaderComponent', () => {
  it('renders the title as the page heading with a description', async () => {
    const fixture = await render(HeaderHostComponent, undefined, [provideRouter([])]);
    expect(q(fixture, 'h1.ui-page-header__title').textContent).toBe('Products');
    expect(q(fixture, '.ui-page-header__description').textContent).toBe('Manage the catalogue');
  });

  it('can render as a level-2 heading and omits an empty description', async () => {
    const fixture = await render(HeaderHostComponent, host => { host.level = 2; host.description = ''; }, [provideRouter([])]);
    expect(q(fixture, 'h2.ui-page-header__title')).not.toBeNull();
    expect(q(fixture, 'h1')).toBeNull();
    expect(q(fixture, '.ui-page-header__description')).toBeNull();
  });

  it('renders a breadcrumb landmark with links for ancestors and the current page as plain text', async () => {
    const fixture = await render(HeaderHostComponent, undefined, [provideRouter([])]);
    const nav = q(fixture, 'nav');
    expect(nav.getAttribute('aria-label')).toBe('Breadcrumb');
    const links = Array.from(nav.querySelectorAll('a'));
    expect(links.map(a => a.textContent!.trim())).toEqual(['Inventory', 'Catalogue']);
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/inventory', '/catalogue']);
    const current = nav.querySelector('[aria-current="page"]')!;
    expect(current.tagName).toBe('SPAN');
    expect(current.textContent).toBe('Products');
  });

  it('omits the breadcrumb landmark when there are no breadcrumbs', async () => {
    const fixture = await render(HeaderHostComponent, host => (host.breadcrumbs = []), [provideRouter([])]);
    expect(q(fixture, 'nav')).toBeNull();
  });

  it('places projected secondary and primary actions in their own groups', async () => {
    const fixture = await render(HeaderHostComponent, undefined, [provideRouter([])]);
    const groups = fixture.nativeElement.querySelectorAll('.ui-page-header__group') as NodeListOf<HTMLElement>;
    expect(groups[0].querySelector('#secondary')).not.toBeNull();
    expect(groups[1].querySelector('#primary')).not.toBeNull();
    expect(groups[1].querySelector('#secondary')).toBeNull();
  });
});

describe('UiStateComponent', () => {
  it('announces loading politely with a default title and spinner', async () => {
    const fixture = await render(StateHostComponent, host => (host.kind = 'loading'));
    const state = q(fixture, 'app-ui-state');
    expect(state.getAttribute('role')).toBe('status');
    expect(state.getAttribute('aria-live')).toBe('polite');
    expect(state.textContent).toContain('Loading…');
    expect(state.querySelector('.ui-state__spinner')).not.toBeNull();
  });

  it('announces errors as alerts with the supplied title and message', async () => {
    const fixture = await render(StateHostComponent, host => {
      host.kind = 'error';
      host.title = 'Could not load products';
      host.message = 'The server did not respond.';
    });
    const state = q(fixture, 'app-ui-state');
    expect(state.getAttribute('role')).toBe('alert');
    expect(state.getAttribute('aria-live')).toBeNull();
    expect(q(fixture, '.ui-state__title').textContent).toBe('Could not load products');
    expect(q(fixture, '.ui-state__message').textContent).toBe('The server did not respond.');
  });

  it('renders the empty state as plain content with a default title and no live role', async () => {
    const fixture = await render(StateHostComponent);
    const state = q(fixture, 'app-ui-state');
    expect(state.hasAttribute('role')).toBe(false);
    expect(q(fixture, '.ui-state__title').textContent).toBe('Nothing to show yet');
    expect(q(fixture, '.ui-state__message')).toBeNull();
  });

  it('keeps feature-owned retry behaviour through projected actions', async () => {
    const fixture = await render(StateHostComponent, host => (host.kind = 'error'));
    q<HTMLButtonElement>(fixture, '.ui-state__actions #retry').click();
    expect(fixture.componentInstance.retries).toBe(1);
  });

  it('hides decorative icons from assistive technology and supports a compact layout', async () => {
    const fixture = await render(StateHostComponent, host => { host.kind = 'empty'; host.compact = true; });
    expect(q(fixture, '.ui-state__icon').getAttribute('aria-hidden')).toBe('true');
    expect(q(fixture, 'app-ui-state').hasAttribute('data-compact')).toBe(true);
  });
});
