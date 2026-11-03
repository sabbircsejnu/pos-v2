import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiStepDirective, UiStepperComponent } from './ui-stepper.component';

@Component({
  standalone: true,
  imports: [UiStepperComponent, UiStepDirective],
  template: `
    <app-ui-stepper
      [orientation]="orientation()"
      [canAdvance]="guard"
      [allowCompletedNavigation]="true"
      [allowFutureNavigation]="readOnly()"
      (advanceBlocked)="blocked.push($event)"
      (stepChange)="changes.push($event)">
      <ng-template appUiStep="Profile" description="Basic details" [completed]="true">
        <p>Profile fields</p>
      </ng-template>
      <ng-template appUiStep="Employment" description="Role and dates">
        <p>Employment fields</p>
      </ng-template>
      <ng-template appUiStep="Review" [error]="hasError()">
        <p>Review details</p>
      </ng-template>
    </app-ui-stepper>
  `
})
class StepperHost {
  orientation = signal<'horizontal' | 'vertical'>('horizontal');
  readOnly = signal(false);
  canMove = false;
  hasError = signal(false);
  blocked: number[] = [];
  changes: number[] = [];
  guard = (index: number): boolean => index === 0 ? this.canMove : true;
}

describe('UiStepperComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create() {
    TestBed.configureTestingModule({ imports: [StepperHost] });
    const fixture = TestBed.createComponent(StepperHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('renders numbered, described steps and keeps feature content in the active panel', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.ui-stepper')?.getAttribute('data-orientation')).toBe('horizontal');
    expect(root.querySelectorAll('.ui-stepper__item')).toHaveLength(3);
    expect(root.querySelector('.ui-stepper__header .ui-stepper__description')?.textContent).toContain('Basic details');
    expect(root.querySelector('[role="region"]')?.textContent).toContain('Profile fields');
    expect(root.querySelector('.ui-stepper__header')?.getAttribute('data-state')).toBe('active');
    expect(root.querySelector('[data-step-index="2"]')?.getAttribute('data-state')).toBe('pending');
  });

  it('blocks advancement when the feature guard reports an invalid step, then advances when allowed', async () => {
    const fixture = await create();
    const host = fixture.componentInstance;
    const next = () => fixture.nativeElement.querySelector('.ui-stepper__navigation button:last-child') as HTMLButtonElement;
    next().click();
    fixture.detectChanges();
    expect(host.blocked).toEqual([0]);
    expect(fixture.nativeElement.querySelector('[role="region"]')?.textContent).toContain('Profile fields');

    host.canMove = true;
    next().click();
    fixture.detectChanges();
    expect(host.changes).toEqual([1]);
    expect(fixture.nativeElement.querySelector('[role="region"]')?.textContent).toContain('Employment fields');
    expect(fixture.nativeElement.querySelector('[data-step-index="0"]')?.getAttribute('data-state')).toBe('completed');
  });

  it('supports previous and completed-step navigation and exposes error states accessibly', async () => {
    const fixture = await create();
    fixture.componentInstance.canMove = true;
    (fixture.nativeElement.querySelector('.ui-stepper__navigation button:last-child') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.ui-stepper__navigation button:first-child') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.changes).toEqual([1, 0]);

    fixture.componentInstance.hasError.set(true);
    fixture.detectChanges();
    const review = fixture.nativeElement.querySelector('[data-step-index="2"] .ui-stepper__header') as HTMLElement;
    expect(review.getAttribute('data-state')).toBe('error');
    expect(review.getAttribute('aria-invalid')).toBe('true');
  });

  it('supports vertical orientation and arrow-key navigation', async () => {
    const fixture = await create();
    const host = fixture.componentInstance;
    host.orientation.set('vertical');
    host.canMove = true;
    fixture.detectChanges();
    const first = fixture.nativeElement.querySelector('[data-step-index="0"] .ui-stepper__header') as HTMLButtonElement;
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ui-stepper')?.getAttribute('data-orientation')).toBe('vertical');
    expect(fixture.nativeElement.querySelector('[data-state="active"] > [role="region"]')?.textContent)
      .toContain('Employment fields');
  });

  it('renders the vertical active panel under its heading and supports unvalidated read-only jumps', async () => {
    const fixture = await create();
    const host = fixture.componentInstance;
    const root = fixture.nativeElement as HTMLElement;
    host.orientation.set('vertical');
    host.readOnly.set(true);
    fixture.detectChanges();
    const activeStep = root.querySelector('.ui-stepper__item[data-state="active"]')!;
    expect(activeStep.querySelector('.ui-stepper__header + [role="region"]')?.textContent).toContain('Profile fields');
    (root.querySelector('[data-step-index="2"] .ui-stepper__header') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelector('.ui-stepper__item[data-state="active"] .ui-stepper__header')?.textContent)
      .toContain('Review');
  });
});
