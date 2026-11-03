import { Directive, ElementRef, effect, inject, input, isDevMode } from '@angular/core';

export type UiActionButtonKind = 'view' | 'edit' | 'delete' | 'download' | 'more';

const ACTION_LABELS: Record<UiActionButtonKind, string> = {
  view: 'View',
  edit: 'Edit',
  delete: 'Delete',
  download: 'Download',
  more: 'More actions'
};

@Directive({
  selector: 'button[appUiActionButton]',
  standalone: true,
  host: {
    class: 'ui-action-btn',
    '[attr.data-action]': 'action()'
  }
})
export class UiActionButtonDirective {
  private readonly element = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;
  private assignedLabel = false;
  private assignedTitle = false;

  readonly action = input<UiActionButtonKind>('view');
  readonly tooltip = input(true);

  constructor() {
    effect(() => {
      const action = this.action();
      if (!this.element.hasAttribute('type')) {
        this.element.type = 'button';
      }
      if (!this.element.hasAttribute('aria-label') || this.assignedLabel) {
        this.element.setAttribute('aria-label', ACTION_LABELS[action]);
        this.assignedLabel = true;
      }
      if (this.tooltip() && (!this.element.hasAttribute('title') || this.assignedTitle)) {
        this.element.title = ACTION_LABELS[action];
        this.assignedTitle = true;
      } else if (!this.tooltip() && this.assignedTitle) {
        this.element.removeAttribute('title');
        this.assignedTitle = false;
      }
      if (isDevMode() && !this.element.querySelector('i, svg, img')) {
        console.warn('appUiActionButton: provide an icon element for the selected action.', this.element);
      }
    });
  }
}
