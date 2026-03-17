import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CompanySettings, TaxSettings, ReceiptSettings, InventorySettings } from '../../models/settings.model';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './settings-page.component.html',
  styleUrls: ['./settings-page.component.css']
})
export class SettingsPageComponent implements OnInit {
  activeTab = signal<'company' | 'tax' | 'receipt' | 'inventory'>('company');
  isLoading = signal(false);
  isSaving = signal(false);

  company = signal<CompanySettings>({
    companyName: '', address: '', phone: '', email: '', website: '',
    taxNumber: '', currency: 'USD', currencySymbol: '$', dateFormat: 'MM/DD/YYYY', timeZone: 'UTC'
  });

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
    private errorHandler: ErrorHandlerService
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
}
