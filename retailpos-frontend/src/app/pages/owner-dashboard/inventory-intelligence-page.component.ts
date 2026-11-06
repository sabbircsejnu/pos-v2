import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, ViewChild, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { UI_KIT, UiBadgeVariant, UiDataTableComponent, UiTableColumn, UiTableQuery } from '../../components/shared/ui';
import {
  BestSellerRow,
  DashboardLocation,
  InventorySummary,
  MetricValue,
  OwnerDashboardMeta,
  PagedResult,
  ProductInsights,
  SlowClassification,
  SlowMoverRow,
  SlowMovers,
  StockValuationCoverage,
  StockValueByLocation,
  TransferOpportunities,
  TransferOpportunity,
  TransferPriority
} from '../../models/owner-dashboard.model';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { InventoryQuery, OwnerDashboardService } from '../../services/owner-dashboard.service';
import { OdBarListComponent, OdBarRow } from './shared/od-charts.component';
import { OdFilterBarComponent } from './shared/od-filter-bar.component';
import { OdKpiCardComponent } from './shared/od-kpi-card.component';
import { OwnerDashboardFilterStore } from './shared/owner-dashboard-filter.store';
import {
  AppliedFilter,
  DATE_PRESETS,
  DatePreset,
  REASON_TEXT,
  coverageValueLabel,
  createWidget,
  isIsoDateOnly,
  parseLocation,
  resolvePreset,
  stockValueLabel,
  validateRange,
  valuationCoverageText
} from './shared/owner-dashboard.logic';

const PREVIEW_ROWS = { bestSellers: 15, slowMovers: 15, transfers: 10 } as const;
const VIEW_ALL_PAGE_SIZE = 25;
const PAGE_SIZES = [25, 50, 100];

const CLASSIFICATION: Record<SlowClassification, { label: string; variant: UiBadgeVariant }> = {
  zero_sales: { label: 'Zero Sales', variant: 'warning' },
  never_sold: { label: 'Never Sold', variant: 'danger' },
  low_sales: { label: 'Low Sales', variant: 'info' }
};

const PRIORITY: Record<TransferPriority, { label: string; variant: UiBadgeVariant }> = {
  high: { label: 'High', variant: 'danger' },
  medium: { label: 'Medium', variant: 'warning' },
  low: { label: 'Low', variant: 'neutral' }
};

