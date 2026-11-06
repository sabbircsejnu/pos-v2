import { BarcodePrintQueueItem } from '../../models/barcode.model';
import {
  LabelPrintSettings,
  barcodeModuleWidthMm,
  buildThermalPrintHtml,
  buildThermalVariantLine,
  computeThermalLabelLayout,
  expandQueue,
  renderThermalBarcode,
  validateLabelSettings,
} from './barcode-label-print';

const settings = (widthMm: number, heightMm: number, margin = 1): LabelPrintSettings => ({
  widthMm,
  heightMm,
  marginTopMm: margin,
  marginRightMm: margin,
  marginBottomMm: margin,
  marginLeftMm: margin,
});

const LONG_TITLE = 'ABA424P - Borka Embro Gher Panel Black With Extra Long Designer Stone Work Collection Edition 2026';

const item = (variantId: number, barcodeValue: string, quantityPrinted: number): BarcodePrintQueueItem => ({
  variantId,
  productName: 'Borka',
  variantName: 'Black',
  variantSku: 'SKU',
  barcodeValue,
  quantityPrinted,
});

describe('barcode label printing', () => {
  // The same cases and values are pinned in ThermalBarcodeLabelTests.cs, so both renderers lay labels out identically.
  describe('layout shared with the PDF', () => {
    it.each([
      ['short 40x25', 40, 25, 1, 'AHIR BORKA BAZAR', 'ABA917P - Australian Orenge', 'Per ML | ABA917P', [2.5, 2.88, 2.6, 2, 5.98, 2.1, 1, 2.42, 2.8, 3.22, 6.9]],
      ['long 50x25', 50, 25, 1, 'AHIR BORKA BAZAR', LONG_TITLE, 'Black / Size 56 | ABA424P-BLK-56', [2.5, 2.88, 1.9, 2, 4.37, 2.1, 1, 2.42, 2.8, 3.22, 8.51]],
      ['long 60x40', 60, 40, 1.5, 'IraniMart', LONG_TITLE, 'Black / Size 56 | ABA424P-BLK-56', [3.4, 3.91, 2.9, 3, 10, 3, 1, 3.45, 3.8, 4.37, 13.67]],
      ['shelf 80x50', 80, 50, 1, 'IraniMart', 'Short', '', [3.4, 3.91, 3.4, 1, 3.91, 0, 0, 0, 3.8, 4.37, 34.61]],
      ['tiny 26x18', 26, 18, 0, 'IraniMart', LONG_TITLE, 'Black / Size 56 | ABA424P-BLK-56', [2.2, 2.53, 1.9, 1, 2.18, 1.7, 1, 1.95, 2.4, 2.76, 6.98]],
    ] as const)('%s', (_name, width, height, margin, company, title, variantLine, expected) => {
      const l = computeThermalLabelLayout(settings(width, height, margin), { company, title, variantLine });
      expect([
        l.company.fontMm, l.company.heightMm,
        l.title.fontMm, l.title.lines, l.title.heightMm,
        l.variant.fontMm, l.variant.lines, l.variant.heightMm,
        l.price.fontMm, l.price.heightMm,
        l.barcodeHeightMm,
      ]).toEqual(expected);
    });

    it.each([[40, 25], [50, 25], [60, 40], [80, 50]])('never overflows a %ix%i label or squeezes the barcode', (width, height) => {
      const l = computeThermalLabelLayout(settings(width, height), { company: LONG_TITLE, title: LONG_TITLE + LONG_TITLE, variantLine: LONG_TITLE });
      const rows = [l.company, l.title, l.variant, l.price].filter(row => row.lines > 0);
      const used = rows.reduce((sum, row) => sum + row.heightMm, 0) + rows.length * 0.4 + l.barcodeHeightMm;
      expect(used).toBeLessThanOrEqual(l.contentHeightMm + 0.01);
      expect(l.barcodeHeightMm).toBeGreaterThanOrEqual(6);
    });

    it('gives up the variant line before the barcode on the shortest labels', () => {
      const l = computeThermalLabelLayout(settings(40, 16, 0), { company: 'IraniMart', title: 'Short title', variantLine: 'Black | SKU' });
      expect(l.variant.lines).toBe(0);
      expect(l.barcodeHeightMm).toBeGreaterThanOrEqual(6);
    });
  });

  describe('expandQueue', () => {
    it('keeps queue order and repeats each item by its quantity', () => {
      const labels = expandQueue([item(1, 'A', 3), item(2, 'B', 1), item(3, 'C', 2)]);
      expect(labels.map(label => label.barcodeValue)).toEqual(['A', 'A', 'A', 'B', 'C', 'C']);
    });

    it('stops at the limit without changing the order', () => {
      const labels = expandQueue([item(1, 'A', 2), item(2, 'B', 5)], 3);
      expect(labels.map(label => label.barcodeValue)).toEqual(['A', 'A', 'B']);
    });
  });

  describe('buildThermalPrintHtml', () => {
    const label = (title: string, value = 'CM00181') => ({
      company: 'AHIR BORKA BAZAR',
      title,
      variantLine: 'Black / Size 56 | ABA424P-BLK-56',
      barcodeSvg: renderThermalBarcode(value).markup,
      priceText: 'MRP: ৳7,490.00',
    });

    it('sizes the physical page to the label, with no page margin for browser headers or footers', () => {
      const html = buildThermalPrintHtml([label('A')], settings(50, 25));
      expect(html).toContain('@page { size: 50mm 25mm; margin: 0; }');
      expect(html).not.toMatch(/210mm|297mm|\bA4\b/);
    });

    it('applies custom sizes and each printer margin in millimetres', () => {
      const html = buildThermalPrintHtml([label('A')], { widthMm: 58.5, heightMm: 30, marginTopMm: 0.5, marginRightMm: 1.5, marginBottomMm: 2, marginLeftMm: 3 });
      expect(html).toContain('@page { size: 58.5mm 30mm; margin: 0; }');
      expect(html).toContain('padding: 0.5mm 1.5mm 2mm 3mm;');
    });

    it('renders one page per label in sequence and leaves no blank page after the last', () => {
      const doc = new DOMParser().parseFromString(
        buildThermalPrintHtml([label('First'), label('Second'), label('Third')], settings(50, 25)),
        'text/html');
      expect(Array.from(doc.querySelectorAll('.label .row:nth-child(2)')).map(row => row.textContent)).toEqual(['First', 'Second', 'Third']);
      const css = doc.querySelector('style')!.textContent!;
      expect(css).toContain('break-after: page');
      expect(css).toContain('.label:last-child { break-after: auto; }');
    });

    it('keeps business, product, variant/SKU, barcode and MRP on every label', () => {
      const doc = new DOMParser().parseFromString(buildThermalPrintHtml([label('ABA424P - Borka')], settings(60, 40)), 'text/html');
      const text = doc.querySelector('.label')!.textContent!;
      expect(text).toContain('AHIR BORKA BAZAR');
      expect(text).toContain('ABA424P - Borka');
      expect(text).toContain('Black / Size 56 | ABA424P-BLK-56');
      expect(text).toContain('MRP: ৳7,490.00');
      expect(doc.querySelector('.label .barcode svg')!.getAttribute('aria-label')).toBe('Barcode CM00181');
    });

    it('reserves a fixed barcode box so a long name is clamped rather than clipping the barcode', () => {
      const doc = new DOMParser().parseFromString(buildThermalPrintHtml([label(LONG_TITLE)], settings(50, 25)), 'text/html');
      expect((doc.querySelector('.barcode') as HTMLElement).style.height).toBe('8.51mm');
      const title = doc.querySelector('.row:nth-child(2)') as HTMLElement;
      expect(title.style.height).toBe('4.37mm');
      expect(title.getAttribute('style')).toContain('-webkit-line-clamp:2');
    });

    it('escapes product text', () => {
      const html = buildThermalPrintHtml([label('<img src=x onerror=alert(1)>')], settings(50, 25));
      expect(html).not.toContain('<img');
      expect(html).toContain('&lt;img');
    });
  });

  describe('renderThermalBarcode', () => {
    it('encodes the real value as CODE128 with a quiet zone inside the image', () => {
      const barcode = renderThermalBarcode('CM00181');
      expect(barcode.markup).toContain('aria-label="Barcode CM00181"');
      // Start (11) + 7 symbols, some merged by subset C + check (11) + stop (13).
      expect(barcode.modules).toBeGreaterThanOrEqual(79);
      expect(barcode.markup).toContain('viewBox="0 0');
    });

    it('rejects a value CODE128 cannot encode rather than printing something else', () => {
      expect(() => renderThermalBarcode('বাংলা')).toThrow();
    });

    it('reports the printed bar width', () => {
      expect(barcodeModuleWidthMm(80, 50)).toBe(0.5);
    });
  });

  describe('buildThermalVariantLine', () => {
    it.each([
      ['Black / Size 56', 'ABA424P-BLK-56', 'Borka', 'Black / Size 56 | ABA424P-BLK-56'],
      ['Default', 'ABA424P', 'Borka', 'ABA424P'],
      ['Borka', 'ABA424P', 'Borka', 'ABA424P'],
      ['ABA424P', 'ABA424P', 'Borka', 'ABA424P'],
      ['Per ML', '', 'Orange', 'Per ML'],
    ])('%s / %s', (variantName, variantSku, productName, expected) => {
      expect(buildThermalVariantLine({ variantName, variantSku, productName })).toBe(expected);
    });
  });

  describe('validateLabelSettings', () => {
    it('accepts the standard label sizes', () => {
      for (const [w, h] of [[40, 25], [50, 25], [60, 40], [80, 50]]) {
        expect(validateLabelSettings(settings(w, h), 'thermal')).toEqual({});
      }
    });

    it('rejects a blank size and reports it against the field', () => {
      const errors = validateLabelSettings({ ...settings(50, 25), widthMm: null as unknown as number }, 'thermal');
      expect(errors['widthMm']).toContain('width');
    });

    it('rejects margins that leave too little room for a label', () => {
      expect(validateLabelSettings(settings(30, 25, 5), 'thermal')['widthMm']).toContain('at least 24 mm');
      expect(validateLabelSettings(settings(50, 20, 5), 'thermal')['heightMm']).toContain('at least 16 mm');
      expect(validateLabelSettings(settings(80, 50, 25), 'thermal')['marginTopMm']).toContain('0 to 20');
    });

    it('allows labels up to the printable A4 area in A4 mode, ignoring thermal margins', () => {
      expect(validateLabelSettings(settings(194, 280, 99), 'a4')).toEqual({});
      expect(validateLabelSettings(settings(195, 40), 'a4')['widthMm']).toContain('194');
    });
  });
});
