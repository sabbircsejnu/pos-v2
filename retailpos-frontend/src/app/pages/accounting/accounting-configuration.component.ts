import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-accounting-configuration',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './accounting-configuration.component.html'
})
export class AccountingConfigurationComponent implements OnInit {
  readonly configuration = signal<any>(null);
  readonly equityAccounts = signal<any[]>([]);
  readonly retainedEarningsAccountId = signal<number | null>(null);
  readonly defaultSupplierPaymentTermsDays = signal(30);
  readonly vatPayableAccountId = signal<number | null>(null);
  readonly vatReceivableAccountId = signal<number | null>(null);
  readonly liabilityAccounts = signal<any[]>([]);
  readonly assetAccounts = signal<any[]>([]);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);

  constructor(
    private accounting: AccountingService,
    private alerts: AlertService,
    private errors: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    forkJoin({ configuration: this.accounting.getAccountingConfiguration(), accounts: this.accounting.getActiveEquityChartAccounts(), liabilities: this.accounting.getChartAccounts('Liability'), assets: this.accounting.getChartAccounts('Asset') }).subscribe({
      next: ({ configuration, accounts, liabilities, assets }) => {
        this.configuration.set(configuration.data);
        this.retainedEarningsAccountId.set(configuration.data?.retainedEarningsAccountId ?? null);
        this.defaultSupplierPaymentTermsDays.set(configuration.data?.defaultSupplierPaymentTermsDays ?? 30);
        this.vatPayableAccountId.set(configuration.data?.vatPayableAccountId ?? null);
        this.vatReceivableAccountId.set(configuration.data?.vatReceivableAccountId ?? null);
        this.equityAccounts.set(accounts.data ?? []);
        this.liabilityAccounts.set(liabilities.data ?? []);
        this.assetAccounts.set(assets.data ?? []);
        this.isLoading.set(false);
      },
      error: error => {
        const message = this.errors.extractErrorMessage(error);
        this.error.set(message);
        this.alerts.error(message);
        this.isLoading.set(false);
      }
    });
  }

  save(): void {
    if (!this.retainedEarningsAccountId()) {
      this.alerts.error('Select an active Equity account for Retained Earnings.');
      return;
    }
    this.isSaving.set(true);
    this.accounting.updateAccountingConfiguration({ retainedEarningsAccountId: this.retainedEarningsAccountId(), defaultSupplierPaymentTermsDays: this.defaultSupplierPaymentTermsDays(), vatPayableAccountId: this.vatPayableAccountId(), vatReceivableAccountId: this.vatReceivableAccountId() }).subscribe({
      next: response => {
        this.configuration.set(response.data);
        this.retainedEarningsAccountId.set(response.data?.retainedEarningsAccountId ?? null);
        this.defaultSupplierPaymentTermsDays.set(response.data?.defaultSupplierPaymentTermsDays ?? 30);
        this.vatPayableAccountId.set(response.data?.vatPayableAccountId ?? null);
        this.vatReceivableAccountId.set(response.data?.vatReceivableAccountId ?? null);
        this.alerts.success('Retained Earnings configuration updated.');
        this.isSaving.set(false);
      },
      error: error => {
        this.alerts.error(this.errors.extractErrorMessage(error));
        this.isSaving.set(false);
      }
    });
  }

  accountLabel(account: any): string { return `${account.accountCode} - ${account.accountName}`; }
  accountName(accountId: number | null | undefined): string {
    return this.equityAccounts().find(account => account.id === accountId) ? this.accountLabel(this.equityAccounts().find(account => account.id === accountId)) : 'Not configured';
  }
}