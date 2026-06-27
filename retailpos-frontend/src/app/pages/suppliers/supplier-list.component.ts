import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { Supplier, SupplierSearchRequest } from '../../models/supplier.model';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-supplier-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './supplier-list.component.html',
  styleUrls: ['./supplier-list.component.css']
})
export class SupplierListComponent implements OnInit, OnDestroy {
  // Expose Math to template
  Math = Math;
  
  // Search filters
  searchQuery = signal('');
  minCreditLimit = signal<number | undefined>(undefined);
  maxCreditLimit = signal<number | undefined>(undefined);

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);
  sortBy = signal('name');
  sortOrder = signal('asc');

  // Debounced search
  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public supplierService: SupplierService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.restoreFromUrl();
    this.setupDebouncedSearch();
    this.performSearch();
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.searchQuery.set(this.listState.str(p, 'search'));
    const min = p['minCreditLimit'] != null ? Number(p['minCreditLimit']) : undefined;
    this.minCreditLimit.set(!isNaN(min as number) && min !== undefined ? min : undefined);
    const max = p['maxCreditLimit'] != null ? Number(p['maxCreditLimit']) : undefined;
    this.maxCreditLimit.set(!isNaN(max as number) && max !== undefined ? max : undefined);
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.sortBy.set(this.listState.str(p, 'sortBy', 'name'));
    this.sortOrder.set(this.listState.str(p, 'sortOrder', 'asc'));
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      search: this.searchQuery() || undefined,
      minCreditLimit: this.minCreditLimit(),
      maxCreditLimit: this.maxCreditLimit(),
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

  private performSearch(): void {
    const searchRequest: SupplierSearchRequest = {
      searchQuery: this.searchQuery() || undefined,
      minCreditLimit: this.minCreditLimit(),
      maxCreditLimit: this.maxCreditLimit(),
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      sortOrder: this.sortOrder()
    };

    this.supplierService.search(searchRequest).subscribe();
  }

  search(): void {
    this.performSearch();
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.minCreditLimit.set(undefined);
    this.maxCreditLimit.set(undefined);
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.performSearch();
  }

  createSupplier(): void {
    this.router.navigate(['/suppliers/create']);
  }

  viewSupplier(id: number): void {
    this.router.navigate(['/suppliers', id]);
  }

  editSupplier(id: number, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/suppliers/edit', id]);
  }

  deleteSupplier(id: number, name: string, event: Event): void {
    event.stopPropagation();
    
    this.alertService.confirm(
      `Are you sure you want to delete supplier "${name}"?`,
      () => {
        this.supplierService.delete(id).subscribe({
          next: () => {
            this.alertService.success('Supplier deleted successfully');
            this.search();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
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
    if (this.sortBy() !== column) return 'fas fa-sort text-gray-400';
    return this.sortOrder() === 'asc' ? 'fas fa-sort-up text-gray-900' : 'fas fa-sort-down text-gray-900';
  }

  getCreditUtilizationClass(supplier: Supplier): string {
    if (!supplier.creditLimit || supplier.creditLimit === 0) {
      return 'text-gray-500';
    }

    const utilization = ((supplier.outstandingBalance || 0) / supplier.creditLimit) * 100;

    if (utilization < 70) return 'text-green-600';
    if (utilization < 90) return 'text-yellow-600';
    return 'text-red-600';
  }

  formatCurrency(amount: number | undefined): string {
    return this.currencyService.format(amount ?? 0);
  }
}
