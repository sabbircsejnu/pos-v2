import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  HostListener,
  computed,
  effect,
  forwardRef,
  inject,
  input,
  signal
} from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator
} from '@angular/forms';
import { CompanyDateService } from '../../../services/company-date.service';
import { UiButtonComponent } from './ui-button.component';

interface CalendarDay {
  iso: string;
  day: number;
  inMonth: boolean;
  disabled: boolean;
  today: boolean;
}

let nextDatePickerId = 0;

@Component({
  selector: 'app-ui-date-picker',
  standalone: true,
  imports: [CommonModule, UiButtonComponent],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => UiDatePickerComponent), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => UiDatePickerComponent), multi: true }
  ],
  template: `
    <div class="ui-date-field" [attr.data-invalid]="showInvalid() ? '' : null">
      @if (label()) {
        <label class="ui-date-field__label" [attr.for]="inputId">
          {{ label() }}@if (required()) {<span class="ui-date-field__required" aria-hidden="true"> *</span><span class="sr-only"> (required)</span>}
        </label>
      }
      <div class="ui-date-field__control">
        <input
          #dateInput
          class="ui-control"
          [id]="inputId"
          type="text"
          autocomplete="off"
          [value]="text()"
          [placeholder]="dates.dateFormat()"
          [disabled]="disabled()"
          [readOnly]="readonly()"
          [required]="required()"
          [attr.aria-label]="label() || 'Date'"
          [attr.aria-invalid]="showInvalid() ? 'true' : null"
          [attr.aria-describedby]="describedBy"
          (input)="onInput(dateInput.value)"
          (click)="open()"
          (blur)="markTouched()"
          (keydown)="onInputKeydown($event)" />
        <button
          appUiButton
          type="button"
          variant="ghost"
          size="sm"
          [iconOnly]="true"
          class="ui-date-field__trigger"
          aria-label="Choose date"
          [disabled]="disabled() || readonly()"
          [attr.aria-expanded]="openState()"
          [attr.aria-controls]="calendarId"
          (click)="toggle()">
          <i class="fas fa-calendar-days" aria-hidden="true"></i>
        </button>
      </div>
      @if (hint()) {
        <p class="ui-date-field__hint" [id]="hintId">{{ hint() }}</p>
      }
      <div class="ui-date-field__error" [id]="errorId" aria-live="polite">
        @if (showInvalid()) { {{ errorMessage() }} }
      </div>

      @if (openState()) {
        <section
          class="ui-date-calendar"
          [id]="calendarId"
          role="dialog"
          aria-label="Choose a date"
          (keydown)="onCalendarKeydown($event)">
          <header class="ui-date-calendar__header">
            <button appUiButton variant="ghost" size="sm" [iconOnly]="true" aria-label="Previous month" [disabled]="!canMoveMonth(-1)" (click)="moveMonth(-1)">
              <i class="fas fa-chevron-left" aria-hidden="true"></i>
            </button>
            <h2 aria-live="polite">{{ monthLabel() }}</h2>
            <button appUiButton variant="ghost" size="sm" [iconOnly]="true" aria-label="Next month" [disabled]="!canMoveMonth(1)" (click)="moveMonth(1)">
              <i class="fas fa-chevron-right" aria-hidden="true"></i>
            </button>
          </header>
          <div class="ui-date-calendar__grid" role="group" [attr.aria-label]="monthLabel()">
            @for (weekday of weekdays; track weekday) {
              <span class="ui-date-calendar__weekday">{{ weekday }}</span>
            }
            @for (day of calendarDays(); track day.iso) {
              <button
                type="button"
                class="ui-date-calendar__day"
                [class.ui-date-calendar__day--outside]="!day.inMonth"
                [class.ui-date-calendar__day--today]="day.today"
                [class.ui-date-calendar__day--selected]="day.iso === value()"
                [attr.data-date]="day.iso"
                [attr.aria-label]="dayLabel(day.iso)"
                [attr.aria-current]="day.today ? 'date' : null"
                [attr.aria-pressed]="day.iso === value()"
                [disabled]="day.disabled || disabled() || readonly()"
                (click)="select(day.iso)">{{ day.day }}</button>
            }
          </div>
          <footer class="ui-date-calendar__footer">
            <button appUiButton variant="ghost" size="sm" (click)="selectToday()">Today</button>
          </footer>
        </section>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      min-width: 0;
    }

    .ui-date-field {
      position: relative;
      min-width: 0;
    }

    .ui-date-field__label {
      display: block;
      margin-bottom: var(--rp-form-label-gap);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-label-size);
      font-weight: var(--rp-font-weight-medium);
      line-height: var(--rp-text-label-line);
    }

    .ui-date-field__required {
      color: var(--rp-color-danger-700);
    }

    .ui-date-field__control {
      position: relative;
      display: flex;
      align-items: center;
    }

    .ui-date-field__control .ui-control {
      padding-inline-end: calc(var(--rp-control-height) + var(--rp-space-2));
    }

    .ui-date-field__trigger {
      position: absolute;
      inset-inline-end: var(--rp-space-1);
    }

    .ui-date-field__hint,
    .ui-date-field__error {
      margin: var(--rp-form-label-gap) 0 0;
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
    }

    .ui-date-field__hint {
      color: var(--rp-color-text-secondary);
    }

    .ui-date-field__error {
      color: var(--rp-color-danger-700);
    }

    .ui-date-field__error:empty {
      display: none;
    }

    .ui-date-field[data-invalid] .ui-date-field__control .ui-control {
      border-color: var(--rp-color-danger-700);
    }

    .ui-date-calendar {
      position: absolute;
      z-index: var(--rp-z-index-popover);
      inset-block-start: calc(100% + var(--rp-space-1));
      inset-inline-start: 0;
      width: min(100%, var(--rp-date-picker-calendar-width));
      padding: var(--rp-space-3);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-popover);
    }

    .ui-date-calendar__header,
    .ui-date-calendar__footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .ui-date-calendar__header h2 {
      margin: 0;
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ui-date-calendar__grid {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      gap: var(--rp-space-1);
      margin-block: var(--rp-space-2);
      text-align: center;
    }

    .ui-date-calendar__weekday {
      padding-block: var(--rp-space-1);
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
      font-weight: var(--rp-font-weight-medium);
    }

    .ui-date-calendar__day {
      display: grid;
      place-items: center;
      aspect-ratio: 1;
      min-width: 0;
      border: 1px solid transparent;
      border-radius: var(--rp-radius-control);
      background: transparent;
      color: var(--rp-color-text-primary);
      font: inherit;
      cursor: pointer;
    }

    .ui-date-calendar__day--outside {
      color: var(--rp-color-text-secondary);
    }

    .ui-date-calendar__day--today {
      border-color: var(--rp-color-primary);
      color: var(--rp-color-primary-fg);
    }

    .ui-date-calendar__day--selected {
      border-color: var(--rp-color-action);
      background: var(--rp-color-action);
      color: var(--rp-color-on-action);
    }

    .ui-date-calendar__day:hover:not(:disabled) {
      background: var(--rp-color-surface-hover);
    }

    .ui-date-calendar__day:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .ui-date-calendar__day:disabled {
      color: var(--rp-color-disabled-fg);
      cursor: not-allowed;
    }

    .ui-date-calendar__footer {
      justify-content: flex-end;
      border-top: 1px solid var(--rp-color-border);
      padding-top: var(--rp-space-2);
    }

    @media (max-width: 40rem) {
      .ui-date-calendar {
        width: 100%;
      }
    }
  `]
})
export class UiDatePickerComponent implements ControlValueAccessor, Validator {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  readonly dates = inject(CompanyDateService);

