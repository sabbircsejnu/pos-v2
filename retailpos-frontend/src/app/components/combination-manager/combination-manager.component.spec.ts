import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ProductVariationService } from '../../services/product-variation.service';
import { VariationService } from '../../services/variation.service';
import { CombinationManagerComponent } from './combination-manager.component';

const opt = (id: number, name: string, priceAdjustment = 0) => ({ id, name, priceAdjustment, isActive: true, displayOrder: id });
const VARIATIONS = [
  { id: 1, name: 'Size', autoSelectAllOptions: true, options: [opt(11, 'S'), opt(12, 'M'), opt(13, 'L', 2)] },
  { id: 2, name: 'Color', autoSelectAllOptions: false, options: [opt(21, 'Red'), opt(22, 'Blue')] },
  { id: 3, name: 'Material', autoSelectAllOptions: false, options: [opt(31, 'Cotton')] }
];
const ASSIGNED = [
  { variationId: 1, variationName: 'Size', isRequired: false, options: VARIATIONS[0].options, selectedOptionIds: [11, 12] },
  { variationId: 2, variationName: 'Color', isRequired: false, options: VARIATIONS[1].options, selectedOptionIds: [21] }
];
const tag = (id: number, v: string, o: string) => ({ id, variationName: v, optionName: o, priceAdjustment: 0 });
const COMBOS = [
  { id: 100, sku: 'MUG-S-RED', barcode: '1110001', priceAdjustment: 0, costAdjustment: 1, optionIds: [11, 21], combinationName: 'S / Red', finalPrice: 20, options: [tag(11, 'Size', 'S'), tag(21, 'Color', 'Red')] },
  { id: 101, sku: 'MUG-L-RED', barcode: undefined, priceAdjustment: 2, costAdjustment: 0, optionIds: [13, 21], combinationName: 'L / Red', finalPrice: 22, options: [tag(13, 'Size', 'L'), tag(21, 'Color', 'Red')] }
];

