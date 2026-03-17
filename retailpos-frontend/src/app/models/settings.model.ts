export interface CompanySettings {
  companyName: string;
  address: string;
  phone: string;
  email: string;
  website: string;
  taxNumber: string;
  currency: string;
  currencySymbol: string;
  dateFormat: string;
  timeZone: string;
}

export interface TaxSettings {
  taxEnabled: boolean;
  defaultTaxRate: number;
  taxName: string;
  pricesIncludeTax: boolean;
}

export interface ReceiptSettings {
  header: string;
  footer: string;
  showLogo: boolean;
  showBarcode: boolean;
  paperSize: string;
}

export interface InventorySettings {
  lowStockThreshold: number;
  enableLowStockAlerts: boolean;
  allowNegativeStock: boolean;
  autoReorder: boolean;
}

export interface SystemSettings {
  company: CompanySettings;
  tax: TaxSettings;
  receipt: ReceiptSettings;
  inventory: InventorySettings;
}