  readonly label = input('');
  readonly hint = input('');
  readonly required = input(false);
  readonly readonly = input(false);
  readonly min = input('');
  readonly max = input('');

  readonly inputId = `ui-date-picker-${++nextDatePickerId}`;
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;
  readonly calendarId = `${this.inputId}-calendar`;
  readonly describedBy = `${this.hintId} ${this.errorId}`;
  readonly weekdays = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
  readonly value = signal('');
  readonly text = signal('');
  readonly disabled = signal(false);
  readonly touched = signal(false);
  readonly dirty = signal(false);
  readonly invalidText = signal(false);
  readonly openState = signal(false);
  readonly displayMonth = signal(this.todayIso().slice(0, 7));

  readonly calendarDays = computed(() => {
    const [year, month] = this.displayMonth().split('-').map(Number);
    const first = new Date(Date.UTC(year, month - 1, 1));
    const start = new Date(Date.UTC(year, month - 1, 1 - first.getUTCDay()));
    const today = this.todayIso();
    return Array.from({ length: 42 }, (_, index): CalendarDay => {
      const date = new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth(), start.getUTCDate() + index));
      const iso = this.toIso(date);
      return {
        iso,
        day: date.getUTCDate(),
        inMonth: date.getUTCMonth() === month - 1,
        disabled: (!!this.min() && iso < this.min()) || (!!this.max() && iso > this.max()),
        today: iso === today
      };
    });
  });

  readonly monthLabel = computed(() => {
    const [year, month] = this.displayMonth().split('-').map(Number);
    return new Intl.DateTimeFormat('en', { month: 'long', year: 'numeric', timeZone: 'UTC' })
      .format(new Date(Date.UTC(year, month - 1, 1)));
  });

  private changed: (value: string) => void = () => {};
  private touchedCallback: () => void = () => {};
  private validatorChanged: () => void = () => {};

  constructor() {
    effect(() => {
      const value = this.value();
      this.dates.dateFormat();
      this.required();
      this.min();
      this.max();
      if (value && !this.invalidText()) {
        this.text.set(this.dates.inputText(value, false));
      }
      this.validatorChanged();
    });
  }

  writeValue(value: string | null | undefined): void {
    const date = value?.slice(0, 10) ?? '';
    const parsed = date && this.isIsoDate(date) ? date : '';
    this.value.set(parsed ?? '');
    this.text.set(parsed ? this.dates.inputText(parsed, false) : '');
    this.invalidText.set(false);
    if (parsed) {
      this.displayMonth.set(parsed.slice(0, 7));
    }
  }

  registerOnChange(fn: (value: string) => void): void {
    this.changed = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.touchedCallback = fn;
  }

  registerOnValidatorChange(fn: () => void): void {
    this.validatorChanged = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
    if (disabled) {
      this.close(false);
    }
  }

  validate(_control: AbstractControl): ValidationErrors | null {
    return this.validationError();
  }

  private validationError(): ValidationErrors | null {
    if (this.invalidText()) {
      return { companyDate: { format: this.dates.dateFormat() } };
    }
    if (!this.value()) {
      return this.required() ? { required: true } : null;
    }
    if (this.min() && this.value() < this.min()) {
      return { minDate: { min: this.min() } };
    }
    if (this.max() && this.value() > this.max()) {
      return { maxDate: { max: this.max() } };
    }
    return null;
  }

  showInvalid(): boolean {
    return (this.touched() || this.dirty()) && this.validationError() !== null;
  }

  errorMessage(): string {
    if (this.invalidText()) {
      return `Enter a valid date in ${this.dates.dateFormat()} format.`;
    }
    if (this.required() && !this.value()) {
      return 'This field is required.';
    }
    if (this.min() && this.value() < this.min()) {
      return `Date must be on or after ${this.dates.inputText(this.min(), false)}.`;
    }
    if (this.max() && this.value() > this.max()) {
      return `Date must be on or before ${this.dates.inputText(this.max(), false)}.`;
    }
    return '';
  }

  onInput(text: string): void {
    this.text.set(text);
    this.dirty.set(true);
    const parsed = text ? this.dates.parse(text) : '';
    this.invalidText.set(text !== '' && parsed === null);
    this.value.set(parsed ?? '');
    this.changed(parsed ?? '');
    this.validatorChanged();
  }

  open(focusCalendar = false): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    this.setDisplayMonth(this.value() || this.todayIso());
    this.openState.set(true);
    if (focusCalendar) {
      const preferredDate = this.value() || this.todayIso();
      queueMicrotask(() => {
        const preferred = this.host.querySelector<HTMLButtonElement>(`[data-date="${preferredDate}"]`);
        const fallback = Array.from(this.host.querySelectorAll<HTMLButtonElement>('[data-date]'))
          .find(day => !day.disabled);
        (preferred && !preferred.disabled ? preferred : fallback)?.focus();
      });
    }
  }

  toggle(): void {
    if (this.openState()) {
      this.close(false);
    } else {
      this.open(true);
    }
  }

  moveMonth(offset: number): void {
    const [year, month] = this.displayMonth().split('-').map(Number);
    const next = new Date(Date.UTC(year, month - 1 + offset, 1));
    this.displayMonth.set(`${next.getUTCFullYear()}-${String(next.getUTCMonth() + 1).padStart(2, '0')}`);
  }

  canMoveMonth(offset: number): boolean {
    const [year, month] = this.displayMonth().split('-').map(Number);
    const candidate = new Date(Date.UTC(year, month - 1 + offset, 1));
    const candidateMonth = `${candidate.getUTCFullYear()}-${String(candidate.getUTCMonth() + 1).padStart(2, '0')}`;
    const minMonth = this.min().slice(0, 7);
    const maxMonth = this.max().slice(0, 7);
    return (!minMonth || candidateMonth >= minMonth) && (!maxMonth || candidateMonth <= maxMonth);
  }

  select(iso: string): void {
    if ((this.min() && iso < this.min()) || (this.max() && iso > this.max())) {
      return;
    }
    this.value.set(iso);
    this.text.set(this.dates.inputText(iso, false));
    this.invalidText.set(false);
    this.dirty.set(true);
    this.changed(iso);
    this.validatorChanged();
    this.markTouched();
    this.close(true);
  }

  selectToday(): void {
    const today = this.todayIso();
    if (!((this.min() && today < this.min()) || (this.max() && today > this.max()))) {
      this.select(today);
    }
  }

  dayLabel(iso: string): string {
    return this.dates.inputText(iso, false);
  }

  markTouched(): void {
    this.touched.set(true);
    this.touchedCallback();
  }

  onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.openState()) {
      event.preventDefault();
      this.close(true);
    } else if (event.key === 'Enter' && this.openState()) {
      event.preventDefault();
      this.markTouched();
      this.close(true);
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.open(true);
    }
  }

  onCalendarKeydown(event: KeyboardEvent): void {
    const current = (event.target as HTMLElement).closest<HTMLElement>('[data-date]');
    const iso = current?.dataset['date'];
    if (!iso) {
      return;
    }
    const offset = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 :
      event.key === 'ArrowDown' ? 7 : event.key === 'ArrowUp' ? -7 : 0;
    if (offset) {
      event.preventDefault();
      const [year, month, day] = iso.split('-').map(Number);
      const next = new Date(Date.UTC(year, month - 1, day + offset));
      const nextIso = this.toIso(next);
      this.displayMonth.set(nextIso.slice(0, 7));
      queueMicrotask(() => this.host.querySelector<HTMLElement>(`[data-date="${nextIso}"]:not(:disabled)`)?.focus());
    }
  }

  @HostListener('document:pointerdown', ['$event'])
  onOutsidePointer(event: PointerEvent): void {
    if (this.openState() && !this.host.contains(event.target as Node)) {
      this.close(false);
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (this.openState()) {
      event.preventDefault();
      this.close(true);
    }
  }

  private close(restoreFocus: boolean): void {
    if (!this.openState()) {
      return;
    }
    this.openState.set(false);
    if (restoreFocus) {
      queueMicrotask(() => this.host.querySelector<HTMLInputElement>('input')?.focus());
    }
  }

  private setDisplayMonth(iso: string): void {
    this.displayMonth.set(iso.slice(0, 7));
  }

  private isIsoDate(value: string): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return false;
    }
    const [year, month, day] = value.split('-').map(Number);
    const date = new Date(Date.UTC(year, month - 1, day));
    return date.getUTCFullYear() === year && date.getUTCMonth() + 1 === month && date.getUTCDate() === day;
  }

  private todayIso(): string {
    return this.toIso(new Date());
  }

  private toIso(date: Date): string {
    return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
  }
}
