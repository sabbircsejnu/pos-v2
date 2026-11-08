import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PosCatalogueItemDto, PosService } from '../../services/pos.service';
import { ExchangeProductPick, ExchangeProductSearchComponent } from './exchange-product-search.component';

function item(overrides: Partial<PosCatalogueItemDto>): PosCatalogueItemDto {
  return {
    variantId: 1, productId: 10, productName: 'Ari Wasenat Premium Borka', variantName: 'Default', productCode: 'ABA852P',
    sku: 'ABA852P-BLK-M', barcode: '8801', attributes: null, categoryId: 1, categoryName: 'Borka', listPrice: 2450,
    primaryImageThumb: null, stockQty: 5, ...overrides
  };
}

const borkaVariants: PosCatalogueItemDto[] = [
  item({ variantId: 1, sku: 'ABA852P-BLK-M', barcode: '8801', attributes: '{"Colour":"Black","Size":"M"}', stockQty: 5 }),
  item({ variantId: 2, sku: 'ABA852P-BLK-L', barcode: '8802', attributes: '{"Colour":"Black","Size":"L"}', stockQty: 0 }),
  item({ variantId: 3, sku: 'ABA852P-NVY-M', barcode: '8803', attributes: '{"Colour":"Navy","Size":"M"}', stockQty: 2 })
];

