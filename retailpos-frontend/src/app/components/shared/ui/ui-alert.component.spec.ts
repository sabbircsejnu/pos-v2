import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiAlertComponent, UiAlertVariant } from './ui-alert.component';

@Component({
  standalone: true,
  imports: [UiAlertComponent],
  template: `
    <app-ui-alert [variant]="variant()" [title]="title()" [dismissible]="dismissible()" (dismissed)="dismissed = dismissed + 1">
      Saved <b>successfully</b>
      <button uiAlertActions type="button" id="undo">Undo</button>
    </app-ui-alert>
  `
})
class AlertHostComponent {
  variant = signal<UiAlertVariant>('info');
  title = signal('');
  dismissible = signal(false);
  dismissed = 0;
}

describe('UiAlertComponent', () => {
  let fixture: ComponentFixture<AlertHostComponent>;
  let host: AlertHostComponent;
  const alert = () => fixture.nativeElement.querySelector('app-ui-alert') as HTMLElement;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [AlertHostComponent] });
    fixture = TestBed.createComponent(AlertHostComponent);
    host = fixture.componentInstance;
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['success', 'status', 'fa-circle-check'],
    ['info', 'status', 'fa-circle-info'],
    ['warning', 'alert', 'fa-triangle-exclamation'],
    ['danger', 'alert', 'fa-circle-exclamation']
  ] as [UiAlertVariant, string, string][])('renders the %s variant with role=%s and its own icon', async (variant, role, icon) => {
    host.variant.set(variant);
    await settle();
    expect(alert().getAttribute('data-variant')).toBe(variant);
    expect(alert().getAttribute('role')).toBe(role);
    const glyph = alert().querySelector('.ui-alert__icon')!;
    expect(glyph.classList).toContain(icon);
    expect(glyph.getAttribute('aria-hidden')).toBe('true');
  });

  it('always shows the message text, an optional title and projected actions', async () => {
    host.title.set('Saved');
    await settle();
    expect(alert().querySelector('.ui-alert__title')!.textContent).toBe('Saved');
    expect(alert().querySelector('.ui-alert__message')!.textContent).toContain('Saved successfully');
    expect(alert().querySelector('.ui-alert__actions #undo')).not.toBeNull();
  });

  it('omits the title element when none is given', () => {
    expect(alert().querySelector('.ui-alert__title')).toBeNull();
  });

  it('is not dismissible by default', () => {
    expect(alert().querySelector('.ui-alert__dismiss')).toBeNull();
  });

  it('can be dismissed with a named native button, hiding itself and emitting dismissed', async () => {
    host.dismissible.set(true);
    await settle();
    const button = alert().querySelector<HTMLButtonElement>('.ui-alert__dismiss')!;
    expect(button.tagName).toBe('BUTTON');
    expect(button.type).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('Dismiss');

    button.click();
    await settle();
    expect(host.dismissed).toBe(1);
    expect(alert().hasAttribute('hidden')).toBe(true);
  });
});
