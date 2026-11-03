import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { UiButtonComponent } from './ui-button.component';
import { UiDialogComponent } from './ui-dialog.component';

/**
 * Confirmation / destructive-action dialog. Presentation only: the feature performs the action in
 * its `(confirmed)` handler and decides when to close, so it can keep the dialog open (with `busy`)
 * while a request runs or when the request fails.
 *
 * ```html
 * <app-ui-confirm-dialog [(open)]="confirmDelete" title="Delete supplier?" confirmLabel="Delete"
 *     [destructive]="true" [busy]="deleting()" (confirmed)="delete()">
 *   This permanently removes {{ supplier().name }}.
 * </app-ui-confirm-dialog>
 * ```
 *
 * It uses role="alertdialog", does not close on backdrop clicks, treats Escape and Cancel the same
 * (`cancelled`), and focuses Cancel first when destructive so Enter never deletes by accident.
 */
@Component({
  selector: 'app-ui-confirm-dialog',
  host: { '[attr.title]': 'null' },
  standalone: true,
  imports: [UiDialogComponent, UiButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-ui-dialog
      [(open)]="open"
      [title]="title()"
      size="sm"
      role="alertdialog"
      [status]="destructive() ? 'danger' : 'info'"
      [closeOnBackdrop]="false"
      [showClose]="false"
      [dismissible]="!busy()"
      (closed)="cancelled.emit()">
      <ng-content />
      <button uiDialogFooter appUiButton variant="outline" type="button" [disabled]="busy()" [attr.data-autofocus]="destructive() ? '' : null" (click)="cancel()">{{ cancelLabel() }}</button>
      <button uiDialogFooter appUiButton type="button" [variant]="destructive() ? 'danger' : 'primary'" [loading]="busy()" [attr.data-autofocus]="destructive() ? null : ''" (click)="confirmed.emit()">{{ confirmLabel() }}</button>
    </app-ui-dialog>
  `
})
export class UiConfirmDialogComponent {
  readonly open = model(false);
  readonly title = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');
  readonly destructive = input(false);
  /** Shows the confirm button as loading and blocks every way of dismissing while the action runs. */
  readonly busy = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  cancel(): void {
    this.open.set(false);
    this.cancelled.emit();
  }
}
