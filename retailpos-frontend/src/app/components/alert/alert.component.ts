import { Component, computed, inject } from '@angular/core';
import { AlertService } from '../../services/alert.service';
import { UiButtonComponent } from '../shared/ui/ui-button.component';
import { UiDialogCloseReason, UiDialogComponent, UiDialogStatus } from '../shared/ui/ui-dialog.component';

/**
 * Renders the modal alerts and confirmations raised through `AlertService` (including the global
 * HTTP error messages). Presentation now comes from the shared `app-ui-dialog` (tokens, focus trap,
 * focus restoration, labelled dialog); the service contract and callbacks are unchanged.
 *
 * Behaviour: informational alerts close with OK, Escape or a backdrop click; confirmations use
 * role="alertdialog", ignore backdrop clicks (as before) and treat Escape as Cancel.
 */
@Component({
  selector: 'app-alert',
  standalone: true,
  imports: [UiDialogComponent, UiButtonComponent],
  template: `
    @if (alertConfig(); as config) {
      <app-ui-dialog
        [open]="true"
        size="sm"
        [title]="config.title"
        [status]="status()"
        [role]="config.type === 'confirm' ? 'alertdialog' : 'dialog'"
        [closeOnBackdrop]="config.type !== 'confirm'"
        [showClose]="false"
        (closed)="onDismissed($event)">
        <p class="m-0 text-rp-ink-secondary">{{ config.message }}</p>
        @if (config.type === 'confirm') {
          <ng-container ngProjectAs="[uiDialogFooter]">
            <button appUiButton variant="outline" type="button" (click)="onCancel()">{{ config.cancelText }}</button>
            <button appUiButton type="button" data-autofocus (click)="onConfirm()">{{ config.confirmText }}</button>
          </ng-container>
        } @else {
          <ng-container ngProjectAs="[uiDialogFooter]">
            <button appUiButton type="button" data-autofocus [variant]="config.type === 'error' ? 'danger' : 'primary'" (click)="close()">OK</button>
          </ng-container>
        }
      </app-ui-dialog>
    }
  `
})
export class AlertComponent {
  private readonly alertService = inject(AlertService);

  alertConfig = computed(() => this.alertService.config());

  readonly status = computed<UiDialogStatus>(() => {
    switch (this.alertConfig()?.type) {
      case 'success':
        return 'success';
      case 'error':
        return 'danger';
      case 'warning':
        return 'warning';
      default:
        return 'info';
    }
  });

  onConfirm(): void {
    const config = this.alertConfig();
    if (config?.onConfirm) {
      config.onConfirm();
    }
    this.close();
  }

  onCancel(): void {
    const config = this.alertConfig();
    if (config?.onCancel) {
      config.onCancel();
    }
    this.close();
  }

  onBackdropClick(): void {
    if (this.alertConfig()?.type !== 'confirm') {
      this.close();
    }
  }

  /** Escape or backdrop: a confirmation is cancelled (callback runs), anything else just closes. */
  onDismissed(_reason?: UiDialogCloseReason): void {
    if (this.alertConfig()?.type === 'confirm') {
      this.onCancel();
    } else {
      this.close();
    }
  }

  close(): void {
    this.alertService.close();
  }
}
