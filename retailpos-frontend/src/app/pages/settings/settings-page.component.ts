import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { MenuService, SidebarDensity } from '../../services/menu.service';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import {
  CompanySettings,
  TaxSettings,
  ReceiptSettings,
  InventorySettings,
  CurrencySettings,
  DEFAULT_CURRENCY_SETTINGS,
  SUPPORTED_CURRENCIES,
} from '../../models/settings.model';

type SettingsTab = 'company' | 'currency' | 'tax' | 'receipt' | 'inventory' | 'appearance';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, AppCurrencyPipe],
  templateUrl: './settings-page.component.html',
  styleUrls: ['./settings-page.component.css']
})
export class SettingsPageComponent implements OnInit {
  activeTab = signal<SettingsTab>('company');
  isLoading = signal(false);
  isSaving = signal(false);

  supportedCurrencies = SUPPORTED_CURRENCIES;
  currencyPreviewAmount = 1234567.89;

  company = signal<CompanySettings>({
    companyName: '', address: '', phone: '', email: '', website: '',
    taxNumber: '', currency: 'BDT', currencySymbol: '৳', dateFormat: 'dd/MM/yyyy', timeZone: 'Asia/Dhaka'
  });

  currency = signal<CurrencySettings>({ ...DEFAULT_CURRENCY_SETTINGS });

  tax = signal<TaxSettings>({
    taxEnabled: false, defaultTaxRate: 0, taxName: 'Tax', pricesIncludeTax: false
  });

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

  ngOnInit(): void {
    this.loadSettings();
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.settingsService.getAllSettings().subscribe({
      next: (res) => {
        const data = res.data;
        if (data) {
          if (data.company) this.company.set(data.company);
          if (data.currency) this.currency.set(data.currency);
          if (data.tax) this.tax.set(data.tax);
          if (data.receipt) this.receipt.set(data.receipt);
          if (data.inventory) this.inventory.set(data.inventory);
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
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

  saveTax(): void {
    this.isSaving.set(true);
    this.settingsService.updateTaxSettings(this.tax()).subscribe({
      next: () => {
        this.alertService.success('Tax settings saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
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

  updateTaxField(field: keyof TaxSettings, value: boolean | number | string): void {
    this.tax.update(t => ({ ...t, [field]: value }));
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
