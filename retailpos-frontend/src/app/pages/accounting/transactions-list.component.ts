import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateTransactionDto } from '../../models/accounting.model';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-transactions-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transactions-list.component.html',
  styleUrls: ['./transactions-list.component.css']
})
export class TransactionsListComponent implements OnInit {
  // Filters
  filterAccountId = signal<number | undefined>(undefined);
  filterStartDate = signal('');
  filterEndDate = signal('');

  // Add transaction modal
  showModal = signal(false);
  isSubmitting = signal(false);
  formAccountId = signal<number | undefined>(undefined);
  formAmount = signal<number>(0);
  formType = signal('debit');
  formDescription = signal('');
  formDate = signal(new Date().toISOString().split('T')[0]);

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.loadTransactions();
    this.accountingService.getAccounts().subscribe();
  }

  loadTransactions(): void {
    const filters = {
      accountId: this.filterAccountId(),
      startDate: this.filterStartDate() || undefined,
      endDate: this.filterEndDate() || undefined
    };
    this.accountingService.getTransactions(filters).subscribe();
  }

  applyFilters(): void {
    this.loadTransactions();
  }

  clearFilters(): void {
    this.filterAccountId.set(undefined);
    this.filterStartDate.set('');
    this.filterEndDate.set('');
    this.loadTransactions();
  }

  openModal(): void {
    this.formAccountId.set(undefined);
    this.formAmount.set(0);
    this.formType.set('debit');
    this.formDescription.set('');
    this.formDate.set(new Date().toISOString().split('T')[0]);
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  onSubmit(): void {
    if (!this.formAccountId()) {
      this.alertService.error('Please select an account');
      return;
    }
    if (!this.formAmount() || this.formAmount() <= 0) {
      this.alertService.error('Amount must be greater than 0');
      return;
    }

    this.isSubmitting.set(true);
    const dto: CreateTransactionDto = {
      accountId: this.formAccountId()!,
      amount: this.formAmount(),
      type: this.formType(),
      description: this.formDescription() || undefined,
      transactionDate: this.formDate()
    };

    this.accountingService.createTransaction(dto).subscribe({
      next: () => {
        this.alertService.success('Transaction created successfully');
        this.showModal.set(false);
        this.isSubmitting.set(false);
        this.loadTransactions();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  getTypeBadgeClass(type: string): string {
    return type === 'credit'
      ? 'bg-green-100 text-green-800'
      : 'bg-red-100 text-red-800';
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
