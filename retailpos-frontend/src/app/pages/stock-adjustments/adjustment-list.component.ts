import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { StockAdjustmentSearchRequest } from '../../models/stock-adjustment.model';

@Component({
  selector: 'app-adjustment-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './adjustment-list.component.html',
  styleUrls: ['./adjustment-list.component.css']
})
export class AdjustmentListComponent implements OnInit, OnDestroy {
  Math = Math;

  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public adjustmentService: StockAdjustmentService,
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

  private performSearch(): void {
    const req: StockAdjustmentSearchRequest = {
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    };
    this.adjustmentService.search(req).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  search(): void {
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

  createAdjustment(): void {
    this.router.navigate(['/stock-adjustments/create']);
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
}
