import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiConfirmDialogComponent } from './ui-confirm-dialog.component';
import { UiDialogCloseReason, UiDialogComponent, UiDialogRole, UiDialogSize, UiDialogStatus } from './ui-dialog.component';

@Component({
  standalone: true,
  imports: [UiDialogComponent],
  template: `
    <button id="trigger" type="button">Open</button>
    <app-ui-dialog
      [(open)]="open"
      title="Edit supplier"
      [size]="size()"
      [status]="status()"
      [role]="role()"
      [dismissible]="dismissible()"
      [closeOnBackdrop]="closeOnBackdrop()"
      [closeOnEscape]="closeOnEscape()"
      (closed)="reasons.push($event)">
      <input id="first" type="text" />
      <button type="button" id="mid">Middle</button>
      <button uiDialogFooter type="button" id="cancel">Cancel</button>
      <button uiDialogFooter type="button" id="save">Save</button>
    </app-ui-dialog>
    <app-ui-dialog [(open)]="innerOpen" title="Inner" size="sm">
      <button type="button" id="inner-btn">Inner action</button>
    </app-ui-dialog>
  `
})
class DialogHostComponent {
  open = signal(false);
  innerOpen = signal(false);
  size = signal<UiDialogSize>('md');
  status = signal<UiDialogStatus>('none');
  role = signal<UiDialogRole>('dialog');
  dismissible = signal(true);
  closeOnBackdrop = signal(true);
  closeOnEscape = signal(true);
  reasons: UiDialogCloseReason[] = [];
}

@Component({
  standalone: true,
  imports: [UiConfirmDialogComponent],
  template: `
    <button id="trigger" type="button">Open</button>
    <app-ui-confirm-dialog
      [(open)]="open"
      title="Delete supplier?"
      confirmLabel="Delete"
      [destructive]="destructive()"
      [busy]="busy()"
      (confirmed)="confirmed = confirmed + 1"
      (cancelled)="cancelled = cancelled + 1">
      This cannot be undone.
    </app-ui-confirm-dialog>
  `
})
class ConfirmHostComponent {
  open = signal(false);
  destructive = signal(true);
  busy = signal(false);
  confirmed = 0;
  cancelled = 0;
}

const pressEscape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
const pressTab = (target: Element, shiftKey = false) => {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
};

