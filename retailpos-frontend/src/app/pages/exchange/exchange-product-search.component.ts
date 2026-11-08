import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subscription, catchError, map, of, switchMap, timer } from 'rxjs';
import { PosCatalogueItemDto, PosService } from '../../services/pos.service';
import { ProductImageService } from '../../services/product-image.service';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT } from '../../components/shared/ui';
import { formatVariantAttributes } from '../pos/pos-variant-attributes';

/** A replacement candidate handed to the exchange workflow (the same shape as a POS lookup result). */
export interface ExchangeProductPick {
  variantId: number;
  productId?: number;
  productName: string;
  variantName?: string | null;
  productCode?: string | null;
  sku: string;
  barcode?: string | null;
  attributes?: string | null;
  effectivePrice?: number | null;
  stockQty?: number | null;
  inStock?: boolean;
}

type SearchState = 'idle' | 'loading' | 'results' | 'empty' | 'error';

const MIN_QUERY = 2;
const DEBOUNCE_MS = 250;

/**
 * Replacement product search for Product Exchange. Reuses the POS search endpoints: `GET /api/pos/catalogue?q=`
 * (server-side, business-scoped, partial match on product name, variant name, SKU, barcode and main product code, with
 * stock at the receiving outlet) and the exact `GET /api/pos/lookup` used by POS scanning. Selling price and stock come
 * from the server; cost is never requested or shown. Only the latest request may update the list.
 */
