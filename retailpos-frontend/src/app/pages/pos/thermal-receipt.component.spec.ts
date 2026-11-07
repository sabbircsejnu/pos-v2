import { TestBed } from '@angular/core/testing';
import { ReceiptDocumentDto } from '../../models/sale.model';
import { applyReceiptPrintPage, clearReceiptPrintPage } from './receipt-print-page';
import { ThermalReceiptComponent } from './thermal-receipt.component';

const SCRIPT = '<script>window.hacked = true</script><img src=x onerror="window.hacked = true">';

function receipt(overrides: Partial<ReceiptDocumentDto> = {}): ReceiptDocumentDto {
  return {
    outputMode: '80mm',
    header: {
      showLogo: false,
      businessName: 'AHIR BORKA BAZAR',
      concernOf: 'PARENT GROUP',
      branchName: 'IRANI MART-1',
      outletName: 'Outlet Five',
      outletAddress: 'Bashundhara City',
      showroomHotline: '+8801898806901',
      centralHotline: '+8801898806900',
      website: 'www.example.test',
      taxIdentityLine: 'BIN: 0029807530203, Mushak-6.3'
    },
    saleInfo: {
      invoiceNumber: 'SLBL-1-C-10511266',
      salesDateTime: '2026-11-05T10:20:00Z',
      invoiceDateText: '05-Nov-2026 04:20 PM',
      cashier: 'Cashier',
      terminal: 'Counter 1',
      customerName: 'MD ROMIJ ALOM',
      customerMobile: '01711039823'
    },
    items: [{
      productName: 'Orna Hijab',
      printName: 'Orna Hijab (ABA1010P)',
      variantLabel: 'ABA1010P',
      quantity: 1,
      unitPrice: 1290,
      discount: 0,
      discountPercent: 0,
      lineTotal: 1290
    }],
    summary: {
      itemCount: 1,
      quantity: 1,
      subtotal: 1290,
      discount: 0,
      tax: 117.27,
      roundOff: 0,
      grandTotal: 1290,
      paymentMethod: 'cash',
      paidAmount: 1290,
      change: 0
    },
    footer: {
      thankYouMessage: 'Thank You',
      returnPolicy: 'No refunds.\nExchange in 15 days.',
      footerText: 'Powered by: Test',
      vatLawNotification: 'S.R.O. No. 19\nVAT applies.',
      showBarcode: false,
      showQr: false,
      generatedAt: '2026-11-05T10:21:00Z'
    },
    amountInWords: 'One Thousand Two Hundred Ninety Taka Only',
    paymentLines: [{ label: 'Cash Amount', amount: 1290 }],
    taxInclusive: true,
    ...overrides
  } as ReceiptDocumentDto;
}

