import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { Supplier, SupplierPerformanceDto } from '../../models/supplier.model';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-supplier-details',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './supplier-details.component.html',
  styleUrls: ['./supplier-details.component.css']
})
export class SupplierDetailsComponent implements OnInit {
  supplier = signal<Supplier | null>(null);
  performance = signal<SupplierPerformanceDto | null>(null);
  isLoading = signal(false);
  isLoadingPerformance = signal(false);

  constructor(
    private supplierService: SupplierService,
    private router: Router,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadSupplier(+id);
      this.loadPerformance(+id);
    }
  }

  loadSupplier(id: number): void {
    this.isLoading.set(true);
    this.supplierService.getById(id).subscribe({
      next: (response) => {
        this.supplier.set(response.data || null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.router.navigate(['/suppliers']);
      }
    });
  }

  loadPerformance(id: number): void {
    this.isLoadingPerformance.set(true);
    this.supplierService.getPerformance(id).subscribe({
      next: (response) => {
        this.performance.set(response.data || null);
        this.isLoadingPerformance.set(false);
      },
      error: (err) => {
        console.error('Error loading performance:', err);
        this.isLoadingPerformance.set(false);
      }
    });
  }

  editSupplier(): void {
    const supplier = this.supplier();
    if (supplier) {
      this.router.navigate(['/suppliers/edit', supplier.id]);
    }
  }

  deleteSupplier(): void {
    const supplier = this.supplier();
    if (supplier) {
      this.alertService.confirm(
        `Are you sure you want to delete supplier "${supplier.name}"?`,
        () => {
          this.supplierService.delete(supplier.id).subscribe({
            next: () => {
              this.alertService.success('Supplier deleted successfully');
              this.router.navigate(['/suppliers']);
            },
            error: (err) => {
              this.alertService.error(this.errorHandler.extractErrorMessage(err));
            }
          });
        }
      );
    }
  }

  backToList(): void {
    this.router.navigate(['/suppliers']);
  }

  getHealthStatusClass(status: string): string {
    switch (status) {
      case 'Good':
        return 'bg-green-100 text-green-800';
      case 'Warning':
        return 'bg-yellow-100 text-yellow-800';
      case 'Critical':
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  getCreditUtilizationColor(percentage: number): string {
    if (percentage < 70) return 'text-green-600';
    if (percentage < 90) return 'text-yellow-600';
    return 'text-red-600';
  }

  formatCurrency(amount: number | undefined): string {
    return this.currencyService.format(amount ?? 0);
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  }
}
