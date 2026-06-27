import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Subject, debounceTime } from 'rxjs';
import JsBarcode from 'jsbarcode';
import {
  BarcodePrintHistory,
  BarcodePrintHistorySearchRequest,
  BarcodePrintQueueItem,
  BarcodeTemplate,
  BarcodeVariantSearchItem,
  LabelSizePreset,
  RecordBarcodePrintHistoryRequest,
} from '../../models/barcode.model';
import { AuthService } from '../../services/auth.service';
import { BarcodeService } from '../../services/barcode.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AlertService } from '../../services/alert.service';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-barcode-labels',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './barcode-labels.component.html',
  styleUrls: ['./barcode-labels.component.css']
})
export class BarcodeLabelsComponent implements OnInit, OnDestroy {
  private static readonly PREVIEW_MM_TO_PX = 3.1;
  private static readonly SVG_NS = 'http://www.w3.org/2000/svg';
  private static readonly SYSTEM_DEFAULT_COMPANY_NAME = 'IraniMart';

  readonly previewSampleItem: BarcodePrintQueueItem = {
    variantId: 0,
    productName: 'Borka Embro Gher Panel Black',
    variantName: 'Black / Size 56',
    mainProductCode: 'ABA424P',
    variantSku: 'ABA424P-BLK-56',
    barcodeValue: 'CM00181',
    variantAttributes: 'Color: Black | Size: 56',
    sellingPrice: 7490,
    quantityPrinted: 1,
  };

  readonly labelSizes: LabelSizePreset[] = [
    { key: '40x25', name: 'Small (40mm x 25mm)', widthMm: 40, heightMm: 25 },
    { key: '50x25', name: 'Standard (50mm x 25mm)', widthMm: 50, heightMm: 25 },
    { key: '60x40', name: 'Large (60mm x 40mm)', widthMm: 60, heightMm: 40 },
    { key: '80x50', name: 'Shelf (80mm x 50mm)', widthMm: 80, heightMm: 50 },
  ];

  readonly templateFieldLabels: Record<string, string> = {
    product_name: 'Product Name',
    variant_name: 'Variant Name',
    variant_information: 'Variant Information',
    variant_attributes: 'Variant Attributes',
    variant_sku: 'SKU',
    sku: 'SKU',
    barcode: 'Barcode',
    selling_price: 'Price',
    price: 'Price',
    company_name: 'Company Name',
    company_logo: 'Company Logo'
  };

  searchQuery = signal('');
  searchResults = signal<BarcodeVariantSearchItem[]>([]);
  searching = signal(false);

  templates = signal<BarcodeTemplate[]>([]);
  hasTemplates = signal(false);
  selectedTemplateId = signal<number | null>(null);
  selectedLabelSize = signal('60x40');
  printMode = signal('browser');
  showHistory = signal(false);
  sourceModule = signal('barcode-center');
  sourceReferenceType = signal('manual');
  sourceReferenceId = signal<number | undefined>(undefined);
  companyName = signal(BarcodeLabelsComponent.SYSTEM_DEFAULT_COMPANY_NAME);

  queue = signal<BarcodePrintQueueItem[]>([]);

  historyRows = signal<BarcodePrintHistory[]>([]);
  historyTotal = signal(0);
  historyPage = signal(1);
  historyPageSize = signal(10);
  historyStartDate = signal('');
  historyEndDate = signal('');
  historyTemplateId = signal<number | undefined>(undefined);
  historyLoading = signal(false);

  isPrinting = signal(false);

