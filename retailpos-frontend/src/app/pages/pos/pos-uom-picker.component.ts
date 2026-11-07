import { Component, DestroyRef, ElementRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { UiButtonComponent } from '../../components/shared/ui/ui-button.component';
import { UiDialogComponent } from '../../components/shared/ui/ui-dialog.component';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { PackStateKind, PackStateView, SellingOption, UnitSaleQuote, VariantUomConfig } from '../../models/uom.model';
import { fitsPrecision, formatUnitQuantity, quantityStep, UomService } from '../../services/uom.service';
import { apiErrorCode, roundQuantity, uomErrorMessage } from '../../utils/uom-line.util';
import { allowedForms, canChoosePackaging, packStateRow, PosUomSelection, sellingPrecision } from './pos-uom';

interface QuoteRequest {
  option: SellingOption;
  quantity: number;
  configId: number;
}

type QuoteOutcome = { quote: UnitSaleQuote; error?: undefined } | { quote?: undefined; error: unknown } | null;

/**
 * Pack and custom quantity picker for a configured variant. Keyboard: 1-9 choose an option, arrow keys move between
 * options, Enter on an option or in the quantity field adds it, Escape closes. Every number here is a preview: the
 * server recomputes price, base quantity and stock at checkout.
 */
@Component({
  selector: 'app-pos-uom-picker',
  standalone: true,
  imports: [FormsModule, UiDialogComponent, UiButtonComponent, AppCurrencyPipe],
  templateUrl: './pos-uom-picker.component.html',
  styleUrls: ['./pos-uom-picker.component.css'],
})
export class PosUomPickerComponent implements OnInit {
  private readonly uom = inject(UomService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly config = input.required<VariantUomConfig>();
  readonly productName = input.required<string>();
  readonly outletId = input<number | null>(null);
  /** Last known stock at the outlet in base units: a hint only. */
  readonly knownStock = input<number | null>(null);
  /** The line's current choice, when re-picking. */
  readonly preset = input<{ sellingOptionId?: number; quantity?: number } | null>(null);

  readonly picked = output<PosUomSelection>();
  readonly cancelled = output<void>();
  /** The server reported newer units, so the screen can refresh what it knows. */
  readonly configChanged = output<VariantUomConfig | null>();

  /** Starts from the input and follows the server if it reports a newer version. */
  readonly liveConfig = signal<VariantUomConfig | null>(null);
  readonly selectedOptionId = signal<number | null>(null);
  readonly quantityText = signal('1');
  readonly form = signal<PackStateKind | undefined>(undefined);
  readonly includePackaging = signal(false);
  readonly quote = signal<UnitSaleQuote | null>(null);
  readonly quoteLoading = signal(false);
  readonly notice = signal('');
  readonly showQuantityError = signal(false);
  readonly packState = signal<PackStateView | null>(null);
  readonly packStateFailed = signal(false);

  readonly options = computed<SellingOption[]>(() => this.liveConfig()?.sellingOptions ?? []);
  readonly option = computed(() => this.options().find(candidate => candidate.id === this.selectedOptionId()) ?? null);
  readonly baseSymbol = computed(() => this.liveConfig()?.baseUnit.symbol ?? '');
  readonly precision = computed(() => {
    const option = this.option();
    const config = this.liveConfig();
    return option && config ? sellingPrecision(option, config) : 0;
  });
  readonly step = computed(() => quantityStep(this.precision()));
  readonly quantityLabel = computed(() => {
    const option = this.option();
    if (!option) return 'Quantity';
    return option.kind === 'pack' ? 'Number of packs' : `Quantity (${option.unit?.symbol ?? this.baseSymbol()})`;
  });
  readonly quantity = computed(() => {
    const text = String(this.quantityText() ?? '').trim();
    return text === '' ? null : Number(text);
  });
  readonly quantityError = computed(() => {
    const quantity = this.quantity();
    if (quantity === null) return 'Enter a quantity.';
    if (!Number.isFinite(quantity) || quantity <= 0) return 'The quantity must be greater than zero.';
    if (!fitsPrecision(quantity, this.precision())) {
      return this.precision() === 0 ? 'The quantity must be a whole number.' : `Use at most ${this.precision()} decimal place(s).`;
    }
    return '';
  });
  readonly forms = computed<PackStateKind[]>(() => {
    const option = this.option();
    const config = this.liveConfig();
    return option && config ? allowedForms(option, config) : [];
  });
  readonly showPackagingToggle = computed(() => {
    const option = this.option();
    return !!option && canChoosePackaging(option, this.form());
  });
  readonly hasRequiredPackaging = computed(() => {
    const form = this.form();
    return (form === undefined || form === 'loose') && !!this.option()?.components.some(component => component.isRequired);
  });
  readonly onHand = computed(() => this.packState()?.onHand ?? this.knownStock());
  readonly stockAfter = computed(() => {
    const quote = this.quote();
    const onHand = this.onHand();
    return quote && onHand !== null && onHand !== undefined ? roundQuantity(Number(onHand) - quote.baseQuantity) : null;
  });

  private readonly quoteRequests = new Subject<QuoteRequest | null>();

  ngOnInit(): void {
    this.quoteRequests.pipe(
      debounceTime(150),
      switchMap(request => {
        if (!request) return of<QuoteOutcome>(null);
        return this.uom.quote(this.config().variantId, request.option.id, request.quantity, request.configId).pipe(
          switchMap(quote => of<QuoteOutcome>({ quote })),
          catchError((error: unknown) => of<QuoteOutcome>({ error })),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(outcome => this.applyQuote(outcome));

    this.adoptConfig(this.config(), this.preset());
    this.loadPackState();
  }

  private adoptConfig(config: VariantUomConfig, preset: { sellingOptionId?: number; quantity?: number } | null): void {
    this.liveConfig.set(config);
    const remembered = config.sellingOptions.find(option => option.id === preset?.sellingOptionId);
    const initial = remembered ?? config.sellingOptions.find(option => option.isDefault) ?? config.sellingOptions[0] ?? null;
    if (initial) this.selectOption(initial, remembered && preset?.quantity ? String(preset.quantity) : undefined);
  }

  private loadPackState(): void {
    const config = this.config();
    const outletId = this.outletId();
    if (!config.packStateTrackingEnabled || !outletId) return;
    this.uom.getPackState(config.variantId, outletId, 'outlet').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: view => this.packState.set(view),
      error: () => this.packStateFailed.set(true),
    });
  }

  selectOption(option: SellingOption, quantity?: string): void {
    const changed = this.selectedOptionId() !== option.id;
    this.selectedOptionId.set(option.id);
    if (changed || quantity !== undefined) {
      this.quantityText.set(quantity ?? (option.kind === 'pack' ? '1' : ''));
      this.showQuantityError.set(false);
      this.includePackaging.set(false);
      const config = this.liveConfig();
      this.form.set(config ? allowedForms(option, config)[0] : undefined);
    }
    this.requestQuote();
  }

  setQuantity(value: string | number | null): void {
    this.quantityText.set(value === null ? '' : String(value));
    this.requestQuote();
  }

  setForm(form: PackStateKind): void {
    this.form.set(form);
    if (form !== 'loose') this.includePackaging.set(false);
  }

  private requestQuote(): void {
    const option = this.option();
    const config = this.liveConfig();
    const quantity = this.quantity();
    this.quote.set(null);
    if (!option || !config || quantity === null || this.quantityError()) {
      this.quoteLoading.set(false);
      this.quoteRequests.next(null);
      return;
    }
    this.quoteLoading.set(true);
    this.quoteRequests.next({ option, quantity, configId: config.configId });
  }

  private applyQuote(outcome: QuoteOutcome): void {
    this.quoteLoading.set(false);
    if (!outcome) return;
    if (outcome.quote) {
      this.notice.set('');
      this.quote.set(outcome.quote);
      return;
    }
    this.notice.set(uomErrorMessage(outcome.error) ?? 'Could not preview this quantity. The server will check it at checkout.');
    if (apiErrorCode(outcome.error) === 'UOM_CONFIG_CHANGED') this.reloadConfig();
  }

  /** The units changed while the picker was open: show the new ones and let the cashier choose again. */
  private reloadConfig(): void {
    this.uom.getVariantConfigs([this.config().variantId]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: configs => {
        const fresh = configs[0] ?? null;
        this.configChanged.emit(fresh);
        if (!fresh || fresh.sellingOptions.length === 0) {
          this.liveConfig.set(null);
          this.selectedOptionId.set(null);
          return;
        }
        this.selectedOptionId.set(null);
        this.adoptConfig(fresh, null);
      },
      error: () => undefined,
    });
  }

  optionDetail(option: SellingOption): string {
    if (option.kind === 'pack') return formatUnitQuantity(option.baseQuantity, this.baseSymbol());
    return `per ${option.unit?.symbol ?? this.baseSymbol()}`;
  }

  formLabel(form: PackStateKind): string {
    return form === 'sealed' ? 'Sealed' : form === 'prepared' ? 'Prepared' : 'Loose';
  }

  /** `3 packs available` or `120 ml available`, from the last pack-state read. */
  formAvailability(form: PackStateKind): string {
    const option = this.option();
    if (!option || !this.packState()) return '';
    const row = packStateRow(this.packState(), option, form);
    if (form === 'loose') return `${formatUnitQuantity(row?.baseQuantity ?? 0, this.baseSymbol())} available`;
    return `${row?.packCount ?? 0} pack(s) available`;
  }

  formatBase(quantity: number): string {
    return formatUnitQuantity(quantity, this.baseSymbol());
  }

  onOptionEnter(event: Event, option: SellingOption): void {
    event.preventDefault();
    this.selectOption(option);
    this.confirm();
  }

  /** Digit and arrow keys drive the option cards. A digit typed into a field stays in that field. */
  onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const inField = target?.tagName === 'INPUT' || target?.tagName === 'SELECT';
    if (inField || event.ctrlKey || event.altKey || event.metaKey) return;

    const options = this.options();
    if (/^[1-9]$/.test(event.key)) {
      const option = options[Number(event.key) - 1];
      if (option) {
        event.preventDefault();
        this.selectOption(option);
        this.focusAfterSelect(option);
      }
      return;
    }

    const delta = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
    if (delta !== 0 && options.length > 0) {
      event.preventDefault();
      const current = options.findIndex(option => option.id === this.selectedOptionId());
      const next = options[(current + delta + options.length) % options.length];
      this.selectOption(next);
      this.focusOption(next);
    }
  }

  private focusAfterSelect(option: SellingOption): void {
    if (option.kind === 'custom') this.focusQuantity();
    else this.focusOption(option);
  }

  private focusOption(option: SellingOption): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-option-id="${option.id}"]`)?.focus());
  }

  private focusQuantity(): void {
    setTimeout(() => {
      const field = this.host.nativeElement.querySelector<HTMLInputElement>('#pos-uom-quantity');
      field?.focus();
      field?.select();
    });
  }

  confirm(): void {
    const option = this.option();
    const config = this.liveConfig();
    const quantity = this.quantity();
    if (!option || !config) return;
    if (quantity === null || this.quantityError()) {
      this.showQuantityError.set(true);
      this.focusQuantity();
      return;
    }
    this.picked.emit({
      option,
      configId: config.configId,
      quantity,
      fulfilmentState: this.form(),
      includePackaging: this.showPackagingToggle() && this.includePackaging(),
    });
  }

  onOpenChange(open: boolean): void {
    if (!open) this.cancelled.emit();
  }
}