/** Stock value, product movement and transfer recommendations. Nothing here moves stock. */
@Component({
  selector: 'app-inventory-intelligence-page',
  standalone: true,
  imports: [OdBarListComponent, OdFilterBarComponent, OdKpiCardComponent, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './owner-dashboard.css',
  template: `
    <main class="od-page">
      <app-ui-page-header title="Inventory Intelligence"
        description="Stock value, best and slow sellers, and suggested transfers between showrooms across the business."
        [breadcrumbs]="[{ label: 'Dashboard', link: '/dashboard' }, { label: 'Inventory Intelligence' }]" />

      @if (metaError()) {
        <app-ui-state kind="error" title="Could not open this dashboard" [message]="metaError()!">
          <button appUiButton variant="outline" (click)="loadMeta()">Retry</button>
        </app-ui-state>
      } @else if (!meta() || !filter()) {
        <app-ui-state kind="loading" title="Loading dashboard…" />
      } @else {
        <div class="od-status">
          <app-ui-badge variant="info">Sales Analysis: {{ filter()!.from | appDate }} to {{ filter()!.to | appDate }}</app-ui-badge>
          @if (summary.data(); as data) {
            <app-ui-badge variant="neutral">Stock Snapshot: {{ data.context.dataAsOfUtc | appDate:'medium' }}</app-ui-badge>
          }
          <button appUiButton type="button" size="sm" variant="outline" [loading]="summary.loading()" (click)="loadAll()">
            <i class="fas fa-rotate" aria-hidden="true"></i> Refresh
          </button>
        </div>

        <app-od-filter-bar ariaLabel="Inventory filters" locationLabel="Location" allLabel="All locations"
          [showrooms]="meta()!.showrooms" [warehouses]="meta()!.warehouses" [categories]="meta()!.categories"
          [today]="meta()!.today" [applied]="filter()!"
          (applyRequested)="apply($event)" (resetRequested)="reset()" />

        @for (warning of summary.data()?.dataQualityWarnings ?? []; track warning) {
          <app-ui-alert variant="warning">{{ warning }}</app-ui-alert>
        }
        @if (summary.error()) {
          <app-ui-state kind="error" [compact]="true" title="Could not load the summary" [message]="summary.error()!">
            <button appUiButton variant="outline" (click)="loadSummary()">Retry</button>
          </app-ui-state>
        }

        <section class="od-kpis" aria-label="Key figures">
          <app-od-kpi-card label="Total Stock Value" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.totalStockValue)" [status]="summary.data()?.totalStockValue?.status ?? 'ok'"
            [note]="summary.data()?.totalStockValue?.note ?? null"
            statusLabel="Partial Value" [detail]="coverageText(summary.data()?.valuation)" />
          <app-od-kpi-card label="Total Stock Quantity" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="count(summary.data()?.totalStockQuantity)" detail="On hand; excludes stock in transit" />
          <app-od-kpi-card label="Active Stocked Products" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="count(summary.data()?.activeStockedProducts)"
            [detail]="summary.data() ? count(summary.data()!.stockedVariants) + ' variants / SKUs' : null" />
          <app-od-kpi-card label="Slow Moving Stock Value" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.slowMovingStockValue)" [status]="summary.data()?.slowMovingStockValue?.status ?? 'ok'"
            [note]="summary.data()?.slowMovingStockValue?.note ?? null"
            statusLabel="Partial Value"
            [detail]="summary.data()?.slowMovingSharePercent != null ? summary.data()!.slowMovingSharePercent + '% of stock value' : coverageText(summary.data()?.slowMovingValuation)" />
          <app-od-kpi-card label="Out of Stock Variants" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="count(summary.data()?.outOfStockVariants)" detail="Showroom and variant pairs already tracked there" />
          <app-od-kpi-card label="Transfer Opportunities" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="count(summary.data()?.transferOpportunities)" [status]="summary.data()?.transferOpportunityStatus ?? 'ok'"
            [detail]="summary.data()?.highPriorityTransferOpportunities != null ? summary.data()!.highPriorityTransferOpportunities + ' high priority' : null" />
        </section>

        <app-ui-card title="Stock Value by Location" [headingLevel]="2" [subtitle]="valuationSubtitle()">
          @if (locations.loading()) {
            <app-ui-state kind="loading" [compact]="true" title="Loading locations…" />
          } @else if (locations.error()) {
            <app-ui-state kind="error" [compact]="true" title="Could not load locations" [message]="locations.error()!">
              <button appUiButton variant="outline" (click)="loadLocations()">Retry</button>
            </app-ui-state>
          } @else if (!locationRows().length) {
            <app-ui-state kind="empty" [compact]="true" title="This business has no locations" />
          } @else {
            @if (locations.data(); as byLocation) {
              <p class="od-helper">{{ valuedSubtotalLabel(byLocation.valuation) }}</p>
            }
            <app-od-bar-list caption="Stock by location" nameHeader="Location"
              valueHeader="Stock value" detailHeader="Detail"
              [rows]="locationRows()" [selectable]="true" (selected)="filterToLocation($event)" />
          }
        </app-ui-card>

        <app-ui-card title="Best Selling Products" [headingLevel]="2"
          subtitle="Ranked by net units sold in the analysis period. Current Available Stock is today's figure.">
          <app-ui-data-table #bestTable caption="Best selling products" ariaLabel="Best selling products" mode="server" rowKey="variantId" density="compact"
            [columns]="bestColumns" [rows]="bestSellers.data()?.items ?? []" [totalRecords]="bestSellers.data()?.totalCount ?? 0"
            [loading]="bestSellers.loading()" [error]="bestSellers.error()" [paginated]="bestAll()"
            [initialPageSize]="viewAllPageSize" [pageSizes]="pageSizes"
            searchPlaceholder="Main code, name or SKU" searchLabel="Search best sellers"
            emptyTitle="No sales in this period" [rowClickable]="true"
            (queryChange)="onBestQuery($event)" (retry)="loadBest()" (rowClick)="openProduct($event.productId, $event.variantId)">
            <ng-template uiTableCell="mainCode" let-row><span class="od-code">{{ row.mainCode || 'Not set' }}</span></ng-template>
            <ng-template uiTableCell="sku" let-row>
              <div class="od-stack"><span>{{ row.sku }}</span><span class="od-muted">{{ row.variantLabel }}</span></div>
            </ng-template>
            <ng-template uiTableCell="netSales" let-row>{{ currency.format(row.netSales) }}</ng-template>
          </app-ui-data-table>
          @if (!bestAll() && (bestSellers.data()?.totalCount ?? 0) > preview.bestSellers) {
            <div class="od-more"><button appUiButton type="button" variant="outline" size="sm" (click)="showAllBest()">View All ({{ bestSellers.data()!.totalCount }})</button></div>
          }
        </app-ui-card>

        <app-ui-card title="Slow Moving Products" [headingLevel]="2" [subtitle]="slowSubtitle()">
          <div class="od-range">
            <app-ui-form-field label="Min stock" size="compact">
              <input appUiControl type="number" min="0" [value]="minStock() ?? ''" (change)="setStockRange('min', $any($event.target).value)">
            </app-ui-form-field>
            <app-ui-form-field label="Max stock" size="compact">
              <input appUiControl type="number" min="0" [value]="maxStock() ?? ''" (change)="setStockRange('max', $any($event.target).value)">
            </app-ui-form-field>
          </div>
          <app-ui-data-table #slowTable caption="Slow moving products" ariaLabel="Slow moving products" mode="server" rowKey="key" density="compact"
            [columns]="slowColumns()" [rows]="slowRows()" [totalRecords]="slow.data()?.rows?.totalCount ?? 0"
            [loading]="slow.loading()" [error]="slow.error()" [paginated]="slowAll()"
            [initialPageSize]="viewAllPageSize" [pageSizes]="pageSizes"
            searchPlaceholder="Main code, name or SKU" searchLabel="Search slow movers"
            emptyTitle="No slow movers" emptyMessage="Nothing with enough stock history is classified as slow." [rowClickable]="true"
            (queryChange)="onSlowQuery($event)" (retry)="loadSlow()" (rowClick)="openProduct($event.productId, $event.variantId)">
            <ng-template uiTableCell="mainCode" let-row><span class="od-code">{{ row.mainCode || 'Not set' }}</span></ng-template>
            <ng-template uiTableCell="sku" let-row>
              <div class="od-stack"><span>{{ row.sku }}</span><span class="od-muted">{{ row.variantLabel }}</span></div>
            </ng-template>
            <ng-template uiTableCell="daysSinceLastSale" let-row>{{ row.daysSinceLastSale ?? 'No sale' }}</ng-template>
            <ng-template uiTableCell="stockValue" let-row>{{ valueLabel(row.stockValue, row.availableStock) }}</ng-template>
            <ng-template uiTableCell="classification" let-row>
              <div class="od-stack">
                <app-ui-badge [variant]="classification(row.classification).variant">{{ classification(row.classification).label }}</app-ui-badge>
                <span class="od-muted">{{ row.reason }}</span>
              </div>
            </ng-template>
          </app-ui-data-table>
          @if (!slowAll() && (slow.data()?.rows?.totalCount ?? 0) > preview.slowMovers) {
            <div class="od-more"><button appUiButton type="button" variant="outline" size="sm" (click)="showAllSlow()">View All ({{ slow.data()!.rows.totalCount }})</button></div>
          }
        </app-ui-card>

        <app-ui-card title="Stock Transfer Opportunities" [headingLevel]="2"
          subtitle="Suggestions only. Creating a transfer opens the normal form, where it is validated and approved as usual.">
          @if (transfers.data()?.status === 'disabled') {
            <app-ui-state kind="empty" [compact]="true" title="Recommendations are switched off" />
          } @else {
            <app-ui-data-table #transferTable caption="Stock transfer opportunities" ariaLabel="Stock transfer opportunities" mode="server" rowKey="recommendationKey" density="compact"
              [columns]="transferColumns()" [rows]="transfers.data()?.rows?.items ?? []" [totalRecords]="transfers.data()?.rows?.totalCount ?? 0"
              [loading]="transfers.loading()" [error]="transfers.error()" [paginated]="transfersAll()"
              [initialPageSize]="viewAllPageSize" [pageSizes]="pageSizes"
              searchPlaceholder="Main code, name or SKU" searchLabel="Search transfer opportunities"
              emptyTitle="No transfer opportunities" emptyMessage="No exact variant has both proven surplus and proven need in your showrooms."
              (queryChange)="onTransferQuery($event)" (retry)="loadTransfers()">
              <ng-template uiTableCell="mainCode" let-row><span class="od-code">{{ row.mainCode || 'Not set' }}</span></ng-template>
              <ng-template uiTableCell="product" let-row>
                <div class="od-stack"><span>{{ row.productName }}</span><span class="od-muted">{{ row.sku }} · {{ row.variantLabel }}</span></div>
              </ng-template>
              <ng-template uiTableCell="source" let-row>
                <div class="od-stack"><span>{{ row.source.locationName }}</span><span class="od-muted">{{ row.source.available }} available · {{ row.source.netUnitsSold }} sold</span></div>
              </ng-template>
              <ng-template uiTableCell="destination" let-row>
                <div class="od-stack"><span>{{ row.destination.locationName }}</span><span class="od-muted">{{ row.destination.available }} available · {{ row.destination.netUnitsSold }} sold</span></div>
              </ng-template>
              <ng-template uiTableCell="priority" let-row>
                <app-ui-badge [variant]="priority(row.priority).variant">{{ priority(row.priority).label }}</app-ui-badge>
              </ng-template>
              <ng-template uiTableCell="action" let-row>
                <div class="od-row-actions">
                  <button appUiButton type="button" size="sm" variant="outline" (click)="openTransfer(row)">View Details</button>
                  @if (row.canInitiate) {
                    <button appUiButton type="button" size="sm" (click)="initiate(row)">Initiate Stock Transfer</button>
                  }
                </div>
              </ng-template>
            </app-ui-data-table>
            @if (!transfersAll() && (transfers.data()?.rows?.totalCount ?? 0) > preview.transfers) {
              <div class="od-more"><button appUiButton type="button" variant="outline" size="sm" (click)="showAllTransfers()">View All ({{ transfers.data()!.rows.totalCount }})</button></div>
            }
          }
        </app-ui-card>
      }

      @if (transferOpen()) {
        <app-ui-dialog [(open)]="transferOpen" title="Transfer recommendation" size="lg">
          @if (transferDetail.loading()) {
            <app-ui-state kind="loading" [compact]="true" title="Checking current stock…" />
          } @else if (transferDetail.error()) {
            <app-ui-alert variant="warning">{{ transferDetail.error() }}</app-ui-alert>
          } @else if (transferDetail.data(); as detail) {
            <p><strong>{{ detail.mainCode || 'Main code not set' }}</strong> · {{ detail.productName }} · {{ detail.sku }} · {{ detail.variantLabel }}</p>
            <p>
              Suggested: <strong>{{ detail.suggestedQty }} unit(s)</strong>
              <app-ui-badge [variant]="priority(detail.priority).variant">{{ priority(detail.priority).label }} priority</app-ui-badge>
            </p>
            <div class="od-compare">
              @for (side of [{ title: 'From: ' + detail.source.locationName, data: detail.source }, { title: 'To: ' + detail.destination.locationName, data: detail.destination }]; track side.title) {
                <app-ui-card [title]="side.title" [headingLevel]="3">
                  <dl class="od-dl">
                    <div><dt>Available now</dt><dd>{{ side.data.available }}</dd></div>
                    <div><dt>Net units sold</dt><dd>{{ side.data.netUnitsSold }}</dd></div>
                    <div><dt>In-stock days</dt><dd>{{ side.data.eligibleSellingDays }}</dd></div>
                    <div><dt>Units per in-stock day</dt><dd>{{ side.data.velocityPerDay }}</dd></div>
                    <div><dt>Days of cover</dt><dd>{{ side.data.coverageDays ?? 'No sales' }}</dd></div>
                    <div><dt>Pending out / in</dt><dd>{{ side.data.pendingOutbound }} / {{ side.data.pendingInbound }}</dd></div>
                  </dl>
                </app-ui-card>
              }
            </div>
            <dl class="od-dl">
              <div><dt>Source keeps</dt><dd>{{ detail.sourceProtectedQty }} unit(s)</dd></div>
              <div><dt>Source can release</dt><dd>{{ detail.sourceSpareQty }} unit(s)</dd></div>
              <div><dt>Destination needs</dt><dd>{{ detail.destinationNeedQty }} unit(s)</dd></div>
              <div><dt>Period</dt><dd>{{ filter()!.from | appDate }} to {{ filter()!.to | appDate }}</dd></div>
            </dl>
            <ul class="od-reasons">
              @for (code of detail.reasonCodes; track code) {<li>{{ reason(code) }}</li>}
            </ul>
            @if (!detail.canInitiate) {
              <app-ui-alert variant="info">You can view this recommendation. Starting a transfer needs the Stock Transfer permission and access to the source location.</app-ui-alert>
            }
          }
          <button uiDialogFooter appUiButton type="button" variant="outline" (click)="transferOpen.set(false)">Close</button>
          @if (transferDetail.data()?.canInitiate) {
            <button uiDialogFooter appUiButton type="button" (click)="initiate(transferDetail.data()!)">Initiate Stock Transfer</button>
          }
        </app-ui-dialog>
      }

      @if (productOpen()) {
        <app-ui-dialog [(open)]="productOpen" title="Product details" size="lg">
          @if (product.loading()) {
            <app-ui-state kind="loading" [compact]="true" title="Loading product…" />
          } @else if (product.error()) {
            <app-ui-alert variant="danger">{{ product.error() }}</app-ui-alert>
          } @else if (product.data(); as detail) {
            <p><strong>{{ detail.mainCode || 'Main code not set' }}</strong> · {{ detail.productName }} · {{ detail.categoryName }}</p>
            <dl class="od-dl">
              <div><dt>Current available stock</dt><dd>{{ detail.totalAvailableStock }}</dd></div>
              <div><dt>Net units sold</dt><dd>{{ detail.totalNetUnitsSold }}</dd></div>
              <div><dt>Net sales</dt><dd>{{ currency.format(detail.totalNetSales) }}</dd></div>
              <div><dt>Stock value</dt><dd>{{ coverageLabel(detail.valuation) }}</dd></div>
            </dl>
            @if (coverageText(detail.valuation); as coverage) {
              <p class="od-helper">{{ coverage }}</p>
            }
            <p class="od-helper">Sales: {{ detail.context.from | appDate }} to {{ detail.context.to | appDate }}. Stock as of {{ detail.context.dataAsOfUtc | appDate:'medium' }}.</p>
            <div class="od-table-wrap">
              <table>
                <caption class="sr-only">Stock and sales by location</caption>
                <thead><tr><th scope="col">Location</th><th scope="col">Variant / SKU</th><th scope="col" class="num">Available</th><th scope="col" class="num">Sold</th><th scope="col" class="num">Stock value</th><th scope="col" class="num">Days since sale</th></tr></thead>
                <tbody>
                  @for (row of detail.locations; track row.locationType + row.locationId + row.variantId) {
                    <tr>
                      <td>{{ row.locationName }}</td>
                      <td>{{ row.sku }} · {{ row.variantLabel }}</td>
                      <td class="num">{{ row.availableStock }}</td>
                      <td class="num">{{ row.netUnitsSold }}</td>
                      <td class="num">{{ valueLabel(row.stockValue, row.availableStock) }}</td>
                      <td class="num">{{ row.daysSinceLastSale ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
          <button uiDialogFooter appUiButton type="button" variant="outline" (click)="productOpen.set(false)">Close</button>
        </app-ui-dialog>
      }
    </main>
  `
})
export class InventoryIntelligencePageComponent implements OnInit {
  private readonly api = inject(OwnerDashboardService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly store = inject(OwnerDashboardFilterStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly currency = inject(CurrencyService);

  @ViewChild('bestTable') private bestTable?: UiDataTableComponent<BestSellerRow>;
  @ViewChild('slowTable') private slowTable?: UiDataTableComponent<SlowMoverRow>;
  @ViewChild('transferTable') private transferTable?: UiDataTableComponent<TransferOpportunity>;

  private readonly toMessage = (error: unknown) => this.errors.extractErrorMessage(error);

  readonly preview = PREVIEW_ROWS;
  readonly viewAllPageSize = VIEW_ALL_PAGE_SIZE;
  readonly pageSizes = PAGE_SIZES;

  readonly meta = signal<OwnerDashboardMeta | null>(null);
  readonly metaError = signal<string | null>(null);
  readonly filter = this.store.inventory;

  // If the Business context changes while this page is open, its locations are no longer valid: load them again.
  private readonly reloadAfterContextChange = effect(() => {
    if (this.meta() && !this.filter()) {
      untracked(() => this.loadMeta());
    }
  });
  readonly minStock = signal<number | null>(null);
  readonly maxStock = signal<number | null>(null);
  readonly bestAll = signal(false);
  readonly slowAll = signal(false);
  readonly transfersAll = signal(false);
  readonly transferOpen = signal(false);
  readonly productOpen = signal(false);

  readonly summary = createWidget<InventorySummary>(this.destroyRef, this.toMessage);
  readonly locations = createWidget<StockValueByLocation>(this.destroyRef, this.toMessage);
  readonly bestSellers = createWidget<PagedResult<BestSellerRow>>(this.destroyRef, this.toMessage);
  readonly slow = createWidget<SlowMovers>(this.destroyRef, this.toMessage);
  readonly transfers = createWidget<TransferOpportunities>(this.destroyRef, this.toMessage);
  readonly transferDetail = createWidget<TransferOpportunity>(this.destroyRef, this.toMessage);
  readonly product = createWidget<ProductInsights>(this.destroyRef, this.toMessage);

  private bestQuery: UiTableQuery | null = null;
  private slowQuery: UiTableQuery | null = null;
  private transferQuery: UiTableQuery | null = null;

  readonly bestColumns: UiTableColumn[] = [
    { key: 'rank', header: 'Rank', type: 'number', sortable: true, width: '4rem' },
    { key: 'mainCode', header: 'Product Main Code', type: 'custom', sortable: true },
    { key: 'productName', header: 'Product Name', sortable: true },
    { key: 'sku', header: 'Variant / SKU', type: 'custom', sortable: true },
    { key: 'categoryName', header: 'Category', sortable: true },
    { key: 'netUnitsSold', header: 'Net Units Sold', type: 'number', sortable: true },
    { key: 'netSales', header: 'Net Sales', type: 'custom', align: 'end', sortable: true },
    { key: 'currentAvailableStock', header: 'Current Available Stock', type: 'number', sortable: true }
  ];

  readonly slowColumns = computed<UiTableColumn[]>(() => [
    { key: 'mainCode', header: 'Main Code', type: 'custom', sortable: true },
    { key: 'productName', header: 'Name', sortable: true },
    { key: 'sku', header: 'Variant / SKU', type: 'custom' },
    { key: 'locationName', header: 'Showroom', sortable: true },
    { key: 'availableStock', header: 'Available Stock', type: 'number', sortable: true },
    { key: 'netUnitsSold', header: 'Net Units Sold', type: 'number', sortable: true },
    { key: 'daysSinceLastSale', header: 'Days Since Last Sale', type: 'custom', align: 'end', sortable: true },
    { key: 'stockValue', header: 'Stock Value', type: 'custom', align: 'end', sortable: true },
    {
      key: 'classification',
      header: 'Classification',
      type: 'custom',
      filter: {
        type: 'select',
        options: [
          { value: 'zero_sales', label: 'Zero Sales' },
          { value: 'low_sales', label: 'Low Sales' }
        ]
      }
    }
  ]);

  readonly transferColumns = computed<UiTableColumn[]>(() => {
    const showrooms = (this.meta()?.showrooms ?? []).map(showroom => ({ value: String(showroom.id), label: showroom.name }));
    return [
      { key: 'mainCode', header: 'Main Code', type: 'custom' },
      { key: 'product', header: 'Product / Exact Variant', type: 'custom' },
      { key: 'source', header: 'Source: Available / Sales', type: 'custom', filter: { type: 'select', options: showrooms } },
      { key: 'destination', header: 'Destination: Available / Sales', type: 'custom', filter: { type: 'select', options: showrooms } },
      { key: 'suggestedQty', header: 'Suggested Qty', type: 'number' },
      {
        key: 'priority',
        header: 'Priority',
        type: 'custom',
        filter: {
          type: 'select',
          options: [
            { value: 'high', label: 'High' },
            { value: 'medium', label: 'Medium' },
            { value: 'low', label: 'Low' }
          ]
        }
      },
      { key: 'action', header: 'Action', type: 'custom' }
    ];
  });

  readonly slowRows = computed(() =>
    (this.slow.data()?.rows.items ?? []).map(row => ({ ...row, key: `${row.variantId}-${row.locationId}` })));

  readonly slowSubtitle = computed(() => {
    const thresholds = this.meta()?.thresholds;
    const held = this.slow.data()?.insufficientEvidenceCount ?? 0;
    const rule = thresholds
      ? `Proposed rule: at least ${thresholds.minEligibleSellingDays} in-stock days and under ${thresholds.slowVelocityPerDay} units per day.`
      : '';
    return held ? `${rule} ${held} item(s) not classified because stock history is too short.` : rule;
  });

  readonly valuationSubtitle = computed(() => `At cost: ${this.meta()?.valuationMethod ?? ''}.`);

  readonly locationRows = computed<OdBarRow[]>(() =>
    (this.locations.data()?.locations ?? []).map(row => ({
      key: `${row.locationType}:${row.locationId}`,
      label: row.locationName,
      // The bar only draws known value. An unknown value has no bar and says so in its label.
      value: row.stockValue ?? 0,
      valueText: this.coverageLabel(row.valuation),
      detail: [
        `${row.quantity.toLocaleString('en-US')} units`,
        row.percentOfTotal === null ? null : `${row.percentOfTotal}% of valued stock`,
        row.valuation.unvaluedRecords > 0 ? valuationCoverageText(row.valuation) : null
      ].filter(Boolean).join(' · ')
    })));

  ngOnInit(): void {
    this.loadMeta();
  }

  loadMeta(): void {
    this.metaError.set(null);
    this.api.meta().subscribe({
      next: meta => {
        this.meta.set(meta);
        this.filter.set(this.initialFilter(meta));
        // The tables ask for their first page themselves as they render.
        this.loadSummary();
        this.loadLocations();
      },
      error: error => this.metaError.set(this.toMessage(error))
    });
  }

  apply(filter: AppliedFilter): void {
    this.filter.set(filter);
    this.syncUrl();
    this.reloadFromFirstPage();
  }

  reset(): void {
    this.filter.set(this.defaults(this.meta()!));
    this.syncUrl();
    this.reloadFromFirstPage();
  }

  loadAll(): void {
    this.loadSummary();
    this.loadLocations();
    this.loadBest();
    this.loadSlow();
    this.loadTransfers();
  }

  filterToLocation(key: string): void {
    this.apply({ ...this.filter()!, location: key });
  }

  loadSummary(): void {
    this.summary.load(this.api.inventorySummary(this.query()));
  }

  loadLocations(): void {
    this.locations.load(this.api.stockByLocation(this.query()));
  }

  onBestQuery(query: UiTableQuery): void {
    this.bestQuery = query;
    this.loadBest();
  }

  onSlowQuery(query: UiTableQuery): void {
    this.slowQuery = query;
    this.loadSlow();
  }

  onTransferQuery(query: UiTableQuery): void {
    this.transferQuery = query;
    this.loadTransfers();
  }

  loadBest(): void {
    const table = this.tableParams(this.bestQuery, this.bestAll(), PREVIEW_ROWS.bestSellers);
    this.bestSellers.load(this.api.bestSellers(this.query(), table));
  }

  loadSlow(): void {
    const table = this.tableParams(this.slowQuery, this.slowAll(), PREVIEW_ROWS.slowMovers);
    this.slow.load(this.api.slowMovers(this.query(), {
      ...table,
      classification: this.slowQuery?.filters['classification'],
      minStock: this.minStock(),
      maxStock: this.maxStock()
    }));
  }

  loadTransfers(): void {
    const table = this.tableParams(this.transferQuery, this.transfersAll(), PREVIEW_ROWS.transfers);
    const filters = this.transferQuery?.filters ?? {};
    this.transfers.load(this.api.transferOpportunities(this.query(), {
      search: table.search,
      page: table.page,
      pageSize: table.pageSize,
      sourceLocationId: filters['source'] ? Number(filters['source']) : null,
      destinationLocationId: filters['destination'] ? Number(filters['destination']) : null,
      priority: filters['priority']
    }));
  }

  showAllBest(): void {
    this.bestAll.set(true);
    this.loadBest();
  }

  showAllSlow(): void {
    this.slowAll.set(true);
    this.loadSlow();
  }

  showAllTransfers(): void {
    this.transfersAll.set(true);
    this.loadTransfers();
  }

  setStockRange(bound: 'min' | 'max', value: string): void {
    const parsed = value === '' ? null : Math.max(0, Math.floor(Number(value)));
    (bound === 'min' ? this.minStock : this.maxStock).set(parsed);
    this.loadSlow();
  }

  openTransfer(row: TransferOpportunity): void {
    const filter = this.filter()!;
    this.transferOpen.set(true);
    // Details are re-read from current stock; the row may already be stale.
    this.transferDetail.load(this.api.transferOpportunity(row.recommendationKey, filter.from, filter.to));
  }

  /** Opens the existing transfer form prefilled and unsubmitted. Nothing is created here. */
  initiate(row: TransferOpportunity): void {
    this.transferOpen.set(false);
    void this.router.navigate(['/stock-transfers/create'], {
      queryParams: {
        fromLocationId: row.source.locationId,
        fromLocationType: 'outlet',
        toLocationId: row.destination.locationId,
        toLocationType: 'outlet',
        variantId: row.variantId,
        sku: row.sku,
        quantity: row.suggestedQty
      }
    });
  }

  openProduct(productId: number, variantId: number): void {
    this.productOpen.set(true);
    this.product.load(this.api.productInsights(productId, variantId, this.query()));
  }

  money(metric: MetricValue | undefined): string | null {
    return metric?.value === null || metric?.value === undefined ? null : this.currency.format(metric.value);
  }

  coverageText(coverage: StockValuationCoverage | null | undefined): string | null {
    return valuationCoverageText(coverage);
  }

  coverageLabel(coverage: StockValuationCoverage): string {
    return coverageValueLabel(coverage, amount => this.currency.format(amount));
  }

  valueLabel(value: number | null, onHand: number): string {
    return stockValueLabel(value, onHand, amount => this.currency.format(amount));
  }

  valuedSubtotalLabel(coverage: StockValuationCoverage): string {
    const coverageNote = valuationCoverageText(coverage);
    const heading = `Total: ${this.coverageLabel(coverage)}`;
    return coverageNote ? `${heading}. ${coverageNote}.` : `${heading}.`;
  }

  count(value: number | null | undefined): string | null {
    return value === null || value === undefined ? null : value.toLocaleString('en-US');
  }

  classification(value: SlowClassification) {
    return CLASSIFICATION[value];
  }

  priority(value: TransferPriority) {
    return PRIORITY[value];
  }

  reason(code: string): string {
    return REASON_TEXT[code] ?? code;
  }

  private reloadFromFirstPage(): void {
    this.loadSummary();
    this.loadLocations();
    this.bestQuery = this.firstPage(this.bestQuery);
    this.slowQuery = this.firstPage(this.slowQuery);
    this.transferQuery = this.firstPage(this.transferQuery);
    this.bestTable?.goTo(1);
    this.slowTable?.goTo(1);
    this.transferTable?.goTo(1);
    this.loadBest();
    this.loadSlow();
    this.loadTransfers();
  }

  private firstPage(query: UiTableQuery | null): UiTableQuery | null {
    return query ? { ...query, page: 1 } : null;
  }

  /** While collapsed, a table shows its short preview; "View All" hands paging to the table. */
  private tableParams(query: UiTableQuery | null, all: boolean, previewSize: number) {
    return {
      search: query?.search || undefined,
      sortBy: query?.sort?.columnKey,
      sortDirection: query?.sort?.direction,
      page: all ? query?.page ?? 1 : 1,
      pageSize: all ? Math.min(query?.pageSize ?? VIEW_ALL_PAGE_SIZE, 100) : previewSize
    };
  }

  private query(): InventoryQuery {
    const filter = this.filter()!;
    const location = parseLocation(filter.location);
    return {
      from: filter.from,
      to: filter.to,
      locationId: location?.id ?? null,
      locationType: location?.type ?? null,
      categoryId: filter.categoryId
    };
  }

  private defaults(meta: OwnerDashboardMeta): AppliedFilter {
    return { preset: 'last30', location: '', categoryId: null, ...resolvePreset('last30', meta.today) };
  }

  /** A deep link wins; then what this screen last applied; then the documented defaults. */
  private initialFilter(meta: OwnerDashboardMeta): AppliedFilter {
    const params = this.route.snapshot.queryParamMap;
    const from = params.get('from');
    const to = params.get('to');
    if (isIsoDateOnly(from) && isIsoDateOnly(to) && !validateRange(from, to)) {
      const location = params.get('location') ?? '';
      const parsed = parseLocation(location);
      const known = (locations: DashboardLocation[]) => locations.some(item => item.id === parsed?.id);
      const valid = parsed && (parsed.type === 'outlet' ? known(meta.showrooms) : known(meta.warehouses));
      const category = Number(params.get('categoryId'));
      return {
        preset: DATE_PRESETS.some(item => item.value === params.get('preset'))
          ? (params.get('preset') as DatePreset)
          : 'custom',
        from,
        to,
        location: valid ? location : '',
        categoryId: meta.categories.some(item => item.id === category) ? category : null
      };
    }

    return this.filter() ?? this.defaults(meta);
  }

  private syncUrl(): void {
    const filter = this.filter()!;
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        from: filter.from,
        to: filter.to,
        preset: filter.preset,
        location: filter.location || null,
        categoryId: filter.categoryId
      }
    });
  }
}
