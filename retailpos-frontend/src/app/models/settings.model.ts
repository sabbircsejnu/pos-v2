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

export type CurrencySymbolPosition = 'before' | 'after';

export interface CurrencySettings {
  currencyCode: string;
  currencySymbol: string;
  currencyName: string;
  symbolPosition: CurrencySymbolPosition;
  decimalPlaces: number;
  thousandsSeparator: string;
  decimalSeparator: string;
}

export interface CurrencyOption {
  code: string;
  symbol: string;
  name: string;
}

export const SUPPORTED_CURRENCIES: CurrencyOption[] = [
  { code: 'BDT', symbol: '৳', name: 'Bangladeshi Taka' },
  { code: 'EUR', symbol: '€', name: 'Euro' },
  { code: 'USD', symbol: '$', name: 'US Dollar' },
];

export const DEFAULT_CURRENCY_SETTINGS: CurrencySettings = {
  currencyCode: 'BDT',
  currencySymbol: '৳',
  currencyName: 'Bangladeshi Taka',
  symbolPosition: 'before',
  decimalPlaces: 2,
  thousandsSeparator: ',',
  decimalSeparator: '.',
};

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

export interface InvoiceNumberSettings {
  prefix: string;
  includeDate: boolean;
  nextNumber: number;
  padding: number;
  separator: string;
}

export interface InventorySettings {
  lowStockThreshold: number;
  enableLowStockAlerts: boolean;
  allowNegativeStock: boolean;
  autoReorder: boolean;
}

export interface SystemSettings {
  company: CompanySettings;
  currency: CurrencySettings;
  tax: TaxSettings;
  receipt: ReceiptSettings;
  invoiceNumber: InvoiceNumberSettings;
  inventory: InventorySettings;
}
