import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, OnDestroy, afterNextRender, inject, input } from '@angular/core';

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'area[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  '[contenteditable="true"]'
].join(',');

/** Traps are stacked so only the top-most (for example a confirm dialog over a drawer) enforces focus. */
const activeTraps: UiFocusTrapDirective[] = [];

/**
 * Keeps keyboard focus inside a modal surface and puts it back where it came from when the surface
 * goes away: `<div appUiFocusTrap role="dialog" aria-modal="true">...</div>`.
 *
 * - On creation focus moves to `[data-autofocus]`, else the first focusable element, else the host.
 * - Tab / Shift+Tab wrap around; focus that escapes the host is pulled back.
 * - On destroy focus returns to the element that was focused when the trap was created.
 */
@Directive({
  selector: '[appUiFocusTrap]',
  standalone: true,
  host: { '(keydown)': 'onKeydown($event)' }
})
export class UiFocusTrapDirective implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly document = inject(DOCUMENT);
  private readonly previouslyFocused = this.document.activeElement as HTMLElement | null;

  readonly restoreFocus = input(true);

  private readonly onFocusIn = (event: FocusEvent): void => {
    if (activeTraps[activeTraps.length - 1] !== this) {
      return;
    }
    if (!this.host.contains(event.target as Node)) {
      this.focusFirst();
    }
  };

  constructor() {
    activeTraps.push(this);
    this.document.addEventListener('focusin', this.onFocusIn);
    afterNextRender(() => this.focusInitial());
  }

  ngOnDestroy(): void {
    this.document.removeEventListener('focusin', this.onFocusIn);
    const index = activeTraps.indexOf(this);
    if (index >= 0) {
      activeTraps.splice(index, 1);
    }
    if (this.restoreFocus() && this.previouslyFocused?.isConnected) {
      this.previouslyFocused.focus();
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab') {
      return;
    }

    const focusable = this.focusableElements();
    if (focusable.length === 0) {
      event.preventDefault();
      this.focusHost();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = this.document.activeElement;

    if (!this.host.contains(active)) {
      event.preventDefault();
      first.focus();
    } else if (event.shiftKey && (active === first || active === this.host)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private focusInitial(): void {
    const preferred = this.host.querySelector<HTMLElement>('[data-autofocus]');
    if (preferred && this.isFocusable(preferred)) {
      preferred.focus();
      return;
    }
    // Skip controls marked data-focus-skip (such as a header close button) when something else can take focus.
    const candidates = this.focusableElements();
    const target = candidates.find(element => !element.hasAttribute('data-focus-skip')) ?? candidates[0];
    if (target) {
      target.focus();
    } else {
      this.focusHost();
    }
  }

  private focusFirst(): void {
    const first = this.focusableElements()[0];
    if (first) {
      first.focus();
    } else {
      this.focusHost();
    }
  }

  private focusHost(): void {
    if (!this.host.hasAttribute('tabindex')) {
      this.host.setAttribute('tabindex', '-1');
    }
    this.host.focus();
  }

  private focusableElements(): HTMLElement[] {
    return Array.from(this.host.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(element => this.isFocusable(element));
  }

  private isFocusable(element: HTMLElement): boolean {
    if (element.hidden || element.hasAttribute('disabled') || element.getAttribute('aria-hidden') === 'true') {
      return false;
    }
    const style = this.document.defaultView?.getComputedStyle(element);
    return !style || (style.display !== 'none' && style.visibility !== 'hidden');
  }
}
