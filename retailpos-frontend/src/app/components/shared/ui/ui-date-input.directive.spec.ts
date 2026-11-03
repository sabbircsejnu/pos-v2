import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { UiDateInputDirective } from './ui-date-input.directive';
import { UiControlDirective } from './ui-control.directive';
import { CompanyDateService } from '../../../services/company-date.service';

@Component({
  imports: [FormsModule, ReactiveFormsModule, UiDateInputDirective, UiControlDirective],
  template: `
    <input appUiControl appCompanyDate type="date" name="date" required [(ngModel)]="date" #dateModel="ngModel"
      [min]="min" [max]="max">
    <input appUiControl appCompanyDate type="datetime-local" [formControl]="timestamp">
  `
})
class Host {
  date = '2026-04-05';
  min = '2026-01-01';
  max = '2026-12-31';
  timestamp = new FormControl('2026-04-05T09:30');
}

describe('company date controls', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function setup() {
    const fixture = TestBed.createComponent(Host);
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    await settle();
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector<HTMLInputElement>('[name="date"]')!;
    const type = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
      await settle();
    };
    return { fixture, el, input, settle, type };
  }

  it('displays the company format while retaining ISO values and Able Pro classes', async () => {
    const { fixture, input, el } = await setup();
    expect(input.type).toBe('text');
    expect(input.placeholder).toBe('dd/MM/yyyy');
    expect(input.value).toBe('05/04/2026');
    expect(fixture.componentInstance.date).toBe('2026-04-05');
    expect(input.classList.contains('ui-control')).toBe(true);
    expect(el.querySelector('button[aria-label="Choose date"]')).not.toBeNull();
  });

  it('parses typing to ISO and rejects invalid days without saving an ambiguous value', async () => {
    const { fixture, input, type } = await setup();
    await type('31/12/2026');
    expect(fixture.componentInstance.date).toBe('2026-12-31');
    await type('31/02/2026');
    expect(fixture.componentInstance.date).toBe('');
    expect(input.value).toBe('31/02/2026');
    expect(input.validationMessage).toContain('dd/MM/yyyy');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    await type('');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    await type('05/04/2026');
    expect(input.getAttribute('aria-invalid')).toBeNull();
  });

  it('keeps min/max validation, applies native calendar selection and supports reactive datetime values', async () => {
    const { fixture, input, el, settle, type } = await setup();
    await type('31/12/2025');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    await type('01/01/2027');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const picker = el.querySelector<HTMLInputElement>('input[type="date"]')!;
    expect(picker.min).toBe('2026-01-01');
    expect(picker.max).toBe('2026-12-31');
    picker.value = '2026-10-22';
    picker.dispatchEvent(new Event('change'));
    await settle();
    expect(input.value).toBe('22/10/2026');
    expect(fixture.componentInstance.date).toBe('2026-10-22');
    const datetime = el.querySelector<HTMLInputElement>('input[placeholder="dd/MM/yyyy HH:mm"]')!;
    expect(datetime.value).toBe('05/04/2026 09:30');
    datetime.value = '22/10/2026 14:45';
    datetime.dispatchEvent(new Event('input'));
    await settle();
    expect(fixture.componentInstance.timestamp.value).toBe('2026-10-22T14:45');
    datetime.value = '22/10/2026 14:45:30';
    datetime.dispatchEvent(new Event('input'));
    await settle();
    expect(fixture.componentInstance.timestamp.hasError('dateStep')).toBe(true);
    datetime.value = '22/10/2026 14:45:00';
    datetime.dispatchEvent(new Event('input'));
    await settle();
    expect(fixture.componentInstance.timestamp.valid).toBe(true);
    fixture.componentInstance.timestamp.disable();
    await settle();
    expect(datetime.disabled).toBe(true);
  });

  it('updates loaded controls and placeholders when company settings change without changing ISO models', async () => {
    const { fixture, input, settle } = await setup();
    TestBed.inject(CompanyDateService).setFormat('MM/DD/YYYY');
    await settle();
    expect(input.placeholder).toBe('MM/dd/yyyy');
    expect(input.value).toBe('04/05/2026');
    expect(fixture.componentInstance.date).toBe('2026-04-05');
    TestBed.inject(CompanyDateService).setFormat('YYYY-MM-DD');
    await settle();
    expect(input.value).toBe('2026-04-05');
  });
});
