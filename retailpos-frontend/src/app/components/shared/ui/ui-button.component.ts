import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  effect,
  inject,
  input,
  isDevMode
} from '@angular/core';

export type UiButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type UiButtonSize = 'sm' | 'md' | 'lg';

/**
 * Presentation for native buttons and links: `<button appUiButton variant="primary">Save</button>`.
 *
 * It decorates the element instead of wrapping it, so `type`, `[disabled]`, `(click)`, `routerLink`
 * and form submission keep their native behaviour. Differences from a plain button:
 * - `type` defaults to "button" (set `type="submit"` explicitly on submit buttons);
 * - while `loading` is true the click is swallowed before any other handler runs (duplicate-submit
 *   protection) and the element exposes `aria-busy`/`aria-disabled` while staying focusable.
 */
@Component({
  selector: 'button[appUiButton], a[appUiButton]',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'ui-btn',
    '[attr.data-variant]': 'variant()',
    '[attr.data-size]': 'size()',
    '[attr.data-icon-only]': 'iconOnly() ? "" : null',
    '[attr.data-block]': 'block() ? "" : null',
    '[attr.aria-busy]': 'loading() ? "true" : null'
  },
  template: `@if (loading()) {<span class="ui-btn__spinner" aria-hidden="true"></span>}<ng-content />`,
  styles: [`
    :host {
      box-sizing: border-box;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: var(--rp-space-2);
      height: var(--rp-control-height);
      padding: 0 var(--rp-space-4);
      border: 1px solid transparent;
      border-radius: var(--rp-radius-control);
      font-family: var(--rp-font-family-base);
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-medium);
      line-height: 1;
      text-decoration: none;
      white-space: nowrap;
      cursor: pointer;
      transition: background-color 150ms ease, border-color 150ms ease, color 150ms ease;
    }

    :host([data-size='sm']) {
      height: var(--rp-control-height-sm);
      padding: 0 var(--rp-space-3);
      font-size: var(--rp-text-label-size);
    }

    :host([data-size='lg']) {
      height: var(--rp-control-height-lg);
      padding: 0 var(--rp-space-5);
    }

    :host([data-block]) {
      display: flex;
      width: 100%;
    }

    :host([data-icon-only]) {
      width: var(--rp-control-height);
      padding: 0;
    }

    :host([data-icon-only][data-size='sm']) {
      width: var(--rp-control-height-sm);
    }

    :host([data-icon-only][data-size='lg']) {
      width: var(--rp-control-height-lg);
    }

    :host([data-variant='primary']) {
      background: var(--rp-color-action);
      color: var(--rp-color-on-action);
    }

    :host([data-variant='primary']:hover) {
      background: var(--rp-color-action-hover);
    }

    :host([data-variant='primary']:active) {
      background: var(--rp-color-action-active);
    }

    :host([data-variant='secondary']) {
      background: var(--rp-color-secondary-bg);
      color: var(--rp-color-text-primary);
    }

    :host([data-variant='secondary']:hover) {
      background: var(--rp-color-secondary-bg-hover);
    }

    :host([data-variant='outline']) {
      border-color: var(--rp-color-action);
      background: transparent;
      color: var(--rp-color-primary-fg);
    }

    :host([data-variant='outline']:hover) {
      background: var(--rp-color-primary-50);
    }

    :host([data-variant='ghost']) {
      background: transparent;
      color: var(--rp-color-text-secondary);
    }

    :host([data-variant='ghost']:hover) {
      background: var(--rp-color-surface-hover);
      color: var(--rp-color-text-primary);
    }

    :host([data-variant='danger']) {
      background: var(--rp-color-danger-action);
      color: var(--rp-color-on-action);
    }

    :host([data-variant='danger']:hover) {
      background: var(--rp-color-danger-action-hover);
    }

    :host([data-variant='danger']:active) {
      background: var(--rp-color-danger-action-active);
    }

    :host(:focus-visible) {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    :host(:disabled),
    :host(:disabled:hover) {
      border-color: var(--rp-color-disabled-border);
      background: var(--rp-color-disabled-bg);
      color: var(--rp-color-disabled-fg);
      cursor: not-allowed;
    }

    :host([aria-busy='true']) {
      cursor: progress;
    }

    .ui-btn__spinner {
      width: 1em;
      height: 1em;
      border: 2px solid currentColor;
      border-right-color: transparent;
      border-radius: var(--rp-radius-pill);
      animation: ui-btn-spin 700ms linear infinite;
    }

    @keyframes ui-btn-spin {
      to {
        transform: rotate(360deg);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      :host {
        transition: none;
      }

      .ui-btn__spinner {
        animation-duration: 1800ms;
      }
    }
  `]
})
export class UiButtonComponent implements OnInit {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly variant = input<UiButtonVariant>('primary');
  readonly size = input<UiButtonSize>('md');
  readonly loading = input(false);
  /** Square button holding only an icon; it must carry an aria-label or title. */
  readonly iconOnly = input(false);
  readonly block = input(false);

  private ariaDisabledSetByLoading = false;

  constructor() {
    // Capture phase on the element itself runs before routerLink / (click) handlers added by consumers.
    const swallowWhileLoading = (event: Event): void => {
      if (this.loading()) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    };
    this.element.addEventListener('click', swallowWhileLoading, true);
    inject(DestroyRef).onDestroy(() => this.element.removeEventListener('click', swallowWhileLoading, true));

    effect(() => {
      if (this.loading()) {
        this.element.setAttribute('aria-disabled', 'true');
        this.ariaDisabledSetByLoading = true;
      } else if (this.ariaDisabledSetByLoading) {
        this.element.removeAttribute('aria-disabled');
        this.ariaDisabledSetByLoading = false;
      }
    });
  }

  ngOnInit(): void {
    if (this.element.tagName === 'BUTTON' && !this.element.hasAttribute('type')) {
      this.element.setAttribute('type', 'button');
    }

    if (isDevMode() && this.iconOnly() && !this.hasAccessibleName()) {
      console.warn('appUiButton: an icon-only button needs aria-label, aria-labelledby or title.', this.element);
    }
  }

  private hasAccessibleName(): boolean {
    return ['aria-label', 'aria-labelledby', 'title'].some(name => !!this.element.getAttribute(name));
  }
}
