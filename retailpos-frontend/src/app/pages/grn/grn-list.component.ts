import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { GrnSearchRequest } from '../../models/grn.model';

@Component({
  selector: 'app-grn-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './grn-list.component.html',
  styleUrls: ['./grn-list.component.css']
})
export class GrnListComponent implements OnInit, OnDestroy {
  Math = Math;

  statusFilter = signal<string>('');
  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public grnService: GrnService,
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
    const req: GrnSearchRequest = {
      status: this.statusFilter() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    };
    this.grnService.search(req).subscribe({
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

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }
}
