import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { UnsavedChangesAware, UnsavedChangesService } from '../services/unsaved-changes.service';

export const unsavedChangesGuard: CanDeactivateFn<UnsavedChangesAware> = (component) => {
  if (!component?.hasUnsavedChanges()) {
    return true;
  }

  const unsavedChangesService = inject(UnsavedChangesService);
  const message = component.getUnsavedChangesMessage?.() ?? unsavedChangesService.defaultMessage;
  return unsavedChangesService.confirmDiscard(message);
};
