import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, finalize, switchMap, tap } from 'rxjs';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../components/shared/ui';
import { SaveUomUnit, UomCatalog, UomConversion, UomDimension, UomUnit } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { UomService } from '../../services/uom.service';
import { DIMENSIONS, DIMENSION_LABELS, MAX_UOM_PRECISION, readUomError } from './uom-ui';

export interface UnitRow extends UomUnit {
  dimensionLabel: string;
  precisionLabel: string;
  conversionText: string;
  scope: 'global' | 'business';
  status: 'active' | 'inactive';
}

/** `1 L = 1,000 ml`, always from the larger unit. For display only: the server applies the stored factors. */
export function formatConversion(conversion: UomConversion): string {
  const { fromSymbol, toSymbol, numerator, denominator } = conversion;
  const [large, small, factor] = numerator >= denominator
    ? [fromSymbol, toSymbol, numerator / denominator]
    : [toSymbol, fromSymbol, denominator / numerator];
  return `1 ${large} = ${factor.toLocaleString('en-US', { maximumFractionDigits: 6 })} ${small}`;
}

export function toUnitRows(catalog: UomCatalog): UnitRow[] {
  return catalog.units.map(unit => {
    const texts = catalog.conversions
      .filter(conversion => conversion.fromUnitId === unit.id || conversion.toUnitId === unit.id)
      .map(formatConversion);
    return {
      ...unit,
      dimensionLabel: DIMENSION_LABELS[unit.dimension] ?? unit.dimension,
      precisionLabel: unit.isWholeUnit ? 'Whole numbers' : `${unit.quantityPrecision} decimal places`,
      conversionText: unit.dimension === 'packaging' ? 'Size set per product' : [...new Set(texts)].join(', '),
      scope: unit.isGlobal ? 'global' : 'business',
      status: unit.isActive ? 'active' : 'inactive',
    };
  });
}

function wholeUnitPrecision(group: AbstractControl): ValidationErrors | null {
  const { isWholeUnit, quantityPrecision } = (group as FormGroup).getRawValue() as { isWholeUnit: boolean; quantityPrecision: number };
  return isWholeUnit && Number(quantityPrecision) !== 0 ? { wholeUnitPrecision: true } : null;
}

