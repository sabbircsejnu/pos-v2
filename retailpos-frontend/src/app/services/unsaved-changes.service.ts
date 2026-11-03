import { Injectable } from '@angular/core';

export interface UnsavedChangesAware {
  hasUnsavedChanges(): boolean;
  getUnsavedChangesMessage?(): string;
}

@Injectable({ providedIn: 'root' })
export class UnsavedChangesService {
  readonly defaultMessage = 'You have unsaved changes. If you leave this page, all unsaved transfer information will be lost.';

  confirmDiscard(message: string = this.defaultMessage): boolean {
    return window.confirm(message);
  }

  applyBeforeUnload(event: BeforeUnloadEvent, shouldWarn: boolean): void {
    if (!shouldWarn) {
      return;
    }

    event.preventDefault();
    event.returnValue = this.defaultMessage;
  }
}
