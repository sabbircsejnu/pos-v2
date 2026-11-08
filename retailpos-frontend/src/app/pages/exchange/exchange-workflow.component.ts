import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { UI_KIT } from '../../components/shared/ui';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { ExchangeProductPick, ExchangeProductSearchComponent } from './exchange-product-search.component';
import { formatVariantAttributes } from '../pos/pos-variant-attributes';
import {
  EXCHANGE_REASONS,
  ExchangeApprovalToken,
  ExchangeApprovalType,
  ExchangePreview,
  ExchangeReasonCode,
  ExchangeReceiptDocument,
  ExchangeRequest,
  ExchangeResult,
  ExchangeSourceInvoice,
  ExchangeSourceLine,
  ExchangeSourceKind,
  exchangeApprovalLabel,
  settlementDirectionLabel
} from '../../models/exchange.model';
import { VariantUomConfig } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { ConnectivityService } from '../../services/connectivity.service';
import { ExchangeService } from '../../services/exchange.service';
import { CashSessionDto, EligibleSalespersonDto, PosService, PosTerminalDto } from '../../services/pos.service';
import { SaleService } from '../../services/sale.service';
import { UomService, newIdempotencyKey } from '../../services/uom.service';
import { PosUomPickerComponent } from '../pos/pos-uom-picker.component';
import { PosUomSelection, cartLineKey, lineNeedsRepick } from '../pos/pos-uom';
import { PosTerminalAvailability, resolveTerminalSelection } from '../pos/pos-terminals';
import { applyReceiptPrintPage } from '../pos/receipt-print-page';
import { readExchangeError, requiredApprovalsFrom } from './exchange-errors';
import { ExchangeReceiptComponent } from './exchange-receipt.component';
import { SettlementDraft, buildSettlements, defaultSettlement, settlementError } from './exchange-settlement';
import { ExchangeSettlementPanelComponent } from './exchange-settlement-panel.component';

/** One returned invoice line the cashier selected. Quantity is in the line's base unit. */
export interface ReturnDraft {
  key: string;
  invoice: ExchangeSourceInvoice;
  line: ExchangeSourceLine;
  quantity: number | null;
  conditionSellable: boolean;
  packagingReusable: boolean;
}

/** One replacement line. The list price and stock are hints; the preview is authoritative. */
export interface ReplacementDraft {
  key: string;
  variantId: number;
  productName: string;
  variantName: string;
  /** Display only: main product code and readable variant attributes (Colour, Size…). */
  productCode?: string | null;
  variantLabel?: string;
  sku: string;
  listPrice: number | null;
  stockQty: number | null;
  quantity: number | null;
  discount: number | null;
  sellingOptionId?: number;
  expectedConfigVersionId?: number;
  fulfilmentState?: string;
  includePackaging: boolean;
  unitLabel?: string;
}

/** A replacement candidate from the product search (POS catalogue search or exact POS lookup). */
type PosLookupResult = ExchangeProductPick;

interface UomPickerState {
  product: PosLookupResult;
  config: VariantUomConfig;
}

type DiscountMode = 'none' | 'line' | 'invoice';
type SessionState = 'idle' | 'loading' | 'active' | 'none' | 'error';

export const WALK_IN_COMBINE_MESSAGE = 'Walk-in invoices cannot be combined (D-03). Exchange a walk-in invoice on its own; only invoices of the same registered customer can be combined.';
export const CONDITION_REQUIRED_MESSAGE = 'Confirm that every returned item is in good, sellable condition. Damaged or unsellable items cannot be exchanged.';
export const OFFLINE_MESSAGE = 'You are offline. Exchange needs a live connection; nothing is saved or queued until you are back online.';

const STEP_REVIEW = 4;
const STEP_SETTLE = 5;

/**
 * Product Exchange workflow (Exchange SRS §4–§17): invoice → returned items → replacements → reason → server review
 * → supervisor approvals and settlement → confirm. The browser never computes final amounts: totals, VAT, the
 * difference, settlement direction and required approvals all come from the server preview, and Confirm re-validates
 * everything. Online only (EX-28).
 */