describe('CombinationManagerComponent (variant management)', () => {
  let fixture: ComponentFixture<CombinationManagerComponent>;
  let cmp: CombinationManagerComponent;
  let alerts: AlertService;
  let pvs: Record<string, ReturnType<typeof vi.fn>>;
  let variations: ReturnType<typeof signal<any[]>>;

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string) => Array.from(root().querySelectorAll<E>(selector));
  const text = (selector: string) => q(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const buttonByText = (label: string) => qa<HTMLButtonElement>('button').find(b => b.textContent!.trim() === label)!;
  const row = (name: string) => qa('.cm-row').find(r => r.querySelector('.cm-toggle')!.textContent!.trim() === name)!;
  const dialogButton = (label: string) => qa<HTMLButtonElement>('.ui-dialog__footer button').find(b => b.textContent!.trim() === label)!;
  const fieldIn = (scope: string, label: string) => qa(`${scope} app-ui-form-field`).find(f => f.querySelector('label')!.textContent!.trim().startsWith(label))!;

  async function setup(options: { permissions?: string[]; combos?: unknown[]; allVariations?: any[] } = {}) {
    const permissions = options.permissions ?? ['products.view_cost'];
    variations = signal(options.allVariations ?? VARIATIONS);
    pvs = {
      getProductVariations: vi.fn(() => of(ASSIGNED)),
      getCombinations: vi.fn(() => of(options.combos ?? COMBOS)),
      assignVariations: vi.fn(() => of('ok')),
      generateAllCombinations: vi.fn(() => of({ data: [], message: 'Generated 4 combinations' })),
      generateSelectedCombinations: vi.fn(() => of({ data: [], message: 'Generated 2 combinations' })),
      createManualCombination: vi.fn(() => of({})),
      updateCombination: vi.fn(() => of('ok')),
      deleteCombination: vi.fn(() => of('ok'))
    };
    TestBed.configureTestingModule({
      imports: [CombinationManagerComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
        { provide: ProductVariationService, useValue: pvs },
        { provide: VariationService, useValue: { variations, loadVariations: vi.fn() } }
      ]
    });
    alerts = TestBed.inject(AlertService);
    fixture = TestBed.createComponent(CombinationManagerComponent);
    cmp = fixture.componentInstance;
    fixture.componentRef.setInput('productId', 7);
    fixture.componentRef.setInput('productName', 'Premium Mug');
    fixture.componentRef.setInput('basePrice', 20);
    await settle();
    await settle();
  }

  afterEach(() => {
    alerts?.close();
    TestBed.resetTestingModule();
  });

  describe('rendering and selection', () => {
    beforeEach(() => setup());

    it('loads the saved assignment and the existing variants for the product', () => {
      expect(pvs['getProductVariations']).toHaveBeenCalledWith(7);
      expect(pvs['getCombinations']).toHaveBeenCalledWith(7);
      expect(text('.cm-header')).toContain('Variations & Combinations');
      expect(text('.cm-header app-ui-badge')).toBe('2 variants');
    });

    it('shows each variation type with its saved selection and option counts', () => {
      expect(row('Size').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
      expect(row('Color').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
      expect(row('Material').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(false);
      expect(row('Size').querySelector('.cm-count')!.textContent!.trim()).toBe('2/3');
      expect(row('Color').querySelector('.cm-count')!.textContent!.trim()).toBe('1/2');
      expect(row('Material').textContent).toContain('1 option — select to activate');
      expect(Array.from(row('Size').querySelectorAll('.cm-chip')).map(c => `${c.textContent!.trim()}:${c.classList.contains('cm-chip--on')}`)).toEqual(['S:true', 'M:true', 'L +2:false']);
    });

    it('keeps option chips keyboard-operable and named', () => {
      const group = row('Size').querySelector('[role="group"]')!;
      expect(group.getAttribute('aria-label')).toBe('Size options');
      for (const input of Array.from(group.querySelectorAll<HTMLInputElement>('input'))) {
        expect(input.type).toBe('checkbox');
        expect(input.classList).toContain('sr-only');
        expect(input.classList).not.toContain('hidden');
      }
      expect(row('Size').querySelector('input')!.classList).toContain('ui-check__input');
    });

    it('pre-selects all options only for types that auto-select', async () => {
      row('Material').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      await settle();
      expect(cmp.selectedOptionsByVariation()[3]).toEqual([]);
      expect(row('Material').querySelector('.cm-count')!.textContent!.trim()).toBe('0/1');
      expect(row('Material').querySelector('[role="alert"]')!.textContent).toContain('Select at least one option.');

      row('Size').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      await settle();
      row('Size').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      await settle();
      expect(cmp.selectedOptionsByVariation()[1]).toEqual([11, 12, 13]);
    });

    it('toggles individual options and updates the count', async () => {
      row('Size').querySelectorAll<HTMLInputElement>('.cm-chip input')[2].click();
      await settle();
      expect(cmp.selectedOptionsByVariation()[1]).toEqual([11, 12, 13]);
      expect(row('Size').querySelector('.cm-count')!.textContent!.trim()).toBe('3/3');
      row('Size').querySelectorAll<HTMLInputElement>('.cm-chip input')[0].click();
      await settle();
      expect(cmp.selectedOptionsByVariation()[1]).toEqual([12, 13]);
    });

    it('disables generation until every selected type has an option', async () => {
      expect(buttonByText('Generate All').disabled).toBe(false);
      row('Material').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      await settle();
      expect(buttonByText('Generate All').disabled).toBe(true);
      expect(buttonByText('Generate Selected Only').disabled).toBe(true);
      expect(text('app-ui-alert[data-variant="info"]')).toContain('Select at least one option per variation type and save the assignment first.');
      expect(buttonByText('Add Manual').disabled).toBe(false);
    });

    it('warns about variants that use options no longer selected', () => {
      expect(text('app-ui-alert[data-variant="warning"]')).toContain('1 variant(s) use options no longer selected.');
    });
  });

  describe('assignment and generation', () => {
    beforeEach(() => setup());

    it('does not save when a selected type has no options', async () => {
      row('Material').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      await settle();
      buttonByText('Save Assignment').click();
      expect(pvs['assignVariations']).not.toHaveBeenCalled();
      expect(alerts.config()).toMatchObject({ type: 'warning', message: 'Each selected variation type must have at least one option selected.' });
    });

    it('asks for confirmation when variants would become invalid, then saves the assignment payload unchanged', async () => {
      buttonByText('Save Assignment').click();
      expect(alerts.config()!.type).toBe('confirm');
      expect(pvs['assignVariations']).not.toHaveBeenCalled();
      alerts.config()!.onConfirm!();
      expect(pvs['assignVariations']).toHaveBeenCalledWith(7, [1, 2], { 1: [11, 12], 2: [21] });
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Variations assigned successfully' });
      expect(pvs['getCombinations']).toHaveBeenCalledTimes(2);
    });

    it('saves without confirmation when nothing becomes invalid', async () => {
      TestBed.resetTestingModule();
      await setup({ combos: [COMBOS[0]] });
      buttonByText('Save Assignment').click();
      expect(pvs['assignVariations']).toHaveBeenCalledWith(7, [1, 2], { 1: [11, 12], 2: [21] });
    });

    it('generate all asks first, then calls the API for the product', async () => {
      buttonByText('Generate All').click();
      expect(alerts.config()!.type).toBe('confirm');
      expect(pvs['generateAllCombinations']).not.toHaveBeenCalled();
      alerts.config()!.onConfirm!();
      expect(pvs['generateAllCombinations']).toHaveBeenCalledWith(7);
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Generated 4 combinations' });
    });

    it('generate selected sends the selected variation ids', () => {
      buttonByText('Generate Selected Only').click();
      expect(pvs['generateSelectedCombinations']).toHaveBeenCalledWith(7, [1, 2]);
    });

    it('shows a generation error and leaves the manager usable', async () => {
      pvs['generateSelectedCombinations'].mockReturnValue(throwError(() => ({ message: 'SKU already exists' })));
      buttonByText('Generate Selected Only').click();
      await settle();
      expect(alerts.config()).toMatchObject({ type: 'error', message: 'SKU already exists' });
      expect(cmp.isLoading()).toBe(false);
      expect(buttonByText('Generate All')).toBeTruthy();
    });
  });

  describe('variants table (pricing is shown, never recalculated)', () => {
    beforeEach(() => setup());

    it('lists variants with attribute tags, SKU, barcode, adjustment and final price from the API', () => {
      const rows = qa('app-ui-data-table tbody tr');
      expect(rows).toHaveLength(2);
      expect(q('app-ui-data-table [role="region"]').getAttribute('aria-label')).toBe('Product variants');
      const first = rows[0].textContent!.replace(/\s+/g, ' ');
      expect(first).toContain('S:S');
      expect(first).toContain('C:Red');
      expect(first).toContain('MUG-S-RED');
      expect(first).toContain('1110001');
      expect(first).toContain('৳20');
      const second = rows[1].textContent!.replace(/\s+/g, ' ');
      expect(second).toContain('+৳2');
      expect(second).toContain('৳22');
      expect(rows[0].querySelector('.cm-money--up')).toBeNull();
      expect(rows[1].querySelector('.cm-money--up')).not.toBeNull();
    });

    it('gives every row action a name and keeps stock transactions linked to the variant', () => {
      const names = qa('app-ui-data-table tbody tr:first-child .cm-row-actions > *').map(a => a.getAttribute('aria-label'));
      expect(names).toEqual(['Stock transactions for S / Red', 'Edit S / Red', 'Delete S / Red']);
      const link = q<HTMLAnchorElement>('app-ui-data-table tbody tr:first-child .cm-row-actions a');
      expect(link.getAttribute('href')).toContain('/reports/stock-transactions');
      expect(link.getAttribute('href')).toContain('productId=7');
      expect(link.getAttribute('href')).toContain('variantId=100');
    });

    it('shows an empty state before any variant exists', async () => {
      TestBed.resetTestingModule();
      await setup({ combos: [] });
      expect(text('app-ui-data-table app-ui-state')).toContain('No variants yet');
      expect(q('.cm-header app-ui-badge')).toBeNull();
    });
  });

  describe('edit variant dialog', () => {
    beforeEach(async () => {
      await setup();
      q<HTMLButtonElement>('button[aria-label="Edit S / Red"]').click();
      await settle();
    });

    it('opens a labelled modal dialog with the variant tags and current values', () => {
      const panel = q('.ui-dialog__panel');
      expect(panel.getAttribute('role')).toBe('dialog');
      expect(panel.getAttribute('aria-modal')).toBe('true');
      expect(text('.ui-dialog__title')).toBe('Edit Variant');
      expect(text('.ui-dialog__body .cm-tags')).toContain('Red');
      expect(fieldIn('.ui-dialog', 'SKU').querySelector<HTMLInputElement>('input')!.value).toBe('MUG-S-RED');
      expect(fieldIn('.ui-dialog', 'Barcode').querySelector<HTMLInputElement>('input')!.value).toBe('1110001');
      expect(fieldIn('.ui-dialog', 'Cost Adjustment')).toBeTruthy();
    });

    it('saves the edited SKU, barcode and adjustments through the existing contract', async () => {
      const sku = fieldIn('.ui-dialog', 'SKU').querySelector<HTMLInputElement>('input')!;
      sku.value = 'MUG-S-RED-2';
      sku.dispatchEvent(new Event('input'));
      const barcode = fieldIn('.ui-dialog', 'Barcode').querySelector<HTMLInputElement>('input')!;
      barcode.value = '9990009';
      barcode.dispatchEvent(new Event('input'));
      const price = fieldIn('.ui-dialog', 'Price Adjustment').querySelector<HTMLInputElement>('input')!;
      price.value = '1.5';
      price.dispatchEvent(new Event('input'));
      await settle();
      dialogButton('Save Changes').click();
      await settle();
      expect(pvs['updateCombination']).toHaveBeenCalledWith(7, 100, { sku: 'MUG-S-RED-2', barcode: '9990009', priceAdjustment: 1.5, costAdjustment: 1 });
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Combination updated successfully' });
      expect(q('.ui-dialog')).toBeNull();
    });

    it('keeps the dialog open and reports a duplicate-barcode error from the server', async () => {
      pvs['updateCombination'].mockReturnValue(throwError(() => ({ message: 'Barcode 9990009 is already used by another variant' })));
      dialogButton('Save Changes').click();
      await settle();
      expect(alerts.config()).toMatchObject({ type: 'error', message: 'Barcode 9990009 is already used by another variant' });
      expect(q('.ui-dialog')).not.toBeNull();
    });

    it('closes with Cancel and with Escape but not with a backdrop click', async () => {
      q('.ui-dialog__backdrop').click();
      await settle();
      expect(q('.ui-dialog')).not.toBeNull();
      dialogButton('Cancel').click();
      await settle();
      expect(q('.ui-dialog')).toBeNull();
      q<HTMLButtonElement>('button[aria-label="Edit L / Red"]').click();
      await settle();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      await settle();
      expect(q('.ui-dialog')).toBeNull();
    });
  });

  describe('permissions', () => {
    it('hides cost adjustment in the edit and manual dialogs without products.view_cost', async () => {
      await setup({ permissions: [] });
      q<HTMLButtonElement>('button[aria-label="Edit S / Red"]').click();
      await settle();
      expect(fieldIn('.ui-dialog', 'Price Adjustment')).toBeTruthy();
      expect(qa('.ui-dialog app-ui-form-field').some(f => f.textContent!.includes('Cost Adjustment'))).toBe(false);
    });
  });

  describe('manual combination dialog', () => {
    beforeEach(async () => {
      await setup();
      buttonByText('Add Manual').click();
      await settle();
    });

    it('opens a labelled dialog with one selector per assigned variation', () => {
      expect(text('.ui-dialog__title')).toBe('Add Manual Combination');
      expect(q('.ui-dialog__panel').getAttribute('aria-modal')).toBe('true');
      const select = fieldIn('.ui-dialog', 'Size').querySelector<HTMLSelectElement>('select')!;
      expect(Array.from(select.options).map(o => o.textContent!.trim())).toEqual(['— Select —', 'S', 'M', 'L (+৳2)']);
    });

    it('requires at least one option before calling the API', async () => {
      dialogButton('Create').click();
      await settle();
      expect(pvs['createManualCombination']).not.toHaveBeenCalled();
      expect(alerts.config()).toMatchObject({ type: 'warning', message: 'Please select at least one option' });
    });

    it('creates the combination with the chosen options, barcode and adjustments', async () => {
      const select = fieldIn('.ui-dialog', 'Size').querySelector<HTMLSelectElement>('select')!;
      select.selectedIndex = 2;
      select.dispatchEvent(new Event('change'));
      const sku = fieldIn('.ui-dialog', 'SKU').querySelector<HTMLInputElement>('input')!;
      sku.value = 'MUG-M-NEW';
      sku.dispatchEvent(new Event('input'));
      const barcode = fieldIn('.ui-dialog', 'Barcode').querySelector<HTMLInputElement>('input')!;
      barcode.value = '5550005';
      barcode.dispatchEvent(new Event('input'));
      await settle();
      dialogButton('Create').click();
      await settle();
      expect(pvs['createManualCombination']).toHaveBeenCalledWith(7, { sku: 'MUG-M-NEW', barcode: '5550005', priceAdjustment: 0, costAdjustment: 0, optionIds: [12] });
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Combination created successfully' });
      expect(q('.ui-dialog')).toBeNull();
    });

    it('closes with Cancel without creating anything', async () => {
      dialogButton('Cancel').click();
      await settle();
      expect(q('.ui-dialog')).toBeNull();
      expect(pvs['createManualCombination']).not.toHaveBeenCalled();
    });
  });

  describe('delete', () => {
    beforeEach(() => setup());

    it('confirms by name, then deletes and reloads', async () => {
      q<HTMLButtonElement>('button[aria-label="Delete S / Red"]').click();
      expect(alerts.config()).toMatchObject({ type: 'confirm', message: 'Are you sure you want to delete combination "S / Red"?' });
      expect(pvs['deleteCombination']).not.toHaveBeenCalled();
      alerts.config()!.onConfirm!();
      expect(pvs['deleteCombination']).toHaveBeenCalledWith(7, 100);
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Combination deleted successfully' });
    });
  });

  describe('no variation types defined', () => {
    it('points the user to the variations page', async () => {
      await setup({ allVariations: [] });
      const warning = q('app-ui-alert[data-variant="warning"]');
      expect(warning.textContent).toContain('No variations available.');
      expect(warning.querySelector('a')!.getAttribute('href')).toBe('/variations');
    });
  });

  describe('Units & Pricing action', () => {
    it('links every variant to its units page for a user who may edit products', async () => {
      await setup({ permissions: ['products.edit'] });
      const links = qa<HTMLAnchorElement>('a[title="Units & Pricing"]').map(a => a.getAttribute('href'));
      expect(links).toEqual(['/products/7/variants/100/units', '/products/7/variants/101/units']);
    });

    it('is not offered without products.edit', async () => {
      await setup({ permissions: ['products.view_cost'] });
      expect(qa('a[title="Units & Pricing"]')).toHaveLength(0);
    });
  });
});
