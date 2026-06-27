import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { BusinessService } from '../../services/business.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { BusinessSummaryDto } from '../../models/business.model';

@Component({
  selector: 'app-business-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './business-list.component.html'
})
export class BusinessListComponent implements OnInit {
  businesses = signal<BusinessSummaryDto[]>([]);
  isLoading = signal(false);
  searchTerm = signal('');
  statusFilter = signal<'all' | 'active' | 'inactive'>('all');

  constructor(
    private businessService: BusinessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.searchTerm.set(this.listState.str(p, 'search'));
    const statusParam = this.listState.str(p, 'status', 'all');
    this.statusFilter.set(
      statusParam === 'active' ? 'active' : statusParam === 'inactive' ? 'inactive' : 'all'
    );
    this.loadBusinesses();
  }

  loadBusinesses(): void {
    this.isLoading.set(true);

    const status = this.statusFilter();
    const isActive = status === 'all' ? undefined : status === 'active';

    this.listState.update(this.route, {
      search: this.searchTerm() || undefined,
      status: status !== 'all' ? status : undefined,
    });

    this.businessService.getBusinesses(this.searchTerm(), isActive).subscribe({
      next: (res) => {
        this.businesses.set(res.data || []);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  createBusiness(): void {
    this.router.navigate(['/businesses/create']);
  }

  manageSetup(business: BusinessSummaryDto): void {
    this.router.navigate(['/businesses', business.businessId, 'setup']);
  }

  toggleStatus(business: BusinessSummaryDto): void {
    const nextState = !business.isActive;

    this.alertService.confirm(
      `Are you sure you want to ${nextState ? 'activate' : 'deactivate'} ${business.businessName}?`,
      () => {
        this.businessService.updateBusinessStatus(business.businessId, { isActive: nextState }).subscribe({
          next: () => {
            this.alertService.success(`Business ${nextState ? 'activated' : 'deactivated'} successfully`);
            this.loadBusinesses();
          },
          error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
        });
      }
    );
  }
}
