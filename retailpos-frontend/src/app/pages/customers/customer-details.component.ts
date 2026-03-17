import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CustomerService } from '../../services/customer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CustomerDto } from '../../models/customer.model';

@Component({
  selector: 'app-customer-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './customer-details.component.html',
  styleUrls: ['./customer-details.component.css']
})
export class CustomerDetailsComponent implements OnInit {
  customer = signal<CustomerDto | null>(null);
  isLoading = signal(false);
  showPointsInput = signal(false);
  pointsToAdd = signal<number>(0);
  isAddingPoints = signal(false);

  constructor(
    private customerService: CustomerService,
    private router: Router,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadCustomer(+id);
    }
  }

  loadCustomer(id: number): void {
    this.isLoading.set(true);
    this.customerService.getById(id).subscribe({
      next: (response) => {
        this.customer.set(response.data || null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.router.navigate(['/customers']);
      }
    });
  }

  editCustomer(): void {
    const c = this.customer();
    if (c) this.router.navigate(['/customers/edit', c.id]);
  }

  togglePointsInput(): void {
    this.showPointsInput.set(!this.showPointsInput());
    this.pointsToAdd.set(0);
  }

  onAddPoints(): void {
    const pts = this.pointsToAdd();
    if (!pts || pts <= 0) {
      this.alertService.error('Enter a valid number of points');
      return;
    }
    const c = this.customer();
    if (!c) return;

    this.isAddingPoints.set(true);
    this.customerService.addPoints(c.id, pts).subscribe({
      next: (response) => {
        this.alertService.success(`${pts} loyalty points added`);
        const updated = response.data;
        if (updated) {
          this.customer.set(updated);
        } else {
          this.customer.set({ ...c, loyaltyPoints: c.loyaltyPoints + pts });
        }
        this.showPointsInput.set(false);
        this.isAddingPoints.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isAddingPoints.set(false);
      }
    });
  }

  backToList(): void {
    this.router.navigate(['/customers']);
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'long', day: 'numeric'
    });
  }

  formatCurrency(amount: number | undefined): string {
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount || 0);
  }
}
