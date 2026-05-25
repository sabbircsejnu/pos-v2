import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AccountDto, CreateAccountDto } from '../../models/accounting.model';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-accounts-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './accounts-list.component.html',
  styleUrls: ['./accounts-list.component.css']
})
export class AccountsListComponent implements OnInit {
  activeTypeFilter = signal('all');
  showModal = signal(false);
  isEditMode = signal(false);
  editingId = signal<number | null>(null);
  isSubmitting = signal(false);

  formName = signal('');
  formType = signal('asset');

  accountTypes = ['asset', 'liability', 'revenue', 'expense'];

  constructor(
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.loadAccounts();
  }

  loadAccounts(): void {
    const type = this.activeTypeFilter() === 'all' ? undefined : this.activeTypeFilter();
    this.accountingService.getAccounts(type).subscribe();
  }

  setTypeFilter(type: string): void {
    this.activeTypeFilter.set(type);
    this.loadAccounts();
  }

  openCreateModal(): void {
    this.isEditMode.set(false);
    this.editingId.set(null);
    this.formName.set('');
    this.formType.set('asset');
    this.showModal.set(true);
  }

  openEditModal(account: AccountDto): void {
    this.isEditMode.set(true);
    this.editingId.set(account.id);
    this.formName.set(account.name);
    this.formType.set(account.type);
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  onSubmit(): void {
    if (!this.formName().trim()) {
      this.alertService.error('Account name is required');
      return;
    }

    this.isSubmitting.set(true);
    const dto: CreateAccountDto = { name: this.formName().trim(), type: this.formType() };

    if (this.isEditMode()) {
      this.accountingService.updateAccount(this.editingId()!, dto).subscribe({
        next: () => {
          this.alertService.success('Account updated successfully');
          this.showModal.set(false);
          this.isSubmitting.set(false);
          this.loadAccounts();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    } else {
      this.accountingService.createAccount(dto).subscribe({
        next: () => {
          this.alertService.success('Account created successfully');
          this.showModal.set(false);
          this.isSubmitting.set(false);
          this.loadAccounts();
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isSubmitting.set(false);
        }
      });
    }
  }

  deleteAccount(account: AccountDto): void {
    this.alertService.confirm(
      `Are you sure you want to delete account "${account.name}"?`,
      () => {
        this.accountingService.deleteAccount(account.id).subscribe({
          next: () => {
            this.alertService.success('Account deleted successfully');
            this.loadAccounts();
          },
          error: (err) => {
            this.alertService.error(this.errorHandler.extractErrorMessage(err));
          }
        });
      }
    );
  }

  getTypeBadgeClass(type: string): string {
    const map: { [key: string]: string } = {
      asset: 'bg-blue-100 text-blue-800',
      liability: 'bg-red-100 text-red-800',
      revenue: 'bg-green-100 text-green-800',
      expense: 'bg-orange-100 text-orange-800'
    };
    return map[type] || 'bg-gray-100 text-gray-800';
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }
}
