/** Unit of measurement API models. The server is authoritative for every quantity, price and conversion. */

export type UomDimension = 'count' | 'volume' | 'mass' | 'length' | 'packaging';
export type PackStateKind = 'loose' | 'sealed' | 'prepared';
export type ReturnCondition = 'resellable' | 'non_resellable';

export interface UomUnit {
  id: number;
  isGlobal: boolean;
  code: string;
  name: string;
  symbol: string;
  dimension: UomDimension;
  quantityPrecision: number;
  isWholeUnit: boolean;
  isActive: boolean;
  isInUse: boolean;
}

export interface UomConversion {
  fromUnitId: number;
  fromSymbol: string;
  toUnitId: number;
  toSymbol: string;
  numerator: number;
  denominator: number;
}

export interface UomCatalog {
  units: UomUnit[];
  conversions: UomConversion[];
}

export interface SaveUomUnit {
  code: string;
  name: string;
  symbol: string;
  dimension: UomDimension;
  quantityPrecision: number;
  isWholeUnit: boolean;
  isActive?: boolean;
}

export interface ResolvedUnit {
  id: number;
  code: string;
  symbol: string;
  dimension: UomDimension;
  precision: number;
  isWhole: boolean;
}

export interface PurchaseOption {
  id: number;
  code: string;
  label: string;
  unit: ResolvedUnit;
  factorNumerator: number;
  factorDenominator: number;
  baseQuantityPerUnit: number;
  isDefault: boolean;
  isSealedPack: boolean;
}

export interface SellingComponent {
  variantId: number;
  quantity: number;
  isRequired: boolean;
}

export interface SellingOption {
  id: number;
  kind: 'pack' | 'custom';
  code: string;
  label: string;
  unit: ResolvedUnit | null;
  baseQuantity: number | null;
  factorNumerator: number;
  factorDenominator: number;
  price: number;
  isDefault: boolean;
  sealedPackCode: string | null;
  isPreparedEligible: boolean;
  components: SellingComponent[];
}

/** The current configuration of a variant. A variant without one is plain pcs. */
export interface VariantUomConfig {
  configId: number;
  version: number;
  variantId: number;
  baseUnit: ResolvedUnit;
  packStateTrackingEnabled: boolean;
  repackingEnabled: boolean;
  purchaseOptions: PurchaseOption[];
  sellingOptions: SellingOption[];
}

export interface VariantUomConfigHistoryRow {
  configId: number;
  version: number;
  baseUnitCode: string;
  effectiveFromUtc: string;
  effectiveToUtc: string | null;
  createdBy: number | null;
}

export interface VariantUomConfigView {
  variantId: number;
  hasStockActivity: boolean;
  current: VariantUomConfig | null;
  history: VariantUomConfigHistoryRow[];
}

export interface PurchaseOptionInput {
  code: string;
  unitId: number;
  label: string;
  /** Ignored by the server for physical units, which always use the standard conversion. */
  factorNumerator: number;
  factorDenominator: number;
  isDefault: boolean;
  isSealedPack: boolean;
}

export interface SellingComponentInput {
  componentVariantId: number;
  quantity: number;
  isRequired: boolean;
}

export interface SellingOptionInput {
  kind: 'pack' | 'custom';
  code: string;
  label: string;
  unitId: number | null;
  baseQuantity: number | null;
  price: number;
  isDefault: boolean;
  sealedPackCode: string | null;
  isPreparedEligible: boolean;
  components: SellingComponentInput[];
}

export interface VariantUomConfigInput {
  baseUnitId: number;
  packStateTrackingEnabled: boolean;
  repackingEnabled: boolean;
  purchaseOptions: PurchaseOptionInput[];
  sellingOptions: SellingOptionInput[];
}

export interface UnitSaleQuote {
  selectedUnit: string;
  enteredQuantity: number;
  baseUnit: string;
  baseQuantity: number;
  priceBeforeDiscountAndVat: number;
  effectiveConfigVersionId: number;
  isPreviewOnly: boolean;
}

export interface PackStateBalanceRow {
  state: PackStateKind;
  packCode: string;
  baseQtyPerPack: number;
  packCount: number;
  baseQuantity: number;
  capitalizedValue: number;
}

export interface PackStateView {
  variantId: number;
  locationId: number;
  locationType: string;
  baseUnitCode: string;
  onHand: number;
  allocatedTotal: number;
  isReconciled: boolean;
  balances: PackStateBalanceRow[];
}

export interface RepackComponent {
  componentVariantId: number;
  componentName: string | null;
  componentSku: string | null;
  quantity: number;
  unitCost: number | null;
  value: number | null;
}

export type RepackStatus = 'draft' | 'executed' | 'cancelled' | 'reversed';

export interface RepackJob {
  id: number;
  reference: string;
  status: RepackStatus;
  locationId: number;
  locationType: string;
  sourceVariantId: number;
  sourceName: string | null;
  sourceSku: string | null;
  outputPackCode: string;
  outputLabel: string;
  packBaseQuantity: number;
  outputCount: number;
  contentBaseQuantity: number;
  baseUnitCode: string | null;
  contentUnitCost: number | null;
  contentValue: number | null;
  packagingValue: number | null;
  costPerPack: number | null;
  notes: string | null;
  createdAt: string;
  executedAt: string | null;
  reversedAt: string | null;
  components: RepackComponent[];
}

export interface RepackJobPage {
  items: RepackJob[];
  totalCount: number;
}

export interface CreateRepackJob {
  sourceVariantId: number;
  locationId: number;
  locationType: string;
  outputSellingOptionId: number;
  outputCount: number;
  idempotencyKey: string;
  notes?: string | null;
}

export interface StockCountEntryInput {
  unitOptionId: number | null;
  enteredQuantity: number;
  state?: PackStateKind | null;
  packCode?: string | null;
  baseQtyPerPack?: number;
}

export interface StockCountEntriesResult {
  lineId: number;
  physicalStock: number;
  difference: number;
}

export interface TransferAllocation {
  state: PackStateKind;
  packCode: string | null;
  baseQtyPerPack: number;
  packCount: number;
  baseQuantity: number;
}

/** Stable error codes returned in the API `errorCode` field. */
export const UOM_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  UOM_CONFIG_CHANGED: 'The units or prices of this item have changed. Review the updated options and try again.',
  UOM_INSUFFICIENT_LOOSE_STOCK: 'There is not enough loose stock. Open a sealed pack first if one is available.',
  UOM_INSUFFICIENT_SEALED_STOCK: 'There are not enough sealed packs.',
  UOM_INSUFFICIENT_PREPARED_STOCK: 'There are not enough prepared packs.',
  UOM_INSUFFICIENT_PACKAGING_STOCK: 'There is not enough packaging in stock.',
  UOM_PACKAGING_COST_MISSING: 'This packaging has no known cost yet. Receive it through a GRN first.',
  UOM_BASE_UNIT_LOCKED: 'This item already has stock or transactions, so its base unit cannot change.',
};
