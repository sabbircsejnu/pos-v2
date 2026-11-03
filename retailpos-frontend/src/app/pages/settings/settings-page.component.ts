import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { MenuService, SidebarDensity } from '../../services/menu.service';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { normalizeDateFormat } from '../../services/company-date.service';
import { UI_KIT } from '../../components/shared/ui';
import {
  CompanySettings,
  ReceiptSettings,
  InventorySettings,
  InvoiceNumberSettings,
  CurrencySettings,
  PosSettings,
  UpdatePosSettingsRequest,
  COMMON_TIME_ZONES,
  DEFAULT_CURRENCY_SETTINGS,
  SUPPORTED_CURRENCIES,
} from '../../models/settings.model';

type SettingsTab = 'company' | 'currency' | 'pos' | 'receipt' | 'inventory' | 'appearance';

/** Editable copy of the Business POS settings; numbers are null while the field is blank. */
export interface PosSettingsForm {
  maxCashSessionDurationHours: number | null;
  defaultTimeZoneId: string;
  taxEnabled: boolean;
  defaultTaxRate: number | null;
  taxName: string;
  pricesIncludeTax: boolean;
}

export type PosSettingsErrors = Partial<Record<keyof PosSettingsForm, string>>;

const EMPTY_POS_FORM: PosSettingsForm = {
  maxCashSessionDurationHours: 12,
  defaultTimeZoneId: 'Asia/Dhaka',
  taxEnabled: false,
  defaultTaxRate: 0,
  taxName: 'Tax',
  pricesIncludeTax: false,
};

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppCurrencyPipe, ...UI_KIT],
  templateUrl: './settings-page.component.html',
  styleUrls: ['./settings-page.component.css']
})
export class SettingsPageComponent implements OnInit {
  private readonly auth = inject(AuthService);

  activeTab = signal<SettingsTab>('company');
  isLoading = signal(false);
  isSaving = signal(false);

  supportedCurrencies = SUPPORTED_CURRENCIES;
  readonly timeZoneSuggestions = COMMON_TIME_ZONES;
  currencyPreviewAmount = 1234567.89;

  company = signal<CompanySettings>({
    companyName: '', address: '', phone: '', email: '', website: '',
    taxNumber: '', currency: 'BDT', currencySymbol: 'à§³', dateFormat: 'dd/MM/yyyy', timeZone: 'Asia/Dhaka'
  });

  currency = signal<CurrencySettings>({ ...DEFAULT_CURRENCY_SETTINGS });

  /** Legacy invoice-number settings: shown read-only, they no longer drive sale numbering. */
  invoiceNumber = signal<InvoiceNumberSettings | null>(null);

  posBaseline = signal<PosSettings | null>(null);
  posForm = signal<PosSettingsForm>({ ...EMPTY_POS_FORM });
  posErrors = signal<PosSettingsErrors>({});
  posLoading = signal(false);
  posLoadError = signal<string | null>(null);
  isSavingPos = signal(false);

  receipt = signal<ReceiptSettings>({
    header: '', footer: '', showLogo: false, showBarcode: false, paperSize: '80mm'
  });

  inventory = signal<InventorySettings>({
    lowStockThreshold: 10, enableLowStockAlerts: true, allowNegativeStock: false, autoReorder: false
  });

  constructor(
    private settingsService: SettingsService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService,
    public menuService: MenuService,
  ) {}

  /** Settings need settings.edit; the discount limit lives on its own screen gated by sales.discount.configure. */
  get canEditPosSettings(): boolean {
    return this.auth.hasPermission('settings.edit');
  }

  get canConfigureDiscount(): boolean {
    return this.auth.hasPermission('sales.discount.configure');
  }

