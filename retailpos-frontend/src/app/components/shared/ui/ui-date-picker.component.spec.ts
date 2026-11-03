import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { CompanyDateService } from '../../../services/company-date.service';
import { UiDatePickerComponent } from './ui-date-picker.component';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, UiDatePickerComponent],
  template: `
    <app-ui-date-picker
      label="Start date"
      hint="Use the company date format"
      [formControl]="date"
      [required]="true"
      min="2024-02-15"
      max="2024-02-29" />
    <app-ui-date-picker label="Optional date" [formControl]="optionalDate" />
  `
})
class DatePickerHost {
  date = new FormControl<string | null>('2024-02-29', Validators.required);
  optionalDate = new FormControl<string | null>('');
}

describe('UiDatePickerComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create() {
    const fixture = TestBed.createComponent(DatePickerHost);
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    await settle();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('input')!;
    const type = async (text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();
    };
    return { fixture, root, input, settle, type };
  }

  it('shows the configured format and keeps date-only ISO values in Reactive Forms', async () => {
    const { fixture, root, input } = await create();
    expect(input.value).toBe('29/02/2024');
    expect(fixture.componentInstance.date.value).toBe('2024-02-29');
    expect(input.getAttribute('aria-describedby')).toContain('ui-date-picker-');
    expect(root.querySelector('.ui-date-field__hint')?.textContent).toContain('company date format');
  });

  it('accepts an empty optional field and respects a disabled Reactive Forms control', async () => {
    const { fixture, root, settle } = await create();
    const inputs = root.querySelectorAll<HTMLInputElement>('input');
    expect(inputs[1].value).toBe('');
    expect(fixture.componentInstance.optionalDate.valid).toBe(true);

    fixture.componentInstance.date.disable();
    await settle();
    expect(inputs[0].disabled).toBe(true);
    expect(root.querySelector<HTMLButtonElement>('[aria-label="Choose date"]')?.disabled).toBe(true);
  });

  it('strictly validates typed dates and rejects non-leap February 29', async () => {
    const { fixture, input, type } = await create();
    await type('31/02/2024');
    expect(fixture.componentInstance.date.value).toBe('');
    expect(fixture.componentInstance.date.hasError('companyDate')).toBe(true);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(fixture.nativeElement.querySelector('.ui-date-field__error').textContent).toContain('dd/MM/yyyy');

    await type('29/02/2023');
    expect(fixture.componentInstance.date.hasError('companyDate')).toBe(true);
    await type('29/02/2024');
    expect(fixture.componentInstance.date.value).toBe('2024-02-29');
    expect(fixture.componentInstance.date.valid).toBe(true);
  });

  it('applies inclusive minimum and maximum dates to manual input and calendar days', async () => {
    const { fixture, input, root, type } = await create();
    await type('14/02/2024');
    expect(fixture.componentInstance.date.hasError('minDate')).toBe(true);
    await type('01/03/2024');
    expect(fixture.componentInstance.date.hasError('maxDate')).toBe(true);

    fixture.componentInstance.date.setValue('2024-02-29');
    await fixture.whenStable();
    fixture.detectChanges();
    input.click();
    fixture.detectChanges();
    expect(root.querySelector<HTMLButtonElement>('[data-date="2024-02-14"]')?.disabled).toBe(true);
    expect(root.querySelector<HTMLButtonElement>('[data-date="2024-02-15"]')?.disabled).toBe(false);
    expect(root.querySelector<HTMLButtonElement>('[data-date="2024-03-01"]')?.disabled).toBe(true);
    expect(root.querySelector<HTMLButtonElement>('[data-date="2024-02-29"]')?.disabled).toBe(false);
  });

  it('opens from both controls, selects a date, and closes on Escape or outside click', async () => {
    const { fixture, input, root, settle } = await create();
    input.focus();
    input.click();
    fixture.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).toBe(input);
    expect(root.querySelector('[role="dialog"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[aria-label="Choose date"]')!.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="dialog"]')).toBeNull();

    root.querySelector<HTMLButtonElement>('[aria-label="Choose date"]')!.click();
    await settle();
    expect(document.activeElement).toBe(root.querySelector('[data-date="2024-02-29"]'));
    root.querySelector<HTMLButtonElement>('[data-date="2024-02-28"]')!.click();
    await settle();
    expect(fixture.componentInstance.date.value).toBe('2024-02-28');
    expect(input.value).toBe('28/02/2024');
    expect(root.querySelector('[role="dialog"]')).toBeNull();

    input.click();
    fixture.detectChanges();
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    fixture.detectChanges();
    expect(root.querySelector('[role="dialog"]')).toBeNull();

    input.click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('[data-date="2024-02-28"]')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await settle();
    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(input);
  });

  it('tracks company format changes without changing the ISO form value', async () => {
    const { fixture, input, settle } = await create();
    TestBed.inject(CompanyDateService).setFormat('MM/DD/YYYY');
    await settle();
    expect(input.value).toBe('02/29/2024');
    expect(fixture.componentInstance.date.value).toBe('2024-02-29');
  });
});
