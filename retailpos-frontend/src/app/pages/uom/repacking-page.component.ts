import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { EMPTY, Observable, Subject, catchError, finalize, of, switchMap, tap } from 'rxjs';
import { UI_KIT, UiBreadcrumb, UiTableColumn, UiTableQuery } from '../../components/shared/ui';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { RepackJob, RepackStatus, SellingOption } from '../../models/uom.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { UomService, formatUnitQuantity } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import {
  IdempotencyKeyHolder,
  LocationOption,
  VARIANT_SEARCH_PERMISSIONS,
  defaultLocationKey,
  locationName,
  locationOptions,
  readUomError,
} from './uom-ui';

type JobAction = 'execute' | 'cancel' | 'reverse';

const STATUS_OPTIONS: { value: RepackStatus; label: string }[] = [
  { value: 'draft', label: 'Draft' },
  { value: 'executed', label: 'Executed' },
  { value: 'cancelled', label: 'Cancelled' },
  { value: 'reversed', label: 'Reversed' },
];

const ACTION_TEXT: Readonly<Record<JobAction, { title: string; confirm: string; destructive: boolean; done: string; failed: string }>> = {
  execute: { title: 'Prepare these packs?', confirm: 'Execute', destructive: false, done: 'executed', failed: 'The job could not be executed.' },
  cancel: { title: 'Cancel this job?', confirm: 'Cancel job', destructive: true, done: 'cancelled', failed: 'The job could not be cancelled.' },
  reverse: { title: 'Reverse this job?', confirm: 'Reverse', destructive: true, done: 'reversed', failed: 'The job could not be reversed.' },
};

