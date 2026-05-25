import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-expenses-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './expenses-list.component.html',
  styleUrls: ['./expenses-list.component.css']
})
export class ExpensesListComponent implements OnInit {
  Math = Math;

  // Filters
  filterCategory = signal('');
  filterStartDate = signal('');
  filterEndDate = signal('');

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);

  readonly categories = ['Rent', 'Utilities', 'Salaries', 'Marketing', 'Supplies', 'Transport', 'Maintenance', 'Other'];

  constructor(
    public accountingService: AccountingService,
    private router: Router,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.loadSummary();
    this.loadExpenses();
  }

  loadSummary(): void {
    this.accountingService.getExpenseSummary().subscribe();
  }

  loadExpenses(): void {
    this.accountingService.searchExpenses({
      category: this.filterCategory() || undefined,
      startDate: this.filterStartDate() || undefined,
      endDate: this.filterEndDate() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    }).subscribe();
  }

  applyFilters(): void {
    this.pageNumber.set(1);
    this.loadExpenses();
  }

  clearFilters(): void {
    this.filterCategory.set('');
    this.filterStartDate.set('');
    this.filterEndDate.set('');
    this.pageNumber.set(1);
    this.loadExpenses();
  }

  createExpense(): void {
    this.router.navigate(['/expenses/create']);
  }

  editExpense(id: number, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/expenses/edit', id]);
  }

  deleteExpense(id: number, category: string, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm(
      `Are you sure you want to delete this ${category} expense?`,
      () => {
        this.accountingService.deleteExpense(id).subscribe({
          next: () => {
            this.alertService.success('Expense deleted successfully');
            this.loadSummary();
            this.loadExpenses();
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
    this.loadExpenses();
  }

  getSummaryCategories(): string[] {
    const summary = this.accountingService.expenseSummary();
    return summary ? Object.keys(summary.byCategory) : [];
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
