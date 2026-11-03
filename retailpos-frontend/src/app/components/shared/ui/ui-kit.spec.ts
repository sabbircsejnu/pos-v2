import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { UI_KIT } from './index';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, ...UI_KIT],
  template: `
    <app-ui-page-header title="New product" [breadcrumbs]="[{ label: 'Products', link: '/products' }, { label: 'New' }]">
      <button uiPagePrimaryActions appUiButton type="submit" form="product-form" [loading]="saving()">Save</button>
    </app-ui-page-header>

    <app-ui-card title="Details" [loading]="loading()">
      <form id="product-form" [formGroup]="form" class="ui-form-stack" (ngSubmit)="submit()">
        <app-ui-form-field label="Name" [required]="true">
          <input appUiControl formControlName="name" />
        </app-ui-form-field>
        <app-ui-badge variant="warning">Draft</app-ui-badge>
      </form>
    </app-ui-card>

    @if (failed()) {
      <app-ui-state kind="error" title="Could not save">
        <button appUiButton variant="outline" id="retry" (click)="retries = retries + 1">Retry</button>
      </app-ui-state>
    }
  `
})
class ScreenHostComponent {
  form = new FormBuilder().nonNullable.group({ name: ['', Validators.required] });
  loading = signal(false);
  saving = signal(false);
  failed = signal(false);
  submits = 0;
  retries = 0;
  submit(): void {
    this.submits++;
  }
}

describe('UI_KIT composition', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create() {
    TestBed.configureTestingModule({ imports: [ScreenHostComponent], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(ScreenHostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('exposes every shared component and directive from one import list', () => {
    expect(UI_KIT).toHaveLength(20);
  });

  it('composes header, card, form field, badge and state in one screen without conflicts', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('h1')!.textContent).toBe('New product');
    expect(root.querySelector('h2.ui-card__title')!.textContent).toBe('Details');
    expect(root.querySelector('app-ui-form-field input')!.classList).toContain('ui-control');
    expect(root.querySelector('app-ui-badge')!.textContent).toContain('Draft');
    expect(root.querySelector('app-ui-state')).toBeNull();
  });

  it('submits the form from a header button outside the form and blocks repeat submits while saving', async () => {
    const fixture = await create();
    const host = fixture.componentInstance;
    const save = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;

    save.click();
    expect(host.submits).toBe(1);

    host.saving.set(true);
    fixture.detectChanges();
    save.click();
    save.click();
    expect(host.submits).toBe(1);
  });

  it('keeps feature-owned retry behaviour inside the error state', async () => {
    const fixture = await create();
    fixture.componentInstance.failed.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const state = fixture.nativeElement.querySelector('app-ui-state') as HTMLElement;
    expect(state.getAttribute('role')).toBe('alert');
    (state.querySelector('#retry') as HTMLButtonElement).click();
    expect(fixture.componentInstance.retries).toBe(1);
  });
});
