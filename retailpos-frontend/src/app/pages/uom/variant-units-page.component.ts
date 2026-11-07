import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../components/shared/ui';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { Product } from '../../models/product.model';
import { UomConversion, UomUnit, VariantUomConfigView } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { UomService, quantityStep } from '../../services/uom.service';
import { readUomError } from './uom-ui';
import {
  ComponentValue,
  ConfigValue,
  PurchaseValue,
  SellingValue,
  standardFactor,
  suggestOptionCode,
  toConfigInput,
  defaultApplyAutomaticPricingRules,
  toConfigValue,
  validateConfig,
} from './variant-config.logic';

type ComponentGroup = FormGroup<{
  componentVariantId: FormControl<number>;
  name: FormControl<string>;
  quantity: FormControl<number | null>;
  isRequired: FormControl<boolean>;
}>;

type PurchaseGroup = FormGroup<{
  code: FormControl<string>;
  label: FormControl<string>;
  unitId: FormControl<number | null>;
  size: FormControl<number | null>;
  factorDenominator: FormControl<number>;
  isDefault: FormControl<boolean>;
  isSealedPack: FormControl<boolean>;
}>;

type SellingGroup = FormGroup<{
  code: FormControl<string>;
  label: FormControl<string>;
  unitId: FormControl<number | null>;
  baseQuantity: FormControl<number | null>;
  price: FormControl<number | null>;
  isDefault: FormControl<boolean>;
  sealedPackCode: FormControl<string>;
  isPreparedEligible: FormControl<boolean>;
  applyAutomaticPricingRules: FormControl<boolean>;
  components: FormArray<ComponentGroup>;
}>;

const CODE_MAX = 40;
const LABEL_MAX = 100;

@Component({
  selector: 'app-uom-variant-units-page',
  standalone: true,
  imports: [NgTemplateOutlet, ReactiveFormsModule, VariantSearchPickerComponent, ...UI_KIT],
  templateUrl: './variant-units-page.component.html',
  styles: [`
    .uv-page { display: grid; gap: var(--rp-space-4); padding: var(--rp-space-3) var(--rp-space-4); }
    .uv-stack { display: grid; gap: var(--rp-space-3); }
    .uv-grid { display: grid; gap: var(--rp-space-3); grid-template-columns: repeat(auto-fit, minmax(min(100%, 11rem), 1fr)); align-items: start; }
    .uv-row { display: grid; gap: var(--rp-space-3); padding: var(--rp-space-3); border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-control); }
    .uv-row__flags { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-3); }
    .uv-row__end { margin-inline-start: auto; }
    .uv-hint { margin: 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); }
    .uv-sub { display: grid; gap: var(--rp-space-2); padding-top: var(--rp-space-2); border-top: 1px solid var(--rp-color-border); }
    .uv-sub__title { margin: 0; font-size: var(--rp-text-helper-size); font-weight: var(--rp-font-weight-semibold); color: var(--rp-color-text-secondary); }
    .uv-component { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--rp-space-3); }
    .uv-component__name { flex: 1 1 10rem; min-width: 0; overflow-wrap: anywhere; }
    .uv-component__qty { flex: 0 1 8rem; }
    .uv-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .uv-errors { margin: 0; padding-inline-start: var(--rp-space-4); }
  `],
})
export class VariantUnitsPageComponent implements OnInit {
  private readonly api = inject(UomService);
  private readonly auth = inject(AuthService);
  private readonly products = inject(ProductService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly productId = Number(this.route.snapshot.paramMap.get('productId'));
  readonly variantId = Number(this.route.snapshot.paramMap.get('variantId'));
  readonly quantityStep = quantityStep;

  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly view = signal<VariantUomConfigView | null>(null);
  readonly units = signal<UomUnit[]>([]);
  readonly conversions = signal<UomConversion[]>([]);
  readonly variantLabel = signal('');
  readonly setupStarted = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly conflict = signal(false);
  readonly notice = signal('');
  readonly validationErrors = signal<string[]>([]);

  readonly pickerTarget = signal<SellingGroup | null>(null);
  readonly pickerOpen = signal(false);
  readonly pickedVariant = signal<VariantSearchItem | null>(null);
  readonly pickerError = signal('');
  readonly pickerQuantity = new FormControl<number | null>(1);
  readonly pickerRequired = new FormControl(true, { nonNullable: true });

  readonly canEdit = computed(() => this.auth.hasPermission('products.edit'));
  readonly current = computed(() => this.view()?.current ?? null);
  readonly baseLocked = computed(() => !!this.view()?.hasStockActivity);
  readonly isPlainPcs = computed(() => !!this.view() && !this.current() && !this.setupStarted());
  readonly historyRows = computed(() => (this.view()?.history ?? []).map(row => ({
    ...row,
    id: row.configId,
    versionLabel: `Version ${row.version}`,
    status: row.effectiveToUtc ? 'past' : 'current',
    createdByLabel: row.createdBy === null ? '' : `User #${row.createdBy}`,
  })));

  readonly breadcrumbs: UiBreadcrumb[] = [
    { label: 'Products', link: ['/products'] },
    { label: 'Product', link: ['/products', this.productId] },
    { label: 'Units & Pricing' },
  ];

  readonly historyColumns: UiTableColumn[] = [
    { key: 'versionLabel', header: 'Version' },
    { key: 'baseUnitCode', header: 'Base unit' },
    { key: 'effectiveFromUtc', header: 'Effective from', type: 'custom' },
    { key: 'effectiveToUtc', header: 'Effective to', type: 'custom' },
    { key: 'createdByLabel', header: 'Saved by' },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: { current: { label: 'Current', variant: 'success' }, past: { label: 'Past', variant: 'neutral' } },
    },
  ];

