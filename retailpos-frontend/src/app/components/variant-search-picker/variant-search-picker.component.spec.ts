import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { VariationService } from '../../services/variation.service';
import { AuthService } from '../../services/auth.service';
import { VariantSearchItem, VariantSearchPickerComponent } from './variant-search-picker.component';

const rows = [
  { id: 1, productName: 'Basmati Rice', sku: 'RICE-1', productCode: 'P1', name: '5kg', stockQuantity: 4 },
  { id: 2, productName: 'Brown Rice', sku: 'RICE-2', productCode: 'P2', name: '1kg', stockQuantity: 0 }
];

describe('VariantSearchPickerComponent', () => {
  let fixture: ComponentFixture<VariantSearchPickerComponent>;
  let cmp: VariantSearchPickerComponent;
  let searchVariants: ReturnType<typeof vi.fn>;
  let getAllVariations: ReturnType<typeof vi.fn>;
  let granted: string[];
  const root = () => fixture.nativeElement as HTMLElement;
  const input = () => root().querySelector('input') as HTMLInputElement;
  const options = () => Array.from(root().querySelectorAll<HTMLElement>('[role="option"]'));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const key = (name: string) => {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    input().dispatchEvent(event);
    return event;
  };
  const showResults = async () => {
    input().dispatchEvent(new Event('focus'));
    cmp.results.set(rows.map(r => ({ ...r })) as VariantSearchItem[]);
    await settle();
  };

  beforeEach(async () => {
    searchVariants = vi.fn(() => of({ data: rows }));
    getAllVariations = vi.fn(() => of([]));
    granted = ['variations.view'];
    TestBed.configureTestingModule({
      imports: [VariantSearchPickerComponent],
      providers: [
        { provide: ProductService, useValue: { searchVariants } },
        { provide: ProductImageService, useValue: { resolveUrl: (p?: string | null) => p ?? '' } },
        { provide: VariationService, useValue: { getAllVariations } },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => granted.includes(permission) } }
      ]
    });
    fixture = TestBed.createComponent(VariantSearchPickerComponent);
    cmp = fixture.componentInstance;
    await settle();
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('loads variation display metadata only for users who hold variations.view', async () => {
    expect(getAllVariations).toHaveBeenCalledTimes(1);

    TestBed.resetTestingModule();
    getAllVariations.mockClear();
    granted = ['inventory.view'];
    TestBed.configureTestingModule({
      imports: [VariantSearchPickerComponent],
      providers: [
        { provide: ProductService, useValue: { searchVariants } },
        { provide: ProductImageService, useValue: { resolveUrl: (p?: string | null) => p ?? '' } },
        { provide: VariationService, useValue: { getAllVariations } },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => granted.includes(permission) } }
      ]
    });
    TestBed.createComponent(VariantSearchPickerComponent).detectChanges();
    expect(getAllVariations).not.toHaveBeenCalled();
  });

  it('skips variation metadata when the caller disables it', () => {
    TestBed.resetTestingModule();
    getAllVariations.mockClear();
    TestBed.configureTestingModule({
      imports: [VariantSearchPickerComponent],
      providers: [
        { provide: ProductService, useValue: { searchVariants } },
        { provide: ProductImageService, useValue: { resolveUrl: (p?: string | null) => p ?? '' } },
        { provide: VariationService, useValue: { getAllVariations } },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => granted.includes(permission) } }
      ]
    });
    const metadataDisabledFixture = TestBed.createComponent(VariantSearchPickerComponent);
    metadataDisabledFixture.componentRef.setInput('useVariationMetadata', false);
    metadataDisabledFixture.detectChanges();

    expect(getAllVariations).not.toHaveBeenCalled();
  });

  it('labels the combobox with the visible label and uses the shared control class', () => {
    const label = root().querySelector('label')!;
    expect(label.getAttribute('for')).toBe(input().id);
    expect(label.textContent).toContain('Search Product');
    expect(input().classList).toContain('ui-control');
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().getAttribute('aria-expanded')).toBe('false');
  });

  it('debounces typing, ignores queries shorter than 2 characters and searches with the location', async () => {
    fixture.componentRef.setInput('locationId', 7);
    fixture.componentRef.setInput('locationType', 'Outlet');
    await settle();
    vi.useFakeTimers();

    input().dispatchEvent(new Event('focus'));
    input().value = 'b';
    input().dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(300);
    expect(searchVariants).not.toHaveBeenCalled();

    for (const value of ['ba', 'bas', 'basm']) {
      input().value = value;
      input().dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(100);
    }
    vi.advanceTimersByTime(300);
    expect(searchVariants).toHaveBeenCalledTimes(1);
    expect(searchVariants).toHaveBeenCalledWith('basm', 1, 20, 7, 'Outlet');
    expect(cmp.results().map(r => r.id)).toEqual([1, 2]);
  });

  it('renders results as a listbox of options with product, SKU and stock', async () => {
    await showResults();
    const listbox = root().querySelector('[role="listbox"]')!;
    expect(input().getAttribute('aria-controls')).toBe(listbox.id);
    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(options().length).toBe(2);
    expect(options()[0].textContent).toContain('Basmati Rice');
    expect(options()[0].textContent).toContain('SKU: RICE-1');
    expect(options()[0].textContent).toContain('Current Stock: 4');
    expect(root().querySelector('[role="status"]')!.textContent).toContain('2 results available');
  });

  it('navigates with the arrow keys and selects with Enter', async () => {
    const selected: VariantSearchItem[] = [];
    cmp.selected.subscribe(v => selected.push(v));
    await showResults();

    expect(key('Enter').defaultPrevented).toBe(false);
    key('ArrowDown');
    key('ArrowDown');
    await settle();
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[1].id);
    key('ArrowUp');
    await settle();
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[0].id);

    expect(key('Enter').defaultPrevented).toBe(true);
    expect(selected.map(v => v.id)).toEqual([1]);
    expect(cmp.results()).toEqual([]);
  });

  it('selects with a click', async () => {
    const selected: VariantSearchItem[] = [];
    cmp.selected.subscribe(v => selected.push(v));
    await showResults();
    options()[1].click();
    expect(selected.map(v => v.id)).toEqual([2]);
  });

  it('marks variants that are already added as aria-disabled and never selects them', async () => {
    const selected: VariantSearchItem[] = [];
    cmp.selected.subscribe(v => selected.push(v));
    fixture.componentRef.setInput('existingVariantIds', [1]);
    await showResults();
    expect(options()[0].getAttribute('aria-disabled')).toBe('true');
    expect(options()[0].textContent).toContain('Already Added');
    expect(options()[1].getAttribute('aria-disabled')).toBeNull();

    options()[0].click();
    expect(selected).toEqual([]);
  });

  it('hides already added variants entirely when hideExisting is set', async () => {
    fixture.componentRef.setInput('existingVariantIds', [1]);
    fixture.componentRef.setInput('hideExisting', true);
    vi.useFakeTimers();
    input().dispatchEvent(new Event('focus'));
    input().value = 'rice';
    input().dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(300);
    expect(cmp.results().map(r => r.id)).toEqual([2]);
  });

  it('closes only the result list on Escape and stops it reaching a hosting dialog', async () => {
    await showResults();
    const event = key('Escape');
    await settle();
    expect(event.defaultPrevented).toBe(true);
    expect(cmp.results()).toEqual([]);
    expect(root().querySelector('[role="listbox"]')).toBeNull();
    expect(key('Escape').defaultPrevented).toBe(false);
  });

  it('shows a named clear button for a selected variant and emits cleared', async () => {
    const cleared = vi.fn();
    cmp.cleared.subscribe(cleared);
    fixture.componentRef.setInput('selectedVariant', { id: 1, productName: 'Basmati Rice', name: '5kg', sku: 'RICE-1' });
    await settle();
    expect(input().value).toBe('Basmati Rice (5kg)');
    const button = root().querySelector<HTMLButtonElement>('button[aria-label="Clear selection"]')!;
    button.click();
    expect(cleared).toHaveBeenCalledTimes(1);
  });

  it('disables the input and the clear button when disabled', async () => {
    fixture.componentRef.setInput('selectedVariant', { id: 1, productName: 'Basmati Rice', name: '5kg' });
    fixture.componentRef.setInput('disabled', true);
    await settle();
    expect(input().disabled).toBe(true);
    expect(root().querySelector<HTMLButtonElement>('button[aria-label="Clear selection"]')!.disabled).toBe(true);
  });
});
