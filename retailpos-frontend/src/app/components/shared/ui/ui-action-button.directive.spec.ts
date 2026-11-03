import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiActionButtonDirective, UiActionButtonKind } from './ui-action-button.directive';

@Component({
  standalone: true,
  imports: [UiActionButtonDirective],
  template: `
    <button appUiActionButton [action]="kind()" [disabled]="disabled()" (click)="clicks = clicks + 1">
      <i class="fas fa-eye" aria-hidden="true"></i>
    </button>
    <button appUiActionButton action="delete" aria-label="Remove selected item" title="Remove selected item">
      <i class="fas fa-trash" aria-hidden="true"></i>
    </button>
  `
})
class ActionButtonHost {
  kind = signal<UiActionButtonKind>('view');
  disabled = signal(false);
  clicks = 0;
}

describe('UiActionButtonDirective', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create() {
    TestBed.configureTestingModule({ imports: [ActionButtonHost] });
    const fixture = TestBed.createComponent(ActionButtonHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('provides a compact semantic action class, default name and tooltip without taking over clicks', async () => {
    const fixture = await create();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.classList).toContain('ui-action-btn');
    expect(button.dataset['action']).toBe('view');
    expect(button.type).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('View');
    expect(button.title).toBe('View');
    button.click();
    expect(fixture.componentInstance.clicks).toBe(1);
  });

  it('updates semantic defaults while preserving explicitly authored accessible names', async () => {
    const fixture = await create();
    fixture.componentInstance.kind.set('edit');
    fixture.detectChanges();
    const [viewEdit, deleteButton] = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    expect(viewEdit.dataset['action']).toBe('edit');
    expect(viewEdit.getAttribute('aria-label')).toBe('Edit');
    expect(viewEdit.title).toBe('Edit');
    expect(deleteButton.getAttribute('aria-label')).toBe('Remove selected item');
    expect(deleteButton.title).toBe('Remove selected item');
  });

  it('leaves disabled and permission visibility to the feature-owned native button', async () => {
    const fixture = await create();
    fixture.componentInstance.disabled.set(true);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();
    expect(button.disabled).toBe(true);
    expect(fixture.componentInstance.clicks).toBe(0);
  });
});