  private readonly searchSubject = new Subject<string>();
  private previewBarcodeCacheKey = '';
  private previewBarcodeCacheValue: SafeHtml | null = null;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly barcodeService: BarcodeService,
    private readonly authService: AuthService,
    private readonly alertService: AlertService,
    private readonly errorHandler: ErrorHandlerService,
    private readonly currency: CurrencyService,
    private readonly sanitizer: DomSanitizer,
  ) {}

  ngOnInit(): void {
    this.companyName.set(this.resolveCompanyName());
    this.readRouteContext();

    this.searchSubject.pipe(debounceTime(350)).subscribe(query => {
      this.searchVariants(query);
    });

    this.loadTemplates();
  }

  ngOnDestroy(): void {
    this.searchSubject.complete();
  }

  can(permission: string): boolean {
    return this.authService.hasPermission(permission);
  }

  onSearchInput(query: string): void {
    this.searchQuery.set(query);
    this.searchSubject.next(query.trim());
  }

  addVariant(variant: BarcodeVariantSearchItem): void {
    const sku = variant.sku || variant.productCode || `VAR-${variant.id}`;
    const mainProductCode = (variant.productCode || '').trim() || this.extractMainProductCodeFromSku(sku);
    const existing = this.queue().find(q => q.variantId === variant.id);
    if (existing) {
      existing.quantityPrinted += 1;
      this.queue.set([...this.queue()]);
      return;
    }

    this.queue.update(rows => ([
      ...rows,
      {
        variantId: variant.id,
        productName: variant.productName,
        variantName: variant.name,
        mainProductCode,
        variantSku: sku,
        barcodeValue: variant.barcode,
        variantAttributes: variant.attributes,
        sellingPrice: variant.finalPrice,
        quantityPrinted: 1,
      }
    ]));

    this.searchQuery.set('');
    this.searchResults.set([]);
  }

  removeQueueItem(variantId: number): void {
    this.queue.update(rows => rows.filter(row => row.variantId !== variantId));
  }

  clearQueue(): void {
    this.queue.set([]);
  }

  updateQuantity(item: BarcodePrintQueueItem, value: number): void {
    const quantity = Number.isFinite(value) ? Math.max(1, Math.floor(value)) : 1;
    item.quantityPrinted = quantity;
    this.queue.set([...this.queue()]);
  }

  getSelectedTemplate(): BarcodeTemplate | undefined {
    const id = this.selectedTemplateId();
    if (!id) {
      return undefined;
    }
    return this.templates().find(t => t.id === id);
  }

  getTotalLabels(): number {
    return this.queue().reduce((sum, row) => sum + row.quantityPrinted, 0);
  }

  loadTemplates(): void {
    this.barcodeService.getTemplates(false).subscribe({
      next: (templates) => {
        const activeTemplates = templates.filter(t => t.isActive);
        this.templates.set(activeTemplates);
        this.hasTemplates.set(activeTemplates.length > 0);

        if (activeTemplates.length === 0) {
          this.selectedTemplateId.set(null);
          return;
        }

        const defaultTemplate = activeTemplates.find(t => t.isDefault) || activeTemplates[0];
        if (defaultTemplate) {
          this.selectedTemplateId.set(defaultTemplate.id);
          this.tryApplyTemplateSize(defaultTemplate);
        }
      },
      error: (error) => this.alertService.error(this.errorHandler.extractErrorMessage(error))
    });
  }

  changeTemplate(templateId: number | null): void {
    this.selectedTemplateId.set(templateId);
    const template = this.getSelectedTemplate();
    if (template) {
      this.tryApplyTemplateSize(template);
    }
  }

  printQueue(): void {
    if (this.queue().length === 0) {
      this.alertService.warning('Add at least one variant to the print queue.');
      return;
    }

    if (!this.hasTemplates()) {
      this.alertService.warning('No active barcode template found. Please create a template first.');
      return;
    }

    const labelSize = this.resolveLabelSize();
    const payload: RecordBarcodePrintHistoryRequest = {
      templateId: this.selectedTemplateId() || undefined,
      companyName: this.companyName(),
      printMode: this.printMode(),
      labelWidthMm: labelSize.widthMm,
      labelHeightMm: labelSize.heightMm,
      sourceModule: this.sourceModule(),
      sourceReferenceType: this.sourceReferenceType(),
      sourceReferenceId: this.sourceReferenceId(),
      items: this.queue().map(item => ({ ...item }))
    };

    if (this.printMode() === 'pdf') {
      this.generatePdfAndRecord(payload);
      return;
    }

    this.isPrinting.set(true);
    this.barcodeService.recordPrintHistory(payload, this.queue().length > 1).subscribe({
      next: () => {
        this.openPrintWindow();
        this.alertService.success(this.printMode() === 'pdf'
          ? 'Print layout opened. Use Save as PDF in the print dialog.'
          : 'Print layout opened successfully.');
        if (this.showHistory()) {
          this.loadHistory();
        }
        this.isPrinting.set(false);
      },
      error: (error) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(error));
      }
    });
  }

  loadHistory(): void {
    if (!this.showHistory()) {
      return;
    }

    const request: BarcodePrintHistorySearchRequest = {
      templateId: this.historyTemplateId(),
      startDate: this.historyStartDate() ? new Date(this.historyStartDate()).toISOString() : undefined,
      endDate: this.historyEndDate() ? new Date(this.historyEndDate()).toISOString() : undefined,
      pageNumber: this.historyPage(),
      pageSize: this.historyPageSize(),
    };

    this.historyLoading.set(true);
    this.barcodeService.searchPrintHistory(request).subscribe({
      next: (result) => {
        this.historyRows.set(result.rows || []);
        this.historyTotal.set(result.totalCount || 0);
        this.historyLoading.set(false);
      },
      error: (error) => {
        this.historyLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(error));
      }
    });
  }

  changeHistoryPage(page: number): void {
    if (!this.showHistory()) {
      return;
    }

    const totalPages = Math.max(1, Math.ceil(this.historyTotal() / this.historyPageSize()));
    this.historyPage.set(Math.min(totalPages, Math.max(1, page)));
    this.loadHistory();
  }

  toggleHistory(): void {
    const next = !this.showHistory();
    this.showHistory.set(next);
    if (next) {
      this.loadHistory();
    }
  }

  formatCurrency(value?: number): string {
    return this.currency.format(value || 0);
  }

  formatDate(value: string): string {
    return new Date(value).toLocaleString();
  }

  getHistoryTotalPages(): number {
    return Math.max(1, Math.ceil(this.historyTotal() / this.historyPageSize()));
  }

  getPreviewItem(): BarcodePrintQueueItem {
    return this.queue()[0] ?? this.previewSampleItem;
  }

  isPreviewUsingSample(): boolean {
    return this.queue().length === 0;
  }

  getPreviewTemplateName(): string {
    return this.getSelectedTemplate()?.name || 'Retail Price Label';
  }

  getPreviewLabelSizeText(): string {
    const size = this.resolveLabelSize();
    return `${size.widthMm}x${size.heightMm}mm`;
  }

  getPreviewTitle(): string {
    return this.buildRetailTitle(this.getPreviewItem());
  }

  getPreviewStickerStyle(): Record<string, string> {
    const size = this.resolveLabelSize();
    const widthPx = Math.round(size.widthMm * BarcodeLabelsComponent.PREVIEW_MM_TO_PX);
    const heightPx = Math.round(size.heightMm * BarcodeLabelsComponent.PREVIEW_MM_TO_PX);

    return {
      width: `${widthPx}px`,
      height: `${heightPx}px`,
    };
  }

  getPreviewBarcodeText(): string {
    const item = this.getPreviewItem();
    return (item.barcodeValue || item.variantSku || '').trim();
  }

  getPreviewBarcodeSvg(): SafeHtml {
    const value = this.getPreviewBarcodeText();
    const size = this.resolveLabelSize();
    const cacheKey = `${value}|${size.widthMm}|${size.heightMm}`;

    if (this.previewBarcodeCacheKey === cacheKey && this.previewBarcodeCacheValue) {
      return this.previewBarcodeCacheValue;
    }

    const markup = this.buildBarcodeSvgMarkup(value, size.widthMm, size.heightMm);
    const safe = this.sanitizer.bypassSecurityTrustHtml(markup);
    this.previewBarcodeCacheKey = cacheKey;
    this.previewBarcodeCacheValue = safe;
    return safe;
  }

  getPreviewPriceText(): string {
    return this.formatCurrency(this.getPreviewItem().sellingPrice);
  }

  private searchVariants(query: string): void {
    if (!query || query.length < 2) {
      this.searchResults.set([]);
      return;
    }

    this.searching.set(true);
    this.barcodeService.searchVariants(query, 1, 20).subscribe({
      next: rows => {
        this.searchResults.set(rows);
        this.searching.set(false);
      },
      error: () => {
        this.searching.set(false);
      }
    });
  }

  private resolveLabelSize(): LabelSizePreset {
    const selected = this.labelSizes.find(x => x.key === this.selectedLabelSize());
    if (selected) {
      return selected;
    }

    return this.labelSizes[2];
  }

  private tryApplyTemplateSize(template: BarcodeTemplate): void {
    const match = this.labelSizes.find(x => x.widthMm === Number(template.labelWidthMm) && x.heightMm === Number(template.labelHeightMm));
    if (match) {
      this.selectedLabelSize.set(match.key);
    }
  }

  private generatePdfAndRecord(payload: RecordBarcodePrintHistoryRequest): void {
    this.isPrinting.set(true);

    this.barcodeService.generatePdf(payload).subscribe({
      next: (response) => {
        const blob = response.body;
        if (!blob) {
          this.isPrinting.set(false);
          this.alertService.error('Failed to generate barcode PDF.');
          return;
        }

        const fileName = this.extractFileName(response) || `barcode-labels-${Date.now()}.pdf`;
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);

        this.barcodeService.recordPrintHistory(payload, this.queue().length > 1).subscribe({
          next: () => {
            this.alertService.success('PDF generated successfully.');
            if (this.showHistory()) {
              this.loadHistory();
            }
            this.isPrinting.set(false);
          },
          error: (error) => {
            this.isPrinting.set(false);
            this.alertService.error(this.errorHandler.extractErrorMessage(error));
          }
        });
      },
      error: (error) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(error));
      }
    });
  }

  private openPrintWindow(): void {
    const labelSize = this.resolveLabelSize();
    const html = this.buildPrintHtml(this.queue(), labelSize.widthMm, labelSize.heightMm, this.companyName());
    const printWindow = window.open('', '_blank', 'noopener,noreferrer');

    if (!printWindow) {
      this.alertService.error('Popup blocked by browser. Please allow popups and try again.');
      return;
    }

    printWindow.document.open();
    printWindow.document.write(html);
    printWindow.document.close();

    setTimeout(() => {
      printWindow.focus();
      printWindow.print();
    }, 350);
  }

  private buildPrintHtml(
    queue: BarcodePrintQueueItem[],
    widthMm: number,
    heightMm: number,
    companyName: string,
  ): string {
    const labels: string[] = [];

    for (const row of queue) {
      for (let i = 0; i < row.quantityPrinted; i += 1) {
        const title = this.buildRetailTitle(row);
        const barcodeValue = (row.barcodeValue || row.variantSku || '').trim();
        const barcodeSvg = this.buildBarcodeSvgMarkup(barcodeValue, widthMm, heightMm);
        labels.push(`
          <div class="label">
            <div class="company">${this.escapeHtml(companyName)}</div>
            <div class="title">${this.escapeHtml(title)}</div>
            <div class="barcode">${barcodeSvg}</div>
            <div class="price">MRP: ${this.escapeHtml(this.formatCurrency(row.sellingPrice))}</div>
          </div>
        `);
      }
    }

    return `
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8" />
<title>Barcode Print</title>
<style>
  @page { margin: 6mm; }
  body {
    margin: 0;
    font-family: 'Times New Roman', Georgia, serif;
    color: #0f172a;
    background: #ffffff;
  }
  .sheet {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(${widthMm}mm, 1fr));
    gap: 2mm;
    align-items: start;
    justify-items: start;
  }
  .label {
    width: ${widthMm}mm;
    height: ${heightMm}mm;
    border: 0.35mm solid #6b7280;
    border-radius: 0;
    padding: 1.4mm;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    justify-content: flex-start;
    align-items: center;
    overflow: hidden;
    background: #fff;
  }
  .company {
    font-size: 3.2mm;
    font-weight: 700;
    text-align: center;
    margin-bottom: 0.4mm;
  }
  .title {
    font-size: 3.05mm;
    font-weight: 600;
    text-align: center;
    line-height: 1.15;
    margin-bottom: 0.5mm;
  }
  .barcode {
    width: 100%;
    margin-top: auto;
    margin-bottom: 0.8mm;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .barcode svg {
    width: 100%;
    max-height: 42%;
  }
  .price {
    font-size: 3.6mm;
    font-weight: 700;
    text-align: center;
    margin-top: auto;
  }
  @media print {
    .label { break-inside: avoid; }
  }
</style>
</head>
<body>
  <div class="sheet">
    ${labels.join('')}
  </div>
</body>
</html>`;
  }

  private buildBarcodeSvgMarkup(value: string, widthMm: number, heightMm: number): string {
    const barcodeValue = value.trim() || 'CM00181';
    const svg = document.createElementNS(BarcodeLabelsComponent.SVG_NS, 'svg');

    JsBarcode(svg, barcodeValue, {
      format: 'CODE128',
      displayValue: false,
      margin: 0,
      height: Math.max(18, Math.round(heightMm * 1.4)),
      width: Math.max(1.2, Math.min(2.2, widthMm / 30)),
      background: '#ffffff',
      lineColor: '#111827',
    });

    svg.setAttribute('preserveAspectRatio', 'none');
    svg.setAttribute('width', '100%');
    svg.setAttribute('height', '100%');
    svg.setAttribute('aria-label', `Barcode ${barcodeValue}`);

    return svg.outerHTML;
  }

  private buildRetailTitle(row: BarcodePrintQueueItem): string {
    const mainProductCode = (row.mainProductCode || '').trim() || this.extractMainProductCodeFromSku(row.variantSku);
    const productName = (row.productName || '').trim();

    if (mainProductCode && productName) {
      return `${mainProductCode} - ${productName}`;
    }

    return mainProductCode || productName || row.variantName || '';
  }

  private resolveCompanyName(): string {
    const user = this.authService.getUserValue();
    const businessName = user?.businessName?.trim();
    const outletName = user?.actingOutletName?.trim() || user?.outletName?.trim();
    return businessName || outletName || BarcodeLabelsComponent.SYSTEM_DEFAULT_COMPANY_NAME;
  }

  private extractMainProductCodeFromSku(variantSku?: string): string {
    const sku = (variantSku || '').trim();
    if (!sku) {
      return '';
    }

    const parts = sku.split('-').filter(Boolean);
    return parts[0] || sku;
  }

  private escapeHtml(value: string): string {
    return value
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  private readRouteContext(): void {
    const query = this.route.snapshot.queryParamMap.get('query');
    const sourceModule = this.route.snapshot.queryParamMap.get('sourceModule');
    const sourceReferenceType = this.route.snapshot.queryParamMap.get('sourceReferenceType');
    const sourceReferenceIdText = this.route.snapshot.queryParamMap.get('sourceReferenceId');

    if (query && query.length >= 2) {
      this.searchQuery.set(query);
      this.searchVariants(query);
    }

    if (sourceModule) {
      this.sourceModule.set(sourceModule);
    }

    if (sourceReferenceType) {
      this.sourceReferenceType.set(sourceReferenceType);
    }

    if (sourceReferenceIdText) {
      const sourceReferenceId = Number(sourceReferenceIdText);
      if (Number.isFinite(sourceReferenceId) && sourceReferenceId > 0) {
        this.sourceReferenceId.set(sourceReferenceId);
      }
    }
  }

  private extractFileName(response: { headers: { get(name: string): string | null } }): string | null {
    const contentDisposition = response.headers.get('content-disposition');
    if (!contentDisposition) {
      return null;
    }

    const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
    if (utf8Match?.[1]) {
      return decodeURIComponent(utf8Match[1]);
    }

    const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
    return plainMatch?.[1] ?? null;
  }
}
