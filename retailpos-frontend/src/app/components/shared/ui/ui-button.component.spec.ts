import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiButtonComponent, UiButtonSize, UiButtonVariant } from './ui-button.component';

@Component({
  standalone: true,
  imports: [UiButtonComponent],
  template: `
    <button
      appUiButton
      [variant]="variant()"
      [size]="size()"
      [loading]="loading()"
      [iconOnly]="iconOnly()"
      [block]="block()"
      [disabled]="disabled()"
      [attr.aria-label]="label()"
      (click)="clicks = clicks + 1">{{ text() }}</button>
  `
})
class ButtonHostComponent {
  variant = signal<UiButtonVariant>('primary');
  size = signal<UiButtonSize>('md');
  loading = signal(false);
  iconOnly = signal(false);
  block = signal(false);
  disabled = signal(false);
  label = signal<string | null>(null);
  text = signal('Save');
  clicks = 0;
}

@Component({
  standalone: true,
  imports: [UiButtonComponent],
  template: `
    <form (submit)="$event.preventDefault(); submits = submits + 1">
      <button appUiButton type="submit" [loading]="loading()" id="submit">Submit</button>
      <button appUiButton id="implicit">No type</button>
    </form>
    <a appUiButton id="link" href="#target" [loading]="loading()" (click)="linkClicks = linkClicks + 1">Link</a>
  `
})
class FormHostComponent {
  loading = signal(false);
  submits = 0;
  linkClicks = 0;
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('UiButtonComponent', () => {
  let fixture: ComponentFixture<ButtonHostComponent>;
  let host: ButtonHostComponent;
  const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [ButtonHostComponent] });
    fixture = TestBed.createComponent(ButtonHostComponent);
    host = fixture.componentInstance;
    await settle(fixture);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('decorates the native button instead of wrapping it', () => {
    expect(button().tagName).toBe('BUTTON');
    expect(button().classList).toContain('ui-btn');
    expect(button().textContent!.trim()).toBe('Save');
    expect(button().tabIndex).toBe(0);
  });

  it.each(['primary', 'secondary', 'outline', 'ghost', 'danger'] as UiButtonVariant[])(
    'exposes the %s variant',
    async variant => {
      host.variant.set(variant);
      await settle(fixture);
      expect(button().getAttribute('data-variant')).toBe(variant);
    }
  );

  it('defaults to the primary variant and medium size', () => {
    expect(button().getAttribute('data-variant')).toBe('primary');
    expect(button().getAttribute('data-size')).toBe('md');
  });

  it('applies size and block modifiers', async () => {
    host.size.set('sm');
    host.block.set(true);
    await settle(fixture);
    expect(button().getAttribute('data-size')).toBe('sm');
    expect(button().hasAttribute('data-block')).toBe(true);
  });

  it('defaults a button to type="button" so it never submits a form by accident', () => {
    expect(button().getAttribute('type')).toBe('button');
  });

  it('is inert while natively disabled', async () => {
    host.disabled.set(true);
    await settle(fixture);
    expect(button().disabled).toBe(true);
    button().click();
    expect(host.clicks).toBe(0);
  });

  it('shows a spinner, reports busy and blocks clicks while loading', async () => {
    host.loading.set(true);
    await settle(fixture);
    expect(button().getAttribute('aria-busy')).toBe('true');
    expect(button().getAttribute('aria-disabled')).toBe('true');
    expect(button().querySelector('.ui-btn__spinner')).not.toBeNull();
    expect(button().textContent).toContain('Save');

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    button().dispatchEvent(event);
    expect(host.clicks).toBe(0);
    expect(event.defaultPrevented).toBe(true);
  });

  it('stays focusable while loading so keyboard users do not lose their place', async () => {
    host.loading.set(true);
    await settle(fixture);
    button().focus();
    expect(button().disabled).toBe(false);
    expect(document.activeElement).toBe(button());
  });

  it('accepts clicks again once loading ends and clears the busy state', async () => {
    host.loading.set(true);
    await settle(fixture);
    host.loading.set(false);
    await settle(fixture);
    expect(button().hasAttribute('aria-busy')).toBe(false);
    expect(button().hasAttribute('aria-disabled')).toBe(false);
    expect(button().querySelector('.ui-btn__spinner')).toBeNull();
    button().click();
    expect(host.clicks).toBe(1);
  });

  it('keeps an icon-only button square and uses the supplied accessible name', async () => {
    host.iconOnly.set(true);
    host.label.set('Delete row');
    host.text.set('');
    await settle(fixture);
    expect(button().hasAttribute('data-icon-only')).toBe(true);
    expect(button().getAttribute('aria-label')).toBe('Delete row');
  });

  it('warns in development when an icon-only button has no accessible name', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const unnamed = TestBed.createComponent(ButtonHostComponent);
    unnamed.componentInstance.iconOnly.set(true);
    await settle(unnamed);
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('icon-only'), expect.anything());

    warn.mockClear();
    const named = TestBed.createComponent(ButtonHostComponent);
    named.componentInstance.iconOnly.set(true);
    named.componentInstance.label.set('Close');
    await settle(named);
    expect(warn).not.toHaveBeenCalled();
    warn.mockRestore();
  });
});

describe('UiButtonComponent inside forms and as a link', () => {
  let fixture: ComponentFixture<FormHostComponent>;
  let host: FormHostComponent;
  const q = (selector: string) => fixture.nativeElement.querySelector(selector) as HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [FormHostComponent] });
    fixture = TestBed.createComponent(FormHostComponent);
    host = fixture.componentInstance;
    await settle(fixture);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('keeps an explicit submit type and still submits the form', () => {
    expect(q('#submit').getAttribute('type')).toBe('submit');
    q('#submit').click();
    expect(host.submits).toBe(1);
  });

  it('does not submit a form from a button without an explicit type', () => {
    q('#implicit').click();
    expect(host.submits).toBe(0);
  });

  it('blocks duplicate submits while the submit button is loading', async () => {
    host.loading.set(true);
    await settle(fixture);

    q('#submit').click();
    q('#submit').click();
    expect(host.submits).toBe(0);

    host.loading.set(false);
    await settle(fixture);
    q('#submit').click();
    expect(host.submits).toBe(1);
  });

  it('supports links and swallows navigation while a link button is loading', async () => {
    host.loading.set(true);
    await settle(fixture);

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    q('#link').dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(host.linkClicks).toBe(0);
    expect(q('#link').getAttribute('aria-busy')).toBe('true');
    expect(q('#link').hasAttribute('type')).toBe(false);
  });
});
