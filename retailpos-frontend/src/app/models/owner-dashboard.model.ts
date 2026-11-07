export type MetricStatus =
  | 'ok'
  | 'estimated'
  | 'partial'
  | 'unavailable'
  | 'not_configured'
  | 'disabled'
  | 'off_day'
  | 'not_valued';

export interface MetricValue {
  value: number | null;
  status: MetricStatus;
  note: string | null;
}

export interface DashboardContext {
  from: string;
  to: string;
  timeZoneId: string;
  locationScope: string;
  dataAsOfUtc: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface DashboardLocation {
  id: number;
  name: string;
  type: 'outlet' | 'warehouse';
}

export interface CategoryOption {
  id: number;
  name: string;
}

export interface OwnerDashboardThresholds {
  minEligibleSellingDays: number;
  slowVelocityPerDay: number;
  minOnHandQty: number;
  minDemandVelocityPerDay: number;
  sourceExcessCoverageDays: number;
  sourceRetainedCoverageDays: number;
  destinationTargetCoverageDays: number;
  minVelocityRatio: number;
  minimumRetainedUnits: number;
  minRecommendationQty: number;
  maxSuggestedQty: number | null;
  highPriorityCoverDays: number;
  mediumPriorityCoverDays: number;
  profitEnabled: boolean;
  transferOpportunitiesEnabled: boolean;
}

export interface OwnerDashboardMeta {
  today: string;
  timeZoneId: string;
  showrooms: DashboardLocation[];
  warehouses: DashboardLocation[];
  categories: CategoryOption[];
  canCreateTransfer: boolean;
  thresholds: OwnerDashboardThresholds;
  valuationMethod: string;
}

export interface SalesFinanceSummary {
  context: DashboardContext;
  netSales: MetricValue;
  netSalesChangePercent: number | null;
  target: MetricValue;
  achievementPercent: number | null;
  shortfall: MetricValue;
  targetExceeded: MetricValue;
  purchase: MetricValue;
  expense: MetricValue;
  profit: MetricValue;
  dataQualityWarnings: string[];
  targetCoverage: SalesTargetCoverage;
  today: TodayTarget;
}

/** How much of the selected scope has a target, so a partial total is never read as a whole one. */
export interface SalesTargetCoverage {
  showroomCount: number;
  fullyConfiguredShowrooms: number;
  partlyConfiguredShowrooms: number;
  notConfiguredShowrooms: number;
  scheduledOffShowrooms: number;
  isComplete: boolean;
}

/** The current business date for the selected showroom scope, whatever period the page shows. */
export interface TodayTarget {
  date: string;
  netSales: number;
  target: MetricValue;
  achievementPercent: number | null;
  shortfall: MetricValue;
  targetExceeded: MetricValue;
}

export interface ShowroomPerformanceRow {
  outletId: number;
  outletName: string;
  netSales: number;
  target: number | null;
  achievementPercent: number | null;
  shortfall: number | null;
  targetExceeded: number | null;
  targetStatus: MetricStatus;
}

export interface ShowroomPerformance {
  context: DashboardContext;
  showrooms: ShowroomPerformanceRow[];
}

export interface CashFlowPoint {
  from: string;
  to: string;
  cashReceived: number;
  expensePayments: number;
}

export interface CashFlow {
  context: DashboardContext;
  bucket: 'daily' | 'weekly';
  points: CashFlowPoint[];
  totalCashReceived: number;
  totalExpensePayments: number;
  difference: number;
}

export interface SalesTrendPoint {
  from: string;
  to: string;
  netSales: number;
  target: number | null;
}

export interface SalesTrend {
  context: DashboardContext;
  bucket: 'daily' | 'weekly';
  points: SalesTrendPoint[];
  targetStatus: MetricStatus;
}

/** A quantity in one unit. Unlike units are listed side by side and never added together. */
export interface UnitQuantity {
  unitCode: string;
  quantity: number;
}

export type StockValuationStatus = 'no_stock' | 'valued' | 'zero_cost' | 'partial' | 'not_valued';

/** How much of some stock has a reliable value. Unknown cost is never counted as zero. */
export interface StockValuationCoverage {
  valuedSubtotal: number | null;
  status: StockValuationStatus;
  valuedRecords: number;
  valuedQuantity: number;
  zeroCostRecords: number;
  zeroCostQuantity: number;
  unvaluedRecords: number;
  unvaluedQuantity: number;
}

export interface InventorySummary {
  context: DashboardContext;
  totalStockValue: MetricValue;
  totalStockQuantity: number;
  activeStockedProducts: number;
  stockedVariants: number;
  slowMovingStockValue: MetricValue;
  slowMovingSharePercent: number | null;
  outOfStockVariants: number;
  transferOpportunities: number | null;
  highPriorityTransferOpportunities: number | null;
  transferOpportunityStatus: MetricStatus;
  negativeStockRows: number;
  unvaluedStockRows: number;
  valuationMethod: string;
  dataQualityWarnings: string[];
  valuation: StockValuationCoverage;
  slowMovingValuation: StockValuationCoverage;
  /** `totalStockQuantity` covers pcs only; every unit appears here. */
  quantityByUnit?: UnitQuantity[] | null;
  /** Dispatched, not yet received or rejected. Never part of the on-hand figures above. */
  inTransit?: InTransitSummary | null;
  /** On hand plus incoming in transit, per unit. */
  totalAccountedByUnit?: UnitQuantity[] | null;
  totalAccountedValuation?: StockValuationCoverage | null;
}

/** Stock moving between locations: per unit, with how much of it has a reliable value. */
export interface InTransitStock {
  quantityByUnit: UnitQuantity[];
  valuation: StockValuationCoverage;
  transfers: number;
  lines: number;
}

/** For all locations incoming and outgoing are the same lines; for one location they are what arrives and what left. */
export interface InTransitSummary {
  incoming: InTransitStock;
  outgoing: InTransitStock;
  businessWide: boolean;
}

export interface InTransitLine {
  transferId: number;
  transferNo: string | null;
  isReturn: boolean;
  variantId: number;
  productId: number;
  mainCode: string | null;
  productName: string;
  sku: string;
  variantLabel: string;
  categoryName: string;
  fromLocationId: number;
  fromLocationType: 'outlet' | 'warehouse';
  fromLocationName: string;
  toLocationId: number;
  toLocationType: 'outlet' | 'warehouse';
  toLocationName: string;
  quantity: number;
  baseUnitCode: string;
  /** Null when the dispatch cost is not reliable; never shown as zero. */
  value: number | null;
  dispatchedAtUtc: string | null;
}

export interface InTransitStockList {
  context: DashboardContext;
  rows: PagedResult<InTransitLine>;
  summary: InTransitSummary;
}

export interface StockValueByLocationRow {
  locationId: number;
  locationType: 'outlet' | 'warehouse';
  locationName: string;
  quantity: number;
  stockValue: number | null;
  percentOfTotal: number | null;
  valuation: StockValuationCoverage;
  quantityByUnit?: UnitQuantity[] | null;
  incoming?: InTransitStock | null;
  outgoing?: InTransitStock | null;
}

export interface StockValueByLocation {
  context: DashboardContext;
  locations: StockValueByLocationRow[];
  totalStockValue: number | null;
  valueStatus: MetricStatus;
  valuation: StockValuationCoverage;
  inTransit?: InTransitStock | null;
}

export interface BestSellerRow {
  rank: number;
  productId: number;
  mainCode: string | null;
  productName: string;
  variantId: number;
  sku: string;
  variantLabel: string;
  categoryName: string;
  netUnitsSold: number;
  netSales: number;
  currentAvailableStock: number;
  baseUnitCode?: string;
}

export type SlowClassification = 'zero_sales' | 'never_sold' | 'low_sales';

export interface SlowMoverRow {
  productId: number;
  mainCode: string | null;
  productName: string;
  variantId: number;
  sku: string;
  variantLabel: string;
  categoryName: string;
  locationId: number;
  locationName: string;
  availableStock: number;
  netUnitsSold: number;
  daysSinceLastSale: number | null;
  stockValue: number | null;
  eligibleSellingDays: number;
  velocityPerDay: number | null;
  classification: SlowClassification;
  reason: string;
  baseUnitCode?: string;
}

export interface SlowMovers {
  context: DashboardContext;
  rows: PagedResult<SlowMoverRow>;
  insufficientEvidenceCount: number;
}

export interface TransferSide {
  locationId: number;
  locationName: string;
  available: number;
  netUnitsSold: number;
  eligibleSellingDays: number;
  velocityPerDay: number;
  coverageDays: number | null;
  pendingOutbound: number;
  pendingInbound: number;
}

export type TransferPriority = 'high' | 'medium' | 'low';

export interface TransferOpportunity {
  recommendationKey: string;
  productId: number;
  mainCode: string | null;
  productName: string;
  variantId: number;
  sku: string;
  variantLabel: string;
  categoryId: number;
  categoryName: string;
  source: TransferSide;
  destination: TransferSide;
  sourceProtectedQty: number;
  sourceSpareQty: number;
  destinationNeedQty: number;
  suggestedQty: number;
  priority: TransferPriority;
  reasonCodes: string[];
  canInitiate: boolean;
  baseUnitCode?: string;
}

export interface TransferOpportunities {
  context: DashboardContext;
  rows: PagedResult<TransferOpportunity>;
  status: MetricStatus;
  stockAsOfUtc: string;
}

export interface ProductInsightLocation {
  locationId: number;
  locationType: 'outlet' | 'warehouse';
  locationName: string;
  variantId: number;
  sku: string;
  variantLabel: string;
  availableStock: number;
  netUnitsSold: number;
  netSales: number;
  stockValue: number | null;
  daysSinceLastSale: number | null;
  baseUnitCode?: string;
}

export interface ProductInsights {
  context: DashboardContext;
  productId: number;
  mainCode: string | null;
  productName: string;
  categoryName: string;
  totalAvailableStock: number;
  totalNetUnitsSold: number;
  totalNetSales: number;
  totalStockValue: number | null;
  locations: ProductInsightLocation[];
  valuation: StockValuationCoverage;
  quantityByUnit?: UnitQuantity[] | null;
}
