import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { ExpenseCategoryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-expense-categories',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './expense-categories.component.html',
  styleUrls: ['./expense-categories.component.css']
})
export class ExpenseCategoriesComponent implements OnInit {
  categories = signal<ExpenseCategoryDto[]>([]);
  isLoading = signal(false);
  isSaving = signal(false);
  editingId = signal<number | null>(null);
  name = signal('');
  expenseAccountId = signal<number | null>(null);

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.accountingService.getExpenseCategories(true).subscribe({
      next: () => {
        this.categories.set(this.accountingService.expenseCategories());
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  startCreate(): void {
    this.editingId.set(null);
    this.name.set('');
    this.expenseAccountId.set(null);
  }

  startEdit(category: ExpenseCategoryDto): void {
    this.editingId.set(category.id);
    this.name.set(category.name);
    this.expenseAccountId.set(category.expenseAccountId);
  }

  save(): void {
    if (!this.name().trim() || !this.expenseAccountId()) {
      this.alertService.error('Category name and expense account are required');
      return;
    }

    this.isSaving.set(true);
    const request = this.editingId()
      ? this.accountingService.updateExpenseCategory(this.editingId()!, { name: this.name().trim(), expenseAccountId: this.expenseAccountId()!, isActive: true })
      : this.accountingService.createExpenseCategory({ name: this.name().trim(), expenseAccountId: this.expenseAccountId()!, isActive: true });

    request.subscribe({
      next: () => {
        this.alertService.success(this.editingId() ? 'Category updated' : 'Category created');
        this.startCreate();
        this.load();
        this.isSaving.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSaving.set(false);
      }
    });
  }
}
