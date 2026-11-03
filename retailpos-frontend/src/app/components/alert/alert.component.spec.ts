import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AlertService } from '../../services/alert.service';
import { AlertComponent } from './alert.component';

const pressEscape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

describe('AlertComponent (service-driven modal alerts)', () => {
  let fixture: ComponentFixture<AlertComponent>;
  let alerts: AlertService;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const buttons = () => Array.from(root().querySelectorAll<HTMLButtonElement>('.ui-dialog__footer button'));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [AlertComponent] });
    alerts = TestBed.inject(AlertService);
    fixture = TestBed.createComponent(AlertComponent);
    await settle();
  });

  afterEach(() => {
    alerts.close();
    TestBed.resetTestingModule();
  });

  it('renders nothing until an alert is raised', () => {
    expect(q('.ui-dialog')).toBeNull();
  });

  it.each([
    ['success', 'success'],
    ['error', 'danger'],
    ['warning', 'warning'],
    ['info', 'info']
  ] as ['success' | 'error' | 'warning' | 'info', string][])('shows %s alerts as a labelled dialog with a %s status', async (type, status) => {
    alerts[type]('Something happened', 'A title');
    await settle();
    expect(q('.ui-dialog__panel').getAttribute('role')).toBe('dialog');
    expect(q('.ui-dialog__title').textContent).toBe('A title');
    expect(q('.ui-dialog__body').textContent).toContain('Something happened');
    expect(q('.ui-dialog__status').getAttribute('data-status')).toBe(status);
    expect(buttons().map(b => b.textContent!.trim())).toEqual(['OK']);
  });

  it('uses a danger OK button for errors and a primary one otherwise', async () => {
    alerts.error('x');
    await settle();
    expect(buttons()[0].getAttribute('data-variant')).toBe('danger');
    alerts.success('y');
    await settle();
    expect(buttons()[0].getAttribute('data-variant')).toBe('primary');
  });

  it('focuses OK, and closes with OK, Escape or a backdrop click', async () => {
    alerts.info('hello');
    await settle();
    expect(document.activeElement).toBe(buttons()[0]);
    buttons()[0].click();
    await settle();
    expect(alerts.config()).toBeNull();

    alerts.info('again');
    await settle();
    pressEscape();
    await settle();
    expect(alerts.config()).toBeNull();

    alerts.info('third');
    await settle();
    q('.ui-dialog__backdrop').click();
    await settle();
    expect(alerts.config()).toBeNull();
    expect(q('.ui-dialog')).toBeNull();
  });

  describe('confirmations', () => {
    it('are alertdialogs with the configured labels and no close button', async () => {
      alerts.confirm('Remove it?', () => undefined, 'Remove', 'Yes, remove', 'Keep');
      await settle();
      expect(q('.ui-dialog__panel').getAttribute('role')).toBe('alertdialog');
      expect(q('.ui-dialog__title').textContent).toBe('Remove');
      expect(buttons().map(b => b.textContent!.trim())).toEqual(['Keep', 'Yes, remove']);
      expect(q('button[aria-label="Close dialog"]')).toBeNull();
    });

    it('run onConfirm and close', async () => {
      const onConfirm = vi.fn();
      const onCancel = vi.fn();
      alerts.confirm('Sure?', onConfirm, 'Confirm', 'Confirm', 'Cancel', onCancel);
      await settle();
      buttons()[1].click();
      await settle();
      expect(onConfirm).toHaveBeenCalledTimes(1);
      expect(onCancel).not.toHaveBeenCalled();
      expect(alerts.config()).toBeNull();
    });

    it('run onCancel from Cancel and from Escape', async () => {
      const onCancel = vi.fn();
      alerts.confirm('Sure?', () => undefined, 'Confirm', 'Confirm', 'Cancel', onCancel);
      await settle();
      buttons()[0].click();
      await settle();
      expect(onCancel).toHaveBeenCalledTimes(1);

      alerts.confirm('Sure?', () => undefined, 'Confirm', 'Confirm', 'Cancel', onCancel);
      await settle();
      pressEscape();
      await settle();
      expect(onCancel).toHaveBeenCalledTimes(2);
      expect(alerts.config()).toBeNull();
    });

    it('ignore backdrop clicks, as before', async () => {
      const onConfirm = vi.fn();
      alerts.confirm('Sure?', onConfirm);
      await settle();
      q('.ui-dialog__backdrop').click();
      await settle();
      expect(alerts.config()).not.toBeNull();
      expect(onConfirm).not.toHaveBeenCalled();
    });

    it('work without an onCancel callback', async () => {
      alerts.confirm('Sure?', () => undefined);
      await settle();
      expect(() => buttons()[0].click()).not.toThrow();
      await settle();
      expect(alerts.config()).toBeNull();
    });
  });
});