describe('ExchangeProductSearchComponent', () => {
  let fixture: ComponentFixture<ExchangeProductSearchComponent>;
  let component: ExchangeProductSearchComponent;
  let pos: { searchCatalogue: ReturnType<typeof vi.fn>; lookup: ReturnType<typeof vi.fn> };
  let picked: ExchangeProductPick[];

  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const input = () => (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
  const options = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="option"]')) as HTMLElement[];
  const key = (name: string) => {
    input().dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  };
  const type = async (value: string) => {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(300);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    vi.useFakeTimers();
    pos = {
      searchCatalogue: vi.fn(() => of({ success: true, data: borkaVariants })),
      lookup: vi.fn(() => of({ data: null }))
    };
    await TestBed.configureTestingModule({
      imports: [ExchangeProductSearchComponent],
      providers: [{ provide: PosService, useValue: pos }]
    }).compileComponents();
    fixture = TestBed.createComponent(ExchangeProductSearchComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('outletId', 7);
    picked = [];
    component.picked.subscribe(value => picked.push(value));
    fixture.detectChanges();
  });

  afterEach(() => vi.useRealTimers());

  it('searches the main product code on the server and lists every variant with price, stock and attributes, never cost', async () => {
    await type('ABA852P');
    expect(pos.searchCatalogue).toHaveBeenCalledWith('ABA852P', 7);
    expect(options().length).toBe(3);
    expect(text()).toContain('Ari Wasenat Premium Borka');
    expect(text()).toContain('Colour: Black | Size: M');
    expect(text()).toContain('Code ABA852P');
    expect(text()).toContain('SKU ABA852P-NVY-M');
    expect(text()).toContain('Barcode 8803');
    expect(text()).toContain('In stock 5');
    expect(text()).toContain('Out of stock');
    expect(text()).toMatch(/2,450\.00/);
    expect(text().toLowerCase()).not.toContain('cost');
  });

  it('debounces typing and needs at least two characters', async () => {
    input().value = 'A';
    input().dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(400);
    expect(pos.searchCatalogue).not.toHaveBeenCalled();
    input().value = 'AB';
    input().dispatchEvent(new Event('input'));
    input().value = 'ABA';
    input().dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(300);
    expect(pos.searchCatalogue).toHaveBeenCalledTimes(1);
    expect(pos.searchCatalogue).toHaveBeenCalledWith('ABA', 7);
  });

  it('puts an exact barcode or SKU match first', async () => {
    await type('8803');
    expect(options()[0].getAttribute('data-testid')).toBe('replacement-result-3');
  });

  it('adds an exact barcode scan on Enter at once, with its product code and attributes', async () => {
    pos.searchCatalogue.mockReturnValue(of({ success: true, data: [borkaVariants[0]] }));
    input().value = '8801';
    input().dispatchEvent(new Event('input'));
    key('Enter');
    await vi.advanceTimersByTimeAsync(0);
    expect(picked.map(p => p.variantId)).toEqual([1]);
    expect(picked[0]).toMatchObject({ productCode: 'ABA852P', attributes: '{"Colour":"Black","Size":"M"}' });
    expect(pos.lookup).not.toHaveBeenCalled();
    expect(component.query()).toBe('');
  });

  it('falls back to the exact POS lookup when the search finds nothing for a scanned code', async () => {
    pos.searchCatalogue.mockReturnValue(of({ success: true, data: [] }));
    pos.lookup.mockImplementation((code: string, _outlet: number, mode: string) =>
      of({ data: mode === 'barcode' && code === 'LEGACY-1' ? { variantId: 9, productName: 'Legacy item', sku: 'L1', stockQty: 2, inStock: true } : null }));
    input().value = 'LEGACY-1';
    input().dispatchEvent(new Event('input'));
    key('Enter');
    await vi.advanceTimersByTimeAsync(0);
    expect(picked.map(p => p.variantId)).toEqual([9]);
  });

  it('a scan typed over open results uses the scanned code, not the highlight left from the previous search (browser UAT)', async () => {
    await type('ABA852P');
    key('ArrowDown'); // highlight a variant of the previous results
    pos.searchCatalogue.mockReturnValue(of({ success: true, data: [{ ...borkaVariants[1], stockQty: 3 }] }));
    input().value = '8802';
    input().dispatchEvent(new Event('input'));
    key('Enter');
    await vi.advanceTimersByTimeAsync(0);
    expect(picked.map(p => p.variantId)).toEqual([2]);
  });

  it('shows the list when Enter finds no single exact code', async () => {
    input().value = 'borka';
    input().dispatchEvent(new Event('input'));
    key('Enter');
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(options().length).toBe(3);
    expect(picked).toEqual([]);
  });

  it('moves through results with the arrow keys and adds the highlighted variant with Enter', async () => {
    await type('ABA852P');
    key('ArrowDown');
    key('ArrowDown');
    expect(options()[2].getAttribute('aria-selected')).toBe('true');
    key('ArrowUp');
    key('ArrowDown');
    key('Enter');
    expect(picked.map(p => p.variantId)).toEqual([3]);
    expect(picked[0].effectivePrice).toBe(2450);
    expect(picked[0].productCode).toBe('ABA852P');
  });

  it('closes the list with Escape', async () => {
    await type('ABA852P');
    key('Escape');
    expect(options().length).toBe(0);
  });

  it('does not add an out-of-stock variant and says why', async () => {
    await type('ABA852P');
    options()[1].click();
    fixture.detectChanges();
    expect(picked).toEqual([]);
    expect(text()).toContain('is out of stock at this outlet');
  });

  it('ignores a slower, older response that arrives after a newer one', async () => {
    const first = new Subject<{ success: boolean; data: PosCatalogueItemDto[] }>();
    const second = new Subject<{ success: boolean; data: PosCatalogueItemDto[] }>();
    pos.searchCatalogue.mockReturnValueOnce(first).mockReturnValueOnce(second);
    await type('borka');
    await type('ABA852P-NVY');
    second.next({ success: true, data: [borkaVariants[2]] });
    first.next({ success: true, data: borkaVariants });
    fixture.detectChanges();
    expect(options().map(o => o.getAttribute('data-testid'))).toEqual(['replacement-result-3']);
  });

  it('shows a no-results message and an error with retry', async () => {
    pos.searchCatalogue.mockReturnValueOnce(of({ success: true, data: [] }));
    await type('ZZZ');
    expect(text()).toContain('No matching product');
    expect(text()).toContain('Nothing matches “ZZZ”');

    pos.searchCatalogue.mockReturnValueOnce(throwError(() => new Error('down')));
    await type('ZZZZ');
    expect(text()).toContain('Products could not be searched');
    pos.searchCatalogue.mockReturnValueOnce(of({ success: true, data: borkaVariants }));
    ((fixture.nativeElement as HTMLElement).querySelector('app-ui-state button') as HTMLButtonElement).click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(options().length).toBe(3);
  });

  it('labels the search box (label for = input id)', () => {
    const label = (fixture.nativeElement as HTMLElement).querySelector('label') as HTMLLabelElement;
    expect(label.getAttribute('for')).toBe(input().id);
  });

  it('exposes an accessible combobox tied to its listbox', async () => {
    await type('ABA852P');
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().getAttribute('aria-expanded')).toBe('true');
    const list = (fixture.nativeElement as HTMLElement).querySelector('[role="listbox"]')!;
    expect(input().getAttribute('aria-controls')).toBe(list.id);
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[0].id);
  });
});
