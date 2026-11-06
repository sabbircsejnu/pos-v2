import { CompanyDateService } from '../../services/company-date.service';
import { getPurchaseLocationName, getPurchaseLocationTypeLabel } from '../../models/purchase-order.model';
import { Component, OnInit, OnDestroy, computed, signal, inject  } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { GrnSearchRequest } from '../../models/grn.model';
import { AuthorizedLocationDto } from '../../models/report.model';
import {
  UiAlertComponent,
  UiButtonComponent,
  UiCardComponent,
  UiControlDirective,
  UiDatePickerComponent,
  UiFormFieldComponent
} from '../../components/shared/ui';

type GrnLocationTypeFilter = '' | 'outlet' | 'warehouse';

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

@Component({
  selector: 'app-grn-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    UiAlertComponent,
    UiButtonComponent,
    UiCardComponent,
    UiControlDirective,
    UiDatePickerComponent,
    UiFormFieldComponent
  ],
  templateUrl: './grn-list.component.html',
  styleUrls: ['./grn-list.component.css']
})
export class GrnListComponent implements OnInit, OnDestroy {
  private readonly companyDates = inject(CompanyDateService);
  private readonly locationAccess = inject(UserOutletAccessService);
  readonly locationTypeLabel = getPurchaseLocationTypeLabel;
  readonly locationName = getPurchaseLocationName;
  Math = Math;

  statusFilter = signal<string>('');
  locationTypeFilter = signal<GrnLocationTypeFilter>('');
  locationFilter = signal<number | null>(null);
  fromDate = signal<string>('');
  toDate = signal<string>('');
  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);

  /** Selectable locations come from the server's GRN access policy; the server also enforces them. */
  outlets = signal<AuthorizedLocationDto[]>([]);
  warehouses = signal<AuthorizedLocationDto[]>([]);

  readonly locationOptions = computed<AuthorizedLocationDto[]>(() => {
    switch (this.locationTypeFilter()) {
      case 'outlet': return this.outlets();
      case 'warehouse': return this.warehouses();
      default: return [];
    }
  });

  readonly allLocationsLabel = computed(() => {
    switch (this.locationTypeFilter()) {
      case 'outlet': return 'All Outlets';
      case 'warehouse': return 'All Warehouses';
      default: return 'All Locations';
    }
  });

  readonly dateRangeError = computed(() =>
    this.fromDate() && this.toDate() && this.fromDate() > this.toDate()
      ? 'From Date cannot be later than To Date.'
      : '');

  readonly hasActiveFilters = computed(() =>
    !!(this.statusFilter() || this.locationTypeFilter() || this.fromDate() || this.toDate()));

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;
  private activeSearch?: Subscription;
  private locationsSubscription?: Subscription;

  constructor(
    public grnService: GrnService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.statusFilter.set(this.listState.str(p, 'status'));
    const locationType = this.listState.str(p, 'locationType');
    this.locationTypeFilter.set(locationType === 'outlet' || locationType === 'warehouse' ? locationType : '');
    this.locationFilter.set(this.listState.optionalId(p, 'locationId') ?? null);
    this.fromDate.set(this.readDate(p, 'fromDate'));
    this.toDate.set(this.readDate(p, 'toDate'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.setupDebouncedSearch();
    this.loadLocations();
  }

  private readDate(params: Params, key: string): string {
    const value = this.listState.str(params, key);
    return ISO_DATE.test(value) ? value : '';
  }

  /** The first search waits for the options, so a stale link cannot request a location the user no longer has. */
  private loadLocations(): void {
    this.locationsSubscription = this.locationAccess.load('GrnView').subscribe({
      next: (response) => {
        this.outlets.set(response.data.outlets);
        this.warehouses.set(response.data.warehouses);
        this.dropUnavailableLocation();
        this.performSearch();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.dropUnavailableLocation();
        this.performSearch();
      }
    });
  }

  private dropUnavailableLocation(): void {
    const type = this.locationTypeFilter();
    const hasType = (type === 'outlet' && this.outlets().length > 0)
      || (type === 'warehouse' && this.warehouses().length > 0);
    if (!hasType) {
      this.locationTypeFilter.set('');
    }
    const location = this.locationFilter();
    if (location !== null && !this.locationOptions().some(option => option.id === location)) {
      this.locationFilter.set(null);
    }
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      status: this.statusFilter() || undefined,
      locationType: this.locationTypeFilter() || undefined,
      locationId: this.locationFilter() ?? undefined,
      fromDate: this.fromDate() || undefined,
      toDate: this.toDate() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
    this.locationsSubscription?.unsubscribe();
    if (this.activeSearch && !this.activeSearch.closed) {
      this.activeSearch.unsubscribe();
      this.grnService.isLoading.set(false);
    }
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(400))
      .subscribe(() => {
        this.syncUrl();
        this.performSearch();
      });
  }

  onFilterChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  onLocationTypeChange(): void {
    this.locationFilter.set(null);
    this.onFilterChange();
  }

  private performSearch(): void {
    if (this.dateRangeError()) {
      return;
    }
    const req: GrnSearchRequest = {
      status: this.statusFilter() || undefined,
      locationType: this.locationTypeFilter() || undefined,
      locationId: this.locationFilter() ?? undefined,
      fromDate: this.fromDate() || undefined,
      toDate: this.toDate() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    };
    // A slower earlier response must not overwrite the results of the current filters.
    this.activeSearch?.unsubscribe();
    this.activeSearch = this.grnService.search(req).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  search(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  clearFilters(): void {
    this.statusFilter.set('');
    this.locationTypeFilter.set('');
    this.locationFilter.set(null);
    this.fromDate.set('');
    this.toDate.set('');
    this.listState.clear(this.route);
    this.search();
  }

  changePage(page: number): void {
    if (this.dateRangeError()) {
      return;
    }
    this.pageNumber.set(page);
    this.syncUrl();
    this.performSearch();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  createGrn(): void {
    this.router.navigate(['/grn/create']);
  }

  viewGrn(id: number): void {
    this.router.navigate(['/grn', id]);
  }

  getStatusBadgeClass(status: string): string {
    switch (status?.toLowerCase()) {
      case 'full': return 'bg-green-100 text-green-800';
      case 'partial': return 'bg-yellow-100 text-yellow-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  }

  formatDateTime(date: string): string {
    if (!date) return '-';
    return this.companyDates.format(date, true, this.companyDates.timeZone());
  }
}