  readonly form = new FormGroup({
    baseUnitId: new FormControl<number | null>(null, { validators: [Validators.required] }),
    packStateTrackingEnabled: new FormControl(false, { nonNullable: true }),
    repackingEnabled: new FormControl(false, { nonNullable: true }),
    purchaseOptions: new FormArray<PurchaseGroup>([]),
    packs: new FormArray<SellingGroup>([]),
    customs: new FormArray<SellingGroup>([]),
  });

  constructor() {
    this.form.controls.packStateTrackingEnabled.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.onTrackingChange());
  }

  ngOnInit(): void {
    this.load();
    this.loadVariantLabel();
  }

  load(): void {
    if (!(this.variantId > 0)) {
      this.loading.set(false);
      this.loadError.set('This item could not be identified. Open it again from its product page.');
      return;
    }
    this.loading.set(true);
    this.loadError.set('');
    forkJoin({
      view: this.api.getVariantConfig(this.variantId),
      catalog: this.api.getCatalog(),
    }).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: ({ view, catalog }) => {
        this.units.set(catalog.units);
        this.conversions.set(catalog.conversions);
        this.applyView(view);
      },
      error: error => this.loadError.set(readUomError(error, 'The unit setup could not be loaded.').message),
    });
  }

  /** Discards the form and takes the server's latest configuration. Only an explicit user action reaches this. */
  reloadAfterConflict(): void {
    this.conflict.set(false);
    this.saveError.set('');
    this.validationErrors.set([]);
    this.notice.set('');
    this.load();
  }

  startSetup(): void {
    this.setupStarted.set(true);
  }

  discard(): void {
    const view = this.view();
    if (!view) return;
    this.applyView(view);
    this.notice.set('');
  }

  // --- Unit helpers ---

  unitOf(id: number | null | undefined): UomUnit | undefined {
    return this.units().find(unit => unit.id === id);
  }

  baseUnit(): UomUnit | undefined {
    return this.unitOf(this.form.controls.baseUnitId.value);
  }

  /** Inactive units are not offered, except one that this configuration already uses. */
  private selectable(selected: number | null, keep: (unit: UomUnit) => boolean): UomUnit[] {
    return this.units().filter(unit => unit.id === selected || (unit.isActive && keep(unit)));
  }

  baseUnitOptions(): UomUnit[] {
    return this.selectable(this.form.controls.baseUnitId.value, unit => unit.dimension !== 'packaging');
  }

  purchaseUnitOptions(row: PurchaseGroup): UomUnit[] {
    const base = this.baseUnit();
    return this.selectable(row.controls.unitId.value, unit => unit.dimension === 'packaging' || unit.dimension === base?.dimension);
  }

  customUnitOptions(row: SellingGroup): UomUnit[] {
    const base = this.baseUnit();
    return this.selectable(row.controls.unitId.value, unit => unit.dimension !== 'packaging' && unit.dimension === base?.dimension);
  }

  sealedCodes(): string[] {
    return this.form.controls.purchaseOptions.controls
      .filter(row => row.controls.isSealedPack.value && row.controls.code.value.trim())
      .map(row => row.controls.code.value.trim());
  }

  isPackaging(row: PurchaseGroup): boolean {
    return this.unitOf(row.controls.unitId.value)?.dimension === 'packaging';
  }

  /** A preview of the conversion. The server resolves the real factor when it saves. */
  conversionHint(row: PurchaseGroup): string {
    const unit = this.unitOf(row.controls.unitId.value);
    const base = this.baseUnit();
    if (!unit || !base || unit.dimension === 'packaging') return '';
    const factor = standardFactor(unit, base, this.conversions());
    if (factor === null) return `${unit.symbol} has no standard conversion to ${base.symbol}.`;
    return `1 ${unit.symbol} = ${factor.toLocaleString('en-US', { maximumFractionDigits: 6 })} ${base.symbol} (standard)`;
  }

  // --- Row editing ---

  addPurchase(): void {
    const list = this.form.controls.purchaseOptions;
    list.push(this.purchaseGroup({
      code: '', label: '', unitId: null, size: null, factorDenominator: 1, isDefault: list.length === 0, isSealedPack: false,
    }, false));
  }

  addPack(): void {
    this.form.controls.packs.push(this.sellingGroup(this.blankSelling('pack'), false));
  }

  addCustom(): void {
    this.form.controls.customs.push(this.sellingGroup({ ...this.blankSelling('custom'), unitId: this.form.controls.baseUnitId.value }, false));
  }

  removePurchase(index: number): void {
    const list = this.form.controls.purchaseOptions;
    const wasDefault = list.at(index).controls.isDefault.value;
    list.removeAt(index);
    if (wasDefault && list.length > 0) list.at(0).controls.isDefault.setValue(true);
  }

  removeSelling(list: FormArray<SellingGroup>, index: number): void {
    list.removeAt(index);
  }

  setPurchaseDefault(row: PurchaseGroup): void {
    this.form.controls.purchaseOptions.controls.forEach(other => other.controls.isDefault.setValue(other === row));
  }

  /** One default across fixed packs and custom quantities. Unticking leaves no default, which the server allows. */
  toggleSellingDefault(row: SellingGroup, checked: boolean): void {
    [...this.form.controls.packs.controls, ...this.form.controls.customs.controls]
      .forEach(other => other.controls.isDefault.setValue(checked && other === row));
  }

  onTrackingChange(): void {
    if (this.form.controls.packStateTrackingEnabled.value) return;
    this.form.controls.repackingEnabled.setValue(false);
    this.form.controls.purchaseOptions.controls.forEach(row => row.controls.isSealedPack.setValue(false));
    this.form.controls.packs.controls.forEach(row => {
      row.controls.isPreparedEligible.setValue(false);
      row.controls.sealedPackCode.setValue('');
    });
  }

  fillCode(row: PurchaseGroup | SellingGroup): void {
    const code = row.controls.code;
    if (code.enabled && !code.value.trim()) code.setValue(suggestOptionCode(row.controls.label.value));
  }

  // --- Packaging components ---

  openPicker(row: SellingGroup): void {
    this.pickerTarget.set(row);
    this.pickedVariant.set(null);
    this.pickerError.set('');
    this.pickerQuantity.setValue(1);
    this.pickerRequired.setValue(true);
    this.pickerOpen.set(true);
  }

  /** The item itself and packaging already on this option cannot be picked again. */
  pickerExistingIds(): number[] {
    const target = this.pickerTarget();
    const taken = target ? target.controls.components.controls.map(group => group.controls.componentVariantId.value) : [];
    return [this.variantId, ...taken];
  }

  addComponent(): void {
    const target = this.pickerTarget();
    const picked = this.pickedVariant();
    const quantity = this.pickerQuantity.value;
    if (!target || !picked) {
      this.pickerError.set('Choose a packaging item.');
      return;
    }
    if (!quantity || quantity <= 0) {
      this.pickerError.set('Enter a packaging quantity greater than zero.');
      return;
    }
    target.controls.components.push(this.componentGroup({
      componentVariantId: picked.id,
      name: picked.sku ? `${picked.productName} (${picked.sku})` : picked.productName,
      quantity,
      isRequired: this.pickerRequired.value,
    }));
    this.pickerOpen.set(false);
  }

  removeComponent(row: SellingGroup, index: number): void {
    row.controls.components.removeAt(index);
  }

  // --- Save ---

  save(): void {
    if (this.saving() || this.conflict() || !this.canEdit()) return;
    this.notice.set('');
    this.saveError.set('');
    const value = this.form.getRawValue() as ConfigValue;
    const errors = validateConfig(value, this.units());
    this.validationErrors.set(errors);
    if (errors.length > 0) {
      this.form.markAllAsTouched();
      return;
    }
    // Zero means "no configuration yet", so two people setting up the same item still conflict.
    const expectedVersion = this.current()?.version ?? 0;
    this.saving.set(true);
    this.api.saveVariantConfig(this.variantId, expectedVersion, toConfigInput(value)).pipe(
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: view => {
        this.applyView(view);
        this.notice.set(view.current ? `Saved as version ${view.current.version}.` : 'Saved.');
      },
      error: error => {
        const info = readUomError(error, 'The unit setup could not be saved.');
        if (info.code === 'UOM_CONFIG_CHANGED') {
          this.conflict.set(true);
        } else {
          this.saveError.set(info.message);
        }
      },
    });
  }

  // --- Internals ---

  private applyView(view: VariantUomConfigView): void {
    this.view.set(view);
    this.setupStarted.set(false);
    const value = toConfigValue(view, this.units());
    const { purchaseOptions, packs, customs, baseUnitId } = this.form.controls;
    purchaseOptions.clear();
    packs.clear();
    customs.clear();
    value.purchaseOptions.forEach(option => purchaseOptions.push(this.purchaseGroup(option, true)));
    value.packs.forEach(option => packs.push(this.sellingGroup(option, true)));
    value.customs.forEach(option => customs.push(this.sellingGroup(option, true)));
    this.form.patchValue({
      baseUnitId: value.baseUnitId,
      packStateTrackingEnabled: value.packStateTrackingEnabled,
      repackingEnabled: value.repackingEnabled,
    });
    if (view.hasStockActivity) baseUnitId.disable();
    else baseUnitId.enable();
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.validationErrors.set([]);
    this.saveError.set('');
  }

  /** A new pack does not take automatic promotions by default; a new custom quantity does (as on the server). */
  private blankSelling(kind: 'pack' | 'custom'): SellingValue {
    return {
      code: '', label: '', unitId: null, baseQuantity: null, price: null,
      isDefault: false, sealedPackCode: '', isPreparedEligible: false, components: [],
      applyAutomaticPricingRules: defaultApplyAutomaticPricingRules(kind),
    };
  }

  /** A saved option keeps its code: stock balances and history refer to it across versions. */
  private codeControl(code: string, saved: boolean): FormControl<string> {
    return new FormControl(
      { value: code, disabled: saved },
      { nonNullable: true, validators: [Validators.required, Validators.maxLength(CODE_MAX)] },
    );
  }

  private purchaseGroup(value: PurchaseValue, saved: boolean): PurchaseGroup {
    return new FormGroup({
      code: this.codeControl(value.code, saved),
      label: new FormControl(value.label, { nonNullable: true, validators: [Validators.maxLength(LABEL_MAX)] }),
      unitId: new FormControl<number | null>(value.unitId, { validators: [Validators.required] }),
      size: new FormControl<number | null>(value.size),
      factorDenominator: new FormControl(value.factorDenominator, { nonNullable: true }),
      isDefault: new FormControl(value.isDefault, { nonNullable: true }),
      isSealedPack: new FormControl(value.isSealedPack, { nonNullable: true }),
    });
  }

  private sellingGroup(value: SellingValue, saved: boolean): SellingGroup {
    return new FormGroup({
      code: this.codeControl(value.code, saved),
      label: new FormControl(value.label, { nonNullable: true, validators: [Validators.maxLength(LABEL_MAX)] }),
      unitId: new FormControl<number | null>(value.unitId),
      baseQuantity: new FormControl<number | null>(value.baseQuantity),
      price: new FormControl<number | null>(value.price, { validators: [Validators.required, Validators.min(0)] }),
      isDefault: new FormControl(value.isDefault, { nonNullable: true }),
      sealedPackCode: new FormControl(value.sealedPackCode, { nonNullable: true }),
      isPreparedEligible: new FormControl(value.isPreparedEligible, { nonNullable: true }),
      applyAutomaticPricingRules: new FormControl(value.applyAutomaticPricingRules, { nonNullable: true }),
      components: new FormArray<ComponentGroup>(value.components.map(component => this.componentGroup(component))),
    });
  }

  private componentGroup(value: ComponentValue): ComponentGroup {
    return new FormGroup({
      componentVariantId: new FormControl(value.componentVariantId, { nonNullable: true }),
      name: new FormControl(value.name, { nonNullable: true }),
      quantity: new FormControl<number | null>(value.quantity, { validators: [Validators.required, Validators.min(0)] }),
      isRequired: new FormControl(value.isRequired, { nonNullable: true }),
    });
  }

  /** Only labels the page. It needs products.view, which an editor may not hold, so it is skipped rather than failing. */
  private loadVariantLabel(): void {
    if (!(this.productId > 0) || !this.auth.hasPermission('products.view')) return;
    this.products.getById(this.productId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response: { data?: Product }) => {
        const product = response?.data;
        if (!product) return;
        const variant = product.variants?.find(candidate => candidate.id === this.variantId);
        const sku = variant?.sku ? ` (${variant.sku})` : '';
        this.variantLabel.set(product.hasVariants && variant ? `${product.name}: ${variant.name}${sku}` : `${product.name}${sku}`);
      },
      error: () => this.variantLabel.set(''),
    });
  }
}