@Component({
  selector: 'app-exchange-product-search',
  standalone: true,
  imports: [AppCurrencyPipe, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="xps" (focusout)="onFocusOut($event)">
      <label class="xps__label" for="exchange-replacement-search">Find replacement product</label>
      <div class="xps__bar">
        <input #searchField appUiControl class="xps__input" type="search" autocomplete="off" spellcheck="false"
          id="exchange-replacement-search" name="replacementSearch"
          placeholder="Scan barcode or type product code, SKU or name"
          role="combobox" aria-autocomplete="list" [attr.aria-expanded]="open()" [attr.aria-controls]="listId"
          [attr.aria-activedescendant]="activeOptionId()" [attr.aria-describedby]="hintId"
          [disabled]="disabled()" [value]="query()"
          (input)="onInput($any($event.target).value)" (keydown)="onKeydown($event)" (focus)="reopen()" />
        @if (state() === 'loading') {
          <span class="xps__spinner" role="status" aria-label="Searching"><i class="fas fa-spinner fa-spin" aria-hidden="true"></i></span>
        }
      </div>
      <p class="xps__hint" [id]="hintId">Enter adds an exact barcode or SKU match at once. Use ↑ ↓ and Enter to choose from the list.</p>

      @if (open()) {
        <div class="xps__panel">
          @switch (state()) {
            @case ('loading') {
              @if (results().length === 0) {
                <app-ui-state kind="loading" title="Searching products…" [compact]="true" />
              }
            }
            @case ('error') {
              <app-ui-state kind="error" title="Products could not be searched" [message]="error()" [compact]="true">
                <button appUiButton type="button" size="sm" variant="outline" (click)="retry()">Try again</button>
              </app-ui-state>
            }
            @case ('empty') {
              <app-ui-state kind="empty" title="No matching product"
                [message]="'Nothing matches “' + lastQuery() + '”. Check the code, or search by product name.'" [compact]="true" />
            }
          }
          @if (results().length > 0) {
            <ul class="xps__list" role="listbox" [id]="listId" aria-label="Matching products">
              @for (item of results(); track item.variantId; let i = $index) {
                <li role="option" class="xps__option" [id]="optionId(i)"
                  [class.is-active]="i === activeIndex()" [class.is-unavailable]="!hasStock(item)"
                  [attr.aria-selected]="i === activeIndex()" [attr.aria-disabled]="!hasStock(item)"
                  [attr.data-testid]="'replacement-result-' + item.variantId"
                  (mousedown)="$event.preventDefault()" (mouseenter)="activeIndex.set(i)" (click)="choose(item)">
                  <span class="xps__thumb" aria-hidden="true">
                    @if (item.primaryImageThumb) {
                      <img [src]="imageUrl(item.primaryImageThumb)" alt="" loading="lazy" (error)="$any($event.target).remove()" />
                    }
                    <i class="fas fa-box"></i>
                  </span>
                  <span class="xps__main">
                    <span class="xps__name">{{ item.productName }}</span>
                    <span class="xps__variant">{{ variantLabel(item) }}</span>
                    <span class="xps__codes">
                      <span>Code <strong>{{ item.productCode || '—' }}</strong></span>
                      <span>SKU <strong>{{ item.sku || '—' }}</strong></span>
                      @if (item.barcode) { <span>Barcode <strong>{{ item.barcode }}</strong></span> }
                    </span>
                  </span>
                  <span class="xps__side">
                    <span class="xps__price">{{ item.listPrice | appCurrency }}</span>
                    @if (hasStock(item)) {
                      <app-ui-badge variant="success">In stock {{ item.stockQty }}</app-ui-badge>
                    } @else {
                      <app-ui-badge variant="danger">Out of stock</app-ui-badge>
                    }
                  </span>
                </li>
              }
            </ul>
          }
        </div>
      }
      @if (message()) {
        <p class="xps__message" role="alert">{{ message() }}</p>
      }
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .xps { position: relative; display: flex; flex-direction: column; gap: var(--rp-form-label-gap); }
    .xps__label { font-size: var(--rp-text-label-size); line-height: var(--rp-text-label-line); font-weight: var(--rp-font-weight-medium); color: var(--rp-color-text-primary); }
    .xps__bar { position: relative; display: flex; align-items: center; }
    /* The shared search control draws the magnifier; room is kept on the right for the busy indicator. */
    .xps__input { width: 100%; padding-inline-end: var(--rp-space-10); }
    .xps__spinner { position: absolute; inset-inline-end: var(--rp-space-3); color: var(--rp-color-text-secondary); }
    .xps__hint, .xps__message { margin: 0; font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); color: var(--rp-color-text-secondary); }
    .xps__message { color: var(--rp-color-status-danger-fg); }
    .xps__panel {
      border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-card); background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-popover); overflow: hidden;
    }
    .xps__list { list-style: none; margin: 0; padding: var(--rp-space-1); max-height: 26rem; overflow-y: auto; }
    .xps__option {
      display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: var(--rp-space-3);
      padding: var(--rp-space-2) var(--rp-space-3); border-radius: var(--rp-radius-control); cursor: pointer;
    }
    .xps__option + .xps__option { margin-top: var(--rp-space-1); }
    .xps__option.is-active { background: var(--rp-color-primary-soft); outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring); outline-offset: calc(-1 * var(--rp-focus-ring-width)); }
    .xps__option.is-unavailable { cursor: not-allowed; opacity: 0.65; }
    .xps__thumb {
      position: relative; display: grid; place-items: center; width: var(--rp-space-12); height: var(--rp-space-12);
      border-radius: var(--rp-radius-md); background: var(--rp-color-surface-muted); color: var(--rp-color-text-secondary); overflow: hidden;
    }
    .xps__thumb img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
    .xps__main { display: flex; flex-direction: column; gap: var(--rp-space-0); min-width: 0; }
    .xps__name { font-weight: var(--rp-font-weight-semibold); color: var(--rp-color-text-primary); overflow-wrap: anywhere; }
    .xps__variant { font-size: var(--rp-text-helper-size); color: var(--rp-color-text-primary); overflow-wrap: anywhere; }
    .xps__codes { display: flex; flex-wrap: wrap; gap: var(--rp-space-1) var(--rp-space-3); font-size: var(--rp-text-helper-size); color: var(--rp-color-text-secondary); }
    .xps__codes strong { font-weight: var(--rp-font-weight-medium); color: var(--rp-color-text-primary); font-family: var(--rp-font-family-mono); }
    .xps__side { display: flex; flex-direction: column; align-items: flex-end; gap: var(--rp-space-1); white-space: nowrap; }
    .xps__price { font-weight: var(--rp-font-weight-semibold); color: var(--rp-color-text-primary); font-variant-numeric: tabular-nums; }
    @media (max-width: 40rem) {
      .xps__option { grid-template-columns: auto minmax(0, 1fr); }
      .xps__side { grid-column: 2; flex-direction: row; align-items: center; justify-content: space-between; }
    }
  `]
})
export class ExchangeProductSearchComponent {
  private readonly pos = inject(PosService);
  private readonly images = inject(ProductImageService);
  private readonly destroyRef = inject(DestroyRef);

  readonly outletId = input<number | null>(null);
  readonly disabled = input(false);
  readonly picked = output<ExchangeProductPick>();

  private static nextId = 0;
  private readonly uid = ExchangeProductSearchComponent.nextId++;
  readonly listId = `xps-list-${this.uid}`;
  readonly hintId = `xps-hint-${this.uid}`;

  private readonly searchField = viewChild<ElementRef<HTMLInputElement>>('searchField');

  readonly query = signal('');
  readonly lastQuery = signal('');
  readonly results = signal<PosCatalogueItemDto[]>([]);
  readonly state = signal<SearchState>('idle');
  readonly error = signal('');
  readonly message = signal('');
  readonly activeIndex = signal(-1);
  readonly open = signal(false);
  readonly activeOptionId = computed(() => (this.open() && this.activeIndex() >= 0 ? this.optionId(this.activeIndex()) : null));

  /** Every request carries a sequence number; only the latest may change the list (stale responses are dropped). */
  private sequence = 0;
  private pending?: Subscription;

  optionId(index: number): string {
    return `${this.listId}-option-${index}`;
  }

  imageUrl(path: string): string {
    return this.images.resolveUrl(path);
  }

  variantLabel(item: PosCatalogueItemDto): string {
    const attributes = formatVariantAttributes(item.attributes);
    if (attributes) return attributes;
    const name = (item.variantName || '').trim();
    return name && name.toLowerCase() !== 'default' && name.toLowerCase() !== 'standard' ? name : 'Standard variant';
  }

  hasStock(item: PosCatalogueItemDto): boolean {
    return Number(item.stockQty ?? 0) > 0;
  }

  focus(): void {
    this.searchField()?.nativeElement.focus();
  }

  onInput(value: string): void {
    this.query.set(value);
    this.message.set('');
    // A highlight belongs to the results of the previous text: drop it, so a scan + Enter uses the scanned code.
    this.activeIndex.set(-1);
    const term = value.trim();
    if (term.length < MIN_QUERY) {
      this.cancel();
      this.results.set([]);
      this.state.set('idle');
      this.open.set(false);
      return;
    }
    this.search(term, DEBOUNCE_MS);
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        if (count === 0) return;
        event.preventDefault();
        this.open.set(true);
        this.activeIndex.set((this.activeIndex() + 1) % count);
        this.scrollActiveIntoView();
        break;
      case 'ArrowUp':
        if (count === 0) return;
        event.preventDefault();
        this.open.set(true);
        this.activeIndex.set(this.activeIndex() <= 0 ? count - 1 : this.activeIndex() - 1);
        this.scrollActiveIntoView();
        break;
      case 'Enter':
        event.preventDefault();
        this.onEnter();
        break;
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          this.open.set(false);
          this.activeIndex.set(-1);
        }
        break;
    }
  }

  reopen(): void {
    if (this.results().length > 0 || this.state() === 'empty' || this.state() === 'error') this.open.set(true);
  }

  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (!next || !(event.currentTarget as HTMLElement).contains(next)) this.open.set(false);
  }

  retry(): void {
    const term = this.query().trim();
    if (term.length >= MIN_QUERY) this.search(term, 0);
  }

  choose(item: PosCatalogueItemDto): void {
    if (!this.hasStock(item)) {
      this.message.set(`${item.productName} (${this.variantLabel(item)}) is out of stock at this outlet.`);
      return;
    }
    this.emit({
      variantId: item.variantId,
      productId: item.productId,
      productName: item.productName,
      variantName: item.variantName,
      productCode: item.productCode,
      sku: item.sku,
      barcode: item.barcode,
      attributes: item.attributes,
      effectivePrice: item.listPrice,
      stockQty: item.stockQty ?? null,
      inStock: true
    });
  }

  /**
   * Enter: a highlighted result wins. Otherwise the text is treated as a scan: the server search runs at once and a
   * single exact barcode/SKU match is added straight away (with its code and attributes); without one, the exact POS
   * lookup is tried, and failing that the matching list is shown.
   */
  private onEnter(): void {
    const index = this.activeIndex();
    const list = this.results();
    if (this.open() && index >= 0 && index < list.length) {
      this.choose(list[index]);
      return;
    }
    const term = this.query().trim();
    const outletId = this.outletId();
    if (!term || !outletId) return;
    // Only results of this exact text count; a list still showing an earlier search is stale.
    const current = this.state() === 'results' && this.lastQuery() === term;
    const exactInList = current ? list.filter(item => this.isExactCode(item, term)) : [];
    if (exactInList.length === 1) {
      this.choose(exactInList[0]);
      return;
    }
    const id = this.begin(term);
    this.pending = this.pos.searchCatalogue(term, outletId).pipe(
      map(response => response?.data ?? []),
      switchMap(items => {
        const exact = items.filter(item => this.isExactCode(item, term));
        return exact.length === 1 || items.length > 0
          ? of({ items, exact: exact.length === 1 ? exact[0] : null, fallback: null as ExchangeProductPick | null })
          : this.exactLookup(term, outletId).pipe(map(found => ({ items, exact: null, fallback: found })));
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: ({ items, exact, fallback }) => {
        if (id !== this.sequence) return;
        if (exact) {
          this.state.set('idle');
          this.choose(exact);
          return;
        }
        if (fallback) {
          this.state.set('idle');
          if (fallback.inStock === false || Number(fallback.stockQty ?? 1) <= 0) {
            this.message.set(`${fallback.productName} is out of stock at this outlet.`);
            return;
          }
          this.emit(fallback);
          return;
        }
        const ranked = this.rank(items, term);
        this.results.set(ranked);
        this.state.set(ranked.length > 0 ? 'results' : 'empty');
        this.activeIndex.set(ranked.length > 0 ? 0 : -1);
        this.open.set(true);
      },
      error: () => {
        if (id !== this.sequence) return;
        this.results.set([]);
        this.error.set('Check the connection and try again.');
        this.state.set('error');
        this.open.set(true);
      }
    });
  }

  private search(term: string, delay: number): void {
    const outletId = this.outletId();
    if (!outletId) return;
    const id = this.begin(term);
    this.pending = timer(delay).pipe(
      switchMap(() => this.pos.searchCatalogue(term, outletId)),
      map(response => response?.data ?? []),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: items => {
        if (id !== this.sequence) return;
        const ranked = this.rank(items, term);
        this.results.set(ranked);
        this.state.set(ranked.length > 0 ? 'results' : 'empty');
        this.activeIndex.set(ranked.length > 0 ? 0 : -1);
        this.open.set(true);
      },
      error: () => {
        if (id !== this.sequence) return;
        this.results.set([]);
        this.error.set('Check the connection and try again.');
        this.state.set('error');
        this.open.set(true);
      }
    });
  }

  private begin(term: string): number {
    this.cancel();
    this.lastQuery.set(term);
    this.error.set('');
    this.state.set('loading');
    return ++this.sequence;
  }

  private cancel(): void {
    this.sequence++;
    this.pending?.unsubscribe();
    this.pending = undefined;
  }

  private exactLookup(code: string, outletId: number): Observable<ExchangeProductPick | null> {
    const lookup = (mode: 'barcode' | 'sku') => this.pos.lookup(code, outletId, mode).pipe(
      map((response: { data?: ExchangeProductPick | null } | null) => response?.data ?? null),
      catchError(() => of(null)));
    return lookup('barcode').pipe(switchMap(found => (found ? of(found) : lookup('sku'))));
  }

  /** Exact barcode or SKU first (a scan), then an exact main product code, then prefix matches; server order otherwise. */
  private rank(items: PosCatalogueItemDto[], term: string): PosCatalogueItemDto[] {
    const q = term.toLowerCase();
    const score = (item: PosCatalogueItemDto): number => {
      if (this.isExactCode(item, term)) return 0;
      if ((item.productCode ?? '').toLowerCase() === q) return 1;
      if ([item.productCode, item.sku, item.barcode, item.productName].some(value => (value ?? '').toLowerCase().startsWith(q))) return 2;
      return 3;
    };
    return items.map((item, index) => ({ item, index, score: score(item) }))
      .sort((a, b) => a.score - b.score || a.index - b.index)
      .map(entry => entry.item);
  }

  private isExactCode(item: PosCatalogueItemDto, term: string): boolean {
    const q = term.toLowerCase();
    return (item.barcode ?? '').toLowerCase() === q || item.sku.toLowerCase() === q;
  }

  private emit(pick: ExchangeProductPick): void {
    this.cancel();
    this.picked.emit(pick);
    this.query.set('');
    this.results.set([]);
    this.state.set('idle');
    this.open.set(false);
    this.activeIndex.set(-1);
    this.message.set('');
    this.focus();
  }

  private scrollActiveIntoView(): void {
    queueMicrotask(() => document.getElementById(this.optionId(this.activeIndex()))?.scrollIntoView({ block: 'nearest' }));
  }
}
