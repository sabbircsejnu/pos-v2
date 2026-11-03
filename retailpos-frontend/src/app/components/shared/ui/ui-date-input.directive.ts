import { Directive, ElementRef, Input, OnChanges, OnDestroy, OnInit, Renderer2, effect, forwardRef, inject } from '@angular/core';
import { AbstractControl, ControlValueAccessor, NG_VALIDATORS, NG_VALUE_ACCESSOR, ValidationErrors, Validator } from '@angular/forms';
import { CompanyDateService } from '../../../services/company-date.service';

/** Keeps ISO form values while the visible control uses the company's calendar-date format. */
@Directive({
  selector: 'input[appCompanyDate]',
  standalone: true,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => UiDateInputDirective), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => UiDateInputDirective), multi: true }
  ],
  host: { '(input)': 'input()', '(blur)': 'blur()', '(keydown.arrowdown)': 'openPicker($event)' }
})
export class UiDateInputDirective implements ControlValueAccessor, Validator, OnInit, OnChanges, OnDestroy {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly renderer = inject(Renderer2);
  private readonly dates = inject(CompanyDateService);
  @Input() min: string | undefined = '';
  @Input() max: string | undefined = '';
  @Input() step: string | number = '';
  private readonly withTime = this.element.getAttribute('type') === 'datetime-local';
  private value = '';
  private invalidText = false;
  private picker?: HTMLInputElement;
  private button?: HTMLButtonElement;
  private wrapper?: HTMLElement;
  private readonly unlisten: (() => void)[] = [];
  private changed: (value: string) => void = () => {};
  private touched: () => void = () => {};
  private validatorChanged: () => void = () => {};

  constructor() {
    effect(() => {
      const pattern = `${this.dates.dateFormat()}${this.withTime ? ' HH:mm' : ''}`;
      this.element.placeholder = pattern;
      this.element.setAttribute('title', `Enter ${pattern} or choose a date`);
      if (!this.invalidText) this.render();
      this.validatorChanged();
    });
  }

  ngOnInit(): void {
    this.element.type = 'text';
    this.element.autocomplete = 'off';
    this.wrapper = this.renderer.createElement('span');
    this.renderer.addClass(this.wrapper, 'company-date-control');
    const parent = this.element.parentNode;
    this.renderer.insertBefore(parent, this.wrapper, this.element);
    this.renderer.appendChild(this.wrapper, this.element);
    this.picker = this.renderer.createElement('input');
    this.picker!.type = this.withTime ? 'datetime-local' : 'date';
    this.picker!.tabIndex = -1;
    this.picker!.setAttribute('aria-hidden', 'true');
    this.renderer.addClass(this.picker, 'company-date-native-picker');
    this.button = this.renderer.createElement('button');
    this.button!.type = 'button';
    const icon = this.renderer.createElement('i');
    this.renderer.addClass(icon, 'fas');
    this.renderer.addClass(icon, 'fa-calendar-days');
    this.renderer.setAttribute(icon, 'aria-hidden', 'true');
    this.renderer.appendChild(this.button, icon);
    this.button!.setAttribute('aria-label', 'Choose date');
    this.renderer.addClass(this.button, 'company-date-trigger');
    this.renderer.appendChild(this.wrapper, this.picker);
    this.renderer.appendChild(this.wrapper, this.button);
    this.unlisten.push(this.renderer.listen(this.button, 'click', (event: Event) => this.openPicker(event)));
    this.unlisten.push(this.renderer.listen(this.picker, 'change', () => {
      this.value = this.picker!.value;
      this.invalidText = false;
      this.render();
      this.changed(this.value);
      this.touched();
      this.validatorChanged();
    }));
    this.syncPicker();
    this.validatorChanged();
  }

  ngOnChanges(): void { this.syncPicker(); this.validatorChanged(); }

  writeValue(value: string | null | undefined): void {
    this.value = value ?? '';
    this.invalidText = false;
    this.render();
  }
  registerOnChange(fn: (value: string) => void): void { this.changed = fn; }
  registerOnTouched(fn: () => void): void { this.touched = fn; }
  registerOnValidatorChange(fn: () => void): void { this.validatorChanged = fn; }
  setDisabledState(disabled: boolean): void {
    this.element.disabled = disabled;
    this.syncPicker();
  }

  input(): void {
    const text = this.element.value;
    const parsed = text ? this.dates.parse(text, this.withTime) : '';
    this.invalidText = parsed === null;
    this.value = parsed ?? '';
    this.element.setCustomValidity(this.invalidText ? `Enter a valid ${this.dates.dateFormat()} date.` : '');
    this.changed(this.value);
    this.validatorChanged();
  }
  blur(): void { this.touched(); if (!this.invalidText) this.render(); }

  validate(_control: AbstractControl): ValidationErrors | null {
    if (this.invalidText) return { companyDate: { format: this.dates.dateFormat() } };
    this.syncPicker();
    if (this.picker?.validity.rangeUnderflow) return { minDate: { min: this.min } };
    if (this.picker?.validity.rangeOverflow) return { maxDate: { max: this.max } };
    if (this.picker?.validity.stepMismatch) return { dateStep: { step: this.picker.step } };
    return null;
  }

  openPicker(event: Event): void {
    event.preventDefault();
    if (this.element.disabled || this.element.readOnly) return;
    this.syncPicker();
    if (this.picker?.showPicker) this.picker.showPicker();
    else this.picker?.click();
  }

  private render(): void {
    this.element.value = this.dates.inputText(this.value, this.withTime);
    this.element.setCustomValidity('');
    this.syncPicker();
  }
  private syncPicker(): void {
    if (!this.picker || !this.button) return;
    this.picker.value = this.value;
    this.picker.min = this.min ?? '';
    this.picker.max = this.max ?? '';
    this.picker.step = String(this.step || (this.withTime ? 60 : 1));
    this.picker.disabled = this.element.disabled || this.element.readOnly;
    this.button.disabled = this.picker.disabled;
  }
  ngOnDestroy(): void {
    this.unlisten.forEach(unlisten => unlisten());
    if (this.wrapper?.parentNode) {
      this.renderer.insertBefore(this.wrapper.parentNode, this.element, this.wrapper);
      this.renderer.removeChild(this.wrapper.parentNode, this.wrapper);
    }
  }
}
