import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdvancedFilterDrawerComponent } from './advanced-filter-drawer.component';

@Component({
  standalone: true,
  imports: [AdvancedFilterDrawerComponent],
  template: `
    <button id="opener" type="button">Filters</button>
    @if (open()) {
      <app-advanced-filter-drawer
        [activeCount]="count()"
        (closed)="calls.push('closed'); open.set(false)"
        (clear)="calls.push('clear')"
        (apply)="calls.push('apply')">
        <label for="name">Name</label>
        <input id="name" type="text" />
        <button type="button" id="last">Last</button>
      </app-advanced-filter-drawer>
    }
  `
})
class DrawerHostComponent {
  open = signal(true);
  count = signal(0);
  calls: string[] = [];
}

describe('AdvancedFilterDrawerComponent', () => {
  let fixture: ComponentFixture<DrawerHostComponent>;
  let host: DrawerHostComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const footer = () => Array.from(root().querySelectorAll<HTMLButtonElement>('.afd__footer button'));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [DrawerHostComponent] });
    fixture = TestBed.createComponent(DrawerHostComponent);
    host = fixture.componentInstance;
    host.open.set(false);
    await settle();
    q<HTMLButtonElement>('#opener').focus();
    host.open.set(true);
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('is a labelled modal dialog and projects the feature filter form', () => {
    const panel = q('.afd__panel');
    expect(panel.getAttribute('role')).toBe('dialog');
    expect(panel.getAttribute('aria-modal')).toBe('true');
    expect(panel.getAttribute('aria-labelledby')).toBe(q('.afd__title').id);
    expect(q('.afd__title').textContent).toBe('Advanced Filters');
    expect(q('.afd__body #name')).not.toBeNull();
  });

  it('keeps the default footer labels and fires apply, clear and close', () => {
    expect(footer().map(b => b.textContent!.trim())).toEqual(['Close Drawer', 'Clear Filters', 'Apply Filters']);
    footer()[2].click();
    footer()[1].click();
    expect(host.calls).toEqual(['apply', 'clear']);
  });

  it('closes from the header button, the footer button and the backdrop', async () => {
    for (const trigger of [
      () => q<HTMLButtonElement>('button[aria-label="Close Drawer"]'),
      () => footer()[0],
      () => q('.afd__backdrop')
    ]) {
      host.open.set(true);
      await settle();
      trigger().click();
      await settle();
    }
    expect(host.calls).toEqual(['closed', 'closed', 'closed']);
  });

  it('shows an applied-filter indicator only when filters are active', async () => {
    expect(q('app-ui-badge')).toBeNull();
    host.count.set(3);
    await settle();
    expect(q('app-ui-badge')!.textContent).toContain('3 applied');
  });

  it('closes on Escape, but not when another control already handled it', async () => {
    const handled = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    handled.preventDefault();
    document.dispatchEvent(handled);
    expect(host.calls).toEqual([]);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await settle();
    expect(host.calls).toEqual(['closed']);
  });

  it('moves focus into the drawer, skipping the header close button', () => {
    expect(document.activeElement).toBe(q('#name'));
  });

  it('wraps Tab inside the drawer and restores focus when it is closed', async () => {
    const opener = q<HTMLButtonElement>('#opener');
    const applyButton = footer()[2];
    applyButton.focus();
    const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    applyButton.dispatchEvent(tab);
    expect(tab.defaultPrevented).toBe(true);
    expect(q('.afd__panel').contains(document.activeElement)).toBe(true);

    host.open.set(false);
    await settle();
    expect(q('.afd__panel')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });
});
