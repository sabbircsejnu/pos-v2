import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';

import { SaleService } from '../../services/sale.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { SaleListDto, SaleSummaryDto } from '../../models/sale.model';

@Component({
  selector: 'app-sales-list',
  standalone: true,
  imports: [CommonModule, FormsModule, AppCurrencyPipe, DatePipe],
  templateUrl: './sales-list.component.html',
  styleUrls: ['./sales-list.component.css']
})
export class SalesListComponent implements OnInit, OnDestroy {
  Math = Math;

  // Filters
  searchQuery = signal('');
  statusFilter = signal('');
  startDate = signal('');
  endDate = signal('');

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);

  // Today's summary
  todaySummary = signal<SaleSummaryDto | null>(null);
  isSummaryLoading = signal(false);

  private searchSubject = new Subject<void>();
  private subs: Subscription[] = [];

  constructor(
    public saleService: SaleService,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.subs.push(
      this.searchSubject.pipe(debounceTime(400)).subscribe(() => this.performSearch())
    );
    this.loadTodaySummary();
    this.search();
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  private loadTodaySummary(): void {
    this.isSummaryLoading.set(true);
    this.saleService.getTodaySummary().subscribe({
      next: (res) => {
        this.todaySummary.set(res.data || res);
        this.isSummaryLoading.set(false);
      },
      error: () => this.isSummaryLoading.set(false)
    });
  }

  onSearchChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  search(): void {
    this.performSearch();
  }

  private performSearch(): void {
    this.saleService.searchSales({
      searchQuery: this.searchQuery() || undefined,
      status: this.statusFilter() || undefined,
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    }).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.statusFilter.set('');
    this.startDate.set('');
    this.endDate.set('');
    this.pageNumber.set(1);
    this.search();
  }

  viewSale(id: number): void {
    this.router.navigate(['/sales', id]);
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.search();
  }

  get totalPages(): number {
    return Math.ceil(this.saleService.totalCount() / this.pageSize()) || 1;
  }

  getStatusClass(status: string): string {
    switch (status?.toLowerCase()) {
      case 'completed': return 'status-completed';
      case 'void': return 'status-void';
      default: return 'status-default';
    }
  }

  getPaymentIcon(method: string): string {
    switch (method?.toLowerCase()) {
      case 'cash': return 'fas fa-money-bill-wave';
      case 'card': return 'fas fa-credit-card';
      case 'mobile': return 'fas fa-mobile-alt';
      default: return 'fas fa-dollar-sign';
    }
  }
}
