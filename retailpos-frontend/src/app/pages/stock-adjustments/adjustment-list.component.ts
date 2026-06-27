import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import {
  StockAdjustmentSearchRequest,
  STOCK_ADJUSTMENT_STATUSES,
  StockAdjustmentStatus,
  getStockAdjustmentStatusColor
} from '../../models/stock-adjustment.model';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthorizedLocationDto } from '../../models/report.model';

@Component({
  selector: 'app-adjustment-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './adjustment-list.component.html',
  styleUrls: ['./adjustment-list.component.css']
})
export class AdjustmentListComponent implements OnInit, OnDestroy {
  Math = Math;
  readonly statuses = STOCK_ADJUSTMENT_STATUSES;

  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);
  status = signal<StockAdjustmentStatus | ''>('');
  locationType = signal<string>('');
  locationId = signal<number | null>(null);
  startDate = signal<string>('');
  endDate = signal<string>('');
  outlets = signal<AuthorizedLocationDto[]>([]);
  warehouses = signal<AuthorizedLocationDto[]>([]);

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public adjustmentService: StockAdjustmentService,
    public auth: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private userOutletAccess: UserOutletAccessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.status.set((p['status'] || '') as StockAdjustmentStatus | '');
    this.locationType.set(p['locationType'] || '');
    this.locationId.set(p['locationId'] ? Number(p['locationId']) : null);
    this.startDate.set(p['startDate'] || '');
    this.endDate.set(p['endDate'] || '');
    this.setupDebouncedSearch();
    this.loadFiltersAndSearch();
  }

  private loadFiltersAndSearch(): void {
    this.userOutletAccess.load().subscribe({
      next: (res) => {
        const data = res.data;
        this.outlets.set(data?.outlets || []);
        this.warehouses.set(data?.warehouses || []);
        this.performSearch();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      page: this.pageNumber(),
      pageSize: this.pageSize(),
      status: this.status() || null,
      locationType: this.locationType() || null,
      locationId: this.locationId() || null,
      startDate: this.startDate() || null,
      endDate: this.endDate() || null,
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(400))
      .subscribe(() => this.performSearch());
  }

  private performSearch(): void {
    const req: StockAdjustmentSearchRequest = {
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      status: this.status() || undefined,
      locationType: this.locationType() || undefined,
      locationId: this.locationId() || undefined,
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
    };
    this.adjustmentService.search(req).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  search(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  changePage(page: number): void {
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

  onLocationTypeChange(nextType: string): void {
    this.locationType.set(nextType);
    this.locationId.set(null);
  }

  clearFilters(): void {
    this.status.set('');
    this.locationType.set('');
    this.locationId.set(null);
    this.startDate.set('');
    this.endDate.set('');
    this.search();
  }

  getLocations(): AuthorizedLocationDto[] {
    return this.locationType() === 'warehouse' ? this.warehouses() : this.outlets();
  }

  createAdjustment(): void {
    if (!this.auth.hasPermission('stock_adjustments.create')) {
      this.alertService.error('You do not have permission to create stock adjustments');
      return;
    }
    this.router.navigate(['/stock-adjustments/create']);
  }

  viewAdjustment(id: number): void {
    this.router.navigate(['/stock-adjustments', id]);
  }

  getStatusColor(status: string): string {
    return getStockAdjustmentStatusColor(status);
  }

  getQtyChangeClass(qty: number): string {
    return qty >= 0 ? 'text-green-600 font-semibold' : 'text-red-600 font-semibold';
  }

  getQtyChangePrefix(qty: number): string {
    return qty > 0 ? '+' : '';
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }

  formatDateTime(date?: string | null): string {
    if (!date) return '-';
    return new Date(date).toLocaleString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }
}