describe('UiDialogComponent', () => {
  let fixture: ComponentFixture<DialogHostComponent>;
  let host: DialogHostComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const panel = () => q('.ui-dialog__panel');
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    document.body.classList.remove('overflow-hidden');
    TestBed.configureTestingModule({ imports: [DialogHostComponent] });
    fixture = TestBed.createComponent(DialogHostComponent);
    host = fixture.componentInstance;
    await settle();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    document.body.classList.remove('overflow-hidden');
  });

  it('renders nothing while closed', () => {
    expect(q('.ui-dialog')).toBeNull();
  });

  it('is a labelled modal dialog with header, body and footer regions when open', async () => {
    host.open.set(true);
    await settle();
    const title = q('.ui-dialog__title');
    expect(panel().getAttribute('role')).toBe('dialog');
    expect(panel().getAttribute('aria-modal')).toBe('true');
    expect(panel().getAttribute('aria-labelledby')).toBe(title.id);
    expect(title.textContent).toBe('Edit supplier');
    expect(panel().getAttribute('aria-describedby')).toBe(q('.ui-dialog__body').id);
    expect(q('.ui-dialog__body #first')).not.toBeNull();
    expect(q('.ui-dialog__footer #save')).not.toBeNull();
    expect(q('.ui-dialog__footer #cancel')).not.toBeNull();
  });

  it.each(['sm', 'md', 'lg'] as UiDialogSize[])('supports the %s size', async size => {
    host.size.set(size);
    host.open.set(true);
    await settle();
    expect(q('.ui-dialog').getAttribute('data-size')).toBe(size);
  });

  it('supports alertdialog and shows a decorative status icon with the title', async () => {
    host.role.set('alertdialog');
    host.status.set('danger');
    host.open.set(true);
    await settle();
    expect(panel().getAttribute('role')).toBe('alertdialog');
    const status = q('.ui-dialog__status');
    expect(status.getAttribute('data-status')).toBe('danger');
    expect(status.getAttribute('aria-hidden')).toBe('true');
  });

  describe('focus management', () => {
    it('moves focus to the first content control, not the header close button', async () => {
      host.open.set(true);
      await settle();
      expect(document.activeElement).toBe(q('#first'));
    });

    it('prefers an element marked data-autofocus', async () => {
      host.open.set(true);
      await settle();
      q('#mid').setAttribute('data-autofocus', '');
      host.open.set(false);
      await settle();
      host.open.set(true);
      await settle();
      expect(document.activeElement).toBe(q('#mid'));
    });

    it('wraps Tab from the last control to the first and Shift+Tab from the first to the last', async () => {
      host.open.set(true);
      await settle();
      const close = q<HTMLButtonElement>('button[aria-label="Close dialog"]');
      q<HTMLButtonElement>('#save').focus();
      const forward = pressTab(q('#save'));
      expect(forward.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(close);

      const backward = pressTab(close, true);
      expect(backward.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(q('#save'));
    });

    it('does not interfere with Tab between controls inside the dialog', async () => {
      host.open.set(true);
      await settle();
      expect(pressTab(q('#first')).defaultPrevented).toBe(false);
    });

    it('pulls focus back when it escapes the dialog', async () => {
      host.open.set(true);
      await settle();
      const trigger = q<HTMLButtonElement>('#trigger');
      trigger.focus();
      expect(panel().contains(document.activeElement)).toBe(true);
    });

    it('restores focus to the element that opened the dialog', async () => {
      const trigger = q<HTMLButtonElement>('#trigger');
      trigger.focus();
      host.open.set(true);
      await settle();
      expect(document.activeElement).not.toBe(trigger);

      host.open.set(false);
      await settle();
      expect(document.activeElement).toBe(trigger);
    });
  });

  describe('dismissal', () => {
    it('closes with Escape and reports the reason', async () => {
      host.open.set(true);
      await settle();
      pressEscape();
      await settle();
      expect(host.open()).toBe(false);
      expect(host.reasons).toEqual(['escape']);
      expect(q('.ui-dialog')).toBeNull();
    });

    it('closes from the close button and from a backdrop click', async () => {
      host.open.set(true);
      await settle();
      q<HTMLButtonElement>('button[aria-label="Close dialog"]').click();
      await settle();
      expect(host.reasons).toEqual(['close']);

      host.open.set(true);
      await settle();
      q('.ui-dialog__backdrop').click();
      await settle();
      expect(host.reasons).toEqual(['close', 'backdrop']);
    });

    it('can ignore Escape or the backdrop independently', async () => {
      host.closeOnEscape.set(false);
      host.closeOnBackdrop.set(false);
      host.open.set(true);
      await settle();
      pressEscape();
      q('.ui-dialog__backdrop').click();
      await settle();
      expect(host.open()).toBe(true);
      expect(host.reasons).toEqual([]);
    });

    it('is fully non-dismissible while busy: no Escape, backdrop or close button', async () => {
      host.dismissible.set(false);
      host.open.set(true);
      await settle();
      expect(q('button[aria-label="Close dialog"]')).toBeNull();
      pressEscape();
      q('.ui-dialog__backdrop').click();
      await settle();
      expect(host.open()).toBe(true);
    });

    it('lets only the top-most dialog react to Escape', async () => {
      host.open.set(true);
      await settle();
      host.innerOpen.set(true);
      await settle();
      pressEscape();
      await settle();
      expect(host.innerOpen()).toBe(false);
      expect(host.open()).toBe(true);
      pressEscape();
      await settle();
      expect(host.open()).toBe(false);
    });
  });

  describe('page scroll lock', () => {
    it('locks body scrolling while open and releases it afterwards', async () => {
      expect(document.body.classList.contains('overflow-hidden')).toBe(false);
      host.open.set(true);
      await settle();
      expect(document.body.classList.contains('overflow-hidden')).toBe(true);
      host.open.set(false);
      await settle();
      expect(document.body.classList.contains('overflow-hidden')).toBe(false);
    });

    it('keeps the lock until the last nested dialog closes', async () => {
      host.open.set(true);
      host.innerOpen.set(true);
      await settle();
      host.innerOpen.set(false);
      await settle();
      expect(document.body.classList.contains('overflow-hidden')).toBe(true);
      host.open.set(false);
      await settle();
      expect(document.body.classList.contains('overflow-hidden')).toBe(false);
    });

    it('never removes an overflow-hidden class it did not add', async () => {
      document.body.classList.add('overflow-hidden');
      host.open.set(true);
      await settle();
      host.open.set(false);
      await settle();
      expect(document.body.classList.contains('overflow-hidden')).toBe(true);
    });

    it('releases the lock when destroyed while open', async () => {
      host.open.set(true);
      await settle();
      fixture.destroy();
      expect(document.body.classList.contains('overflow-hidden')).toBe(false);
    });
  });
});

