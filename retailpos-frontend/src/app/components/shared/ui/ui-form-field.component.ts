import { Component, contentChild, effect, input, signal } from '@angular/core';
import { UiControlDirective } from './ui-control.directive';

let nextFieldId = 0;

const DEFAULT_MESSAGES: Record<string, (error: unknown) => string> = {
  required: () => 'This field is required.',
  email: () => 'Enter a valid email address.',
  minlength: error => `Enter at least ${(error as { requiredLength: number }).requiredLength} characters.`,
  maxlength: error => `Enter no more than ${(error as { requiredLength: number }).requiredLength} characters.`,
  min: error => `Value must be at least ${(error as { min: number }).min}.`,
  max: error => `Value must be no more than ${(error as { max: number }).max}.`,
  pattern: () => 'Enter a value in the expected format.'
};

/**
 * Label, hint and validation message around one native control:
 *
 * ```html
 * <app-ui-form-field label="Email" hint="We never share it" [required]="true">
 *   <input appUiControl type="email" formControlName="email" />
 * </app-ui-form-field>
 * ```
 *
 * It projects the control (no value accessor in between) and reads its validation state through
 * `appUiControl`. Validators, submission and server-side errors stay with the feature: pass
 * `[error]` for a message that comes from the API, or `[messages]` to override wording per error key.
 */
@Component({
  selector: 'app-ui-form-field',
  standalone: true,
  // Default change detection on purpose: the error text depends on form control state
  // (touched/dirty/errors), which is not signal based and changes outside this component's inputs.
  template: `
    <div
      class="ui-field"
      [attr.data-appearance]="appearance()"
      [attr.data-size]="size()"
      [attr.data-has-value]="hasValue() ? '' : null"
      (input)="syncValue()"
      (change)="syncValue()">
      @if (label() && appearance() !== 'filled') {
        <label class="ui-field__label" [attr.for]="control()?.id()">{{ label() }}@if (required()) {<span class="ui-field__required" aria-hidden="true"> *</span><span class="sr-only"> (required)</span>}</label>
      }
      <div class="ui-field__control">
        <ng-content select="[uiFieldPrefix]" />
        @if (label() && appearance() === 'filled') {
          <label class="ui-field__label ui-field__label--inset" [attr.for]="control()?.id()">{{ label() }}@if (required()) {<span class="ui-field__required" aria-hidden="true"> *</span><span class="sr-only"> (required)</span>}</label>
        }
        <ng-content />
        @if (showClearButton()) {
          <button type="button" class="ui-field__clear" [attr.aria-label]="'Clear ' + (label() || 'field')" (click)="clearControl()">
            <i class="fas fa-times" aria-hidden="true"></i>
          </button>
        }
        <ng-content select="[uiFieldSuffix]" />
      </div>
    </div>
    @if (hint()) {
      <p class="ui-field__hint" [id]="hintId">{{ hint() }}</p>
    }
    <div class="ui-field__error" [id]="errorId" aria-live="polite">@if (errorMessage(); as message) {<i class="fas fa-circle-exclamation" aria-hidden="true"></i><span>{{ message }}</span>}</div>
    @if (counter() && maxLength() !== null) {
      <p class="ui-field__counter" aria-live="polite">{{ valueLength() }} / {{ maxLength() }}</p>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .ui-field__label {
      display: block;
      margin-bottom: var(--rp-form-label-gap);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-label-size);
      font-weight: var(--rp-font-weight-medium);
      line-height: var(--rp-text-label-line);
    }

    .ui-field__required {
      color: var(--rp-color-danger-700);
    }

    .ui-field__hint {
      margin: var(--rp-form-label-gap) 0 0;
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
    }

    .ui-field__error {
      display: flex;
      align-items: flex-start;
      gap: var(--rp-space-1);
      color: var(--rp-color-danger-700);
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
    }

    .ui-field__error:not(:empty) {
      margin-top: var(--rp-form-label-gap);
    }

    .ui-field__error i {
      margin-top: var(--rp-space-1);
    }

    .ui-field__control {
      display: flex;
      align-items: center;
      gap: var(--rp-space-2);
      min-width: 0;
    }

    .ui-field__label--inset {
      margin: 0;
    }

    .ui-field__counter {
      margin: var(--rp-form-label-gap) 0 0;
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
      text-align: end;
    }

    .ui-field__clear {
      display: inline-flex;
      flex: 0 0 auto;
      align-items: center;
      justify-content: center;
      width: var(--rp-control-height-sm);
      height: var(--rp-control-height-sm);
      border: 0;
      border-radius: var(--rp-radius-control);
      background: transparent;
      color: var(--rp-color-text-secondary);
      cursor: pointer;
    }

    .ui-field__clear:hover {
      background: var(--rp-color-surface-hover);
      color: var(--rp-color-text-primary);
    }

    .ui-field__clear:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .ui-field__clear:disabled {
      color: var(--rp-color-disabled-fg);
      cursor: not-allowed;
    }
  `]
})
export class UiFormFieldComponent {
  readonly label = input('');
  readonly hint = input('');
  readonly appearance = input<'outline' | 'filled'>('outline');
  readonly size = input<'comfortable' | 'compact'>('comfortable');
  readonly clearable = input(false);
  readonly counter = input(false);
  readonly maxLength = input<number | null>(null);
  /** Adds the visible and screen-reader "required" marker; validation itself stays in the form. */
  readonly required = input(false);
  /** Explicit message (for example from an API response); wins over validator messages. */
  readonly error = input('');
  /** Overrides default wording per validator key: { required: 'Enter the SKU' }. */
  readonly messages = input<Record<string, string>>({});

  readonly control = contentChild(UiControlDirective);
  readonly valueLength = signal(0);

  private readonly fieldId = ++nextFieldId;
  readonly hintId = `ui-field-hint-${this.fieldId}`;
  readonly errorId = `ui-field-error-${this.fieldId}`;

  constructor() {
    effect(() => {
      const control = this.control();
      if (!control) {
        return;
      }
      control.describedBy.set(`${this.hintId} ${this.errorId}`);
      control.externalInvalid.set(!!this.error());
      if (this.required()) {
        control.required.set(true);
      }
      this.syncValue();
    });
  }

  hasValue(): boolean {
    return !!this.control()?.nativeElement.value;
  }

  showClearButton(): boolean {
    const element = this.control()?.nativeElement;
    if (element instanceof HTMLInputElement) {
      return !!this.clearable() && !!element.value && !element.disabled && !element.readOnly &&
        !['checkbox', 'radio', 'file', 'button', 'submit', 'reset'].includes(element.type);
    }
    return !!this.clearable() && element instanceof HTMLTextAreaElement && !!element.value &&
      !element.disabled && !element.readOnly;
  }

  syncValue(): void {
    this.valueLength.set(this.control()?.nativeElement.value.length ?? 0);
  }

  clearControl(): void {
    this.control()?.clearValue();
    this.syncValue();
  }

  errorMessage(): string | null {
    const explicit = this.error();
    if (explicit) {
      return explicit;
    }

    const control = this.control();
    const errors = control?.ngControl?.control?.errors;
    if (!control || !errors || !control.showInvalid()) {
      return null;
    }

    const key = Object.keys(errors)[0];
    const custom = this.messages()[key];
    if (custom) {
      return custom;
    }
    if (typeof errors[key] === 'string') {
      return errors[key];
    }
    return DEFAULT_MESSAGES[key]?.(errors[key]) ?? 'This value is not valid.';
  }
}
