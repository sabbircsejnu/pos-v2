import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  StockTransferDto,
  StockTransferSearchRequest,
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
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.setupDebouncedSearch();
    this.search();
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(400))
      .subscribe(() => this.performSearch());
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
    this.performSearch();
  }

  clearFilters(): void {
    this.statusFilter.set('');
    this.search();
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.performSearch();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.performSearch();
  }

  createTransfer(): void {
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
    this.alertService.confirm('Receive this transfer and update stock?', () => {
      this.transferService.receive(id).subscribe({
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

  getStatusColor(status: string): string {
    return getTransferStatusColor(status);
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }
}
