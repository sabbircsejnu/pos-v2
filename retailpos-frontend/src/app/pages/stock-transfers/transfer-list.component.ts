import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import {
  StockTransferDto,
  ReceiveStockTransferDto,
  StockTransferSearchRequest,
  getTransferTypeLabel,
  getTransferStatusLabel,
  getTransferStatusColor
} from '../../models/stock-transfer.model';

@Component({
  selector: 'app-transfer-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transfer-list.component.html',
  styleUrls: ['./transfer-list.component.css']
})
export class TransferListComponent implements OnInit, OnDestroy {
  Math = Math;
  defaultLocation = signal<{ id: number; type: 'outlet' | 'warehouse' } | null>(null);

  statusFilter = signal<string>('');
  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);

  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingAction?: (reason: string) => void;

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public transferService: StockTransferService,
    public auth: AuthService,
    private userOutletAccess: UserOutletAccessService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.statusFilter.set(this.listState.str(p, 'status'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.setupDebouncedSearch();
    this.loadDefaultLocation();
    this.performSearch();
  }

  private loadDefaultLocation(): void {
    this.userOutletAccess.load().subscribe({
      next: (response) => {
        const auth = response?.data;
        if (auth?.defaultLocationId && auth?.defaultLocationType) {
          this.defaultLocation.set({ id: auth.defaultLocationId, type: auth.defaultLocationType });
          return;
        }

        this.defaultLocation.set(null);
      },
      error: () => {
        this.defaultLocation.set(null);
      }
    });
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      status: this.statusFilter() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
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

  private performSearch(): void {
    const req: StockTransferSearchRequest = {
      status: this.statusFilter() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    };
    this.transferService.search(req).subscribe({
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
    this.listState.clear(this.route);
    this.search();
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

  createTransfer(): void {
    if (!this.auth.hasPermission('stock_transfers.create')) {
      this.alertService.error('You do not have permission to create stock transfers');
      return;
    }
    this.router.navigate(['/stock-transfers/create']);
  }

  viewTransfer(id: number): void {
    this.router.navigate(['/stock-transfers', id]);
  }

  approveTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm('Approve this stock transfer?', () => {
      this.transferService.approve(id).subscribe({
        next: () => {
          this.alertService.success('Transfer approved');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  rejectTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.openReasonModal('Reject Transfer', (reason) => {
      this.transferService.reject(id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer rejected');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  sendTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm('Mark this transfer as in-transit?', () => {
      this.transferService.send(id).subscribe({
        next: () => {
          this.alertService.success('Transfer marked as in-transit');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  receiveTransfer(id: number, event: Event): void {
    event.stopPropagation();
    const transfer = this.transferService.transfers().find(t => t.id === id);
    if (!transfer) {
      this.alertService.error('Transfer details not found for receiving');
      return;
    }

    const payload = this.buildFullReceivePayload(transfer);
    this.alertService.confirm('Receive this transfer and update stock?', () => {
      this.transferService.receive(id, payload).subscribe({
        next: () => {
          this.alertService.success('Transfer received and stock updated');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  cancelTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.openReasonModal('Cancel Transfer', (reason) => {
      this.transferService.cancel(id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer cancelled');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  private openReasonModal(title: string, action: (reason: string) => void): void {
    this.reasonModalTitle.set(title);
    this.reasonText.set('');
    this.pendingAction = action;
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    if (!this.reasonText().trim()) {
      this.alertService.error('Please enter a reason');
      return;
    }
    this.showReasonModal.set(false);
    if (this.pendingAction) {
      this.pendingAction(this.reasonText().trim());
      this.pendingAction = undefined;
    }
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.pendingAction = undefined;
  }

  getStatusLabel(status: string): string {
    return getTransferStatusLabel(status);
  }

  getTypeLabel(type: string): string {
    return getTransferTypeLabel(type);
  }

  getStatusColor(status: string): string {
    return getTransferStatusColor(status);
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }

  canReceiveOrRejectTransfer(transfer: StockTransferDto): boolean {
    const userDefault = this.defaultLocation();
    if (!userDefault) {
      return false;
    }

    return transfer.toLocationId === userDefault.id
      && transfer.toLocationType.toLowerCase() === userDefault.type;
  }

  private buildFullReceivePayload(transfer: StockTransferDto): ReceiveStockTransferDto {
    return {
      notes: 'Full acceptance from list action',
      items: transfer.items.map(item => ({
        variantId: item.variantId,
        acceptedQuantity: item.transferQuantity > 0 ? item.transferQuantity : item.quantity,
        rejectedQuantity: 0,
        remarks: item.remarks
      }))
    };
  }
}
