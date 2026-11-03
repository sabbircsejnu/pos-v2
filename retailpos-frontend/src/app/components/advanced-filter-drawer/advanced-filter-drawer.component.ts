import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';
import { UiBadgeComponent } from '../shared/ui/ui-badge.component';
import { UiButtonComponent } from '../shared/ui/ui-button.component';
import { UiFocusTrapDirective } from '../shared/ui/ui-focus-trap.directive';

let nextDrawerId = 0;

/**
 * Right-hand filter drawer. The feature owns the filter form (projected content) and the
 * apply/clear/close handlers. The drawer provides the presentation: shared tokens, a labelled modal
 * dialog with a focus trap and focus restoration, Escape to close, sticky header and footer, a full
 * width layout on phones, and an optional "applied filters" indicator (`activeCount`).
 */
@Component({
  selector: 'app-advanced-filter-drawer',
  host: { '[attr.title]': 'null' },
  standalone: true,
  imports: [CommonModule, UiBadgeComponent, UiButtonComponent, UiFocusTrapDirective],
  template: `
    <div class="afd__backdrop" aria-hidden="true" (click)="closed.emit()"></div>

    <aside class="afd__panel" appUiFocusTrap role="dialog" aria-modal="true" [attr.aria-labelledby]="titleId">
      <header class="afd__header">
        <h2 class="afd__title" [id]="titleId">{{ title }}</h2>
        @if (activeCount > 0) {
          <app-ui-badge variant="info">{{ activeCount }} applied</app-ui-badge>
        }
        <button appUiButton variant="ghost" size="sm" [iconOnly]="true" data-focus-skip aria-label="Close Drawer" title="Close" (click)="closed.emit()">
          <i class="fas fa-times" aria-hidden="true"></i>
        </button>
      </header>

      <section class="afd__body">
        <ng-content></ng-content>
      </section>

      <footer class="afd__footer">
        <button appUiButton variant="ghost" (click)="closed.emit()">{{ closeLabel }}</button>
        <button appUiButton variant="outline" (click)="clear.emit()">{{ clearLabel }}</button>
        <button appUiButton variant="primary" (click)="apply.emit()">{{ applyLabel }}</button>
      </footer>
    </aside>
  `,
  styles: [`
    :host {
      display: contents;
      font-family: var(--rp-font-family-base);
    }

    .afd__backdrop {
      position: fixed;
      inset: 0;
      z-index: 64;
      background: var(--rp-color-backdrop);
    }

    .afd__panel {
      position: fixed;
      top: 0;
      right: 0;
      z-index: 65;
      display: flex;
      flex-direction: column;
      width: 100%;
      height: 100%;
      background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-drawer);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
    }

    .afd__panel:focus {
      outline: none;
    }

    @media (min-width: 768px) {
      .afd__panel {
        width: 640px;
      }
    }

    .afd__header {
      display: flex;
      flex-shrink: 0;
      align-items: center;
      gap: var(--rp-space-3);
      padding: var(--rp-space-3) var(--rp-space-5);
      border-bottom: 1px solid var(--rp-color-border);
    }

    .afd__title {
      flex: 1;
      margin: 0;
      font-size: var(--rp-text-section-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: var(--rp-text-section-line);
    }

    .afd__body {
      flex: 1;
      overflow-y: auto;
      padding: var(--rp-space-5);
    }

    .afd__footer {
      display: flex;
      flex-shrink: 0;
      flex-direction: column-reverse;
      gap: var(--rp-space-2);
      padding: var(--rp-space-3) var(--rp-space-5);
      border-top: 1px solid var(--rp-color-border);
      background: var(--rp-color-surface-muted);
    }

    @media (min-width: 640px) {
      .afd__footer {
        flex-direction: row;
        align-items: center;
        justify-content: flex-end;
      }
    }
  `]
})
export class AdvancedFilterDrawerComponent {
  @Input() title = 'Advanced Filters';
  @Input() applyLabel = 'Apply Filters';
  @Input() clearLabel = 'Clear Filters';
  @Input() closeLabel = 'Close Drawer';
  /** Number of filters currently applied; shows an "n applied" indicator when above zero. */
  @Input() activeCount = 0;

  @Output() closed = new EventEmitter<void>();
  @Output() clear = new EventEmitter<void>();
  @Output() apply = new EventEmitter<void>();

  readonly titleId = `afd-title-${++nextDrawerId}`;

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (!event.defaultPrevented) {
      event.preventDefault();
      this.closed.emit();
    }
  }
}
