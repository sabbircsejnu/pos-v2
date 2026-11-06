import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { inject as injectCompanyDates } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, effect, signal } from '@angular/core';
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
import { UI_KIT } from '../../components/shared/ui';
import {
  DEFAULT_PRINTER_MARGIN_MM,
  LabelPrintSettings,
  MIN_RELIABLE_MODULE_WIDTH_MM,
  ThermalLabelContent,
  barcodeModuleWidthMm,
  buildThermalPrintHtml,
  buildThermalVariantLine,
  expandQueue,
  renderThermalBarcode,
  validateLabelSettings,
} from './barcode-label-print';

type PrintFormat = 'thermal' | 'a4';
type PrintOutput = 'browser' | 'pdf';

const PRINT_SETTINGS_STORAGE_KEY = 'barcode_label_print_settings';
const THERMAL_PREVIEW_LIMIT = 12;

@Component({
  selector: 'app-barcode-labels',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, RouterLink, ...UI_KIT],
  templateUrl: './barcode-labels.component.html',
  styleUrls: ['./barcode-labels.component.css']
})
export class BarcodeLabelsComponent implements OnInit, OnDestroy {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
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
  customWidthMm = signal<number | null>(60);
  customHeightMm = signal<number | null>(40);
  marginTopMm = signal<number | null>(DEFAULT_PRINTER_MARGIN_MM);
  marginRightMm = signal<number | null>(DEFAULT_PRINTER_MARGIN_MM);
  marginBottomMm = signal<number | null>(DEFAULT_PRINTER_MARGIN_MM);
  marginLeftMm = signal<number | null>(DEFAULT_PRINTER_MARGIN_MM);
  printFormat = signal<PrintFormat>('thermal');
  printOutput = signal<PrintOutput>('browser');
  /** The value print history stores: the PDF output, or else how the browser print was laid out. */
  readonly printMode = computed(() => {
    if (this.printOutput() === 'pdf') {
      return 'pdf';
    }
    return this.printFormat() === 'thermal' ? 'thermal' : 'browser';
  });
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

  readonly labelSettings = computed<LabelPrintSettings>(() => {
    const size = this.resolveLabelSize();
    return {
      widthMm: size.widthMm,
      heightMm: size.heightMm,
      marginTopMm: this.marginTopMm() as number,
      marginRightMm: this.marginRightMm() as number,
      marginBottomMm: this.marginBottomMm() as number,
      marginLeftMm: this.marginLeftMm() as number,
    };
  });
  readonly settingsErrors = computed(() => validateLabelSettings(this.labelSettings(), this.printFormat()));
  readonly settingsValid = computed(() => Object.keys(this.settingsErrors()).length === 0);

  /** The print document itself, so the preview cannot differ from what is sent to the printer. */
  readonly thermalPreview = computed<{ html: SafeHtml; shown: number; total: number } | null>(() => {
    if (this.printFormat() !== 'thermal' || !this.settingsValid()) {
      return null;
    }

    const items = this.queue().length > 0 ? this.queue() : [this.previewSampleItem];
    try {
      const labels = this.buildThermalLabels(items, THERMAL_PREVIEW_LIMIT);
      return {
        html: this.sanitizer.bypassSecurityTrustHtml(buildThermalPrintHtml(labels, this.labelSettings())),
        shown: labels.length,
        total: items.reduce((sum, row) => sum + row.quantityPrinted, 0),
      };
    } catch {
      return null;
    }
  });

  readonly thinBarcodeWarning = computed(() => {
    if (this.printFormat() !== 'thermal' || !this.settingsValid()) {
      return '';
    }

    const settings = this.labelSettings();
    const contentWidthMm = settings.widthMm - settings.marginLeftMm - settings.marginRightMm;
    const thin = new Set<string>();
    for (const row of this.queue()) {
      try {
        const value = this.resolveBarcodeValue(row);
        if (value && barcodeModuleWidthMm(renderThermalBarcode(value).modules, contentWidthMm) < MIN_RELIABLE_MODULE_WIDTH_MM) {
          thin.add(value);
        }
      } catch {
        // Values that cannot be encoded are reported when printing starts.
      }
    }
    return thin.size > 0
      ? `These barcodes may not scan reliably at this label width: ${[...thin].join(', ')}. Use a wider label or smaller side margins.`
      : '';
  });

