import { TestBed } from '@angular/core/testing';
import { DomSanitizer } from '@angular/platform-browser';
import { SecurityContext } from '@angular/core';
import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { BarcodeLabelsComponent } from './barcode-labels.component';
import { BarcodeService } from '../../services/barcode.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

function buildRoute(query: Record<string, string>) {
  return {
    snapshot: {
      queryParamMap: convertToParamMap(query)
    }
  };
}

describe('BarcodeLabelsComponent', () => {
  const seededTemplates = [
    {
      id: 1,
      name: 'Retail Price Label',
      templateType: 'retail',
      paperType: 'label',
      labelWidthMm: 60,
      labelHeightMm: 40,
      isDefault: true,
      isActive: true,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      fields: []
    }
  ];

  const barcodeServiceMock = {
    searchVariants: vi.fn().mockReturnValue(of([])),
    getTemplates: vi.fn().mockReturnValue(of(seededTemplates)),
    searchPrintHistory: vi.fn().mockReturnValue(of({ rows: [], totalCount: 0 })),
    recordPrintHistory: vi.fn().mockReturnValue(of({ id: 1 })),
    generatePdf: vi.fn()
  };

  const authServiceMock = {
    hasPermission: vi.fn().mockReturnValue(true),
    getUserValue: vi.fn().mockReturnValue({ businessName: 'AHIR BORKA BAZAR' })
  };

  const alertServiceMock = {
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn()
  };

  const errorHandlerServiceMock = {
    extractErrorMessage: vi.fn().mockReturnValue('error')
  };

  const currencyServiceMock = {
    format: vi.fn().mockImplementation((value: number) => value.toString())
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [BarcodeLabelsComponent],
      providers: [
        { provide: ActivatedRoute, useValue: buildRoute({}) },
        { provide: BarcodeService, useValue: barcodeServiceMock },
        { provide: AuthService, useValue: authServiceMock },
        { provide: AlertService, useValue: alertServiceMock },
        { provide: ErrorHandlerService, useValue: errorHandlerServiceMock },
        { provide: CurrencyService, useValue: currencyServiceMock }
      ]
    }).compileComponents();
  });

  it('reads route query context on init', () => {
    TestBed.overrideProvider(ActivatedRoute, {
      useValue: buildRoute({
        query: 'TSH-RED-S',
        sourceModule: 'inventory',
        sourceReferenceType: 'variant',
        sourceReferenceId: '77'
      })
    });

    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.searchQuery()).toBe('TSH-RED-S');
    expect(component.sourceModule()).toBe('inventory');
    expect(component.sourceReferenceType()).toBe('variant');
    expect(component.sourceReferenceId()).toBe(77);
    expect(barcodeServiceMock.searchVariants).toHaveBeenCalledWith('TSH-RED-S', 1, 20);
  });

  it('propagates source context into print-history payload', () => {
    TestBed.overrideProvider(ActivatedRoute, {
      useValue: buildRoute({
        sourceModule: 'grn',
        sourceReferenceType: 'grn',
        sourceReferenceId: '1024'
      })
    });

    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    (component as any).openPrintWindow = vi.fn();
    component.queue.set([
      {
        variantId: 1001,
        productName: 'Classic T-Shirt',
        variantName: 'Red / Small',
        variantSku: 'TSH-RED-S',
        barcodeValue: 'BRC-RED-S',
        variantAttributes: 'Color: Red | Size: S',
        sellingPrice: 7490,
        quantityPrinted: 1
      }
    ]);

    component.printQueue();

    expect(barcodeServiceMock.recordPrintHistory).toHaveBeenCalledTimes(1);
    const [payload, isBulk] = barcodeServiceMock.recordPrintHistory.mock.calls[0];
    expect(isBulk).toBe(false);
    expect(payload.sourceModule).toBe('grn');
    expect(payload.sourceReferenceType).toBe('grn');
    expect(payload.sourceReferenceId).toBe(1024);
  });

  it('keeps sourceReferenceId undefined when route value is invalid', () => {
    TestBed.overrideProvider(ActivatedRoute, {
      useValue: buildRoute({ sourceReferenceId: 'abc' })
    });

    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.sourceReferenceId()).toBeUndefined();
  });

  it('uses sample data in preview when queue is empty', () => {
    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.isPreviewUsingSample()).toBe(true);
    expect(component.getPreviewItem().variantSku).toBe('ABA424P-BLK-56');
    expect(component.getPreviewTitle()).toContain('Borka Embro Gher Panel Black');
  });

  it('uses selected variant data in preview when queue has items', () => {
    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.queue.set([
      {
        variantId: 3001,
        productName: 'Australian Orenge',
        variantName: 'Per ML',
        variantSku: 'ABA917P',
        barcodeValue: 'CM00917',
        variantAttributes: 'Per ML',
        sellingPrice: 140,
        quantityPrinted: 1
      }
    ]);

    expect(component.isPreviewUsingSample()).toBe(false);
    expect(component.getPreviewItem().variantSku).toBe('ABA917P');
    expect(component.getPreviewBarcodeText()).toBe('CM00917');
    expect(component.getPreviewTitle()).toBe('ABA917P - Australian Orenge');
  });

  it('renders barcode bars svg in preview', () => {
    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.queue.set([
      {
        variantId: 3001,
        productName: 'Australian Orenge',
        variantName: 'Per ML',
        variantSku: 'ABA917P',
        barcodeValue: 'CM00917',
        variantAttributes: 'Per ML',
        sellingPrice: 140,
        quantityPrinted: 1
      }
    ]);

    const safe = component.getPreviewBarcodeSvg();
    const sanitizer = TestBed.inject(DomSanitizer);
    const markup = sanitizer.sanitize(SecurityContext.HTML, safe) || '';

    expect(markup).toContain('<svg');
    expect(markup).toContain('aria-label="Barcode CM00917"');
  });

  describe('print formats', () => {
    const queueItem = (over: Record<string, unknown> = {}) => ({
      variantId: 1001,
      productName: 'Classic T-Shirt',
      variantName: 'Red / Small',
      mainProductCode: 'TSH',
      variantSku: 'TSH-RED-S',
      barcodeValue: 'BRC-RED-S',
      sellingPrice: 7490,
      quantityPrinted: 2,
      ...over
    });

    const create = () => {
      const fixture = TestBed.createComponent(BarcodeLabelsComponent);
      fixture.detectChanges();
      return { fixture, component: fixture.componentInstance };
    };

    beforeEach(() => localStorage.clear());
    afterEach(() => {
      document.querySelectorAll('iframe[aria-hidden]').forEach(frame => frame.remove());
    });

    it('defaults to the thermal format with browser output', () => {
      const { component } = create();
      expect(component.printFormat()).toBe('thermal');
      expect(component.printOutput()).toBe('browser');
      expect(component.printMode()).toBe('thermal');
    });

    it('records the existing history values for each format and output', () => {
      const { component } = create();
      component.printFormat.set('a4');
      expect(component.printMode()).toBe('browser');
      component.printOutput.set('pdf');
      expect(component.printMode()).toBe('pdf');
    });

    it('applies a template size with no matching preset as a custom size', () => {
      const { component } = create();
      component.templates.set([{ ...seededTemplates[0], id: 2, labelWidthMm: 58, labelHeightMm: 30 }]);
      component.changeTemplate(2);
      expect(component.selectedLabelSize()).toBe('custom');
      expect(component.labelSettings().widthMm).toBe(58);
      expect(component.labelSettings().heightMm).toBe(30);
    });

    it('turns an edited dimension into a custom size and keeps the other dimension', () => {
      const { component } = create();
      component.changeLabelSize('50x25');
      component.setLabelDimension('width', 58);
      expect(component.selectedLabelSize()).toBe('custom');
      expect(component.labelSettings().widthMm).toBe(58);
      expect(component.labelSettings().heightMm).toBe(25);
    });

    it('builds the preview from the print document, with labels in sequence', () => {
      const { component } = create();
      component.changeLabelSize('50x25');
      component.queue.set([queueItem({ quantityPrinted: 3 }), queueItem({ variantId: 2, productName: 'Second', barcodeValue: 'BRC-2', quantityPrinted: 1 })]);

      const preview = component.thermalPreview()!;
      expect(preview.shown).toBe(4);
      expect(preview.total).toBe(4);

      const html = TestBed.inject(DomSanitizer).sanitize(SecurityContext.HTML, preview.html) ?? '';
      expect(html).toContain('Barcode BRC-RED-S');
      expect(html.indexOf('Barcode BRC-RED-S')).toBeLessThan(html.indexOf('Barcode BRC-2'));
    });

    it('limits how many labels the preview renders but reports the full count', () => {
      const { component } = create();
      component.queue.set([queueItem({ quantityPrinted: 500 })]);
      const preview = component.thermalPreview()!;
      expect(preview.shown).toBe(12);
      expect(preview.total).toBe(500);
    });

    it('prints every requested label on its own page through a frame with the thermal page size', () => {
      const { component } = create();
      component.changeLabelSize('50x25');
      component.queue.set([queueItem({ quantityPrinted: 3 }), queueItem({ variantId: 2, barcodeValue: 'BRC-2', quantityPrinted: 2 })]);

      component.printQueue();

      const frame = document.querySelector('iframe[aria-hidden]') as HTMLIFrameElement;
      const doc = new DOMParser().parseFromString(frame.srcdoc, 'text/html');
      expect(doc.querySelector('style')!.textContent).toContain('@page { size: 50mm 25mm; margin: 0; }');
      expect(Array.from(doc.querySelectorAll('.label .barcode svg')).map(svg => svg.getAttribute('aria-label'))).toEqual([
        'Barcode BRC-RED-S', 'Barcode BRC-RED-S', 'Barcode BRC-RED-S', 'Barcode BRC-2', 'Barcode BRC-2'
      ]);
    });

    it('keeps the multi-column layout in A4 mode', () => {
      const { component } = create();
      component.printFormat.set('a4');
      component.queue.set([queueItem()]);

      component.printQueue();

      const html = (document.querySelector('iframe[aria-hidden]') as HTMLIFrameElement).srcdoc;
      expect(html).toContain('grid-template-columns: repeat(auto-fill, minmax(60mm, 1fr))');
      expect(html).not.toContain('@page { size');
      expect(html.match(/class="label"/g)).toHaveLength(2);
    });

    it('calls the print dialog of the frame once it has loaded, and removes the frame afterwards', () => {
      const { component } = create();
      component.queue.set([queueItem()]);
      component.printQueue();

      const frame = document.querySelector('iframe[aria-hidden]') as HTMLIFrameElement;
      const print = vi.spyOn(frame.contentWindow!, 'print').mockImplementation(() => undefined);
      frame.onload!(new Event('load'));
      expect(print).toHaveBeenCalledTimes(1);

      frame.contentWindow!.dispatchEvent(new Event('afterprint'));
      expect(document.querySelector('iframe[aria-hidden]')).toBeNull();
    });

    it('sends the format, margins and displayed MRP to the PDF endpoint', () => {
      barcodeServiceMock.generatePdf.mockReturnValue(of({ body: null, headers: { get: () => null } }));
      const { component } = create();
      component.printOutput.set('pdf');
      component.marginLeftMm.set(2);
      component.queue.set([queueItem()]);

      component.printQueue();

      expect(barcodeServiceMock.generatePdf).toHaveBeenCalledTimes(1);
      const [payload] = barcodeServiceMock.generatePdf.mock.calls[0];
      expect(payload.printLayout).toBe('thermal');
      expect(payload.printMode).toBe('pdf');
      expect(payload.marginLeftMm).toBe(2);
      expect(payload.items[0].priceText).toBe('MRP: 7490');
      expect(payload.items[0].mainProductCode).toBe('TSH');
    });

    it('does not record or print when the settings are invalid', () => {
      const { component } = create();
      component.queue.set([queueItem()]);
      component.setLabelDimension('width', null);

      component.printQueue();

      expect(component.settingsErrors()['widthMm']).toBeTruthy();
      expect(component.thermalPreview()).toBeNull();
      expect(barcodeServiceMock.recordPrintHistory).not.toHaveBeenCalled();
      expect(alertServiceMock.warning).toHaveBeenCalledWith('Correct the label size and margins before printing.');
    });

    it('refuses to print an item with nothing to encode, rather than inventing a barcode', () => {
      const { component } = create();
      component.queue.set([queueItem({ barcodeValue: '', variantSku: '' })]);

      component.printQueue();

      expect(alertServiceMock.error).toHaveBeenCalledWith('Classic T-Shirt has no barcode or SKU to print.');
      expect(barcodeServiceMock.recordPrintHistory).not.toHaveBeenCalled();
      expect(document.querySelector('iframe[aria-hidden]')).toBeNull();
    });

    it('names a barcode CODE128 cannot encode before anything is recorded', () => {
      const { component } = create();
      component.queue.set([queueItem({ barcodeValue: 'বাংলা' })]);

      component.printQueue();

      expect(alertServiceMock.error).toHaveBeenCalledWith('Barcode "বাংলা" for Classic T-Shirt contains characters CODE128 cannot encode.');
      expect(barcodeServiceMock.recordPrintHistory).not.toHaveBeenCalled();
    });

    it('warns when bars would be too thin for a thermal head to print reliably', () => {
      const { component } = create();
      component.changeLabelSize('40x25');
      component.queue.set([queueItem({ barcodeValue: 'ABCDEFGHIJKLMNOPQRSTUVWX' })]);
      expect(component.thinBarcodeWarning()).toContain('ABCDEFGHIJKLMNOPQRSTUVWX');

      component.queue.set([queueItem({ barcodeValue: 'CM00181' })]);
      expect(component.thinBarcodeWarning()).toBe('');
    });

    it('remembers the printer setup on this device, ahead of the template default', async () => {
      const first = create();
      first.component.changeLabelSize('50x25');
      first.component.marginLeftMm.set(2.5);
      first.component.printFormat.set('a4');
      first.fixture.detectChanges();
      await first.fixture.whenStable();

      const second = create();
      expect(second.component.selectedLabelSize()).toBe('50x25');
      expect(second.component.marginLeftMm()).toBe(2.5);
      expect(second.component.printFormat()).toBe('a4');
    });

    it('falls back to defaults when the stored setup is unreadable', () => {
      localStorage.setItem('barcode_label_print_settings', '{not json');
      const { component } = create();
      expect(component.printFormat()).toBe('thermal');
      expect(component.selectedLabelSize()).toBe('60x40');
    });

    it('offers both print formats in the settings card', () => {
      const { fixture } = create();
      const options = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('option')).map(option => option.textContent?.trim());
      expect(options).toContain('Thermal Label');
      expect(options).toContain('A4 Sheet');
      expect(options).toContain('Custom size');
    });
  });

  it('prefills label company name from outlet when business name is missing', () => {
    authServiceMock.getUserValue.mockReturnValueOnce({ outletName: 'Banani Outlet', permissions: ['*'] });

    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.companyName()).toBe('Banani Outlet');
  });

  it('keeps history hidden and does not load on init', () => {
    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.showHistory()).toBe(false);
    expect(barcodeServiceMock.searchPrintHistory).not.toHaveBeenCalled();
  });

  it('loads history only after clicking show history', () => {
    const fixture = TestBed.createComponent(BarcodeLabelsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    expect(barcodeServiceMock.searchPrintHistory).not.toHaveBeenCalled();

    component.toggleHistory();

    expect(component.showHistory()).toBe(true);
    expect(barcodeServiceMock.searchPrintHistory).toHaveBeenCalledTimes(1);
  });
});
