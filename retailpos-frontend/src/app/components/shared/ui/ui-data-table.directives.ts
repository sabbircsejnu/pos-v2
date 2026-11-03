import { Directive, TemplateRef, inject, input } from '@angular/core';

export interface UiTableCellContext<T = unknown> {
  /** The row object. */
  $implicit: T;
  /** The raw column value read from the row. */
  value: unknown;
  /** The formatted display text of the cell. */
  text: string;
  index: number;
}

export interface UiTableRowActionsContext<T = unknown> {
  $implicit: T;
  index: number;
}

/**
 * Custom cell for one column, matched by column key:
 * `<ng-template uiTableCell="name" let-row let-text="text">...</ng-template>`.
 * The template is feature code, so it can use the feature's own permission checks and handlers.
 */
@Directive({ selector: 'ng-template[uiTableCell]', standalone: true })
export class UiTableCellDirective {
  readonly columnKey = input.required<string>({ alias: 'uiTableCell' });
  readonly template = inject(TemplateRef<UiTableCellContext>);
}

/**
 * Row actions column: `<ng-template uiTableRowActions let-row>...</ng-template>`. Permission checks
 * stay in the feature template (`@if (canEdit(row)) {...}`); the table only provides the column.
 */
@Directive({ selector: 'ng-template[uiTableRowActions]', standalone: true })
export class UiTableRowActionsDirective {
  readonly template = inject(TemplateRef<UiTableRowActionsContext>);
}
