import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { CustomerService } from '../../services/customer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './customer-list.component.html',
  styleUrls: ['./customer-list.component.css']
})
export class CustomerListComponent implements OnInit, OnDestroy {
  Math = Math;

  searchQuery = signal('');
  pageNumber = signal(1);
  pageSize = signal(10);

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public customerService: CustomerService,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.setupDebouncedSearch();
    this.loadCustomers();
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(400))
      .subscribe(() => this.performSearch());
  }

  onSearchChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  loadCustomers(): void {
    this.performSearch();
  }

  private performSearch(): void {
    this.customerService.getAll(
      this.searchQuery() || undefined,
      this.pageNumber(),
      this.pageSize()
    ).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.pageNumber.set(1);
    this.performSearch();
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

  createCustomer(): void {
    this.router.navigate(['/customers/create']);
  }

  viewCustomer(id: number): void {
    this.router.navigate(['/customers', id]);
  }

  editCustomer(id: number, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/customers/edit', id]);
  }

  deleteCustomer(id: number, name: string, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm(`Are you sure you want to delete customer "${name}"?`, () => {
      this.customerService.delete(id).subscribe({
        next: () => {
          this.alertService.success('Customer deleted successfully');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }
}
