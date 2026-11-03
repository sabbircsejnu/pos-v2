import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  Renderer2,
  computed,
  effect,
  inject,
  input,
  model,
  output
} from '@angular/core';
import { UiButtonComponent } from './ui-button.component';
import { UiFocusTrapDirective } from './ui-focus-trap.directive';

export type UiDialogSize = 'sm' | 'md' | 'lg';
export type UiDialogStatus = 'none' | 'success' | 'warning' | 'danger' | 'info';
export type UiDialogRole = 'dialog' | 'alertdialog';
export type UiDialogCloseReason = 'close' | 'escape' | 'backdrop';

const STATUS_ICONS: Record<UiDialogStatus, string> = {
  none: '',
  success: 'fas fa-circle-check',
  warning: 'fas fa-triangle-exclamation',
  danger: 'fas fa-circle-exclamation',
  info: 'fas fa-circle-info'
};

let nextDialogId = 0;
/** Open dialogs, top-most last: only the top one reacts to Escape. */
const openDialogs: number[] = [];
let scrollLocks = 0;
/** True only when a dialog added the body class itself, so a pre-existing class is never removed. */
let bodyClassAdded = false;

/**
 * Modal dialog presentation. The feature owns the state and the business actions:
 *
 * ```html
 * <app-ui-dialog [(open)]="showEdit" title="Edit supplier" size="md">
 *   <form ...>...</form>
 *   <button uiDialogFooter appUiButton variant="outline" (click)="showEdit.set(false)">Cancel</button>
 *   <button uiDialogFooter appUiButton type="submit" [loading]="saving()" (click)="save()">Save</button>
 * </app-ui-dialog>
 * ```
 *
 * Behaviour: labelled by its title, `aria-modal`, focus moves in on open (to `[data-autofocus]` or the
 * first control), is trapped, and returns to the trigger on close. Escape, the backdrop and the close
 * button dismiss it unless `dismissible` is false (use that while a save is running). Page scrolling
 * is locked while open. Prefer wrapping the dialog in `@if` for heavy content: projected content is
 * created even while the dialog is closed.
 */
