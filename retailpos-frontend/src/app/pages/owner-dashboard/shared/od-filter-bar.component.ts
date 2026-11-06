import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { merge } from 'rxjs';
import { CategoryOption, DashboardLocation } from '../../../models/owner-dashboard.model';
import { UI_KIT } from '../../../components/shared/ui';
import { AppliedFilter, DATE_PRESETS, DatePreset, resolvePreset, validateRange } from './owner-dashboard.logic';

/**
 * Screen filters with a draft and a committed state: typing changes nothing until Apply, so no request fires on a
 * half-entered date. The Business is not a filter: it is the server's authenticated context.
 */
@Component({
  selector: 'app-od-filter-bar',
  standalone: true,
  imports: [ReactiveFormsModule, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: block; }
    .od-filters { display: grid; gap: var(--rp-form-gap); align-items: start; grid-template-columns: minmax(0, 1fr); }
    .od-filters__actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); align-self: end; }
    @media (min-width: 640px) { .od-filters { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (min-width: 1024px) { .od-filters { grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); } }
  `],
  template: `
    <app-ui-card>
      <form class="od-filters" [formGroup]="form" (ngSubmit)="apply()" [attr.aria-label]="ariaLabel()">
        <app-ui-form-field [label]="locationLabel()">
          <select appUiControl formControlName="location">
            <option value="">{{ allLabel() }}</option>
            @if (warehouses().length) {
              <optgroup label="Showrooms">
                @for (location of showrooms(); track location.id) {
                  <option [value]="'outlet:' + location.id">{{ location.name }}</option>
                }
              </optgroup>
              <optgroup label="Warehouses">
                @for (location of warehouses(); track location.id) {
                  <option [value]="'warehouse:' + location.id">{{ location.name }}</option>
                }
              </optgroup>
            } @else {
              @for (location of showrooms(); track location.id) {
                <option [value]="'outlet:' + location.id">{{ location.name }}</option>
              }
            }
          </select>
        </app-ui-form-field>
        @if (categories().length) {
          <app-ui-form-field label="Category">
            <select appUiControl formControlName="categoryId">
              <option value="">All categories</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </app-ui-form-field>
        }
        <app-ui-form-field label="Period">
          <select appUiControl formControlName="preset" (change)="onPresetChange()">
            @for (preset of presets; track preset.value) {
              <option [value]="preset.value">{{ preset.label }}</option>
            }
          </select>
        </app-ui-form-field>
        <app-ui-date-picker label="From" formControlName="from" [required]="true" />
        <app-ui-date-picker label="To" formControlName="to" [required]="true" />
        <div class="od-filters__actions">
          <button appUiButton type="submit">Apply</button>
          <button appUiButton type="button" variant="outline" (click)="resetRequested.emit()">Reset</button>
        </div>
      </form>
      @if (error()) {
        <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
      }
    </app-ui-card>
  `
})
export class OdFilterBarComponent {
  readonly ariaLabel = input('Dashboard filters');
  readonly locationLabel = input('Showroom');
  readonly allLabel = input('All showrooms');
  readonly showrooms = input<readonly DashboardLocation[]>([]);
  readonly warehouses = input<readonly DashboardLocation[]>([]);
  readonly categories = input<readonly CategoryOption[]>([]);
  readonly today = input.required<string>();
  readonly applied = input.required<AppliedFilter>();

  readonly applyRequested = output<AppliedFilter>();
  readonly resetRequested = output<void>();

  readonly presets = DATE_PRESETS;
  readonly error = signal<string | null>(null);

  readonly form = inject(FormBuilder).nonNullable.group({
    location: '',
    categoryId: '',
    preset: 'today' as DatePreset,
    from: '',
    to: ''
  });

  constructor() {
    // The committed filter can also change from outside: Reset, or a click on a showroom bar.
    effect(() => {
      const filter = this.applied();
      this.form.setValue({
        location: filter.location,
        categoryId: filter.categoryId === null ? '' : String(filter.categoryId),
        preset: filter.preset,
        from: filter.from,
        to: filter.to
      }, { emitEvent: false });
      this.error.set(null);
    });

    // Editing a date by hand makes the range custom. Programmatic updates above and below emit no event.
    merge(this.form.controls.from.valueChanges, this.form.controls.to.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.preset.setValue('custom', { emitEvent: false }));
  }

  onPresetChange(): void {
    const preset = this.form.controls.preset.value;
    if (preset !== 'custom') {
      this.form.patchValue(resolvePreset(preset, this.today()), { emitEvent: false });
      this.error.set(null);
    }
  }

  apply(): void {
    const value = this.form.getRawValue();
    const problem = validateRange(value.from, value.to);
    this.error.set(problem);
    if (problem) {
      return;
    }

    this.applyRequested.emit({
      preset: value.preset,
      location: value.location,
      categoryId: value.categoryId ? Number(value.categoryId) : null,
      from: value.from,
      to: value.to
    });
  }
}
