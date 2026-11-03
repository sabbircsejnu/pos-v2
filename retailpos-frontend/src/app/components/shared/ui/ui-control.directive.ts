import { Directive, ElementRef, inject, signal } from '@angular/core';
import { NgControl } from '@angular/forms';

let nextControlId = 0;

/**
 * Shared styling and accessibility wiring for native form controls:
 * `<input appUiControl formControlName="name">`, `<select appUiControl [(ngModel)]="x">`, `<textarea appUiControl>`.
 *
 * It is an attribute directive, not a ControlValueAccessor wrapper: Reactive Forms, ngModel,
 * validators and the native element keep working untouched. It only adds classes (`ui-control`, or
 * `ui-check__input` for checkbox/radio, see src/styles.css), a stable id, `aria-invalid` once the
 * control is invalid and touched or dirty, and the `aria-describedby`/`aria-required` hints that
 * `app-ui-form-field` provides.
 */
@Directive({
  selector: 'input[appUiControl], select[appUiControl], textarea[appUiControl]',
  standalone: true,
  exportAs: 'appUiControl',
  host: {
    '[class.ui-control]': '!checkable',
    '[class.ui-check__input]': 'checkable',
    '[class.ui-control--invalid]': 'showInvalid()',
    '[attr.id]': 'id()',
    '[attr.aria-invalid]': 'showInvalid() ? "true" : null',
    '[attr.aria-describedby]': 'describedByAttribute()',
    '[attr.aria-required]': 'required() ? "true" : null'
  }
})
export class UiControlDirective {
  private readonly element = inject<ElementRef<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>>(ElementRef).nativeElement;

  /** Present for both Reactive Forms and ngModel; null for a control outside any form directive. */
  readonly ngControl = inject(NgControl, { optional: true, self: true });
  readonly nativeElement = this.element;

  readonly id = signal(this.element.id || `ui-control-${++nextControlId}`);
  /** Set by app-ui-form-field to reference its hint and error text. */
  readonly describedBy = signal<string | null>(null);
  /** Set by app-ui-form-field when the field shows an explicit `[error]` (API or feature-side validation). */
  readonly externalInvalid = signal(false);
  readonly required = signal(this.element.hasAttribute('required'));

  readonly checkable =
    this.element instanceof HTMLInputElement && (this.element.type === 'checkbox' || this.element.type === 'radio');

  private readonly authoredDescribedBy = this.element.getAttribute('aria-describedby');

  /** Errors are shown only after interaction, so an untouched empty required field is not flagged. */
  showInvalid(): boolean {
    if (this.externalInvalid()) {
      return true;
    }
    const control = this.ngControl;
    return !!control && !!control.invalid && !!(control.touched || control.dirty);
  }

  describedByAttribute(): string | null {
    const ids = [this.authoredDescribedBy, this.describedBy()].filter(Boolean).join(' ');
    return ids || null;
  }

  clearValue(): void {
    if (!(this.element instanceof HTMLInputElement || this.element instanceof HTMLTextAreaElement)) {
      return;
    }
    this.element.value = '';
    this.element.dispatchEvent(new Event('input', { bubbles: true }));
    this.element.focus();
  }
}