function render(document: ReceiptDocumentDto): HTMLElement {
  const fixture = TestBed.createComponent(ThermalReceiptComponent);
  fixture.componentRef.setInput('document', document);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const text = (element: HTMLElement) => (element.textContent ?? '').replace(/\s+/g, ' ');

describe('ThermalReceiptComponent', () => {
  it('prints the entered unit, quantity and price the backend sends for a unit line', () => {
    const root = render(receipt({
      items: [{
        productName: 'Attar',
        printName: 'Attar [12 ml bottle] (ATR)',
        variantLabel: 'ATR',
        quantity: 2.5,
        unitPrice: 500,
        discount: 0,
        discountPercent: 0,
        lineTotal: 1250
      }]
    }));

    const row = root.querySelector('.tr-items tbody tr')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(row).toContain('Attar [12 ml bottle] (ATR)');
    expect(row).toContain('2.5');
    expect(row).toContain('500');
  });

  it('prints every configured line of the legacy invoice', () => {
    const content = text(render(receipt()));

    for (const expected of [
      'AHIR BORKA BAZAR',
      'A concern of PARENT GROUP',
      'Branch: IRANI MART-1',
      'Address: Bashundhara City',
      'Showroom Hotline: +8801898806901',
      'Central Hotline: +8801898806900',
      'Web: www.example.test',
      'BIN: 0029807530203, Mushak-6.3',
      'Invoice',
      '05-Nov-2026 04:20 PM',
      'SLBL-1-C-10511266',
      'MD ROMIJ ALOM',
      '01711039823',
      'Orna Hijab (ABA1010P)',
      'All prices are inclusive of VAT.',
      'Amount Due1290.00',
      'Paid1290.00',
      'Cash Amount1290.00',
      'Change0.00',
      '(+/-) Rounding0.00',
      'One Thousand Two Hundred Ninety Taka Only',
      'Return / Exchange Policy',
      'Thank You',
      'Powered by: Test'
    ]) {
      expect(content).toContain(expected);
    }
  });

  it('hides lines whose settings are empty and falls back to the outlet name for the branch', () => {
    const element = render(receipt({
      header: { showLogo: false, businessName: 'Shop', outletName: 'Outlet Five' },
      saleInfo: { ...receipt().saleInfo, customerMobile: null },
      footer: { showBarcode: false, showQr: false, generatedAt: '2026-11-05T10:21:00Z' },
      amountInWords: null
    }));
    const content = text(element);

    expect(content).toContain('Branch: Outlet Five');
    for (const hidden of ['A concern of', 'Address:', 'Hotline', 'Web:', 'BIN:', 'Mobile', 'Return / Exchange Policy', 'Amount In Words']) {
      expect(content).not.toContain(hidden);
    }
    expect(element.querySelector('.tr-identity')).toBeNull();
  });

  it('never shows the cashier, terminal or a generated timestamp, so reprints stay identical', () => {
    const content = text(render(receipt()));

    expect(content).not.toContain('Counter 1');
    expect(content).not.toContain('Cashier');
    expect(content).not.toContain('Generated');
  });

  it('shows VAT as a charged row only when prices exclude it', () => {
    const exclusive = text(render(receipt({ taxInclusive: false })));
    expect(exclusive).toContain('VAT117.27');
    expect(exclusive).not.toContain('All prices are inclusive of VAT.');

    const inclusive = text(render(receipt({ taxInclusive: true })));
    expect(inclusive).not.toContain('VAT117.27');
  });

  it('prints exactly the payment rows the server sends, keeping Paid and Change', () => {
    const content = text(render(receipt({
      paymentLines: [
        { label: 'Cash Amount', amount: 290 },
        { label: 'Mobile Banking Amount', amount: 1000 }
      ]
    })));

    expect(content).toContain('Cash Amount290.00');
    expect(content).toContain('Mobile Banking Amount1000.00');
    expect(content).not.toContain('Card Amount');
    expect(content).toContain('Paid1290.00');
    expect(content).toContain('Change0.00');
  });

  it('shows the Discount row only when the total discount is above zero', () => {
    const base = receipt();
    expect(text(render(base))).not.toContain('Discount');

    const discounted = text(render(receipt({ summary: { ...base.summary, discount: 50 } })));
    expect(discounted).toContain('Discount50.00');
  });

  it('prints the platform footer below the Receipt Settings footer and hides it completely when absent', () => {
    const enabled = render(receipt({
      footer: { ...receipt().footer, footerText: 'Please come again', poweredByText: 'Powered by: Irani Mart Technology' }
    }));
    expect(enabled.querySelector('.tr-powered-by')?.textContent).toBe('Powered by: Irani Mart Technology');
    expect(text(enabled).indexOf('Please come again')).toBeLessThan(text(enabled).indexOf('Powered by:'));

    const onlyPlatform = render(receipt({
      footer: { ...receipt().footer, footerText: undefined, poweredByText: 'Powered by: Irani Mart Technology' }
    }));
    expect(onlyPlatform.querySelector('.tr-powered-by')).not.toBeNull();

    const disabled = render(receipt({
      footer: { ...receipt().footer, footerText: 'Please come again', poweredByText: null }
    }));
    expect(disabled.querySelector('.tr-powered-by')).toBeNull();
    expect(text(disabled)).toContain('Please come again');
  });

  it('keeps multiline policy text in a line-preserving block', () => {
    const element = render(receipt());
    const policy = Array.from(element.querySelectorAll('.tr-multiline'))
      .find(node => node.textContent?.includes('No refunds.'));

    expect(policy?.textContent).toContain('No refunds.\nExchange in 15 days.');
  });

  it('renders configured text as text, never as markup', () => {
    const element = render(receipt({
      header: { showLogo: false, businessName: SCRIPT, concernOf: SCRIPT, branchName: SCRIPT, outletAddress: SCRIPT, website: SCRIPT },
      footer: {
        returnPolicy: SCRIPT,
        vatLawNotification: SCRIPT,
        footerText: SCRIPT,
        thankYouMessage: SCRIPT,
        showBarcode: false,
        showQr: false,
        generatedAt: '2026-11-05T10:21:00Z'
      }
    }));

    expect(element.querySelector('script')).toBeNull();
    expect(element.querySelector('img')).toBeNull();
    expect(element.textContent).toContain(SCRIPT);
    expect((window as unknown as { hacked?: boolean }).hacked).toBeUndefined();
  });
});

describe('receipt print page', () => {
  const pageRule = () => document.getElementById('receipt-print-page')?.textContent ?? '';

  afterEach(() => clearReceiptPrintPage());

  it('sizes the thermal page to the rendered receipt', () => {
    const host = document.createElement('div');
    host.id = 'receipt-print';
    host.innerHTML = '<div class="thermal-receipt"></div>';
    document.body.appendChild(host);
    const receiptElement = host.firstElementChild as HTMLElement;
    receiptElement.getBoundingClientRect = () => ({ height: 960 }) as DOMRect;

    applyReceiptPrintPage('80mm');
    expect(pageRule()).toBe('@page { size: 80mm 256mm; margin: 0; }');

    host.remove();
  });

  it('uses A4 for the A4 mode and removes the rule after printing', () => {
    applyReceiptPrintPage('A4');
    expect(pageRule()).toContain('size: A4');

    window.dispatchEvent(new Event('afterprint'));
    expect(document.getElementById('receipt-print-page')).toBeNull();
  });
});
