import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, forwardRef, inject, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { UiBadgeComponent } from '../shared/ui';
import { WEEKDAYS } from './outlet-sales-target.logic';

let nextId = 0;

/** A dropdown of the seven weekdays with several selectable, shown as removable chips. The value is a list of 0–6 day numbers. */
@Component({
  selector: 'app-weekday-multi-select',
  standalone: true,
  imports: [UiBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => WeekdayMultiSelectComponent), multi: true }],
  styles: [`
    :host { display: block; }
    .wms__label { display: block; margin-bottom: var(--rp-space-1); color: var(--rp-color-text-primary); font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-medium); }
    .wms { position: relative; }
    .wms__trigger { display: flex; align-items: center; justify-content: space-between; gap: var(--rp-space-2); width: 100%; text-align: left; cursor: pointer; }
    .wms__menu { position: absolute; z-index: 20; top: 100%; left: 0; right: 0; display: grid; gap: var(--rp-space-1); margin: var(--rp-space-1) 0 0; padding: var(--rp-space-2); border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-control); background: var(--rp-color-surface-card); list-style: none; }
    .wms__chips { display: flex; flex-wrap: wrap; gap: var(--rp-space-1); margin: var(--rp-space-2) 0 0; padding: 0; list-style: none; }
    .wms__chip { display: inline-flex; align-items: center; gap: var(--rp-space-1); }
    .wms__remove { padding: 0 var(--rp-space-1); border: 0; background: transparent; color: var(--rp-color-text-secondary); cursor: pointer; }
    .wms__hint { margin: var(--rp-space-1) 0 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    .wms__error { margin: var(--rp-space-1) 0 0; color: var(--rp-color-danger-700); font-size: var(--rp-text-helper-size); }
  `],
  template: `
    <span class="wms__label" [id]="labelId">{{ label() }}</span>
    <div class="wms">
      <button type="button" class="ui-control wms__trigger" [disabled]="disabled()"
        [attr.aria-labelledby]="labelId + ' ' + summaryId" aria-haspopup="true" [attr.aria-expanded]="open()"
        [attr.aria-describedby]="hint() ? hintId : null" (click)="toggle()" (blur)="markTouched()">
        <span [id]="summaryId">{{ summary() }}</span>
        <i class="fas fa-chevron-down" aria-hidden="true"></i>
      </button>
      @if (open()) {
        <ul class="wms__menu" role="group" [attr.aria-labelledby]="labelId">
          @for (day of weekdays; track day.value) {
            <li>
              <label class="ui-check">
                <input type="checkbox" class="ui-check__input" [checked]="isSelected(day.value)" (change)="toggleDay(day.value)" />
                <span>{{ day.label }}</span>
              </label>
            </li>
          }
        </ul>
      }
    </div>
    @if (selectedDays().length) {
      <ul class="wms__chips" aria-label="Selected days">
        @for (day of selectedDays(); track day.value) {
          <li class="wms__chip">
            <app-ui-badge variant="neutral">
              {{ day.label }}
              @if (!disabled()) {
                <button type="button" class="wms__remove" [attr.aria-label]="'Remove ' + day.label" (click)="toggleDay(day.value)">
                  <i class="fas fa-times" aria-hidden="true"></i>
                </button>
              }
            </app-ui-badge>
          </li>
        }
      </ul>
    }
    @if (hint()) {
      <p class="wms__hint" [id]="hintId">{{ hint() }}</p>
    }
    <div aria-live="polite">
      @if (error()) {
        <p class="wms__error"><i class="fas fa-circle-exclamation" aria-hidden="true"></i> {{ error() }}</p>
      }
    </div>
  `
})
export class WeekdayMultiSelectComponent implements ControlValueAccessor {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly id = ++nextId;

  readonly label = input.required<string>();
  readonly hint = input<string>('');
  readonly error = input<string>('');

  readonly labelId = `wms-label-${this.id}`;
  readonly summaryId = `wms-summary-${this.id}`;
  readonly hintId = `wms-hint-${this.id}`;
  readonly weekdays = WEEKDAYS;

  readonly value = signal<number[]>([]);
  readonly open = signal(false);
  readonly disabled = signal(false);

  readonly selectedDays = computed(() => WEEKDAYS.filter(day => this.value().includes(day.value)));
  readonly summary = computed(() => {
    const days = this.selectedDays();
    return days.length ? days.map(day => day.label).join(', ') : 'No off days';
  });

  private onChange: (value: number[]) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  isSelected(day: number): boolean {
    return this.value().includes(day);
  }

  toggle(): void {
    this.open.update(open => !open);
  }

  toggleDay(day: number): void {
    const next = this.isSelected(day) ? this.value().filter(item => item !== day) : [...this.value(), day];
    this.value.set(next);
    this.onChange(next);
    this.onTouched();
  }

  markTouched(): void {
    this.onTouched();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (this.open() && !event.composedPath().includes(this.host)) {
      this.open.set(false);
    }
  }

  @HostListener('keydown.escape')
  onEscape(): void {
    this.open.set(false);
  }

  writeValue(value: number[] | null): void {
    this.value.set(value ?? []);
  }

  registerOnChange(fn: (value: number[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) {
      this.open.set(false);
    }
  }
}