@Component({
  selector: 'app-exchange-workflow',
  standalone: true,
  imports: [
    FormsModule, ReactiveFormsModule, RouterLink, AppCurrencyPipe, AppDatePipe, PosUomPickerComponent, ExchangeProductSearchComponent,
    ExchangeReceiptComponent, ExchangeSettlementPanelComponent, ...UI_KIT
  ],
  templateUrl: './exchange-workflow.component.html',
  styleUrls: ['./exchange.css']
})
export class ExchangeWorkflowComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly exchanges = inject(ExchangeService);
  private readonly pos = inject(PosService);
  private readonly sales = inject(SaleService);
  private readonly uom = inject(UomService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  readonly online = inject(ConnectivityService).online;

  private readonly invoiceField = viewChild<ElementRef<HTMLInputElement>>('invoiceField');
  private readonly returnScanField = viewChild<ElementRef<HTMLInputElement>>('returnScanField');
  private readonly productSearch = viewChild(ExchangeProductSearchComponent);

  readonly breadcrumbs = [{ label: 'POS', link: '/pos' }, { label: 'Product Exchange' }];
  readonly reasons = EXCHANGE_REASONS;
  readonly approvalLabel = exchangeApprovalLabel;
  readonly directionLabel = settlementDirectionLabel;

  // ---- Context: outlet, terminal, cash session, salesperson ----
  readonly outletId = signal<number | null>(null);
  readonly outletName = signal('');
  readonly cashierName = signal('');
  readonly terminals = signal<PosTerminalDto[]>([]);
  readonly terminalAvailability = signal<PosTerminalAvailability | 'loading' | 'error'>('loading');
  readonly terminalId = signal<number | null>(null);
  readonly session = signal<CashSessionDto | null>(null);
  readonly sessionState = signal<SessionState>('idle');
  readonly salespersons = signal<EligibleSalespersonDto[]>([]);
  readonly salespersonId = signal<number | null>(null);
  readonly canDiscount = computed(() => this.auth.hasPermission('sales.discount.apply'));

  readonly blockReason = computed(() => {
    if (!this.online()) return OFFLINE_MESSAGE;
    if (!this.outletId()) return 'Your account has no outlet assigned, so an exchange cannot be recorded here.';
    if (this.terminalAvailability() === 'none') return 'This outlet has no active POS terminal.';
    if (!this.terminalId()) return this.terminalAvailability() === 'loading' ? '' : 'Select the POS terminal for this exchange.';
    if (this.sessionState() === 'none') return 'No active cash session for this terminal. Open one on the POS screen first.';
    if (this.sessionState() === 'error') return 'The cash session could not be checked. Reload to try again.';
    return '';
  });

  // ---- Step navigation ----
  readonly activeStep = signal(0);
  readonly stepMessage = signal('');

  // ---- Step 1: source invoices ----
  readonly invoiceInput = signal('');
  readonly invoiceLoading = signal(false);
  readonly invoiceError = signal('');
  readonly invoices = signal<ExchangeSourceInvoice[]>([]);

  // ---- Step 2: returned items ----
  readonly returnScan = signal('');
  readonly returnError = signal('');
  readonly returns = signal<ReturnDraft[]>([]);
  readonly lineChoices = signal<Array<{ invoice: ExchangeSourceInvoice; line: ExchangeSourceLine }>>([]);
  readonly lineChoiceOpen = signal(false);

  // ---- Step 3: replacements ----
  readonly replacementError = signal('');
  readonly replacements = signal<ReplacementDraft[]>([]);
  readonly discountMode = signal<DiscountMode>('none');
  readonly invoiceDiscount = signal<number | null>(null);
  readonly uomPicker = signal<UomPickerState | null>(null);

  // ---- Step 4: reason ----
  readonly reasonForm = this.fb.nonNullable.group({
    reasonCode: this.fb.nonNullable.control<ExchangeReasonCode | ''>('', Validators.required),
    reasonOtherText: this.fb.nonNullable.control('', Validators.maxLength(500)),
    internalNote: this.fb.nonNullable.control('', Validators.maxLength(1000))
  });

  // ---- Step 5: server review ----
  readonly preview = signal<ExchangePreview | null>(null);
  private readonly previewSignature = signal<string | null>(null);
  readonly previewLoading = signal(false);
  readonly previewError = signal('');
  private previewSequence = 0;

  // ---- Approvals ----
  readonly requiredApprovals = signal<ExchangeApprovalType[]>([]);
  readonly approvalTokens = signal<ExchangeApprovalToken[]>([]);
  private refundApprovedFor: { method: string; amount: number } | null = null;
  readonly approvalOpen = signal(false);
  readonly supervisorLogin = signal('');
  readonly supervisorPassword = signal('');
  readonly approvalReasons = signal<Record<string, string>>({});
  readonly approvalBusy = signal(false);
  readonly approvalError = signal('');

  // ---- Settlement and confirm ----
  readonly settlement = signal<SettlementDraft>(defaultSettlement('none', 0));
  readonly confirmBusy = signal(false);
  readonly confirmError = signal('');
  /** One idempotency key per confirm payload: a retry of the same payload reuses it, any change makes a new one. */
  private confirmAttempt: { signature: string; key: string } | null = null;
  readonly result = signal<ExchangeResult | null>(null);
  readonly receipt = signal<ExchangeReceiptDocument | null>(null);
  readonly receiptError = signal('');
  readonly pdfBusy = signal(false);

  readonly returnsProblem = computed(() => this.returnsError(this.returns()));
  readonly replacementsProblem = computed(() => this.replacementsError());

  /** Valid approval tokens: a refund approval only counts for the method and amount it was granted for. */
  readonly usableTokens = computed(() => {
    const draft = this.settlement();
    const amount = this.preview()?.settlementAmount ?? 0;
    return this.approvalTokens().filter(token => token.type !== 'refund'
      || (this.refundApprovedFor?.method === draft.refundMethod && this.refundApprovedFor?.amount === amount));
  });

  readonly missingApprovals = computed(() =>
    this.requiredApprovals().filter(type => !this.usableTokens().some(token => token.type === type)));

  /** Types the supervisor dialog asks for now. A refund approval waits until the refund method is chosen. */
  readonly approvalTypesToRequest = computed(() => this.missingApprovals().filter(type =>
    type !== 'refund' || (this.preview()?.settlementDirection === 'refund' && !!this.settlement().refundMethod)));

  readonly settlementProblem = computed(() => {
    const preview = this.preview();
    return preview ? settlementError(preview.settlementDirection, preview.settlementAmount, this.settlement()) : '';
  });

  readonly canAdvance = (index: number): boolean => this.canLeaveStep(index);

  ngOnInit(): void {
    const user = this.auth.getUserValue();
    this.cashierName.set(user?.name ?? '');
    const outletId = user?.outletId ?? null;
    this.outletId.set(outletId);
    this.outletName.set(user?.outletName ?? '');
    if (outletId) this.loadTerminals(outletId);
    else this.terminalAvailability.set('none');

    const query = this.route.snapshot.queryParamMap;
    const invoice = query.get('invoice');
    const saleId = Number(query.get('saleId'));
    if (invoice) {
      this.invoiceInput.set(invoice);
      this.addInvoice();
    } else if (Number.isFinite(saleId) && saleId > 0) {
      this.preselectSale(saleId);
    }
  }

  // ============================================================== context

  private loadTerminals(outletId: number): void {
    this.pos.getTerminals(outletId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res: { data?: PosTerminalDto[] } | PosTerminalDto[] | null) => {
        const list = (Array.isArray(res) ? res : res?.data) ?? [];
        this.terminals.set(list);
        const selection = resolveTerminalSelection(list, this.terminalId());
        this.terminalAvailability.set(selection.availability);
        this.terminalId.set(selection.selectedId);
        this.loadSession();
      },
      error: () => {
        this.terminals.set([]);
        this.terminalAvailability.set('error');
        this.terminalId.set(null);
      }
    });
  }

  onTerminalChange(value: string | number | null): void {
    const id = Number(value);
    this.terminalId.set(Number.isFinite(id) && id > 0 ? id : null);
    this.invalidatePreview();
    this.loadSession();
  }

  private loadSession(): void {
    const outletId = this.outletId();
    const terminalId = this.terminalId();
    if (!outletId || !terminalId) {
      this.session.set(null);
      this.sessionState.set('idle');
      return;
    }
    this.sessionState.set('loading');
    this.pos.getActiveCashSession(outletId, terminalId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res: { data?: CashSessionDto | null } | null) => {
        const session = res?.data ?? null;
        this.session.set(session);
        this.sessionState.set(session ? 'active' : 'none');
        if (session) this.loadSalespersons();
      },
      error: () => {
        this.session.set(null);
        this.sessionState.set('error');
      }
    });
  }

  private loadSalespersons(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.pos.getSalespersons(outletId, this.terminalId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        const list = res?.data?.salespersons ?? [];
        this.salespersons.set(list);
        const current = this.salespersonId();
        if (current === null || !list.some(person => person.userId === current)) {
          this.salespersonId.set(res?.data?.defaultSalespersonUserId ?? null);
        }
      },
      error: () => this.salespersons.set([])
    });
  }

  onSalespersonChange(value: string | number | null): void {
    const id = Number(value);
    this.salespersonId.set(Number.isFinite(id) && id > 0 ? id : null);
    this.invalidatePreview();
  }

  private preselectSale(saleId: number): void {
    this.invoiceLoading.set(true);
    this.sales.getSaleById(saleId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res: { data?: { saleNumber?: string } } | null) => {
        this.invoiceLoading.set(false);
        const number = res?.data?.saleNumber;
        if (number) {
          this.invoiceInput.set(number);
          this.addInvoice();
        }
      },
      error: (err: unknown) => {
        this.invoiceLoading.set(false);
        this.invoiceError.set(readExchangeError(err).message);
      }
    });
  }

  // ============================================================== step 1: invoices

  addInvoice(): void {
    const number = this.invoiceInput().trim();
    this.invoiceError.set('');
    if (!number) {
      this.invoiceError.set('Scan or enter the invoice number from the receipt.');
      return;
    }
    if (!this.online()) {
      this.invoiceError.set(OFFLINE_MESSAGE);
      return;
    }
    if (this.invoiceLoading()) return;
    if (this.invoices().some(invoice => invoice.invoiceNumber.toLowerCase() === number.toLowerCase())) {
      this.invoiceError.set(`Invoice ${number} is already added.`);
      return;
    }
    this.invoiceLoading.set(true);
    this.exchanges.findSourceInvoice(number).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.invoiceLoading.set(false);
        const invoice = res?.data;
        if (!invoice) {
          this.invoiceError.set('No invoice with that number was found.');
          return;
        }
        const problem = this.invoiceProblem(invoice);
        if (problem) {
          this.invoiceError.set(problem);
          return;
        }
        this.invoices.update(list => [...list, invoice]);
        this.invoiceInput.set('');
        this.invalidatePreview();
        this.focus(this.invoiceField());
      },
      error: (err: unknown) => {
        this.invoiceLoading.set(false);
        this.invoiceError.set(readExchangeError(err).message);
      }
    });
  }

  /**
   * Identity of a selected source. An earlier exchange (EX-04) is a pseudo-invoice with saleId 0, so it is keyed by its
   * exchange number; a sale by its id.
   */
  sourceKey(invoice: ExchangeSourceInvoice): string {
    return invoice.kind === 'exchange' ? `x:${invoice.invoiceNumber}` : `s:${invoice.saleId}`;
  }

  /** The return source of a line: an original sale line, or an earlier exchange's replacement line (EX-04). */
  lineSourceKind(invoice: ExchangeSourceInvoice, line: ExchangeSourceLine): ExchangeSourceKind {
    return line.sourceKind ?? (invoice.kind === 'exchange' ? 'exchange_replacement_line' : 'sale_item');
  }

  private returnKey(invoice: ExchangeSourceInvoice, line: ExchangeSourceLine): string {
    return invoice.kind === 'exchange' ? `${this.sourceKey(invoice)}:${line.saleItemId}` : `${invoice.saleId}:${line.saleItemId}`;
  }

  /** D-03: one invoice of any customer, or several invoices of the same registered (non-walk-in) customer. */
  invoiceProblem(invoice: ExchangeSourceInvoice): string {
    if (this.invoices().some(existing => this.sourceKey(existing) === this.sourceKey(invoice))) return `Invoice ${invoice.invoiceNumber} is already added.`;
    if (!invoice.eligible) return `Invoice ${invoice.invoiceNumber} cannot be exchanged (status: ${invoice.status}).`;
    if (!invoice.lines.some(line => line.remainingQuantity > 0)) return `Nothing is left to return on invoice ${invoice.invoiceNumber}.`;
    const first = this.invoices()[0];
    if (first) {
      if (first.isWalkIn || invoice.isWalkIn || !first.customerId || !invoice.customerId) return WALK_IN_COMBINE_MESSAGE;
      if (first.customerId !== invoice.customerId) {
        return `Invoice ${invoice.invoiceNumber} belongs to a different customer. Only invoices of the same registered customer can be combined.`;
      }
    }
    return '';
  }

  removeInvoice(source: ExchangeSourceInvoice): void {
    const key = this.sourceKey(source);
    this.invoices.update(list => list.filter(invoice => this.sourceKey(invoice) !== key));
    this.returns.update(list => list.filter(draft => this.sourceKey(draft.invoice) !== key));
    this.invalidatePreview();
  }

  // ============================================================== step 2: returned items

  scanReturn(): void {
    const code = this.returnScan().trim().toLowerCase();
    this.returnError.set('');
    if (!code) return;
    const matches = this.invoices().flatMap(invoice => invoice.lines
      .filter(line => (line.barcode ?? '').toLowerCase() === code || (line.sku ?? '').toLowerCase() === code)
      .map(line => ({ invoice, line })));
    const usable = matches.filter(match => match.line.remainingQuantity > 0 && match.line.evidenceAvailable);
    if (usable.length === 0) {
      this.returnError.set(matches.length > 0
        ? 'That item has nothing left to return on the selected invoice(s), or it lacks the original price evidence.'
        : 'That item is not on the selected invoice(s). Product, SKU and variant must match the invoice line.');
      return;
    }
    this.returnScan.set('');
    if (usable.length === 1) {
      this.addReturn(usable[0].invoice, usable[0].line);
      return;
    }
    this.lineChoices.set(usable);
    this.lineChoiceOpen.set(true);
  }

  chooseLine(invoice: ExchangeSourceInvoice, line: ExchangeSourceLine): void {
    this.lineChoiceOpen.set(false);
    this.lineChoices.set([]);
    this.addReturn(invoice, line);
  }

  addReturn(invoice: ExchangeSourceInvoice, line: ExchangeSourceLine): void {
    this.returnError.set('');
    if (!line.evidenceAvailable) {
      this.returnError.set(`${line.productName} lacks the original price or cost evidence, so it cannot be exchanged.`);
      return;
    }
    if (line.remainingQuantity <= 0) {
      this.returnError.set(`${line.productName} has nothing left to return.`);
      return;
    }
    const key = this.returnKey(invoice, line);
    const existing = this.returns().find(draft => draft.key === key);
    if (existing) {
      const next = Math.min(line.remainingQuantity, Number(existing.quantity ?? 0) + 1);
      this.updateReturn(key, { quantity: next });
    } else {
      const quantity = line.remainingQuantity >= 1 ? 1 : line.remainingQuantity;
      this.returns.update(list => [...list, { key, invoice, line, quantity, conditionSellable: false, packagingReusable: false }]);
      this.invalidatePreview();
    }
    this.focus(this.returnScanField());
  }

  updateReturn(key: string, change: Partial<Pick<ReturnDraft, 'quantity' | 'conditionSellable' | 'packagingReusable'>>): void {
    this.returns.update(list => list.map(draft => draft.key === key ? { ...draft, ...change } : draft));
    this.invalidatePreview();
  }

  removeReturn(key: string): void {
    this.returns.update(list => list.filter(draft => draft.key !== key));
    this.invalidatePreview();
  }

  isReturned(invoice: ExchangeSourceInvoice, line: ExchangeSourceLine): boolean {
    return this.returns().some(draft => draft.key === this.returnKey(invoice, line));
  }

  returnsError(list: ReturnDraft[]): string {
    if (list.length === 0) return 'Add at least one returned item.';
    for (const draft of list) {
      const quantity = Number(draft.quantity);
      if (draft.quantity === null || !Number.isFinite(quantity) || quantity <= 0) {
        return `Enter a returned quantity greater than zero for ${draft.line.productName}.`;
      }
      if (quantity > draft.line.remainingQuantity) {
        return `The returned quantity for ${draft.line.productName} exceeds the remaining ${draft.line.remainingQuantity}.`;
      }
    }
    if (list.some(draft => !draft.conditionSellable)) return CONDITION_REQUIRED_MESSAGE;
    return '';
  }

  // ============================================================== step 3: replacements

  /** A product chosen in the replacement search (stock and selling price come from the server search). */
  onReplacementPicked(product: PosLookupResult): void {
    this.replacementError.set('');
    if (!this.online()) {
      this.replacementError.set(OFFLINE_MESSAGE);
      return;
    }
    this.routeReplacement(product);
  }

  private routeReplacement(product: PosLookupResult): void {
    this.uom.getVariantConfigs([product.variantId]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: configs => {
        const config = configs.find(item => item.variantId === product.variantId) ?? null;
        if (config && (config.sellingOptions.length > 0 || lineNeedsRepick({}, config))) {
          this.uomPicker.set({ product, config });
          return;
        }
        this.addPlainReplacement(product);
      },
      error: () => this.addPlainReplacement(product)
    });
  }

  private addPlainReplacement(product: PosLookupResult): void {
    const key = String(product.variantId);
    const existing = this.replacements().find(line => line.key === key);
    if (existing) {
      this.updateReplacement(key, { quantity: Number(existing.quantity ?? 0) + 1 });
    } else {
      this.replacements.update(list => [...list, this.newReplacement(product, key, 1)]);
      this.invalidatePreview();
    }
    this.focusProductSearch();
  }

  onUomPicked(selection: PosUomSelection): void {
    const picker = this.uomPicker();
    if (!picker) return;
    this.uomPicker.set(null);
    const key = cartLineKey({
      variantId: picker.product.variantId,
      sellingOptionId: selection.option.id,
      fulfilmentState: selection.fulfilmentState,
      includePackaging: selection.includePackaging
    });
    const line: ReplacementDraft = {
      ...this.newReplacement(picker.product, key, selection.quantity),
      listPrice: selection.option.price ?? null,
      sellingOptionId: selection.option.id,
      expectedConfigVersionId: selection.configId,
      fulfilmentState: selection.fulfilmentState,
      includePackaging: selection.includePackaging,
      unitLabel: selection.option.label
    };
    this.replacements.update(list => [...list.filter(item => item.key !== key), line]);
    this.invalidatePreview();
    this.focusProductSearch();
  }

  onUomPickerCancelled(): void {
    this.uomPicker.set(null);
    this.focusProductSearch();
  }

  onUomConfigChanged(config: VariantUomConfig | null): void {
    const picker = this.uomPicker();
    if (!picker) return;
    if (config) {
      this.uomPicker.set({ ...picker, config });
    } else {
      this.uomPicker.set(null);
      this.addPlainReplacement(picker.product);
    }
  }

  private newReplacement(product: PosLookupResult, key: string, quantity: number): ReplacementDraft {
    return {
      key,
      variantId: product.variantId,
      productName: product.productName,
      variantName: product.variantName ?? '',
      productCode: product.productCode ?? null,
      variantLabel: replacementVariantLabel(product),
      sku: product.sku,
      listPrice: product.effectivePrice ?? null,
      stockQty: product.stockQty ?? null,
      quantity,
      discount: null,
      includePackaging: false
    };
  }

  updateReplacement(key: string, change: Partial<Pick<ReplacementDraft, 'quantity' | 'discount'>>): void {
    this.replacements.update(list => list.map(line => line.key === key ? { ...line, ...change } : line));
    this.invalidatePreview();
  }

  removeReplacement(key: string): void {
    this.replacements.update(list => list.filter(line => line.key !== key));
    this.invalidatePreview();
  }

  /** EX-27: product-wise discounts or one invoice discount, never both. Switching clears the other kind. */
  setDiscountMode(mode: DiscountMode): void {
    this.discountMode.set(mode);
    if (mode !== 'line') this.replacements.update(list => list.map(line => ({ ...line, discount: null })));
    if (mode !== 'invoice') this.invoiceDiscount.set(null);
    this.invalidatePreview();
  }

  setInvoiceDiscount(value: unknown): void {
    this.invoiceDiscount.set(this.toNumber(value));
    this.invalidatePreview();
  }

  replacementsError(): string {
    const list = this.replacements();
    if (list.length === 0) return 'Add at least one replacement item.';
    for (const line of list) {
      const quantity = Number(line.quantity);
      if (line.quantity === null || !Number.isFinite(quantity) || quantity <= 0) {
        return `Enter a quantity greater than zero for ${line.productName}.`;
      }
      if (this.discountMode() === 'line' && line.discount !== null && (!Number.isFinite(line.discount) || line.discount < 0)) {
        return `Enter a non-negative discount for ${line.productName}.`;
      }
    }
    const invoiceDiscount = this.invoiceDiscount();
    if (this.discountMode() === 'invoice' && invoiceDiscount !== null && (!Number.isFinite(invoiceDiscount) || invoiceDiscount < 0)) {
      return 'Enter a non-negative invoice discount.';
    }
    return '';
  }

  // ============================================================== step 4: reason

  reasonError(): string {
    const value = this.reasonForm.getRawValue();
    if (!value.reasonCode) return 'Choose the exchange reason.';
    if (value.reasonCode === 'other' && !value.reasonOtherText.trim()) return 'Describe the reason when "Other" is selected.';
    if (this.reasonForm.invalid) return 'The reason details or internal note are too long.';
    return '';
  }

  onReasonChanged(): void {
    const control = this.reasonForm.controls.reasonOtherText;
    control.setValidators(this.reasonForm.controls.reasonCode.value === 'other'
      ? [Validators.required, Validators.maxLength(500)]
      : [Validators.maxLength(500)]);
    control.updateValueAndValidity();
    this.invalidatePreview();
  }

  // ============================================================== request and preview

  buildRequest(): ExchangeRequest | null {
    const outletId = this.outletId();
    const reason = this.reasonForm.getRawValue();
    if (!outletId || !reason.reasonCode) return null;
    const lineDiscounts = this.discountMode() === 'line';
    return {
      receivingOutletId: outletId,
      terminalId: this.terminalId(),
      // Only real sales are source sale ids; an earlier exchange is referenced through its replacement lines (EX-04).
      sourceSaleIds: this.invoices().filter(invoice => invoice.kind !== 'exchange').map(invoice => invoice.saleId),
      returns: this.returns().map(draft => ({
        sourceKind: this.lineSourceKind(draft.invoice, draft.line),
        sourceLineId: draft.line.saleItemId,
        quantity: Number(draft.quantity),
        conditionSellable: draft.conditionSellable,
        packagingReusable: draft.packagingReusable
      })),
      replacements: this.replacements().map(line => ({
        variantId: line.variantId,
        quantity: Number(line.quantity),
        discountAmount: lineDiscounts ? Number(line.discount ?? 0) : 0,
        ...(line.sellingOptionId ? {
          sellingOptionId: line.sellingOptionId,
          expectedConfigVersionId: line.expectedConfigVersionId,
          fulfilmentState: line.fulfilmentState
        } : {}),
        includePackaging: !!line.includePackaging
      })),
      invoiceDiscount: this.discountMode() === 'invoice' ? Number(this.invoiceDiscount() ?? 0) : 0,
      salespersonUserId: this.salespersonId(),
      reasonCode: reason.reasonCode,
      reasonOtherText: reason.reasonCode === 'other' ? reason.reasonOtherText.trim() : null,
      internalNote: reason.internalNote.trim() || null
    };
  }

  private signatureOf(request: ExchangeRequest | null): string | null {
    return request ? JSON.stringify(request) : null;
  }

  /** The preview still describes exactly what is on screen. */
  previewIsCurrent(): boolean {
    const signature = this.previewSignature();
    return !!this.preview() && signature !== null && signature === this.signatureOf(this.buildRequest());
  }

  private invalidatePreview(): void {
    if (this.preview() || this.previewSignature()) {
      this.preview.set(null);
      this.previewSignature.set(null);
      this.clearApprovals();
    }
    this.confirmError.set('');
  }

  private clearApprovals(): void {
    this.approvalTokens.set([]);
    this.requiredApprovals.set([]);
    this.refundApprovedFor = null;
  }

  runPreview(): void {
    this.previewError.set('');
    const blocked = this.blockReason();
    if (blocked) {
      this.previewError.set(blocked);
      return;
    }
    const request = this.buildRequest();
    if (!request) {
      this.previewError.set('Complete the previous steps first.');
      return;
    }
    const signature = this.signatureOf(request);
    const sequence = ++this.previewSequence;
    this.previewLoading.set(true);
    this.exchanges.preview(request).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        if (sequence !== this.previewSequence) return;
        this.previewLoading.set(false);
        const preview = res?.data ?? null;
        if (!preview) {
          this.previewError.set('The exchange could not be reviewed. Try again.');
          return;
        }
        const previous = this.preview();
        if (previous?.previewHash !== preview.previewHash) this.clearApprovals();
        if (!previous || previous.settlementDirection !== preview.settlementDirection || previous.settlementAmount !== preview.settlementAmount) {
          this.settlement.set(defaultSettlement(preview.settlementDirection, preview.settlementAmount));
        }
        this.preview.set(preview);
        this.previewSignature.set(signature);
        this.requiredApprovals.set([...(preview.requiredApprovals ?? [])]);
      },
      error: (err: unknown) => {
        if (sequence !== this.previewSequence) return;
        this.previewLoading.set(false);
        this.preview.set(null);
        this.previewSignature.set(null);
        this.previewError.set(readExchangeError(err).message);
      }
    });
  }

  returnName(sourceLineId: number, sourceKind?: ExchangeSourceKind): string {
    const draft = this.findReturn(sourceLineId, sourceKind);
    return draft ? this.lineLabel(draft.line) : `Line ${sourceLineId}`;
  }

  returnInvoice(sourceLineId: number, sourceKind?: ExchangeSourceKind): string {
    return this.findReturn(sourceLineId, sourceKind)?.invoice.invoiceNumber ?? '';
  }

  private findReturn(sourceLineId: number, sourceKind?: ExchangeSourceKind): ReturnDraft | undefined {
    return this.returns().find(item => item.line.saleItemId === sourceLineId
      && (!sourceKind || this.lineSourceKind(item.invoice, item.line) === sourceKind));
  }

  replacementName(index: number, variantId: number): string {
    const line = this.replacements()[index];
    const match = line && line.variantId === variantId ? line : this.replacements().find(item => item.variantId === variantId);
    if (!match) return `Variant ${variantId}`;
    return [match.productName, match.variantName && match.variantName.toLowerCase() !== 'default' ? match.variantName : '', match.unitLabel ?? '']
      .filter(Boolean).join(' · ');
  }

  lineLabel(line: ExchangeSourceLine): string {
    const variant = line.variantName && line.variantName.toLowerCase() !== 'default' ? ` · ${line.variantName}` : '';
    return `${line.productName}${variant}`;
  }

  private productLabel(product: PosLookupResult): string {
    return product.variantName ? `${product.productName} - ${product.variantName}` : product.productName;
  }

  phoneVerificationLabel(value: string | null | undefined): string {
    switch (value) {
      case 'not_required': return 'Not required';
      case 'skipped_walkin': return 'Skipped (walk-in invoice)';
      case 'matched_customer_record': return 'Matched the customer record (not a verified identity)';
      default: return value || '—';
    }
  }

  // ============================================================== steps

  /** What the current step still needs, without side effects (drives the disabled Next button and its hint). */
  stepPending(index: number = this.activeStep()): string {
    switch (index) {
      case 0: return this.invoices().length === 0 ? 'Add the original invoice to continue.' : '';
      case 1: return this.returnsProblem();
      case 2: return this.replacementsError();
      case 3: return this.reasonError();
      case STEP_REVIEW: return this.previewLoading() ? 'The review is still loading.' : this.previewIsCurrent() ? '' : 'Review the exchange with the server first.';
      default: return '';
    }
  }

  canLeaveStep(index: number): boolean {
    let problem = '';
    switch (index) {
      case 0: problem = this.invoices().length === 0 ? 'Add at least one invoice.' : ''; break;
      case 1: problem = this.returnsProblem(); break;
      case 2: problem = this.replacementsError(); break;
      case 3:
        this.reasonForm.markAllAsTouched();
        problem = this.reasonError();
        break;
      case STEP_REVIEW:
        problem = this.previewLoading() ? 'The review is still loading.' : this.previewIsCurrent() ? '' : 'Review the exchange with the server first.';
        break;
    }
    this.stepMessage.set(problem);
    return !problem;
  }

  onStepChange(index: number): void {
    this.stepMessage.set('');
    if ((index === STEP_REVIEW || index === STEP_SETTLE) && !this.previewIsCurrent() && !this.previewLoading()) {
      this.runPreview();
    }
  }

  // ============================================================== approvals

  openApprovals(): void {
    if (!this.online()) {
      this.approvalError.set(OFFLINE_MESSAGE);
      return;
    }
    const reasons: Record<string, string> = {};
    this.approvalTypesToRequest().forEach(type => { reasons[type] = this.approvalReasons()[type] ?? ''; });
    this.approvalReasons.set(reasons);
    this.approvalError.set('');
    this.supervisorPassword.set('');
    this.approvalOpen.set(true);
  }

  setApprovalReason(type: string, value: string): void {
    this.approvalReasons.update(current => ({ ...current, [type]: value }));
  }

  closeApprovals(): void {
    this.supervisorPassword.set('');
    this.approvalOpen.set(false);
  }

  submitApprovals(): void {
    if (this.approvalBusy()) return;
    const preview = this.preview();
    const request = this.buildRequest();
    const types = this.approvalTypesToRequest();
    const login = this.supervisorLogin().trim();
    const password = this.supervisorPassword();
    this.approvalError.set('');
    if (!preview || !request || !this.previewIsCurrent()) {
      this.approvalError.set('Review the exchange again before requesting approval.');
      this.supervisorPassword.set('');
      return;
    }
    if (!this.online()) {
      this.approvalError.set(OFFLINE_MESSAGE);
      return;
    }
    if (types.length === 0) {
      this.approvalError.set('There is nothing to approve right now.');
      return;
    }
    if (!login || !password) {
      this.approvalError.set('Enter the supervisor email and password.');
      return;
    }
    const reasons = this.approvalReasons();
    const missingReason = types.find(type => !(reasons[type] ?? '').trim());
    if (missingReason) {
      this.approvalError.set(`Enter a reason for: ${exchangeApprovalLabel(missingReason)}.`);
      return;
    }
    const includesRefund = types.includes('refund');
    const refundMethod = this.settlement().refundMethod;
    this.approvalBusy.set(true);
    this.exchanges.grantApprovals({
      preview: request,
      previewHash: preview.previewHash,
      approvals: types.map(type => ({ type, reason: reasons[type].trim() })),
      supervisorLogin: login,
      supervisorPassword: password,
      ...(includesRefund && refundMethod ? { refundAmount: preview.settlementAmount, refundMethod } : {})
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.supervisorPassword.set('');
        this.approvalBusy.set(false);
        const granted = res?.data ?? [];
        this.approvalTokens.update(current => [
          ...current.filter(token => !granted.some(item => item.type === token.type)),
          ...granted
        ]);
        if (includesRefund && refundMethod) this.refundApprovedFor = { method: refundMethod, amount: preview.settlementAmount };
        this.approvalReasons.set({});
        this.approvalOpen.set(false);
      },
      error: (err: unknown) => {
        this.supervisorPassword.set('');
        this.approvalBusy.set(false);
        this.approvalError.set(readExchangeError(err).message);
      }
    });
    // The request body has been handed to HttpClient: the password never stays in component state.
    this.supervisorPassword.set('');
  }

  // ============================================================== confirm

  canConfirm(): boolean {
    return this.online()
      && !this.blockReason()
      && !this.confirmBusy()
      && this.previewIsCurrent()
      && this.missingApprovals().length === 0
      && !this.settlementProblem();
  }

  confirmDisabledReason(): string {
    if (!this.online()) return OFFLINE_MESSAGE;
    const blocked = this.blockReason();
    if (blocked) return blocked;
    if (!this.previewIsCurrent()) return 'Review the exchange with the server first.';
    if (this.missingApprovals().length > 0) return 'Supervisor approval is still required.';
    return this.settlementProblem();
  }

  confirm(): void {
    if (this.confirmBusy() || !this.canConfirm()) return;
    const preview = this.preview()!;
    const request = this.buildRequest()!;
    const payload = {
      ...request,
      previewHash: preview.previewHash,
      settlements: buildSettlements(preview.settlementDirection, preview.settlementAmount, this.settlement()),
      approvalTokens: this.usableTokens().map(token => token.approvalToken)
    };
    const signature = JSON.stringify(payload);
    if (!this.confirmAttempt || this.confirmAttempt.signature !== signature) {
      this.confirmAttempt = { signature, key: newIdempotencyKey('exchange') };
    }
    this.confirmError.set('');
    this.confirmBusy.set(true);
    this.exchanges.confirm({ ...payload, idempotencyKey: this.confirmAttempt.key })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: res => {
          this.confirmBusy.set(false);
          this.confirmAttempt = null;
          const result = res?.data ?? null;
          if (!result) {
            this.confirmError.set('The exchange result was not returned. Check Exchange History before trying again.');
            return;
          }
          this.result.set(result);
          this.loadReceipt(result.exchangeId);
        },
        error: (err: unknown) => {
          this.confirmBusy.set(false);
          this.handleConfirmError(err);
        }
      });
  }

  private handleConfirmError(err: unknown): void {
    const info = readExchangeError(err);
    this.confirmError.set(info.message);
    switch (info.code) {
      case 'EXCHANGE_PREVIEW_STALE':
        // The server recalculated different values: drop the stale review and its approvals, and review again.
        this.preview.set(null);
        this.previewSignature.set(null);
        this.clearApprovals();
        this.activeStep.set(STEP_REVIEW);
        this.runPreview();
        break;
      case 'EXCHANGE_APPROVAL_REQUIRED': {
        const required = requiredApprovalsFrom(info.data) as ExchangeApprovalType[];
        if (required.length > 0) {
          this.requiredApprovals.update(current => [...new Set([...current, ...required])]);
          this.approvalTokens.update(tokens => tokens.filter(token => !required.includes(token.type)));
        }
        break;
      }
      case 'EXCHANGE_APPROVAL_INVALID':
        this.approvalTokens.set([]);
        this.refundApprovedFor = null;
        break;
      case 'EXCHANGE_CASH_INSUFFICIENT':
        this.settlement.update(draft => ({ ...draft, refundMethod: null, refundConfirmed: false }));
        break;
    }
  }

  // ============================================================== completion

  private loadReceipt(exchangeId: number): void {
    this.receiptError.set('');
    this.exchanges.getReceiptDocument(exchangeId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => this.receipt.set(res?.data ?? null),
      error: (err: unknown) => this.receiptError.set(readExchangeError(err).message)
    });
  }

  /** A print failure never undoes the exchange; the attempt is recorded for the audit trail. */
  printReceipt(): void {
    const result = this.result();
    if (!result || !this.receipt()) return;
    let succeeded = true;
    try {
      applyReceiptPrintPage('80mm');
      window.print();
    } catch {
      succeeded = false;
    }
    this.exchanges.recordPrintAttempt(result.exchangeId, { terminalId: this.terminalId(), succeeded })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined });
  }

  downloadPdf(): void {
    const result = this.result();
    if (!result || this.pdfBusy()) return;
    this.pdfBusy.set(true);
    this.exchanges.getReceiptPdf(result.exchangeId, 'thermal').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        this.pdfBusy.set(false);
        downloadBlob(blob, `exchange-${result.exchangeNumber}.pdf`);
      },
      error: (err: unknown) => {
        this.pdfBusy.set(false);
        this.receiptError.set(readExchangeError(err).message);
      }
    });
  }

  newExchange(): void {
    this.invoices.set([]);
    this.returns.set([]);
    this.replacements.set([]);
    this.discountMode.set('none');
    this.invoiceDiscount.set(null);
    this.reasonForm.reset({ reasonCode: '', reasonOtherText: '', internalNote: '' });
    this.preview.set(null);
    this.previewSignature.set(null);
    this.clearApprovals();
    this.settlement.set(defaultSettlement('none', 0));
    this.confirmAttempt = null;
    this.confirmError.set('');
    this.previewError.set('');
    this.invoiceError.set('');
    this.invoiceInput.set('');
    this.result.set(null);
    this.receipt.set(null);
    this.activeStep.set(0);
    void this.router.navigate(['/pos/exchange']);
  }

  // ============================================================== helpers

  toNumber(value: unknown): number | null {
    if (value === null || value === undefined || value === '') return null;
    const number = Number(value);
    return Number.isFinite(number) ? number : null;
  }

  onEnter(event: Event, action: () => void): void {
    event.preventDefault();
    action();
  }

  private focusProductSearch(): void {
    setTimeout(() => this.productSearch()?.focus(), 0);
  }

  private focus(ref: ElementRef<HTMLInputElement> | undefined): void {
    if (ref) setTimeout(() => ref.nativeElement.focus(), 0);
  }
}

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Readable variant of a replacement: its attributes (Colour: Black | Size: M), else a non-default variant name. */
export function replacementVariantLabel(product: Pick<ExchangeProductPick, 'attributes' | 'variantName'>): string {
  const attributes = formatVariantAttributes(product.attributes);
  if (attributes) return attributes;
  const name = (product.variantName ?? '').trim();
  return name && !['default', 'standard'].includes(name.toLowerCase()) ? name : '';
}