@Component({
  selector: 'app-uom-repacking-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AppCurrencyPipe, VariantSearchPickerComponent, ...UI_KIT],
  template: `
    <main class="rj-page">
      <app-ui-page-header
        title="Repacking"
        description="Prepare ready-to-sell packs from loose stock and packaging."
        [breadcrumbs]="breadcrumbs">
        @if (canExecute()) {
          <button uiPagePrimaryActions appUiButton type="button" (click)="openCreate()">
            <i class="fas fa-plus" aria-hidden="true"></i> New repack job
          </button>
        }
      </app-ui-page-header>

      <app-ui-alert variant="info" title="Wastage is not recorded here">
        A repack job always uses the exact quantity it plans. If stock was spilled or damaged, record the loss as a stock adjustment, then prepare the packs.
        @if (canAdjust()) {
          <a class="rj-link" routerLink="/stock-adjustments/create">Create a stock adjustment</a>
        }
      </app-ui-alert>

      @if (notice()) {
        <app-ui-alert variant="success" [dismissible]="true" (dismissed)="notice.set('')">{{ notice() }}</app-ui-alert>
      }

      <app-ui-data-table
        ariaLabel="Repack jobs"
        density="compact"
        mode="server"
        rowKey="id"
        [columns]="columns()"
        [rows]="rows()"
        [totalRecords]="total()"
        [loading]="loading()"
        [error]="loadError() || null"
        errorTitle="Could not load repack jobs"
        [responseSequence]="responseSequence()"
        [searchable]="false"
        [initialPageSize]="25"
        emptyTitle="No repack jobs yet"
        emptyMessage="Create a job to prepare ready-to-sell packs."
        (queryChange)="onQuery($event)"
        (retry)="reload()">
        <ng-template uiTableRowActions let-row>
          <button appUiButton type="button" variant="ghost" size="sm"
            [attr.aria-label]="'Review job ' + row.reference" (click)="review(row)">Review</button>
        </ng-template>
      </app-ui-data-table>

      <app-ui-dialog [(open)]="createOpen" title="New repack job" size="lg" [dismissible]="!creating()">
        <form id="uom-repack-form" class="ui-form-stack" [formGroup]="createForm" (ngSubmit)="submitCreate()">
          @if (createError()) {
            <app-ui-alert variant="danger">{{ createError() }}</app-ui-alert>
          }
          @if (!canSearch()) {
            <app-ui-alert variant="warning">Finding an item needs a permission such as Products view or Inventory view. Ask an administrator to add one to your role.</app-ui-alert>
          }
          <app-ui-form-field label="Location" [required]="true" hint="Where the loose stock and packaging are held">
            <select appUiControl formControlName="locationKey">
              <option value="">Choose a location</option>
              @for (option of locations(); track option.key) {
                <option [value]="option.key">{{ option.name }} ({{ option.type }})</option>
              }
            </select>
          </app-ui-form-field>
          <app-variant-search-picker
            label="Source item"
            placeholder="Search by name, code or SKU..."
            [useVariationMetadata]="false"
            [disabled]="!canSearch()"
            [selectedVariant]="source()"
            [locationId]="selectedLocation()?.id ?? null"
            [locationType]="selectedLocation()?.type ?? null"
            (selected)="selectSource($event)"
            (cleared)="selectSource(null)" />
          @if (sourceLoading()) {
            <app-ui-state kind="loading" [compact]="true" title="Loading packs..." />
          } @else if (sourceError()) {
            <app-ui-alert variant="warning">{{ sourceError() }}</app-ui-alert>
          }
          @if (packs().length > 0) {
            <div class="rj-grid">
              <app-ui-form-field label="Output pack" [required]="true">
                <select appUiControl formControlName="optionId">
                  <option [ngValue]="null">Choose a pack</option>
                  @for (pack of packs(); track pack.id) {
                    <option [ngValue]="pack.id">{{ pack.label }} ({{ quantity(pack.baseQuantity) }})</option>
                  }
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Packs to prepare" [required]="true" [messages]="{ pattern: 'Enter a whole number.' }">
                <input appUiControl type="number" min="1" step="1" formControlName="count" />
              </app-ui-form-field>
            </div>
          }
          @if (preview(); as plan) {
            <div class="rj-preview" aria-live="polite">
              <p class="rj-preview__title">Preview</p>
              <p class="rj-text">Content needed: <strong>{{ plan.content }}</strong>@if (looseText()) { <span>, {{ looseText() }}</span> }</p>
              @if (plan.packaging.length > 0) {
                <ul class="rj-list">
                  @for (line of plan.packaging; track line) {
                    <li>{{ line }}</li>
                  }
                </ul>
              } @else {
                <p class="rj-text">No required packaging.</p>
              }
              <p class="rj-hint">The server confirms quantities and names when it creates the draft, and calculates cost when the job runs.</p>
            </div>
          }
          <app-ui-form-field label="Notes" [counter]="true" [maxLength]="500">
            <textarea appUiControl rows="2" maxlength="500" formControlName="notes"></textarea>
          </app-ui-form-field>
        </form>
        <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="creating()" (click)="createOpen.set(false)">Close</button>
        <button uiDialogFooter appUiButton type="submit" form="uom-repack-form" [loading]="creating()">Create draft</button>
      </app-ui-dialog>

      <app-ui-dialog [(open)]="reviewOpen" [title]="'Repack job ' + (job()?.reference ?? '')" size="lg" [dismissible]="!acting()">
        @if (job(); as current) {
          <div class="ui-form-stack">
            @if (actionError()) {
              <app-ui-alert variant="danger">{{ actionError() }}</app-ui-alert>
            }
            <dl class="rj-facts">
              <div><dt>Status</dt><dd><app-ui-badge [variant]="statusVariant(current.status)">{{ statusLabel(current.status) }}</app-ui-badge></dd></div>
              <div><dt>Item</dt><dd>{{ current.sourceName || 'Item #' + current.sourceVariantId }}@if (current.sourceSku) { <span> ({{ current.sourceSku }})</span> }</dd></div>
              <div><dt>Location</dt><dd>{{ locationLabel(current) }}</dd></div>
              <div><dt>Output</dt><dd>{{ current.outputCount }} x {{ current.outputLabel }}</dd></div>
              <div><dt>Content</dt><dd>{{ jobQuantity(current.contentBaseQuantity, current) }}</dd></div>
              <div><dt>Content value</dt><dd>{{ money(current, current.contentValue) }}</dd></div>
              <div><dt>Packaging value</dt><dd>{{ money(current, current.packagingValue) }}</dd></div>
              <div><dt>Cost per pack</dt><dd>{{ money(current, current.costPerPack) }}</dd></div>
              @if (current.executedAt) {
                <div><dt>Executed</dt><dd>{{ current.executedAt | appDate:'medium' }}</dd></div>
              }
              @if (current.reversedAt) {
                <div><dt>Reversed</dt><dd>{{ current.reversedAt | appDate:'medium' }}</dd></div>
              }
            </dl>
            @if (current.notes) {
              <p class="rj-text">{{ current.notes }}</p>
            }
            <div>
              <p class="rj-preview__title">Packaging</p>
              @if (current.components.length > 0) {
                <ul class="rj-list">
                  @for (component of current.components; track component.componentVariantId) {
                    <li>
                      {{ component.componentName || 'Item #' + component.componentVariantId }}@if (component.componentSku) { <span> ({{ component.componentSku }})</span> }:
                      {{ component.quantity }}@if (component.value !== null) { <span>, {{ component.value | appCurrency }}</span> }
                    </li>
                  }
                </ul>
              } @else {
                <p class="rj-text">No packaging on this job.</p>
              }
            </div>
          </div>
        }
        @if (canExecute() && job()?.status === 'draft') {
          <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="acting()" (click)="askAction('cancel')">Cancel job</button>
        }
        @if (canExecute() && job()?.status === 'draft') {
          <button uiDialogFooter appUiButton type="button" [disabled]="acting()" (click)="askAction('execute')">Execute</button>
        }
        @if (canExecute() && job()?.status === 'executed') {
          <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="acting()" (click)="askAction('reverse')">Reverse</button>
        }
        <button uiDialogFooter appUiButton type="button" variant="ghost" [disabled]="acting()" (click)="reviewOpen.set(false)">Close</button>
      </app-ui-dialog>

      <app-ui-confirm-dialog
        [(open)]="confirmOpen"
        [title]="actionText()?.title ?? ''"
        [confirmLabel]="actionText()?.confirm ?? 'Confirm'"
        cancelLabel="Back"
        [destructive]="actionText()?.destructive ?? false"
        [busy]="acting()"
        (confirmed)="runAction()">
        @switch (pendingAction()) {
          @case ('execute') {
            This uses {{ job() ? jobQuantity(job()!.contentBaseQuantity, job()!) : '' }} of loose stock and the listed packaging, and creates {{ job()?.outputCount }} prepared packs. It runs once.
          }
          @case ('cancel') {
            The draft is closed. No stock has moved.
          }
          @case ('reverse') {
            The packs return to loose stock and the packaging returns to its stock. This is blocked if any of the packs have been sold or moved.
          }
        }
      </app-ui-confirm-dialog>
    </main>
  `,
  styles: [`
    .rj-page { display: grid; gap: var(--rp-space-4); padding: var(--rp-space-3) var(--rp-space-4); }
    .rj-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr)); align-items: start; }
    .rj-link { display: inline-block; margin-inline-start: var(--rp-space-1); color: var(--rp-color-action); text-decoration: underline; }
    .rj-preview { display: grid; gap: var(--rp-space-2); padding: var(--rp-space-3); border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-control); background: var(--rp-color-surface-muted); }
    .rj-preview__title { margin: 0 0 var(--rp-space-1); font-weight: var(--rp-font-weight-semibold); }
    .rj-text { margin: 0; }
    .rj-list { margin: 0; padding-inline-start: var(--rp-space-4); }
    .rj-hint { margin: 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); }
    .rj-facts { display: grid; gap: var(--rp-space-3); grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr)); margin: 0; }
    .rj-facts dt { color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    .rj-facts dd { margin: 0; overflow-wrap: anywhere; }
  `],
})
export class RepackingPageComponent implements OnInit {
  private readonly api = inject(UomService);
  private readonly auth = inject(AuthService);
  private readonly access = inject(UserOutletAccessService);
  private readonly currency = inject(CurrencyService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly query$ = new Subject<UiTableQuery>();
  private readonly source$ = new Subject<VariantSearchItem | null>();
  private readonly createKey = new IdempotencyKeyHolder('repack');
  private lastQuery: UiTableQuery | null = null;
  private defaultKey = '';

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Inventory' }, { label: 'Repacking' }];

  readonly canExecute = computed(() => this.auth.hasPermission('inventory.repacking.execute'));
  readonly canAdjust = computed(() => this.auth.hasPermission('stock_adjustments.create'));
  readonly canSearch = computed(() => this.auth.hasAnyPermission([...VARIANT_SEARCH_PERMISSIONS]));
  private readonly canReadLoose = computed(() => this.auth.hasAnyPermission(['inventory.pack_state.view', 'inventory.view', 'sales.create']));

  readonly locations = signal<LocationOption[]>([]);
  readonly rows = signal<(RepackJob & { locationText: string; outputText: string; contentText: string })[]>([]);
  readonly total = signal<number | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly responseSequence = signal<number | null>(null);
  readonly notice = signal('');

  readonly columns = computed<UiTableColumn[]>(() => [
    { key: 'reference', header: 'Reference' },
    { key: 'sourceName', header: 'Item', emptyText: 'Unnamed item' },
    {
      key: 'locationText', header: 'Location',
      filter: { type: 'select', options: this.locations().map(option => ({ value: option.key, label: option.name })) },
    },
    { key: 'outputText', header: 'Output' },
    { key: 'contentText', header: 'Content', align: 'end' },
    {
      key: 'status', header: 'Status', type: 'status',
      filter: { type: 'select', options: STATUS_OPTIONS },
      statusMap: {
        draft: { label: 'Draft', variant: 'info' },
        executed: { label: 'Executed', variant: 'success' },
        cancelled: { label: 'Cancelled', variant: 'neutral' },
        reversed: { label: 'Reversed', variant: 'warning' },
      },
    },
  ]);

  readonly createOpen = signal(false);
  readonly creating = signal(false);
  readonly createError = signal('');
  readonly source = signal<VariantSearchItem | null>(null);
  readonly sourceLoading = signal(false);
  readonly sourceError = signal('');
  readonly packs = signal<SellingOption[]>([]);
  readonly baseUnitCode = signal<string | null>(null);
  readonly selectedLocation = signal<LocationOption | null>(null);
  readonly preview = signal<{ content: string; contentQuantity: number; packaging: string[] } | null>(null);
  readonly looseAvailable = signal<number | null>(null);
  readonly looseText = computed(() => {
    const loose = this.looseAvailable();
    return loose === null ? '' : `${this.quantity(loose)} loose available`;
  });
  readonly createForm = new FormGroup({
    locationKey: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    optionId: new FormControl<number | null>(null, { validators: [Validators.required] }),
    count: new FormControl<number | null>(1, { validators: [Validators.required, Validators.min(1), Validators.pattern(/^\d+$/)] }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  readonly reviewOpen = signal(false);
  readonly confirmOpen = signal(false);
  readonly job = signal<RepackJob | null>(null);
  readonly acting = signal(false);
  readonly actionError = signal('');
  readonly pendingAction = signal<JobAction | null>(null);
  readonly actionText = computed(() => {
    const action = this.pendingAction();
    return action ? ACTION_TEXT[action] : null;
  });

  constructor() {
    this.createForm.controls.locationKey.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.onLocationChange());
    this.createForm.controls.optionId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.refreshPreview());
    this.createForm.controls.count.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.refreshPreview());

    this.query$.pipe(
      switchMap(query => {
        this.loading.set(true);
        this.loadError.set('');
        const [locationType, locationId] = (query.filters['locationText'] ?? '').split(':');
        return this.api.getRepackJobs({
          status: query.filters['status'],
          locationType: locationType || undefined,
          locationId: locationId ? Number(locationId) : undefined,
          page: query.page,
          pageSize: query.pageSize,
        }).pipe(
          tap({
            next: page => {
              this.rows.set(page.items.map(job => this.toRow(job)));
              this.total.set(page.totalCount);
              this.responseSequence.set(query.sequence);
            },
            error: error => {
              this.responseSequence.set(query.sequence);
              this.loadError.set(readUomError(error, 'The repack jobs could not be loaded.').message);
            },
          }),
          catchError(() => EMPTY),
          finalize(() => this.loading.set(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe();

    this.source$.pipe(
      switchMap(source => {
        this.packs.set([]);
        this.baseUnitCode.set(null);
        this.sourceError.set('');
        this.createForm.controls.optionId.setValue(null);
        if (!source) {
          this.sourceLoading.set(false);
          return EMPTY;
        }
        this.sourceLoading.set(true);
        return this.api.getVariantConfig(source.id).pipe(
          tap({
            next: view => {
              const config = view.current;
              if (!config?.repackingEnabled || !config.packStateTrackingEnabled) {
                this.sourceError.set('Repacking is not turned on for this item. Turn it on in the item\'s Units & Pricing.');
                return;
              }
              const packs = config.sellingOptions.filter(option => option.kind === 'pack' && option.isPreparedEligible);
              if (packs.length === 0) {
                this.sourceError.set('This item has no pack that can be prepared. Mark one in its Units & Pricing.');
                return;
              }
              this.baseUnitCode.set(config.baseUnit.code);
              this.packs.set(packs);
              this.refreshLoose();
            },
            error: error => this.sourceError.set(readUomError(error, 'The packs of this item could not be loaded.').message),
          }),
          catchError(() => EMPTY),
          finalize(() => this.sourceLoading.set(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe();
  }

  ngOnInit(): void {
    this.access.load('InventoryStockView').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        const options = locationOptions(response.data);
        this.locations.set(options);
        this.defaultKey = defaultLocationKey(response.data, options);
        this.rows.update(rows => rows.map(row => this.toRow(row)));
      },
      // Jobs still list without location names; the create dialog then has no location to offer.
      error: () => this.locations.set([]),
    });
  }

  // --- List ---

  onQuery(query: UiTableQuery): void {
    this.lastQuery = query;
    this.query$.next(query);
  }

  reload(): void {
    if (this.lastQuery) this.query$.next(this.lastQuery);
  }

  quantity(value: number | null | undefined): string {
    return formatUnitQuantity(value, this.baseUnitCode());
  }

  jobQuantity(value: number, job: RepackJob): string {
    return formatUnitQuantity(value, job.baseUnitCode);
  }

  locationLabel(job: RepackJob): string {
    return locationName(this.locations(), job.locationId, job.locationType);
  }

  statusLabel(status: RepackStatus): string {
    return STATUS_OPTIONS.find(option => option.value === status)?.label ?? status;
  }

  statusVariant(status: RepackStatus): 'info' | 'success' | 'neutral' | 'warning' {
    return status === 'draft' ? 'info' : status === 'executed' ? 'success' : status === 'reversed' ? 'warning' : 'neutral';
  }

  /** Server-calculated values only. Nothing is valued until the job runs, and an unvalued result is never shown as zero. */
  money(job: RepackJob, value: number | null): string {
    if (value !== null) return this.currency.format(value);
    return job.status === 'draft' ? 'Calculated when executed' : 'Not valued';
  }

  // --- Create ---

  openCreate(): void {
    this.createKey.clear();
    this.createError.set('');
    this.source.set(null);
    this.source$.next(null);
    this.createForm.reset({ locationKey: this.defaultKey, optionId: null, count: 1, notes: '' });
    this.createOpen.set(true);
  }

  onLocationChange(): void {
    this.selectedLocation.set(this.locations().find(option => option.key === this.createForm.controls.locationKey.value) ?? null);
    this.refreshLoose();
  }

  selectSource(source: VariantSearchItem | null): void {
    this.source.set(source);
    this.source$.next(source);
  }

  /** A preview only, from the configuration on screen. Only required packaging is consumed by a repack, as on the server. */
  refreshPreview(): void {
    const pack = this.packs().find(option => option.id === this.createForm.controls.optionId.value);
    const count = Number(this.createForm.controls.count.value);
    if (!pack || !Number.isInteger(count) || count <= 0) {
      this.preview.set(null);
      return;
    }
    const contentQuantity = count * (pack.baseQuantity ?? 0);
    this.preview.set({
      content: this.quantity(contentQuantity),
      contentQuantity,
      packaging: pack.components
        .filter(component => component.isRequired)
        .map(component => `Packaging item #${component.variantId}: ${(component.quantity * count).toLocaleString('en-US', { maximumFractionDigits: 6 })}`),
    });
  }

  submitCreate(): void {
    if (this.creating()) return;
    const source = this.source();
    const location = this.selectedLocation();
    if (!source) {
      this.createError.set('Choose a source item.');
      return;
    }
    if (this.createForm.invalid || !location) {
      this.createForm.markAllAsTouched();
      this.createError.set(this.packs().length === 0 ? 'This item has no pack to prepare.' : '');
      return;
    }
    const optionId = Number(this.createForm.controls.optionId.value);
    const outputCount = Number(this.createForm.controls.count.value);
    const notes = this.createForm.controls.notes.value.trim();
    const idempotencyKey = this.createKey.keyFor([source.id, location.key, optionId, outputCount, notes].join('|'));
    this.creating.set(true);
    this.createError.set('');
    this.api.createRepackJob({
      sourceVariantId: source.id,
      locationId: location.id,
      locationType: location.type,
      outputSellingOptionId: optionId,
      outputCount,
      idempotencyKey,
      notes: notes || null,
    }).pipe(
      finalize(() => this.creating.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: job => {
        this.createKey.clear();
        this.createOpen.set(false);
        this.notice.set(`Draft ${job.reference} created. Review it, then execute it.`);
        this.review(job);
        this.reload();
      },
      error: error => this.createError.set(readUomError(error, 'The job could not be created.').message),
    });
  }

  // --- Review and actions ---

  review(job: RepackJob): void {
    this.job.set(job);
    this.actionError.set('');
    this.reviewOpen.set(true);
  }

  askAction(action: JobAction): void {
    this.pendingAction.set(action);
    this.actionError.set('');
    this.confirmOpen.set(true);
  }

  runAction(): void {
    const job = this.job();
    const action = this.pendingAction();
    if (!job || !action || this.acting()) return;
    const requests: Record<JobAction, (id: number) => Observable<RepackJob>> = {
      execute: id => this.api.executeRepackJob(id),
      cancel: id => this.api.cancelRepackJob(id),
      reverse: id => this.api.reverseRepackJob(id),
    };
    this.acting.set(true);
    this.actionError.set('');
    requests[action](job.id).pipe(
      finalize(() => this.acting.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: updated => {
        this.job.set(updated);
        this.confirmOpen.set(false);
        this.notice.set(`Job ${updated.reference} ${ACTION_TEXT[action].done}.`);
        this.reload();
      },
      error: error => {
        this.confirmOpen.set(false);
        this.actionError.set(readUomError(error, ACTION_TEXT[action].failed).message);
        this.reload();
      },
    });
  }

  // --- Internals ---

  private refreshLoose(): void {
    this.looseAvailable.set(null);
    const source = this.source();
    const location = this.selectedLocation();
    if (!source || !location || !this.canReadLoose() || this.packs().length === 0) return;
    const looseRequest = this.api.getPackState(source.id, location.id, location.type).pipe(catchError(() => of(null)));
    looseRequest.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(view => {
      if (!view || this.source()?.id !== source.id || this.selectedLocation()?.key !== location.key) return;
      this.looseAvailable.set(view.balances.filter(row => row.state === 'loose').reduce((sum, row) => sum + row.baseQuantity, 0));
    });
  }

  private toRow(job: RepackJob) {
    return {
      ...job,
      locationText: locationName(this.locations(), job.locationId, job.locationType),
      outputText: `${job.outputCount} x ${job.outputLabel}`,
      contentText: formatUnitQuantity(job.contentBaseQuantity, job.baseUnitCode),
    };
  }
}
