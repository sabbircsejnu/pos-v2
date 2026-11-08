import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  computed,
  contentChildren,
  inject,
  input,
  model,
  output
} from '@angular/core';
import { UiButtonComponent } from './ui-button.component';

let nextStepperId = 0;

export type UiStepperOrientation = 'horizontal' | 'vertical';
export type UiStepState = 'active' | 'completed' | 'pending' | 'error';

@Component({
  selector: 'app-ui-stepper',
  standalone: true,
  imports: [NgTemplateOutlet, UiButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol
      class="ui-stepper"
      [attr.data-orientation]="orientation()"
      role="list"
      [attr.aria-label]="ariaLabel()"
      (keydown)="onHeaderKeydown($event)">
      @for (step of steps(); track step; let i = $index) {
        <li class="ui-stepper__item" role="listitem" [attr.data-step-index]="i" [attr.data-state]="stepState(i)">
          <button
            type="button"
            class="ui-stepper__header"
            [attr.data-state]="stepState(i)"
            [id]="headerId(i)"
            [attr.aria-current]="activeIndex() === i ? 'step' : null"
            [attr.aria-controls]="panelId(i)"
            [attr.aria-expanded]="activeIndex() === i"
            [attr.aria-invalid]="step.error() ? 'true' : null"
            [tabIndex]="activeIndex() === i ? 0 : -1"
            (click)="activate(i)">
            <span class="ui-stepper__marker" aria-hidden="true">
              @if (step.error()) { <i class="fas fa-exclamation" aria-hidden="true"></i> }
              @else if (stepState(i) === 'completed') { <i class="fas fa-check" aria-hidden="true"></i> }
              @else { {{ i + 1 }} }
            </span>
            <span class="ui-stepper__label">
              <span class="ui-stepper__title">{{ step.title() }}</span>
              @if (step.description()) { <span class="ui-stepper__description">{{ step.description() }}</span> }
              <span class="sr-only">{{ stateLabel(stepState(i)) }}</span>
            </span>
          </button>
          @if (orientation() === 'vertical' && activeIndex() === i) {
            <section
              class="ui-stepper__panel"
              [id]="panelId(i)"
              role="region"
              tabindex="0"
              [attr.aria-labelledby]="headerId(i)">
              <ng-container [ngTemplateOutlet]="step.template" />
            </section>
          }
        </li>
      }
    </ol>

    @if (orientation() === 'horizontal' && activeStep(); as step) {
      <section
        class="ui-stepper__panel"
        [id]="panelId(activeIndex())"
        role="region"
        tabindex="0"
        [attr.aria-labelledby]="headerId(activeIndex())">
        <ng-container [ngTemplateOutlet]="step.template" />
      </section>
    }

    @if (steps().length > 1) {
      <nav class="ui-stepper__navigation" aria-label="Step navigation">
        @if (activeIndex() > 0) {
          <button appUiButton variant="outline" type="button" (click)="previous()">Previous</button>
        }
        @if (activeIndex() < steps().length - 1) {
          <button appUiButton variant="primary" type="button" class="ui-stepper__next" [disabled]="nextDisabled()" (click)="next()">Next</button>
        }
      </nav>
    }
  `,
  styles: [`
    :host {
      display: block;
      min-width: 0;
    }

    .ui-stepper {
      display: flex;
      min-width: 0;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .ui-stepper[data-orientation='horizontal'] {
      flex-direction: row;
      align-items: stretch;
      border-bottom: 1px solid var(--rp-color-border);
    }

    .ui-stepper[data-orientation='vertical'] {
      flex-direction: column;
      gap: 0;
    }

    .ui-stepper__item {
      position: relative;
      min-width: 0;
    }

    .ui-stepper[data-orientation='horizontal'] .ui-stepper__item {
      display: flex;
      flex: 1 1 0;
    }

    .ui-stepper__header {
      position: relative;
      display: flex;
      flex: 1 1 0;
      align-items: center;
      gap: var(--rp-space-2);
      min-width: 0;
      padding: var(--rp-space-3);
      border: 0;
      border-bottom: var(--rp-focus-ring-width) solid transparent;
      background: transparent;
      color: var(--rp-color-text-secondary);
      text-align: start;
      cursor: pointer;
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__item {
      flex: none;
      width: 100%;
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__item:not(:last-child)::before {
      position: absolute;
      z-index: 0;
      top: calc(var(--rp-space-3) + var(--rp-control-height-sm));
      bottom: 0;
      left: calc(var(--rp-space-3) + var(--rp-space-4));
      width: 1px;
      background: var(--rp-color-border);
      content: '';
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__header {
      z-index: 1;
      flex: none;
      width: 100%;
      border: 1px solid transparent;
      border-radius: var(--rp-radius-md);
      background: transparent;
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__header[data-state='active'] {
      border-color: var(--rp-color-border);
      background: var(--rp-color-surface-muted);
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__header[data-state='completed'] .ui-stepper__marker {
      border-color: var(--rp-color-success-700);
      background: var(--rp-color-success-50);
      color: var(--rp-color-success-700);
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__item[data-state='completed']:not(:last-child)::before {
      background: var(--rp-color-success-700);
    }

    .ui-stepper__item[data-state='active'] > .ui-stepper__header {
      border-bottom-color: var(--rp-color-primary);
      color: var(--rp-color-text-primary);
    }

    .ui-stepper__item[data-state='completed'] .ui-stepper__marker {
      border-color: var(--rp-color-success-700);
      background: var(--rp-color-success-50);
      color: var(--rp-color-success-700);
    }

    .ui-stepper__item[data-state='error'] .ui-stepper__marker {
      border-color: var(--rp-color-danger-700);
      background: var(--rp-color-danger-50);
      color: var(--rp-color-danger-700);
    }

    .ui-stepper__marker {
      display: grid;
      flex: 0 0 auto;
      place-items: center;
      width: var(--rp-control-height-sm);
      height: var(--rp-control-height-sm);
      border: 1px solid var(--rp-color-border-control);
      border-radius: var(--rp-radius-pill);
      background: var(--rp-color-surface-card);
      color: var(--rp-color-text-secondary);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ui-stepper__label {
      display: grid;
      gap: var(--rp-space-1);
      min-width: 0;
    }

    .ui-stepper__title {
      color: inherit;
      font-size: var(--rp-text-label-size);
      font-weight: var(--rp-font-weight-medium);
    }

    .ui-stepper__description {
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
    }

    .ui-stepper__header:focus-visible,
    .ui-stepper__panel:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .ui-stepper__panel {
      padding-block: var(--rp-space-4);
    }

    .ui-stepper[data-orientation='vertical'] .ui-stepper__panel {
      margin-left: calc(var(--rp-space-3) + var(--rp-control-height-sm) + var(--rp-space-2));
      padding: var(--rp-space-3) 0 var(--rp-space-6);
    }

    .ui-stepper__navigation {
      display: flex;
      justify-content: space-between;
      gap: var(--rp-space-2);
    }

    /* Next keeps the trailing edge on every step, including the first one (no Previous). */
    .ui-stepper__next {
      margin-inline-start: auto;
    }

    @media (max-width: 48rem) {
      .ui-stepper[data-orientation='horizontal'] {
        flex-direction: column;
        border: 0;
        gap: var(--rp-space-1);
      }

      .ui-stepper[data-orientation='horizontal'] .ui-stepper__header {
        flex: none;
        width: 100%;
        border: 1px solid var(--rp-color-border);
        border-radius: var(--rp-radius-card);
      }
    }
  `]
})
export class UiStepperComponent {
  private readonly id = ++nextStepperId;
  readonly steps = contentChildren(UiStepDirective);
  readonly orientation = input<UiStepperOrientation>('horizontal');
  readonly ariaLabel = input('Progress');
  readonly allowCompletedNavigation = input(false);
  readonly allowFutureNavigation = input(false);
  readonly canAdvance = input<((currentIndex: number) => boolean) | null>(null);
  /** Optional: disables Next while the feature knows the current step is incomplete (the guard still runs on Next). */
  readonly nextDisabled = input(false);
  readonly activeIndex = model(0);
  readonly stepChange = output<number>();
  readonly advanceBlocked = output<number>();
  readonly activeStep = computed(() => this.steps()[this.activeIndex()] ?? null);

  headerId(index: number): string {
    return `ui-stepper-${this.id}-tab-${index}`;
  }

  panelId(index: number): string {
    return `ui-stepper-${this.id}-panel-${index}`;
  }

  stepState(index: number): UiStepState {
    const step = this.steps()[index];
    if (step?.error()) {
      return 'error';
    }
    if (index === this.activeIndex()) {
      return 'active';
    }
    if (step?.completed() || index < this.activeIndex()) {
      return 'completed';
    }
    return 'pending';
  }

  stateLabel(state: UiStepState): string {
    return state === 'active' ? 'Current step' : state === 'completed' ? 'Completed' :
      state === 'error' ? 'Has errors' : 'Pending';
  }

  next(): void {
    const current = this.activeIndex();
    if (current >= this.steps().length - 1 || this.nextDisabled()) {
      return;
    }
    const guard = this.canAdvance();
    if (guard && !guard(current)) {
      this.advanceBlocked.emit(current);
      return;
    }
    this.setActive(current + 1);
  }

  previous(): void {
    this.setActive(this.activeIndex() - 1);
  }

  activate(index: number): void {
    const current = this.activeIndex();
    if (index === current || index < 0 || index >= this.steps().length) {
      return;
    }
    if (this.allowFutureNavigation()) {
      this.setActive(index);
      return;
    }
    if (index === current + 1) {
      this.next();
      return;
    }
    if (index < current && this.allowCompletedNavigation()) {
      this.setActive(index);
    }
  }

  onHeaderKeydown(event: KeyboardEvent): void {
    const current = Number((event.target as HTMLElement).closest<HTMLElement>('[data-step-index]')?.dataset['stepIndex']);
    if (!Number.isInteger(current)) {
      return;
    }
    const horizontal = this.orientation() === 'horizontal';
    const forward = horizontal ? 'ArrowRight' : 'ArrowDown';
    const backward = horizontal ? 'ArrowLeft' : 'ArrowUp';
    let target: number | null = null;
    if (event.key === forward) {
      target = Math.min(current + 1, this.steps().length - 1);
    } else if (event.key === backward) {
      target = Math.max(current - 1, 0);
    } else if (event.key === 'Home') {
      target = 0;
    } else if (event.key === 'End') {
      target = this.steps().length - 1;
    }
    if (target !== null) {
      event.preventDefault();
      this.activate(target);
      document.getElementById(this.headerId(this.activeIndex()))?.focus();
    }
  }

  private setActive(index: number): void {
    const next = Math.max(0, Math.min(index, this.steps().length - 1));
    if (next !== this.activeIndex()) {
      this.activeIndex.set(next);
      this.stepChange.emit(next);
    }
  }
}

@Directive({ selector: 'ng-template[appUiStep]', standalone: true })
export class UiStepDirective {
  readonly title = input.required<string>({ alias: 'appUiStep' });
  readonly description = input('');
  readonly completed = input(false);
  readonly error = input(false);
  readonly template = inject(TemplateRef<void>);
}
