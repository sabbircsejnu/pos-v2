import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, finalize, switchMap, tap } from 'rxjs';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../components/shared/ui';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { PackStateBalanceRow, PackStateKind, PackStateView } from '../../models/uom.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { AuthService } from '../../services/auth.service';
import { UomService, formatUnitQuantity } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import {
  IdempotencyKeyHolder,
  LocationOption,
  VARIANT_SEARCH_PERMISSIONS,
  defaultLocationKey,
  locationOptions,
  readUomError,
} from './uom-ui';

const STATE_LABELS: Readonly<Record<PackStateKind, string>> = { sealed: 'Sealed', loose: 'Loose', prepared: 'Prepared' };

@Component({
  selector: 'app-uom-pack-state-page',
  standalone: true,
  imports: [ReactiveFormsModule, AppCurrencyPipe, VariantSearchPickerComponent, ...UI_KIT],
  template: `
    <main class="ps-page">
      <app-ui-page-header
        title="Pack State"
        description="How an item's stock at a location splits into sealed packs, loose quantity and prepared packs."
        [breadcrumbs]="breadcrumbs" />

      @if (notice()) {
        <app-ui-alert variant="success" [dismissible]="true" (dismissed)="notice.set('')">{{ notice() }}</app-ui-alert>
      }
      @if (!canSearch()) {
        <app-ui-alert variant="warning">Finding an item needs a permission such as Products view or Inventory view. Ask an administrator to add one to your role.</app-ui-alert>
      }

      <app-ui-card title="Choose item and location">
        <div class="ps-grid">
          <app-variant-search-picker
            label="Item"
            placeholder="Search by name, code or SKU..."
            [useVariationMetadata]="false"
            [disabled]="!canSearch()"
            [selectedVariant]="variant()"
            [locationId]="location()?.id ?? null"
            [locationType]="location()?.type ?? null"
            (selected)="selectVariant($event)"
            (cleared)="selectVariant(null)" />
          <app-ui-form-field label="Location" [required]="true" [error]="locationsError()">
            <select appUiControl [formControl]="locationKey">
              <option value="">Choose a location</option>
              @for (option of locations(); track option.key) {
                <option [value]="option.key">{{ option.name }} ({{ option.type }})</option>
              }
            </select>
          </app-ui-form-field>
        </div>
      </app-ui-card>

      @if (loading()) {
        <app-ui-state kind="loading" title="Loading pack state..." />
      } @else if (loadError()) {
        <app-ui-state kind="error" title="Could not load pack state" [message]="loadError()">
          <button appUiButton type="button" variant="outline" (click)="refresh()">Retry</button>
        </app-ui-state>
      } @else if (notTracked()) {
        <app-ui-state kind="empty" title="This item does not track sealed and loose stock"
          message="Turn on tracking in the item's Units & Pricing to use pack state." />
      } @else if (state(); as view) {
        @if (!view.isReconciled) {
          <app-ui-alert variant="warning" title="Pack state does not match stock">
            On hand is {{ quantity(view.onHand, view.baseUnitCode) }} but the breakdown adds up to {{ quantity(view.allocatedTotal, view.baseUnitCode) }}.
          </app-ui-alert>
        }
        <app-ui-card title="Stock breakdown">
          <dl class="ps-summary">
            <div><dt>On hand</dt><dd>{{ quantity(view.onHand, view.baseUnitCode) }}</dd></div>
            <div><dt>In breakdown</dt><dd>{{ quantity(view.allocatedTotal, view.baseUnitCode) }}</dd></div>
            <div>
              <dt>Status</dt>
              <dd>
                <app-ui-badge [variant]="view.isReconciled ? 'success' : 'warning'">{{ view.isReconciled ? 'Matches stock' : 'Mismatch' }}</app-ui-badge>
              </dd>
            </div>
          </dl>
          <app-ui-data-table
            ariaLabel="Pack state balances"
            density="compact"
            rowKey="rowKey"
            [columns]="columns"
            [rows]="rows()"
            [searchable]="false"
            [paginated]="false"
            emptyTitle="No stock at this location">
            <ng-template uiTableCell="capitalizedValue" let-row>{{ row.capitalizedValue > 0 ? (row.capitalizedValue | appCurrency) : '' }}</ng-template>
            <ng-template uiTableRowActions let-row>
              @if (canManage() && row.state === 'sealed' && row.packCount > 0) {
                <button appUiButton type="button" variant="ghost" size="sm"
                  [attr.aria-label]="'Open sealed pack ' + row.packCode" (click)="openDialog(row)">Open</button>
              }
            </ng-template>
          </app-ui-data-table>
        </app-ui-card>
      } @else if (!variant() || !location()) {
        <app-ui-state kind="empty" title="Choose an item and a location" message="The stock breakdown appears here." />
      }

      <app-ui-dialog [(open)]="dialogOpen" title="Open sealed packs" [dismissible]="!opening()">
        <form id="uom-open-pack-form" class="ui-form-stack" [formGroup]="openForm" (ngSubmit)="submitOpen()">
          @if (openError()) {
            <app-ui-alert variant="danger">{{ openError() }}</app-ui-alert>
          }
          <app-ui-form-field label="Sealed pack" [required]="true">
            <select appUiControl formControlName="packKey">
              @for (row of sealedRows(); track row.rowKey) {
                <option [value]="row.rowKey">{{ row.packCode }}: {{ row.packCount }} of {{ row.baseQtyPerPack }} {{ state()?.baseUnitCode }}</option>
              }
            </select>
          </app-ui-form-field>
          <app-ui-form-field label="Packs to open" [required]="true" [messages]="{ pattern: 'Enter a whole number.' }">
            <input appUiControl type="number" min="1" step="1" formControlName="count" data-autofocus />
          </app-ui-form-field>
          @if (openPreview()) {
            <p class="ps-hint">{{ openPreview() }} The item's total quantity and value do not change. No empty container is added to stock.</p>
          }
        </form>
        <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="opening()" (click)="dialogOpen.set(false)">Cancel</button>
        <button uiDialogFooter appUiButton type="submit" form="uom-open-pack-form" [loading]="opening()">Open packs</button>
      </app-ui-dialog>
    </main>
  `,
  styles: [`
    .ps-page { display: grid; gap: var(--rp-space-4); padding: var(--rp-space-3) var(--rp-space-4); }
    .ps-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); align-items: start; }
    .ps-summary { display: grid; gap: var(--rp-space-3); grid-template-columns: repeat(auto-fit, minmax(min(100%, 9rem), 1fr)); margin: 0 0 var(--rp-space-4); }
    .ps-summary dt { color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    .ps-summary dd { margin: 0; font-weight: var(--rp-font-weight-semibold); }
    .ps-hint { margin: 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); }
    @media (min-width: 768px) { .ps-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
})
export class PackStatePageComponent implements OnInit {
  private readonly api = inject(UomService);
  private readonly auth = inject(AuthService);
  private readonly access = inject(UserOutletAccessService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly load$ = new Subject<{ variantId: number; location: LocationOption }>();
  private readonly openKey = new IdempotencyKeyHolder('pack-open');

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Inventory' }, { label: 'Pack State' }];
  readonly columns: UiTableColumn[] = [
    { key: 'stateLabel', header: 'State' },
    { key: 'packCode', header: 'Pack', emptyText: '' },
    { key: 'perPackText', header: 'Per pack', emptyText: '' },
    { key: 'packCountText', header: 'Packs', emptyText: '' },
    { key: 'quantityText', header: 'Quantity', align: 'end' },
    { key: 'capitalizedValue', header: 'Packaging value', type: 'custom', align: 'end' },
  ];

  readonly canManage = computed(() => this.auth.hasPermission('inventory.pack_state.manage'));
  readonly canSearch = computed(() => this.auth.hasAnyPermission([...VARIANT_SEARCH_PERMISSIONS]));

  readonly locations = signal<LocationOption[]>([]);
  readonly locationsError = signal('');
  readonly locationKey = new FormControl('', { nonNullable: true });
  readonly location = signal<LocationOption | null>(null);
  readonly variant = signal<VariantSearchItem | null>(null);
  readonly state = signal<PackStateView | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly notTracked = signal(false);
  readonly notice = signal('');

  readonly dialogOpen = signal(false);
  readonly opening = signal(false);
  readonly openError = signal('');
  readonly openPreview = signal('');
  readonly openForm = new FormGroup({
    packKey: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    count: new FormControl<number | null>(1, { validators: [Validators.required, Validators.min(1), Validators.pattern(/^\d+$/)] }),
  });

  readonly rows = computed(() => {
    const view = this.state();
    return (view?.balances ?? []).map(row => this.toRow(row, view?.baseUnitCode ?? null));
  });
  readonly sealedRows = computed(() => this.rows().filter(row => row.state === 'sealed' && row.packCount > 0));

  constructor() {
    // switchMap drops the answer for an item or location that is no longer selected.
    this.load$.pipe(
      switchMap(({ variantId, location }) => {
        this.loading.set(true);
        this.loadError.set('');
        this.notTracked.set(false);
        return this.api.getPackState(variantId, location.id, location.type).pipe(
          tap({
            next: view => this.state.set(view),
            error: error => {
              this.state.set(null);
              const info = readUomError(error, 'The pack state could not be loaded.');
              if (info.code === 'UOM_PACK_STATE_NOT_ENABLED') this.notTracked.set(true);
              else this.loadError.set(info.message);
            },
          }),
          catchError(() => EMPTY),
          finalize(() => this.loading.set(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe();

    this.locationKey.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.refresh());
    this.openForm.controls.packKey.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.applyCountLimit());
    this.openForm.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.updatePreview());
  }

  ngOnInit(): void {
    this.access.load('InventoryStockView').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        const options = locationOptions(response.data);
        this.locations.set(options);
        this.locationKey.setValue(defaultLocationKey(response.data, options));
      },
      error: error => this.locationsError.set(readUomError(error, 'Your locations could not be loaded.').message),
    });
  }

  selectVariant(variant: VariantSearchItem | null): void {
    this.variant.set(variant);
    this.notice.set('');
    this.refresh();
  }

  refresh(): void {
    const location = this.locations().find(option => option.key === this.locationKey.value) ?? null;
    this.location.set(location);
    const variant = this.variant();
    if (!variant || !location) {
      this.state.set(null);
      this.loadError.set('');
      this.notTracked.set(false);
      return;
    }
    this.load$.next({ variantId: variant.id, location });
  }

  quantity(value: number, unit: string | null): string {
    return formatUnitQuantity(value, unit);
  }

  openDialog(row: { rowKey: string }): void {
    this.openError.set('');
    this.openKey.clear();
    this.openForm.reset({ packKey: row.rowKey, count: 1 });
    this.applyCountLimit();
    this.updatePreview();
    this.dialogOpen.set(true);
  }

  /** Blocks asking for more than the balance on screen. The server still checks the live balance. */
  applyCountLimit(): void {
    const limit = this.selectedSealed()?.packCount ?? 1;
    const count = this.openForm.controls.count;
    count.setValidators([Validators.required, Validators.min(1), Validators.max(limit), Validators.pattern(/^\d+$/)]);
    count.updateValueAndValidity();
  }

  submitOpen(): void {
    if (this.opening()) return;
    if (this.openForm.invalid) {
      this.openForm.markAllAsTouched();
      return;
    }
    const pack = this.selectedSealed();
    const variant = this.variant();
    const location = this.location();
    const count = Number(this.openForm.controls.count.value);
    if (!pack || !variant || !location) return;

    const fingerprint = [variant.id, location.key, pack.packCode, pack.baseQtyPerPack, count].join('|');
    const idempotencyKey = this.openKey.keyFor(fingerprint);
    this.opening.set(true);
    this.openError.set('');
    this.api.openSealedPack({
      variantId: variant.id,
      locationId: location.id,
      locationType: location.type,
      packCode: pack.packCode,
      baseQtyPerPack: pack.baseQtyPerPack,
      count,
      idempotencyKey,
    }).pipe(
      finalize(() => this.opening.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: view => {
        this.openKey.clear();
        this.state.set(view);
        this.dialogOpen.set(false);
        this.notice.set(`Opened ${count} sealed ${pack.packCode}.`);
      },
      error: error => this.openError.set(readUomError(error, 'The pack could not be opened. Try again.').message),
    });
  }

  private selectedSealed() {
    return this.sealedRows().find(row => row.rowKey === this.openForm.controls.packKey.value);
  }

  /** A preview only. The server converts the pack when it opens it. */
  private updatePreview(): void {
    const pack = this.selectedSealed();
    const count = Number(this.openForm.controls.count.value);
    if (!pack || !(count > 0)) {
      this.openPreview.set('');
      return;
    }
    const unit = this.state()?.baseUnitCode ?? null;
    this.openPreview.set(`${count} sealed ${pack.packCode} becomes ${this.quantity(count * pack.baseQtyPerPack, unit)} loose.`);
  }

  private toRow(row: PackStateBalanceRow, unit: string | null) {
    const isLoose = row.state === 'loose';
    return {
      ...row,
      rowKey: `${row.state}:${row.packCode}:${row.baseQtyPerPack}`,
      stateLabel: STATE_LABELS[row.state] ?? row.state,
      perPackText: isLoose ? '' : formatUnitQuantity(row.baseQtyPerPack, unit),
      packCountText: isLoose ? '' : row.packCount.toLocaleString('en-US'),
      quantityText: formatUnitQuantity(row.baseQuantity, unit),
    };
  }
}
