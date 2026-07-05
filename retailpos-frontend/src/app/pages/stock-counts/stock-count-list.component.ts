import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { StockCountService } from '../../services/stock-count.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthorizedLocationDto } from '../../models/report.model';
import { STOCK_COUNT_STATUSES, StockCountStatus } from '../../models/stock-count.model';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-stock-count-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-count-list.component.html',
  styleUrls: ['./stock-count-list.component.css']
})
export class StockCountListComponent implements OnInit, OnDestroy {
  Math = Math;
  readonly phase3Enabled = environment.stockCountPhase3Enabled;
  readonly statuses = STOCK_COUNT_STATUSES;
  readonly rejectionReasonMaxLength = 500;

  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingRejectId: number | null = null;

  pageNumber = signal(1);
  pageSize = signal(20);
  searchQuery = signal('');
  status = signal<StockCountStatus | ''>('');
  dateFrom = signal('');
  dateTo = signal('');
  locationType = signal<string>('');
  locationId = signal<number | null>(null);

  outlets = signal<AuthorizedLocationDto[]>([]);
  warehouses = signal<AuthorizedLocationDto[]>([]);
  canSelectAnyLocation = signal(false);

  private sub?: Subscription;

  constructor(
    public service: StockCountService,
    public auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
    private listState: ListStateService,
    private locationAccess: UserOutletAccessService,
    private alert: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 20));
    this.searchQuery.set(this.listState.str(p, 'search'));
    this.status.set((this.listState.str(p, 'status') as StockCountStatus | '') || '');
    this.dateFrom.set(this.listState.str(p, 'dateFrom'));
    this.dateTo.set(this.listState.str(p, 'dateTo'));
    this.locationType.set(this.listState.str(p, 'locationType'));
    this.locationId.set(this.listState.optionalId(p, 'locationId') ?? null);

    this.sub = this.locationAccess.load().subscribe({
      next: (res) => {
        const data = res.data;
        this.outlets.set(data?.outlets || []);
        this.warehouses.set(data?.warehouses || []);
        this.canSelectAnyLocation.set(!!data?.isGlobalAccess || this.auth.hasPermission('StockCount.ViewAll'));

        if (!this.canSelectAnyLocation()) {
          this.locationType.set(data?.defaultLocationType || this.locationType() || 'outlet');
          this.locationId.set(data?.defaultLocationId ?? this.locationId());
        }

        this.load();
      },
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      page: this.pageNumber(),
      pageSize: this.pageSize(),
      search: this.searchQuery() || null,
      status: this.status() || null,
      dateFrom: this.dateFrom() || null,
      dateTo: this.dateTo() || null,
      locationType: this.locationType() || null,
      locationId: this.locationId() || null,
    });
  }

  load(): void {
    this.syncUrl();
    this.service.search({
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      search: this.searchQuery() || undefined,
      status: this.status() || undefined,
      dateFrom: this.dateFrom() || undefined,
      dateTo: this.dateTo() || undefined,
      locationType: this.locationType() || undefined,
      locationId: this.locationId() || undefined,
    }).subscribe({
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.status.set('');
    this.dateFrom.set('');
    this.dateTo.set('');

    if (this.canSelectAnyLocation()) {
      this.locationType.set('');
      this.locationId.set(null);
    }

    this.pageNumber.set(1);
    this.load();
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.load();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.load();
  }

  onLocationTypeChange(nextType: string): void {
    this.locationType.set(nextType);
    this.locationId.set(null);
  }

  getLocations(): AuthorizedLocationDto[] {
    return this.locationType() === 'warehouse' ? this.warehouses() : this.outlets();
  }

  createNew(): void {
    this.router.navigate(['/stock-counts/create']);
  }

  viewDetails(id: number): void {
    this.router.navigate(['/stock-counts', id]);
  }

  download(id: number, stockCountNo: string): void {
    this.service.downloadExcel(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `StockCount_${stockCountNo}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  print(id: number): void {
    this.service.getPrintData(id).subscribe({
      next: (res) => this.service.printDocument(res.data),
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  canSubmit(status: string): boolean {
    return status === 'Draft' && this.auth.hasPermission('StockCount.Submit');
  }

  canReject(status: string): boolean {
    return status === 'Submitted' && this.auth.hasPermission('StockCount.Reject');
  }

  canReopen(status: string): boolean {
    return status === 'Rejected' && this.auth.hasPermission('StockCount.Reopen');
  }

  canApprove(status: string): boolean {
    return this.phase3Enabled && status === 'Submitted' && this.auth.hasPermission('StockCount.Approve');
  }

  canGenerateAdjustmentDraft(status: string): boolean {
    return this.phase3Enabled && status === 'Approved' && this.auth.hasPermission('StockCount.Approve');
  }

  submit(id: number): void {
    this.alert.confirm(
      'Submit this stock count for review?',
      () => {
        this.service.submit(id).subscribe({
          next: () => {
            this.alert.success('Stock count submitted successfully');
            this.load();
          },
          error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
        });
      },
      'Confirm Submit',
      'Submit'
    );
  }

  reject(id: number): void {
    this.reasonModalTitle.set('Reject Stock Count');
    this.reasonText.set('');
    this.pendingRejectId = id;
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    if (this.pendingRejectId == null) {
      this.closeReasonModal();
      return;
    }

    const trimmedReason = this.reasonText().trim();
    if (!trimmedReason) {
      this.alert.error('Please enter a rejection reason');
      return;
    }

    if (trimmedReason.length > this.rejectionReasonMaxLength) {
      this.alert.error(`Rejection reason cannot exceed ${this.rejectionReasonMaxLength} characters`);
      return;
    }

    const id = this.pendingRejectId;
    const reason = trimmedReason;
    this.closeReasonModal();

    this.service.reject(id, reason).subscribe({
      next: () => {
        this.alert.success('Stock count rejected');
        this.load();
      },
      error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.pendingRejectId = null;
    this.reasonText.set('');
  }

  approve(id: number): void {
    this.alert.confirm(
      'Approve this submitted stock count?',
      () => {
        this.service.approve(id).subscribe({
          next: () => {
            this.alert.success('Stock count approved');
            this.load();
          },
          error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
        });
      },
      'Confirm Approve',
      'Approve'
    );
  }

  reopen(id: number): void {
    this.alert.confirm(
      'Reopen this rejected stock count?',
      () => {
        this.service.reopen(id).subscribe({
          next: () => {
            this.alert.success('Stock count reopened');
            this.load();
          },
          error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
        });
      },
      'Confirm Reopen',
      'Reopen'
    );
  }

  generateAdjustmentDraft(id: number): void {
    this.alert.confirm(
      'Generate stock adjustment draft from differences?',
      () => {
        this.service.generateAdjustmentDraft(id).subscribe({
          next: () => {
            this.alert.success('Stock adjustment draft generated');
            this.load();
          },
          error: (err) => this.alert.error(this.errorHandler.extractErrorMessage(err))
        });
      },
      'Confirm Generate Draft',
      'Generate'
    );
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  formatDateTime(date?: string | null): string {
    if (!date) return '-';
    return new Date(date).toLocaleString('en-US', { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Draft': return 'bg-gray-100 text-gray-700';
      case 'Submitted': return 'bg-blue-100 text-blue-700';
      case 'Rejected': return 'bg-red-100 text-red-700';
      case 'Approved': return 'bg-green-100 text-green-700';
      default: return 'bg-gray-100 text-gray-700';
    }
  }
}
