import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { UiControlDirective } from './ui-control.directive';
import { UiFormFieldComponent } from './ui-form-field.component';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, UiFormFieldComponent, UiControlDirective],
  template: `
    <form [formGroup]="form">
      <app-ui-form-field label="Email" hint="We never share it" [required]="true" [messages]="messages()" [error]="serverError()">
        <input appUiControl type="email" formControlName="email" />
      </app-ui-form-field>
      <app-ui-form-field label="Name">
        <input appUiControl formControlName="name" />
      </app-ui-form-field>
      <app-ui-form-field label="Notes">
        <textarea appUiControl formControlName="notes" readonly></textarea>
      </app-ui-form-field>
      <app-ui-form-field label="Kind">
        <select appUiControl formControlName="kind"><option value="a">A</option><option value="b">B</option></select>
      </app-ui-form-field>
      <app-ui-form-field
        label="Amount"
        appearance="filled"
        size="compact"
        [clearable]="true"
        [counter]="true"
        [maxLength]="8">
        <span uiFieldPrefix>$</span>
        <input appUiControl formControlName="amount" maxlength="8" />
        <span uiFieldSuffix>.00</span>
      </app-ui-form-field>
      <label class="ui-check"><input appUiControl type="checkbox" formControlName="agree" /> <span>Agree</span></label>
      <label class="ui-check"><input appUiControl type="radio" value="x" formControlName="choice" /> <span>X</span></label>
    </form>
  `
})
class ReactiveHostComponent {
  form = new FormBuilder().nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    name: ['', [Validators.minLength(3)]],
    notes: [''],
    kind: ['a'],
    amount: [''],
    agree: [false],
    choice: ['']
  });
  messages = signal<Record<string, string>>({});
  serverError = signal('');
}

@Component({
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent, UiControlDirective],
  template: `
    <app-ui-form-field label="Code" [required]="true">
      <input appUiControl name="code" [(ngModel)]="code" required />
    </app-ui-form-field>
    <input appUiControl id="standalone" aria-describedby="authored" />
  `
})
class TemplateHostComponent {
  code = '';
}

