import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { SupplierService } from '../../services/supplier.service';
import { WarehouseService } from '../../services/warehouse.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { PurchaseOrder, PurchaseOrderSearchRequest, getPOStatusLabel, getPOStatusColor } from '../../models/purchase-order.model';
import { CurrencyService } from '../../services/currency.service';
import { AuthService } from '../../services/auth.service';
import { DocumentPdfService } from '../../services/document-pdf.service';

@Component({
  selector: 'app-po-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './po-list.component.html',
  styleUrls: ['./po-list.component.css']
})
export class PoListComponent implements OnInit, OnDestroy {
  Math = Math;

  // Search and filter signals
  searchQuery = signal<string>('');
  status = signal<string>('');
  supplierId = signal<number | undefined>(undefined);
  warehouseId = signal<number | undefined>(undefined);
  startDate = signal<string>('');
  endDate = signal<string>('');
  
  // Pagination signals
  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);
  sortBy = signal<string>('order_date');
  sortOrder = signal<string>('desc');

  // Reason modal for reject/cancel
  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingReasonAction?: (reason: string) => void;

  // Debounce
  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public poService: PurchaseOrderService,
    public supplierService: SupplierService,
    public warehouseService: WarehouseService,
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService,
    private documentPdfService: DocumentPdfService
  ) {}

  can(permission: string): boolean {
    return this.authService.hasPermission(permission);
  }

  ngOnInit(): void {
    this.loadSuppliers();
    this.loadWarehouses();
    this.restoreFromUrl();
    this.setupDebouncedSearch();
    this.performSearch();
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.searchQuery.set(this.listState.str(p, 'search'));
    this.status.set(this.listState.str(p, 'status'));
    this.supplierId.set(this.listState.optionalId(p, 'supplierId'));
    this.warehouseId.set(this.listState.optionalId(p, 'warehouseId'));
    this.startDate.set(this.listState.str(p, 'startDate'));
    this.endDate.set(this.listState.str(p, 'endDate'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.sortBy.set(this.listState.str(p, 'sortBy', 'order_date'));
    this.sortOrder.set(this.listState.str(p, 'sortOrder', 'desc'));
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      search: this.searchQuery() || undefined,
      status: this.status() || undefined,
      supplierId: this.supplierId(),
      warehouseId: this.warehouseId(),
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      sortOrder: this.sortOrder(),
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(500))
      .subscribe(() => {
        this.syncUrl();
        this.performSearch();
      });
  }

  onSearchChange(): void {
    this.searchSubject.next();
  }

  loadSuppliers(): void {
    this.supplierService.getAll().subscribe({
      error: (err: any) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  loadWarehouses(): void {
    this.warehouseService.getAllWarehouses().subscribe({
      error: (err: any) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  private performSearch(): void {
    const searchRequest: PurchaseOrderSearchRequest = {
      searchQuery: this.searchQuery() || undefined,
      status: this.status() || undefined,
      supplierId: this.supplierId(),
      warehouseId: this.warehouseId(),
      startDate: this.startDate() ? new Date(this.startDate()) : undefined,
      endDate: this.endDate() ? new Date(this.endDate()) : undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      sortOrder: this.sortOrder()
    };

    this.poService.search(searchRequest).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  search(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.status.set('');
    this.supplierId.set(undefined);
    this.warehouseId.set(undefined);
    this.startDate.set('');
    this.endDate.set('');
    this.listState.clear(this.route);
    this.pageNumber.set(1);
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

  sort(column: string): void {
    if (this.sortBy() === column) {
      this.sortOrder.set(this.sortOrder() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(column);
      this.sortOrder.set('asc');
    }
    this.syncUrl();
    this.performSearch();
  }

  getSortIcon(column: string): string {
    if (this.sortBy() !== column) return '↕';
    return this.sortOrder() === 'asc' ? '↑' : '↓';
  }

  getStatusLabel(status: string): string {
    return getPOStatusLabel(status);
  }

  getStatusColor(status: string): string {
    return getPOStatusColor(status);
  }

  viewDetails(id: number): void {
    this.router.navigate(['/purchase-orders', id]);
  }

  editPo(po: PurchaseOrder): void {
    if (po.status === 'draft' || po.status === 'sent_back') {
      this.router.navigate(['/purchase-orders/edit', po.id]);
    } else {
      this.alertService.warning('Only draft or sent-back purchase orders can be edited');
    }
  }

  submitForApproval(id: number): void {
    this.alertService.confirm('Submit this purchase order for approval?', () => {
      this.poService.submit(id).subscribe({
        next: () => {
          this.alertService.success('Purchase order submitted for approval');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  approvePo(id: number): void {
    this.alertService.confirm('Approve this purchase order?', () => {
      this.poService.approve(id).subscribe({
        next: () => {
          this.alertService.success('Purchase order approved');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  rejectPo(id: number): void {
    this.openReasonModal('Reject Purchase Order', (reason) => {
      this.poService.reject(id, reason).subscribe({
        next: () => {
          this.alertService.success('Purchase order rejected');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  cancelPo(id: number): void {
    this.openReasonModal('Cancel Purchase Order', (reason) => {
      this.poService.cancel(id, reason).subscribe({
        next: () => {
          this.alertService.success('Purchase order cancelled');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  deletePo(id: number): void {
    this.alertService.confirm('Delete this purchase order? This action cannot be undone.', () => {
      this.poService.delete(id).subscribe({
        next: () => {
          this.alertService.success('Purchase order deleted successfully');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  downloadPoPdf(po: PurchaseOrder): void {
    this.documentPdfService.downloadPurchaseOrder(po.id).subscribe({
      next: (response) => {
        this.documentPdfService.triggerBrowserDownload(response, `${po.poNumber || `PO-${po.id.toString().padStart(6, '0')}`}.pdf`);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  createNew(): void {
    this.router.navigate(['/purchase-orders/create']);
  }

  private openReasonModal(title: string, action: (reason: string) => void): void {
    this.reasonModalTitle.set(title);
    this.reasonText.set('');
    this.pendingReasonAction = action;
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    if (!this.reasonText().trim()) {
      this.alertService.error('Please enter a reason');
      return;
    }
    this.showReasonModal.set(false);
    if (this.pendingReasonAction) {
      this.pendingReasonAction(this.reasonText().trim());
      this.pendingReasonAction = undefined;
    }
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.pendingReasonAction = undefined;
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', { 
      year: 'numeric', 
      month: 'short', 
      day: 'numeric' 
    });
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  activeFilterCount(): number {
    let count = 0;
    if (this.status()) count++;
    if (this.supplierId()) count++;
    if (this.warehouseId()) count++;
    if (this.startDate()) count++;
    if (this.endDate()) count++;
    return count;
  }

  hasActiveFilters(): boolean {
    return this.activeFilterCount() > 0;
  }

  statusCount(status: string): number {
    return this.poService.purchaseOrders().filter(po => po.status === status).length;
  }

  onFilterChange(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }
}