  ngOnInit(): void {
    this.loadSettings();
    this.loadPosSettings();
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.settingsService.getAllSettings().subscribe({
      next: (res) => {
        const data = res.data;
        if (data) {
          if (data.company) this.company.set({ ...data.company, dateFormat: normalizeDateFormat(data.company.dateFormat) });
          if (data.currency) this.currency.set(data.currency);
          if (data.receipt) this.receipt.set(data.receipt);
          if (data.inventory) this.inventory.set(data.inventory);
          if (data.invoiceNumber) this.invoiceNumber.set(data.invoiceNumber);
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  loadPosSettings(): void {
    this.posLoading.set(true);
    this.posLoadError.set(null);
    this.settingsService.getPosSettings().subscribe({
      next: (res) => {
        if (res?.data) this.applyPosSettings(res.data);
        this.posLoading.set(false);
      },
      error: (err) => {
        this.posLoading.set(false);
        this.posLoadError.set(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  private applyPosSettings(settings: PosSettings): void {
    this.posBaseline.set(settings);
    this.posForm.set({
      maxCashSessionDurationHours: settings.maxCashSessionDurationHours,
      defaultTimeZoneId: settings.defaultTimeZoneId,
      taxEnabled: settings.taxEnabled,
      defaultTaxRate: settings.defaultTaxRate,
      taxName: settings.taxName,
      pricesIncludeTax: settings.pricesIncludeTax,
    });
    this.posErrors.set({});
  }

  updatePosField<K extends keyof PosSettingsForm>(field: K, value: PosSettingsForm[K] | string): void {
    const isNumeric = field === 'maxCashSessionDurationHours' || field === 'defaultTaxRate';
    const next = isNumeric ? (value === null || value === '' ? null : Number(value)) : value;
    this.posForm.update(form => ({ ...form, [field]: next }));
    this.posErrors.update(errors => ({ ...errors, [field]: undefined }));
  }

  validatePosSettings(): PosSettingsErrors {
    const form = this.posForm();
    const errors: PosSettingsErrors = {};
    const hours = form.maxCashSessionDurationHours;
    if (this.canEditPosSettings) {
      if (hours === null || !Number.isInteger(hours) || hours < 1 || hours > 20) {
        errors.maxCashSessionDurationHours = 'Enter a whole number of hours from 1 to 20.';
      }
      if (!form.defaultTimeZoneId.trim()) {
        errors.defaultTimeZoneId = 'Enter an IANA time zone, for example Asia/Dhaka.';
      }
      if (!form.taxName.trim()) {
        errors.taxName = 'Tax name is required.';
      }
      const rate = form.defaultTaxRate;
      if (rate === null || Number.isNaN(rate) || rate < 0 || rate > 100) {
        errors.defaultTaxRate = 'Enter a rate from 0 to 100.';
      }
    }
    return errors;
  }

  /** Only the changed settings.edit fields; the discount limit is saved from its own screen. */
  buildPosPayload(): UpdatePosSettingsRequest {
    const form = this.posForm();
    const baseline = this.posBaseline();
    const payload: UpdatePosSettingsRequest = {};
    const changed = <K extends keyof PosSettingsForm>(field: K) => !baseline || baseline[field] !== form[field];

    if (this.canEditPosSettings) {
      if (changed('maxCashSessionDurationHours')) payload.maxCashSessionDurationHours = form.maxCashSessionDurationHours!;
      if (changed('defaultTimeZoneId')) payload.defaultTimeZoneId = form.defaultTimeZoneId.trim();
      if (changed('taxEnabled')) payload.taxEnabled = form.taxEnabled;
      if (changed('defaultTaxRate')) payload.defaultTaxRate = form.defaultTaxRate!;
      if (changed('taxName')) payload.taxName = form.taxName.trim();
      if (changed('pricesIncludeTax')) payload.pricesIncludeTax = form.pricesIncludeTax;
    }
    return payload;
  }

  savePosSettings(): void {
    if (this.isSavingPos()) return;

    const errors = this.validatePosSettings();
    this.posErrors.set(errors);
    if (Object.keys(errors).length) return;

    const payload = this.buildPosPayload();
    if (!Object.keys(payload).length) {
      this.alertService.info('There are no POS setting changes to save.');
      return;
    }

    this.isSavingPos.set(true);
    this.settingsService.updatePosSettings(payload).subscribe({
      next: (res) => {
        if (res?.data) this.applyPosSettings(res.data);
        this.alertService.success('POS settings saved successfully');
        this.isSavingPos.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSavingPos.set(false);
      }
    });
  }

  saveCompany(): void {
    this.isSaving.set(true);
    this.settingsService.updateCompanySettings(this.company()).subscribe({
      next: () => {
        this.alertService.success('Company settings saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  saveCurrency(): void {
    const errors = this.validateCurrency(this.currency());
    if (errors.length) {
      this.alertService.error(errors[0]);
      return;
    }

    this.isSaving.set(true);
    this.settingsService.updateCurrencySettings(this.currency()).subscribe({
      next: (res) => {
        const updated: CurrencySettings = res?.data?.currency ?? this.currency();
        this.currencyService.setSettings(updated);
        this.currency.set(updated);
        this.alertService.success('Currency settings saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  /**
   * When the user picks a currency from the dropdown we pre-fill code, symbol
   * and name; the rest (separators, decimal places, symbol position) stay as
   * configured so admins can keep their formatting preferences.
   */
  onCurrencyDropdownChange(code: string): void {
    const match = this.supportedCurrencies.find(c => c.code === code);
    if (!match) return;
    this.currency.update(c => ({
      ...c,
      currencyCode: match.code,
      currencySymbol: match.symbol,
      currencyName: match.name,
    }));
  }

  updateCurrencyField<K extends keyof CurrencySettings>(field: K, value: CurrencySettings[K]): void {
    this.currency.update(c => ({ ...c, [field]: value }));
  }

  currencyPreview(): string {
    const s = this.currency();
    const value = this.currencyPreviewAmount.toFixed(this.clamp(s.decimalPlaces, 0, 4));
    const [intPart, decPart] = value.split('.');
    const withSep = s.thousandsSeparator
      ? intPart.replace(/\B(?=(\d{3})+(?!\d))/g, s.thousandsSeparator)
      : intPart;
    const number = decPart ? `${withSep}${s.decimalSeparator}${decPart}` : withSep;
    return s.symbolPosition === 'after'
      ? `${number}${s.currencySymbol}`
      : `${s.currencySymbol}${number}`;
  }

  private clamp(n: number, min: number, max: number): number {
    return Math.min(Math.max(n, min), max);
  }

  private validateCurrency(c: CurrencySettings): string[] {
    const errors: string[] = [];
    if (!c.currencyCode || !c.currencyCode.trim()) errors.push('Currency code is required');
    if (!c.currencySymbol || !c.currencySymbol.trim()) errors.push('Currency symbol is required');
    if (c.decimalPlaces < 0 || c.decimalPlaces > 4) errors.push('Decimal places must be between 0 and 4');
    if (c.symbolPosition !== 'before' && c.symbolPosition !== 'after') errors.push('Symbol position must be Before or After');
    return errors;
  }

  saveReceipt(): void {
    this.isSaving.set(true);
    this.settingsService.updateReceiptSettings(this.receipt()).subscribe({
      next: () => {
        this.alertService.success('Receipt settings saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  saveInventory(): void {
    this.isSaving.set(true);
    this.settingsService.updateInventorySettings(this.inventory()).subscribe({
      next: () => {
        this.alertService.success('Inventory settings saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }

  updateCompanyField(field: keyof CompanySettings, value: string): void {
    this.company.update(c => ({ ...c, [field]: value }));
  }

  updateReceiptField(field: keyof ReceiptSettings, value: boolean | string): void {
    this.receipt.update(r => ({ ...r, [field]: value }));
  }

  updateInventoryField(field: keyof InventorySettings, value: boolean | number): void {
    this.inventory.update(i => ({ ...i, [field]: value }));
  }

  setSidebarDensity(density: SidebarDensity): void {
    this.menuService.setSidebarDensity(density);
    this.alertService.success(`Sidebar density set to ${density}.`);
  }
}
