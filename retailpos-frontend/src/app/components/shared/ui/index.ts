import { UiAlertComponent } from './ui-alert.component';
import { UiBadgeComponent } from './ui-badge.component';
import { UiButtonComponent } from './ui-button.component';
import { UiCardComponent } from './ui-card.component';
import { UiConfirmDialogComponent } from './ui-confirm-dialog.component';
import { UiControlDirective } from './ui-control.directive';
import { UiDataTableComponent } from './ui-data-table.component';
import { UiTableCellDirective, UiTableRowActionsDirective } from './ui-data-table.directives';
import { UiDialogComponent } from './ui-dialog.component';
import { UiFocusTrapDirective } from './ui-focus-trap.directive';
import { UiFormFieldComponent } from './ui-form-field.component';
import { UiPageHeaderComponent } from './ui-page-header.component';
import { UiStateComponent } from './ui-state.component';
import { UiDateInputDirective } from './ui-date-input.directive';
import { AppDatePipe } from '../../../pipes/app-date.pipe';
import { UiActionButtonDirective } from './ui-action-button.directive';
import { UiDatePickerComponent } from './ui-date-picker.component';
import { UiStepDirective, UiStepperComponent } from './ui-stepper.component';

export * from './ui-alert.component';
export * from './ui-badge.component';
export * from './ui-button.component';
export * from './ui-card.component';
export * from './ui-confirm-dialog.component';
export * from './ui-control.directive';
export * from './ui-data-table.component';
export * from './ui-data-table.directives';
export * from './ui-data-table.logic';
export * from './ui-data-table.types';
export * from './ui-dialog.component';
export * from './ui-focus-trap.directive';
export * from './ui-form-field.component';
export * from './ui-page-header.component';
export * from './ui-state.component';
export * from './ui-date-input.directive';
export * from './ui-action-button.directive';
export * from './ui-date-picker.component';
export * from './ui-stepper.component';
export { AppDatePipe } from '../../../pipes/app-date.pipe';

/** Everything in the shared UI kit, for `imports: [...UI_KIT]` in a feature component. */
export const UI_KIT = [
  UiDateInputDirective,
  UiDatePickerComponent,
  UiActionButtonDirective,
  UiStepperComponent,
  UiStepDirective,
  AppDatePipe,
  UiAlertComponent,
  UiBadgeComponent,
  UiButtonComponent,
  UiCardComponent,
  UiConfirmDialogComponent,
  UiControlDirective,
  UiDataTableComponent,
  UiTableCellDirective,
  UiTableRowActionsDirective,
  UiDialogComponent,
  UiFocusTrapDirective,
  UiFormFieldComponent,
  UiPageHeaderComponent,
  UiStateComponent
] as const;