describe('UiConfirmDialogComponent', () => {
  let fixture: ComponentFixture<ConfirmHostComponent>;
  let host: ConfirmHostComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const footerButtons = () => Array.from(root().querySelectorAll<HTMLButtonElement>('.ui-dialog__footer button'));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [ConfirmHostComponent] });
    fixture = TestBed.createComponent(ConfirmHostComponent);
    host = fixture.componentInstance;
    host.open.set(true);
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('is an alertdialog with the message, a labelled title and a danger presentation when destructive', () => {
    expect(q('.ui-dialog__panel').getAttribute('role')).toBe('alertdialog');
    expect(q('.ui-dialog__title').textContent).toBe('Delete supplier?');
    expect(q('.ui-dialog__body').textContent).toContain('This cannot be undone.');
    expect(q('.ui-dialog__status').getAttribute('data-status')).toBe('danger');
    expect(footerButtons().map(b => b.textContent!.trim())).toEqual(['Cancel', 'Delete']);
    expect(footerButtons()[1].getAttribute('data-variant')).toBe('danger');
  });

  it('focuses Cancel first for destructive actions so Enter cannot delete by accident', () => {
    expect(document.activeElement).toBe(footerButtons()[0]);
  });

  it('focuses the confirm button first for a non-destructive confirmation', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [ConfirmHostComponent] });
    fixture = TestBed.createComponent(ConfirmHostComponent);
    host = fixture.componentInstance;
    host.destructive.set(false);
    host.open.set(true);
    await settle();
    expect(document.activeElement).toBe(footerButtons()[1]);
    expect(footerButtons()[1].getAttribute('data-variant')).toBe('primary');
  });

  it('emits confirmed but leaves closing to the feature', async () => {
    footerButtons()[1].click();
    await settle();
    expect(host.confirmed).toBe(1);
    expect(host.open()).toBe(true);
  });

  it('cancels from the Cancel button and from Escape, and ignores backdrop clicks', async () => {
    q('.ui-dialog__backdrop').click();
    await settle();
    expect(host.open()).toBe(true);

    pressEscape();
    await settle();
    expect(host.cancelled).toBe(1);
    expect(host.open()).toBe(false);

    host.open.set(true);
    await settle();
    footerButtons()[0].click();
    await settle();
    expect(host.cancelled).toBe(2);
    expect(host.open()).toBe(false);
  });

  it('blocks every way out while busy and shows the confirm button as loading', async () => {
    host.busy.set(true);
    await settle();
    const [cancel, confirm] = footerButtons();
    expect(cancel.disabled).toBe(true);
    expect(confirm.getAttribute('aria-busy')).toBe('true');
    pressEscape();
    await settle();
    expect(host.open()).toBe(true);
    expect(host.cancelled).toBe(0);

    confirm.click();
    expect(host.confirmed).toBe(0);
  });
});
