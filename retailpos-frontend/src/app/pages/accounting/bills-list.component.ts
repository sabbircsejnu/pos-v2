import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { SupplierService } from '../../services/supplier.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateBillDto } from '../../models/accounting.model';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-bills-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bills-list.component.html',
  styleUrls: ['./bills-list.component.css']
})
export class BillsListComponent implements OnInit {
  Math = Math;

  activeStatusFilter = signal('all');

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);

  // Add bill modal
  showModal = signal(false);
  isSubmitting = signal(false);
  formSupplierId = signal<number | undefined>(undefined);
  formPoId = signal<number | undefined>(undefined);
  formAmountDue = signal<number>(0);
  formDueDate = signal('');

  constructor(
    public accountingService: AccountingService,
    public supplierService: SupplierService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.supplierService.loadSuppliers();
    this.loadSummary();
    this.loadBills();
  }

  loadSummary(): void {
    this.accountingService.getBillSummary().subscribe();
  }

  loadBills(): void {
    this.accountingService.searchBills({
      status: this.activeStatusFilter() === 'all' ? undefined : this.activeStatusFilter(),
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    }).subscribe();
  }

  setStatusFilter(status: string): void {
    this.activeStatusFilter.set(status);
    this.pageNumber.set(1);
    this.loadBills();
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.loadBills();
  }

  openModal(): void {
    this.formSupplierId.set(undefined);
    this.formPoId.set(undefined);
    this.formAmountDue.set(0);
    this.formDueDate.set('');
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  onSubmit(): void {
    if (!this.formSupplierId()) {
      this.alertService.error('Supplier ID is required');
      return;
    }
    if (!this.formAmountDue() || this.formAmountDue() <= 0) {
      this.alertService.error('Amount due must be greater than 0');
      return;
    }
    if (!this.formDueDate()) {
      this.alertService.error('Due date is required');
      return;
    }

    this.isSubmitting.set(true);
    const dto: CreateBillDto = {
      supplierId: this.formSupplierId()!,
      poId: this.formPoId() || undefined,
      amountDue: this.formAmountDue(),
      dueDate: this.formDueDate()
    };

    this.accountingService.createBill(dto).subscribe({
      next: () => {
        this.alertService.success('Bill created successfully');
        this.showModal.set(false);
        this.isSubmitting.set(false);
        this.loadSummary();
        this.loadBills();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  markAsPaid(id: number, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm('Mark this bill as paid?', () => {
      this.accountingService.updateBillStatus(id, { status: 'paid' }).subscribe({
        next: () => {
          this.alertService.success('Bill marked as paid');
          this.loadSummary();
          this.loadBills();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    });
  }

  getStatusBadgeClass(status: string): string {
    const map: { [key: string]: string } = {
      unpaid: 'bg-red-100 text-red-800',
      partial: 'bg-yellow-100 text-yellow-800',
      paid: 'bg-green-100 text-green-800'
    };
    return map[status] || 'bg-gray-100 text-gray-800';
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