describe('appUiControl and app-ui-form-field with Reactive Forms', () => {
  let fixture: ComponentFixture<ReactiveHostComponent>;
  let host: ReactiveHostComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const input = (name: string) => root().querySelector(`[formcontrolname="${name}"]`) as HTMLInputElement;
  const field = (index: number) => root().querySelectorAll('app-ui-form-field')[index] as HTMLElement;
  const errorText = (index: number) => field(index).querySelector('.ui-field__error')!.textContent!.trim();
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const type = async (element: HTMLInputElement, value: string) => {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle();
  };
  const blur = async (element: HTMLElement) => {
    element.dispatchEvent(new Event('blur'));
    await settle();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [ReactiveHostComponent] });
    fixture = TestBed.createComponent(ReactiveHostComponent);
    host = fixture.componentInstance;
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('styles text-like controls and checkable controls differently', () => {
    expect(input('email').classList).toContain('ui-control');
    expect(input('notes').classList).toContain('ui-control');
    expect(input('kind').classList).toContain('ui-control');
    expect(input('agree').classList).toContain('ui-check__input');
    expect(input('agree').classList).not.toContain('ui-control');
    expect(input('choice').classList).toContain('ui-check__input');
  });

  it('associates each label with its control through a generated id', () => {
    const label = field(0).querySelector('label')!;
    expect(label.getAttribute('for')).toBe(input('email').id);
    expect(input('email').id).toMatch(/^ui-control-\d+$/);
    expect(new Set([input('email').id, input('name').id, input('kind').id]).size).toBe(3);
  });

  it('marks required fields visibly and for assistive technology', () => {
    expect(field(0).querySelector('.ui-field__required')!.getAttribute('aria-hidden')).toBe('true');
    expect(field(0).querySelector('.sr-only')!.textContent).toContain('required');
    expect(input('email').getAttribute('aria-required')).toBe('true');
    expect(input('name').hasAttribute('aria-required')).toBe(false);
  });

  it('links the hint and the error region with aria-describedby', () => {
    const describedBy = input('email').getAttribute('aria-describedby')!.split(' ');
    expect(describedBy).toHaveLength(2);
    expect(root().querySelector(`#${describedBy[0]}`)!.textContent).toContain('We never share it');
    expect(root().querySelector(`#${describedBy[1]}`)!.getAttribute('aria-live')).toBe('polite');
  });

  it('does not flag an untouched pristine invalid control', () => {
    expect(host.form.controls.email.invalid).toBe(true);
    expect(errorText(0)).toBe('');
    expect(input('email').hasAttribute('aria-invalid')).toBe(false);
    expect(input('email').classList).not.toContain('ui-control--invalid');
  });

  it('shows the required message after blur and clears it once the value is valid', async () => {
    await blur(input('email'));
    expect(errorText(0)).toContain('This field is required.');
    expect(input('email').getAttribute('aria-invalid')).toBe('true');
    expect(input('email').classList).toContain('ui-control--invalid');

    await type(input('email'), 'a@b.test');
    expect(errorText(0)).toBe('');
    expect(input('email').hasAttribute('aria-invalid')).toBe(false);
  });

  it('shows validator specific messages once the user has edited the control', async () => {
    await type(input('email'), 'nope');
    expect(errorText(0)).toContain('Enter a valid email address.');

    await type(input('name'), 'ab');
    expect(errorText(1)).toContain('Enter at least 3 characters.');
  });

  it('lets the feature override wording per validator key', async () => {
    host.messages.set({ required: 'Enter your email' });
    await blur(input('email'));
    expect(errorText(0)).toContain('Enter your email');
  });

  it('shows a server supplied error ahead of validator messages', async () => {
    host.serverError.set('Email already registered');
    await settle();
    expect(errorText(0)).toContain('Email already registered');
  });

  it('flags the control as invalid for assistive technology while an explicit error is shown, and clears it again', async () => {
    expect(input('email').getAttribute('aria-invalid')).toBeNull();
    host.serverError.set('Email already registered');
    await settle();
    expect(input('email').getAttribute('aria-invalid')).toBe('true');
    expect(input('email').classList).toContain('ui-control--invalid');
    host.serverError.set('');
    await settle();
    expect(input('email').getAttribute('aria-invalid')).toBeNull();
  });

  it('shows string errors set by the feature, such as API validation results', async () => {
    host.form.controls.name.setErrors({ taken: 'Name already used' });
    host.form.controls.name.markAsTouched();
    await settle();
    expect(errorText(1)).toContain('Name already used');
  });

  it('keeps the form value, dirty state and disabled state bound to the native control', async () => {
    await type(input('name'), 'Alice');
    expect(host.form.controls.name.value).toBe('Alice');
    expect(host.form.controls.name.dirty).toBe(true);

    host.form.controls.name.setValue('Bob');
    await settle();
    expect(input('name').value).toBe('Bob');

    host.form.controls.kind.disable();
    await settle();
    expect(input('kind').disabled).toBe(true);
    host.form.controls.kind.enable();
    await settle();
    expect(input('kind').disabled).toBe(false);
  });

  it('supports filled compact fields with prefix, suffix, clear action and character count', async () => {
    const field = root().querySelectorAll('app-ui-form-field')[4] as HTMLElement;
    const amount = field.querySelector<HTMLInputElement>('input')!;
    expect(field.querySelector('.ui-field')?.getAttribute('data-appearance')).toBe('filled');
    expect(field.querySelector('.ui-field')?.getAttribute('data-size')).toBe('compact');
    expect(field.textContent).toContain('$');
    expect(field.textContent).toContain('.00');
    expect(field.querySelector('.ui-field__counter')?.textContent).toContain('0 / 8');
    expect(field.querySelector('.ui-field__clear')).toBeNull();

    amount.value = '123';
    amount.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(host.form.controls.amount.value).toBe('123');
    expect(field.querySelector('.ui-field__counter')?.textContent).toContain('3 / 8');
    const clear = field.querySelector<HTMLButtonElement>('.ui-field__clear')!;
    expect(clear.getAttribute('aria-label')).toBe('Clear Amount');
    clear.click();
    await settle();
    expect(host.form.controls.amount.value).toBe('');
    expect(amount.value).toBe('');
  });

  it('keeps checkbox and radio bindings and preserves the readonly attribute', async () => {
    input('agree').click();
    await settle();
    expect(host.form.controls.agree.value).toBe(true);

    input('choice').click();
    await settle();
    expect(host.form.controls.choice.value).toBe('x');
    expect(input('notes').hasAttribute('readonly')).toBe(true);
  });

  it('leaves form validity to the validators, not the presentation layer', async () => {
    host.form.markAllAsTouched();
    await settle();
    expect(host.form.valid).toBe(false);
    expect(errorText(0)).toContain('This field is required.');
    await type(input('email'), 'ok@example.test');
    expect(host.form.valid).toBe(true);
  });
});

describe('appUiControl with ngModel and outside a field', () => {
  let fixture: ComponentFixture<TemplateHostComponent>;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [TemplateHostComponent] });
    fixture = TestBed.createComponent(TemplateHostComponent);
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('works with template-driven forms and validates the native required attribute', async () => {
    const code = fixture.nativeElement.querySelector('[name="code"]') as HTMLInputElement;
    code.dispatchEvent(new Event('blur'));
    await settle();
    expect(fixture.nativeElement.querySelector('.ui-field__error').textContent).toContain('This field is required.');

    code.value = 'A1';
    code.dispatchEvent(new Event('input'));
    await settle();
    expect(fixture.componentInstance.code).toBe('A1');
    expect(fixture.nativeElement.querySelector('.ui-field__error').textContent.trim()).toBe('');
  });

  it('keeps authored aria-describedby and ids on a control used without a field', () => {
    const standalone = fixture.nativeElement.querySelector('#standalone') as HTMLInputElement;
    expect(standalone.id).toBe('standalone');
    expect(standalone.getAttribute('aria-describedby')).toBe('authored');
    expect(standalone.classList).toContain('ui-control');
  });
});
