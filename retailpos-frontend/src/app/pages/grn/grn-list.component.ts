import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { SettingsService } from '../../services/settings.service';
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
  businessTimeZone = signal<string>('Asia/Dhaka');

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public grnService: GrnService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private settingsService: SettingsService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.statusFilter.set(this.listState.str(p, 'status'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.loadBusinessTimeZone();
    this.setupDebouncedSearch();
    this.performSearch();
  }

  private loadBusinessTimeZone(): void {
    this.settingsService.getCompanySettings().subscribe({
      next: (res) => {
        const tz = res?.data?.timeZone;
        if (typeof tz === 'string' && tz.trim()) {
          this.businessTimeZone.set(tz.trim());
        }
      },
      error: () => {
        // Fall back to default timezone if settings cannot be loaded.
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

  formatDateTime(date: string): string {
    if (!date) return '-';

    const value = new Date(date);
    if (Number.isNaN(value.getTime())) return '-';

    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: this.businessTimeZone(),
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    }).formatToParts(value);

    const day = parts.find(p => p.type === 'day')?.value ?? '--';
    const month = parts.find(p => p.type === 'month')?.value ?? '---';
    const year = parts.find(p => p.type === 'year')?.value ?? '----';
    const hour = parts.find(p => p.type === 'hour')?.value ?? '--';
    const minute = parts.find(p => p.type === 'minute')?.value ?? '--';
    const dayPeriod = (parts.find(p => p.type === 'dayPeriod')?.value ?? '').toUpperCase();

    return `${day} ${month}, ${year} ${hour}:${minute} ${dayPeriod}`.trim();
  }
}
