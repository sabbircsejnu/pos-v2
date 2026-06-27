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