@Component({
  selector: 'app-uom-units-page',
  standalone: true,
  imports: [ReactiveFormsModule, ...UI_KIT],
  template: `
    <main class="uu-page">
      <app-ui-page-header
        title="Units of Measurement"
        description="The units items can be bought, stocked and sold in. System units are shared and read-only. A pack size such as a jar is set on each item."
        [breadcrumbs]="breadcrumbs">
        @if (canManage()) {
          <button uiPagePrimaryActions appUiButton type="button" (click)="openCreate()">
            <i class="fas fa-plus" aria-hidden="true"></i> Add unit
          </button>
        }
      </app-ui-page-header>

      @if (notice()) {
        <app-ui-alert class="uu-alert" variant="success" [dismissible]="true" (dismissed)="notice.set('')">{{ notice() }}</app-ui-alert>
      }

      @if (loading() && rows().length === 0) {
        <app-ui-state kind="loading" title="Loading units..." />
      } @else if (loadError()) {
        <app-ui-state kind="error" title="Could not load units" [message]="loadError()">
          <button appUiButton type="button" variant="outline" (click)="reload()">Retry</button>
        </app-ui-state>
      } @else {
        <app-ui-data-table
          ariaLabel="Units of measurement"
          density="compact"
          rowKey="id"
          [columns]="columns"
          [rows]="rows()"
          emptyTitle="No units found"
          searchLabel="Search units"
          searchPlaceholder="Search code, name or symbol..."
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            @if (row.isGlobal) {
              <span class="uu-readonly">Read-only</span>
            } @else if (canManage()) {
              <div class="uu-actions">
                <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit unit ' + row.code" (click)="openEdit(row)">Edit</button>
                <button appUiButton type="button" variant="ghost" size="sm"
                  [attr.aria-label]="(row.isActive ? 'Deactivate unit ' : 'Reactivate unit ') + row.code"
                  (click)="askToggle(row)">{{ row.isActive ? 'Deactivate' : 'Reactivate' }}</button>
              </div>
            }
          </ng-template>
        </app-ui-data-table>
      }

      <app-ui-dialog [(open)]="dialogOpen" [title]="editing() ? 'Edit unit' : 'Add unit'" [dismissible]="!saving()">
        <form id="uom-unit-form" class="ui-form-stack" [formGroup]="form" (ngSubmit)="save()">
          @if (saveError()) {
            <app-ui-alert variant="danger">{{ saveError() }}</app-ui-alert>
          }
          @if (editing()?.isInUse) {
            <app-ui-alert variant="info">This unit is in use, so its code and dimension cannot change and its precision cannot be reduced. Deactivate it to stop further use.</app-ui-alert>
          }
          <div class="uu-grid">
            <app-ui-form-field label="Code" [required]="true" hint="Up to 20 characters">
              <input appUiControl formControlName="code" maxlength="20" data-autofocus />
            </app-ui-form-field>
            <app-ui-form-field label="Symbol" [required]="true" hint="Shown next to quantities">
              <input appUiControl formControlName="symbol" maxlength="20" />
            </app-ui-form-field>
          </div>
          <app-ui-form-field label="Name" [required]="true">
            <input appUiControl formControlName="name" maxlength="100" />
          </app-ui-form-field>
          <app-ui-form-field label="Dimension" [required]="true" hint="Use Packaging for names such as jar or box. Their size is set on each item.">
            <select appUiControl formControlName="dimension">
              @for (dimension of dimensions; track dimension) {
                <option [value]="dimension">{{ dimensionLabels[dimension] }}</option>
              }
            </select>
          </app-ui-form-field>
          <app-ui-form-field label="Quantity precision" [required]="true" [hint]="'Decimal places allowed, 0 to ' + maxPrecision"
            [messages]="{ min: minPrecisionMessage() }">
            <input appUiControl type="number" step="1" min="0" [max]="maxPrecision" formControlName="quantityPrecision" />
          </app-ui-form-field>
          <label class="ui-check">
            <input appUiControl type="checkbox" formControlName="isWholeUnit" />
            <span>Whole numbers only</span>
          </label>
          @if (form.hasError('wholeUnitPrecision')) {
            <p class="uu-error" role="alert">A whole-number unit must have a precision of 0.</p>
          }
        </form>
        <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="saving()" (click)="dialogOpen.set(false)">Cancel</button>
        <button uiDialogFooter appUiButton type="submit" form="uom-unit-form" [loading]="saving()">Save unit</button>
      </app-ui-dialog>

      <app-ui-confirm-dialog
        [(open)]="confirmOpen"
        [title]="toggleTarget()?.isActive ? 'Deactivate unit?' : 'Reactivate unit?'"
        [confirmLabel]="toggleTarget()?.isActive ? 'Deactivate' : 'Reactivate'"
        [destructive]="!!toggleTarget()?.isActive"
        [busy]="saving()"
        (confirmed)="confirmToggle()">
        @if (toggleTarget()?.isActive) {
          {{ toggleTarget()?.name }} will no longer be offered for new setups. Existing items and past transactions keep it.
        } @else {
          {{ toggleTarget()?.name }} will be offered for new setups again.
        }
        @if (saveError()) {
          <app-ui-alert class="uu-alert-top" variant="danger">{{ saveError() }}</app-ui-alert>
        }
      </app-ui-confirm-dialog>
    </main>
  `,
  styles: [`
    .uu-page { padding: var(--rp-space-3) var(--rp-space-4); }
    .uu-alert { display: block; margin-bottom: var(--rp-space-4); }
    .uu-alert-top { display: block; margin-top: var(--rp-space-3); }
    .uu-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .uu-actions { display: inline-flex; flex-wrap: wrap; gap: var(--rp-space-1); }
    .uu-readonly { color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    .uu-error { margin: 0; color: var(--rp-color-status-danger-fg); font-size: var(--rp-text-helper-size); }
    @media (min-width: 640px) { .uu-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
})
export class UomUnitsPageComponent implements OnInit {
  private readonly api = inject(UomService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload$ = new Subject<void>();

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Catalog' }, { label: 'Units of Measurement' }];
  readonly dimensions = DIMENSIONS;
  readonly dimensionLabels = DIMENSION_LABELS;
  readonly maxPrecision = MAX_UOM_PRECISION;

  readonly columns: UiTableColumn[] = [
    { key: 'code', header: 'Code', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'symbol', header: 'Symbol' },
    {
      key: 'dimensionLabel', header: 'Dimension', sortable: true,
      filter: { type: 'select', options: DIMENSIONS.map(dimension => ({ value: DIMENSION_LABELS[dimension], label: DIMENSION_LABELS[dimension] })) },
    },
    { key: 'precisionLabel', header: 'Precision' },
    { key: 'conversionText', header: 'Standard conversions', emptyText: 'None' },
    {
      key: 'scope', header: 'Scope', type: 'status',
      statusMap: { global: { label: 'System', variant: 'info' }, business: { label: 'Custom', variant: 'neutral' } },
    },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: { active: { label: 'Active', variant: 'success' }, inactive: { label: 'Inactive', variant: 'neutral' } },
    },
  ];

  readonly rows = signal<UnitRow[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly notice = signal('');
  readonly dialogOpen = signal(false);
  readonly confirmOpen = signal(false);
  readonly editing = signal<UnitRow | null>(null);
  readonly toggleTarget = signal<UnitRow | null>(null);
  readonly canManage = computed(() => this.auth.hasPermission('uom.manage'));
  readonly minPrecisionMessage = computed(() => {
    const unit = this.editing();
    return unit?.isInUse ? `This unit is in use, so its precision cannot go below ${unit.quantityPrecision}.` : 'Value must be at least 0.';
  });

  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(20)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    symbol: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(20)] }),
    dimension: new FormControl<UomDimension>('count', { nonNullable: true, validators: [Validators.required] }),
    quantityPrecision: new FormControl(0, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0), Validators.max(MAX_UOM_PRECISION), Validators.pattern(/^\d+$/)],
    }),
    isWholeUnit: new FormControl(false, { nonNullable: true }),
  }, { validators: wholeUnitPrecision });

  constructor() {
    // switchMap drops a slower earlier response, so a stale catalog never replaces a newer one.
    this.reload$.pipe(
      switchMap(() => {
        this.loading.set(true);
        this.loadError.set('');
        return this.api.getCatalog().pipe(
          tap({
            next: catalog => this.rows.set(toUnitRows(catalog)),
            error: error => this.loadError.set(readUomError(error, 'The units could not be loaded.').message),
          }),
          catchError(() => EMPTY),
          finalize(() => this.loading.set(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe();
  }

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.reload$.next();
  }

  openCreate(): void {
    this.editing.set(null);
    this.saveError.set('');
    this.form.reset({ code: '', name: '', symbol: '', dimension: 'count', quantityPrecision: 0, isWholeUnit: false });
    this.form.controls.code.enable();
    this.form.controls.dimension.enable();
    this.form.controls.isWholeUnit.enable();
    this.setMinPrecision(0);
    this.dialogOpen.set(true);
  }

  openEdit(unit: UnitRow): void {
    this.editing.set(unit);
    this.saveError.set('');
    this.form.reset({
      code: unit.code,
      name: unit.name,
      symbol: unit.symbol,
      dimension: unit.dimension,
      quantityPrecision: unit.quantityPrecision,
      isWholeUnit: unit.isWholeUnit,
    });
    // Mirrors the server rule for a unit in use, so the form does not offer a change it will reject.
    const lock = (control: AbstractControl, locked: boolean) => (locked ? control.disable() : control.enable());
    lock(this.form.controls.code, unit.isInUse);
    lock(this.form.controls.dimension, unit.isInUse);
    lock(this.form.controls.isWholeUnit, unit.isInUse && !unit.isWholeUnit);
    this.setMinPrecision(unit.isInUse ? unit.quantityPrecision : 0);
    this.dialogOpen.set(true);
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const unit = this.editing();
    const payload: SaveUomUnit = { ...value, quantityPrecision: Number(value.quantityPrecision), isActive: unit?.isActive ?? true };
    this.saving.set(true);
    this.saveError.set('');
    const request = unit ? this.api.updateUnit(unit.id, payload) : this.api.createUnit(payload);
    request.pipe(
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: saved => {
        this.dialogOpen.set(false);
        this.notice.set(`Unit ${saved.code} saved.`);
        this.reload();
      },
      error: error => this.saveError.set(readUomError(error, 'The unit could not be saved.').message),
    });
  }

  askToggle(unit: UnitRow): void {
    this.toggleTarget.set(unit);
    this.saveError.set('');
    this.confirmOpen.set(true);
  }

  confirmToggle(): void {
    const unit = this.toggleTarget();
    if (!unit || this.saving()) return;
    this.saving.set(true);
    this.saveError.set('');
    this.api.updateUnit(unit.id, {
      code: unit.code,
      name: unit.name,
      symbol: unit.symbol,
      dimension: unit.dimension,
      quantityPrecision: unit.quantityPrecision,
      isWholeUnit: unit.isWholeUnit,
      isActive: !unit.isActive,
    }).pipe(
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: saved => {
        this.confirmOpen.set(false);
        this.notice.set(`Unit ${saved.code} ${saved.isActive ? 'reactivated' : 'deactivated'}.`);
        this.reload();
      },
      error: error => this.saveError.set(readUomError(error, 'The unit could not be changed.').message),
    });
  }

  private setMinPrecision(min: number): void {
    const control = this.form.controls.quantityPrecision;
    control.setValidators([Validators.required, Validators.min(min), Validators.max(MAX_UOM_PRECISION), Validators.pattern(/^\d+$/)]);
    control.updateValueAndValidity();
  }
}