  private hasRememberedLabelSize = false;
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
  ) {
    this.restorePrintSettings();
    effect(() => this.savePrintSettings());
  }

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
          // A size this device has already been set up for wins over the template default.
          if (!this.hasRememberedLabelSize) {
            this.tryApplyTemplateSize(defaultTemplate);
          }
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

  changeLabelSize(key: string): void {
    if (key === 'custom') {
      const current = this.resolveLabelSize();
      this.customWidthMm.set(current.widthMm);
      this.customHeightMm.set(current.heightMm);
    }
    this.selectedLabelSize.set(key);
  }

  setLabelDimension(dimension: 'width' | 'height', value: number | null): void {
    const current = this.resolveLabelSize();
    this.customWidthMm.set(dimension === 'width' ? value : current.widthMm);
    this.customHeightMm.set(dimension === 'height' ? value : current.heightMm);
    this.selectedLabelSize.set('custom');
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

    if (!this.settingsValid()) {
      this.alertService.warning('Correct the label size and margins before printing.');
      return;
    }

    const barcodeProblem = this.findBarcodeProblem();
    if (barcodeProblem) {
      this.alertService.error(barcodeProblem);
      return;
    }

    const settings = this.labelSettings();
    const payload: RecordBarcodePrintHistoryRequest = {
      templateId: this.selectedTemplateId() || undefined,
      companyName: this.companyName(),
      printMode: this.printMode(),
      printLayout: this.printFormat(),
      labelWidthMm: settings.widthMm,
      labelHeightMm: settings.heightMm,
      marginTopMm: settings.marginTopMm,
      marginRightMm: settings.marginRightMm,
      marginBottomMm: settings.marginBottomMm,
      marginLeftMm: settings.marginLeftMm,
      sourceModule: this.sourceModule(),
      sourceReferenceType: this.sourceReferenceType(),
      sourceReferenceId: this.sourceReferenceId(),
      items: this.queue().map(item => ({ ...item, priceText: this.buildPriceText(item) }))
    };

    if (this.printOutput() === 'pdf') {
      this.generatePdfAndRecord(payload);
      return;
    }

    this.isPrinting.set(true);
    this.barcodeService.recordPrintHistory(payload, this.queue().length > 1).subscribe({
      next: () => {
        this.openPrintWindow();
        this.alertService.success('Print dialog opened.');
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
    return this.companyDates.format(value, true);
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

  getFormatLabel(): string {
    return this.printFormat() === 'thermal' ? 'Thermal Label' : 'A4 Sheet';
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

    let markup = '';
    try {
      markup = this.buildBarcodeSvgMarkup(value, size.widthMm, size.heightMm);
    } catch {
      // A value that cannot be encoded shows no bars here; printing reports it by name.
    }
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
    if (this.selectedLabelSize() === 'custom') {
      return {
        key: 'custom',
        name: 'Custom',
        widthMm: this.customWidthMm() as number,
        heightMm: this.customHeightMm() as number,
      };
    }

    return this.labelSizes.find(x => x.key === this.selectedLabelSize()) ?? this.labelSizes[2];
  }

  private tryApplyTemplateSize(template: BarcodeTemplate): void {
    const widthMm = Number(template.labelWidthMm);
    const heightMm = Number(template.labelHeightMm);
    const match = this.labelSizes.find(x => x.widthMm === widthMm && x.heightMm === heightMm);
    if (match) {
      this.selectedLabelSize.set(match.key);
      return;
    }

    if (widthMm > 0 && heightMm > 0) {
      this.customWidthMm.set(widthMm);
      this.customHeightMm.set(heightMm);
      this.selectedLabelSize.set('custom');
    }
  }

  private resolveBarcodeValue(row: BarcodePrintQueueItem): string {
    return (row.barcodeValue || row.variantSku || '').trim();
  }

  private buildPriceText(row: BarcodePrintQueueItem): string {
    return `MRP: ${this.formatCurrency(row.sellingPrice)}`;
  }

  private buildThermalLabels(items: BarcodePrintQueueItem[], limit?: number): ThermalLabelContent[] {
    const company = this.companyName();
    const barcodes = new Map<string, string>();

    return expandQueue(items, limit).map(row => {
      const value = this.resolveBarcodeValue(row);
      let barcodeSvg = barcodes.get(value);
      if (barcodeSvg === undefined) {
        barcodeSvg = renderThermalBarcode(value).markup;
        barcodes.set(value, barcodeSvg);
      }

      return {
        company,
        title: this.buildRetailTitle(row),
        variantLine: buildThermalVariantLine(row),
        barcodeSvg,
        priceText: this.buildPriceText(row),
      };
    });
  }

  /** Checked before anything is recorded, so history never claims a print that could not be produced. */
  private findBarcodeProblem(): string | null {
    for (const row of this.queue()) {
      const value = this.resolveBarcodeValue(row);
      if (!value) {
        return `${row.productName} has no barcode or SKU to print.`;
      }
      try {
        renderThermalBarcode(value);
      } catch {
        return `Barcode "${value}" for ${row.productName} contains characters CODE128 cannot encode.`;
      }
    }
    return null;
  }

  private restorePrintSettings(): void {
    try {
      const stored = JSON.parse(localStorage.getItem(PRINT_SETTINGS_STORAGE_KEY) ?? 'null');
      if (!stored) {
        return;
      }

      if (stored.format === 'thermal' || stored.format === 'a4') {
        this.printFormat.set(stored.format);
      }
      if (stored.output === 'browser' || stored.output === 'pdf') {
        this.printOutput.set(stored.output);
      }
      if (stored.sizeKey === 'custom' || this.labelSizes.some(size => size.key === stored.sizeKey)) {
        this.selectedLabelSize.set(stored.sizeKey);
        this.customWidthMm.set(this.storedNumber(stored.widthMm, 60));
        this.customHeightMm.set(this.storedNumber(stored.heightMm, 40));
        this.hasRememberedLabelSize = true;
      }
      this.marginTopMm.set(this.storedNumber(stored.marginTopMm, DEFAULT_PRINTER_MARGIN_MM));
      this.marginRightMm.set(this.storedNumber(stored.marginRightMm, DEFAULT_PRINTER_MARGIN_MM));
      this.marginBottomMm.set(this.storedNumber(stored.marginBottomMm, DEFAULT_PRINTER_MARGIN_MM));
      this.marginLeftMm.set(this.storedNumber(stored.marginLeftMm, DEFAULT_PRINTER_MARGIN_MM));
    } catch {
      // Unreadable or blocked storage: keep the defaults.
    }
  }

  private savePrintSettings(): void {
    const settings = {
      format: this.printFormat(),
      output: this.printOutput(),
      sizeKey: this.selectedLabelSize(),
      widthMm: this.customWidthMm(),
      heightMm: this.customHeightMm(),
      marginTopMm: this.marginTopMm(),
      marginRightMm: this.marginRightMm(),
      marginBottomMm: this.marginBottomMm(),
      marginLeftMm: this.marginLeftMm(),
    };
    if (!this.settingsValid()) {
      return;
    }

    try {
      localStorage.setItem(PRINT_SETTINGS_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Blocked storage: the settings still apply to this session.
    }
  }

  private storedNumber(value: unknown, fallback: number): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
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
    const settings = this.labelSettings();
    const html = this.printFormat() === 'thermal'
      ? buildThermalPrintHtml(this.buildThermalLabels(this.queue()), settings)
      : this.buildPrintHtml(this.queue(), settings.widthMm, settings.heightMm, this.companyName());

    // A hidden frame prints its own document, including its @page rules, and cannot be stopped by a popup blocker.
    const frame = document.createElement('iframe');
    frame.setAttribute('aria-hidden', 'true');
    frame.tabIndex = -1;
    frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:1200px;height:800px;border:0;';
    frame.onload = () => {
      const printWindow = frame.contentWindow;
      if (!printWindow) {
        frame.remove();
        this.alertService.error('Could not open the print dialog. Please try again.');
        return;
      }

      printWindow.addEventListener('afterprint', () => frame.remove(), { once: true });
      printWindow.focus();
      printWindow.print();
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
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
    const barcodeValue = value.trim();
    if (!barcodeValue) {
      return '';
    }

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
