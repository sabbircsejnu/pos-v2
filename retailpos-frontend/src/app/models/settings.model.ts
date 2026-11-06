export interface CompanySettings {
  companyName: string;
  /** Optional; invoices print "A concern of {value}" below the business name. */
  concernOf?: string;
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
  showQr?: boolean;
  defaultReceiptSize?: string;
  paperSize: string;
  autoPrintAfterSale?: boolean;
  receiptCopies?: number;
  thankYouMessage?: string;
  returnPolicy?: string;
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

/** Result of GET/PUT /api/settings/pos: Business POS settings plus the caller's discount permissions. */
export interface PosSettings {
  /** 1–20; applied when the cash-session lifecycle is enabled. */
  maxCashSessionDurationHours: number;
  /** IANA time zone id (e.g. Asia/Dhaka) used by outlets that have no override. */
  defaultTimeZoneId: string;
  taxEnabled: boolean;
  defaultTaxRate: number;
  taxName: string;
  pricesIncludeTax: boolean;
  /** 0–100; 0 disables manual discounts. */
  maxManualDiscountPercent: number;
  version: number;
  updatedAt: string | null;
  isPersisted: boolean;
  canApplyDiscount: boolean;
  canOverrideDiscountLimit: boolean;
}

/** Partial update: omitted members stay unchanged. The discount limit needs sales.discount.configure, the rest settings.edit. */
export type UpdatePosSettingsRequest = Partial<Pick<PosSettings,
  'maxCashSessionDurationHours' | 'defaultTimeZoneId' | 'taxEnabled' | 'defaultTaxRate' | 'taxName'
  | 'pricesIncludeTax' | 'maxManualDiscountPercent'>>;

export interface PosSettingsResponse {
  success?: boolean;
  message?: string;
  data: PosSettings;
}

/** Suggestions for IANA time zone inputs; any valid IANA id is accepted by the API. */
export const COMMON_TIME_ZONES: string[] = [
  'Asia/Dhaka', 'Asia/Kolkata', 'Asia/Karachi', 'Asia/Kathmandu', 'Asia/Colombo', 'Asia/Dubai', 'Asia/Riyadh',
  'Asia/Singapore', 'Asia/Kuala_Lumpur', 'Asia/Bangkok', 'Asia/Jakarta', 'Asia/Hong_Kong', 'Asia/Tokyo',
  'Europe/London', 'Europe/Paris', 'Europe/Berlin', 'America/New_York', 'America/Chicago', 'America/Los_Angeles',
  'Australia/Sydney', 'UTC',
];

export interface SystemSettings {
  company: CompanySettings;
  currency: CurrencySettings;
  tax: TaxSettings;
  receipt: ReceiptSettings;
  invoiceNumber: InvoiceNumberSettings;
  inventory: InventorySettings;
  pos?: Pick<PosSettings, 'maxCashSessionDurationHours'>;
}