@Component({
  selector: 'app-ui-dialog',
  host: { '[attr.title]': 'null' },
  standalone: true,
  imports: [UiButtonComponent, UiFocusTrapDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div class="ui-dialog" [attr.data-size]="size()">
        <div class="ui-dialog__backdrop" aria-hidden="true" (click)="onBackdropClick()"></div>
        <div
          class="ui-dialog__panel"
          appUiFocusTrap
          [attr.role]="role()"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          [attr.aria-describedby]="bodyId">
          <div class="ui-dialog__header">
            @if (statusIcon()) {
              <span class="ui-dialog__status" [attr.data-status]="status()" aria-hidden="true"><i [class]="statusIcon()"></i></span>
            }
            <h2 class="ui-dialog__title" [id]="titleId">{{ title() }}</h2>
            @if (dismissible() && showClose()) {
              <button appUiButton variant="ghost" size="sm" [iconOnly]="true" data-focus-skip aria-label="Close dialog" title="Close" (click)="dismiss('close')">
                <i class="fas fa-times" aria-hidden="true"></i>
              </button>
            }
          </div>
          <div class="ui-dialog__body" [id]="bodyId"><ng-content /></div>
          <div class="ui-dialog__footer"><ng-content select="[uiDialogFooter]" /></div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host {
      display: contents;
    }

    .ui-dialog {
      position: fixed;
      inset: 0;
      z-index: 70;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--rp-space-4);
      font-family: var(--rp-font-family-base);
    }

    .ui-dialog__backdrop {
      position: absolute;
      inset: 0;
      background: var(--rp-color-backdrop);
    }

    .ui-dialog__panel {
      position: relative;
      display: flex;
      flex-direction: column;
      width: 100%;
      max-width: 32rem;
      max-height: calc(100dvh - 2rem);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-modal);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
    }

    .ui-dialog__panel:focus {
      outline: none;
    }

    .ui-dialog[data-size='sm'] .ui-dialog__panel {
      max-width: 24rem;
    }

    .ui-dialog[data-size='lg'] .ui-dialog__panel {
      max-width: 48rem;
    }

    .ui-dialog__header {
      display: flex;
      align-items: center;
      gap: var(--rp-space-3);
      padding: var(--rp-space-4) var(--rp-card-padding);
      border-bottom: 1px solid var(--rp-color-border);
    }

    .ui-dialog__title {
      flex: 1;
      margin: 0;
      font-size: var(--rp-text-section-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: var(--rp-text-section-line);
    }

    .ui-dialog__status {
      display: inline-flex;
      flex-shrink: 0;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border-radius: var(--rp-radius-pill);
      background: var(--rp-color-status-info-bg);
      color: var(--rp-color-status-info-icon);
    }

    .ui-dialog__status[data-status='success'] {
      background: var(--rp-color-status-success-bg);
      color: var(--rp-color-status-success-icon);
    }

    .ui-dialog__status[data-status='warning'] {
      background: var(--rp-color-status-warning-bg);
      color: var(--rp-color-status-warning-icon);
    }

    .ui-dialog__status[data-status='danger'] {
      background: var(--rp-color-status-danger-bg);
      color: var(--rp-color-status-danger-icon);
    }

    .ui-dialog__body {
      flex: 1;
      overflow-y: auto;
      padding: var(--rp-card-padding);
    }

    .ui-dialog__footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--rp-space-2);
      padding: var(--rp-space-3) var(--rp-card-padding);
      border-top: 1px solid var(--rp-color-border);
      border-radius: 0 0 var(--rp-radius-card) var(--rp-radius-card);
      background: var(--rp-color-surface-muted);
    }

    .ui-dialog__footer:empty {
      display: none;
    }

    /* Phones: a full-width sheet anchored to the bottom, with stacked full-width actions. */
    @media (max-width: 639.98px) {
      .ui-dialog {
        align-items: flex-end;
        padding: 0;
      }

      .ui-dialog__panel {
        max-width: none;
        max-height: 92dvh;
        border-radius: var(--rp-radius-card) var(--rp-radius-card) 0 0;
      }

      .ui-dialog__footer {
        flex-direction: column-reverse;
        align-items: stretch;
        border-radius: 0;
      }

      .ui-dialog__footer ::ng-deep > * {
        width: 100%;
      }
    }
  `]
})
export class UiDialogComponent implements OnDestroy {
  private readonly document = inject(DOCUMENT);
  private readonly renderer = inject(Renderer2);
  private readonly id = ++nextDialogId;

  readonly open = model(false);
  readonly title = input.required<string>();
  readonly size = input<UiDialogSize>('md');
  readonly status = input<UiDialogStatus>('none');
  /** Use "alertdialog" for confirmations and destructive actions that need an explicit answer. */
  readonly role = input<UiDialogRole>('dialog');
  /** When false the dialog cannot be dismissed (Escape, backdrop and the close button are all disabled). */
  readonly dismissible = input(true);
  readonly closeOnBackdrop = input(true);
  readonly closeOnEscape = input(true);
  readonly showClose = input(true);

  /** Emits after the user dismisses the dialog; `open` has already been set to false. */
  readonly closed = output<UiDialogCloseReason>();

  readonly titleId = `ui-dialog-title-${this.id}`;
  readonly bodyId = `ui-dialog-body-${this.id}`;
  readonly statusIcon = computed(() => STATUS_ICONS[this.status()]);

  private registered = false;

  constructor() {
    effect(() => {
      if (this.open()) {
        this.register();
      } else {
        this.unregister();
      }
    });
  }

  ngOnDestroy(): void {
    this.unregister();
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (!this.open() || !this.closeOnEscape() || openDialogs[openDialogs.length - 1] !== this.id) {
      return;
    }
    if (this.dismiss('escape')) {
      event.preventDefault();
    }
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop()) {
      this.dismiss('backdrop');
    }
  }

  dismiss(reason: UiDialogCloseReason): boolean {
    if (!this.open() || !this.dismissible()) {
      return false;
    }
    this.open.set(false);
    this.closed.emit(reason);
    return true;
  }

  private register(): void {
    if (this.registered) {
      return;
    }
    this.registered = true;
    openDialogs.push(this.id);
    if (scrollLocks++ === 0 && !this.document.body.classList.contains('overflow-hidden')) {
      this.renderer.addClass(this.document.body, 'overflow-hidden');
      bodyClassAdded = true;
    }
  }

  private unregister(): void {
    if (!this.registered) {
      return;
    }
    this.registered = false;
    const index = openDialogs.indexOf(this.id);
    if (index >= 0) {
      openDialogs.splice(index, 1);
    }
    scrollLocks = Math.max(0, scrollLocks - 1);
    if (scrollLocks === 0 && bodyClassAdded) {
      this.renderer.removeClass(this.document.body, 'overflow-hidden');
      bodyClassAdded = false;
    }
  }
}
