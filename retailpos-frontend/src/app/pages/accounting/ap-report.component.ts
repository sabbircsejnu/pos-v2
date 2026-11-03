import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';
import { AccountingService } from '../../services/accounting.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { ApReportFilter } from '../../models/accounting.model';

@Component({
  selector: 'app-ap-report',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule],
  template: `
    <section class="p-6 space-y-6">
      <header class="flex flex-wrap items-start justify-between gap-4">
        <div><p class="text-xs font-semibold uppercase tracking-wide text-slate-500">Accounts payable</p><h1 class="text-2xl font-semibold text-slate-900">{{ title }}</h1></div>
        <button class="btn btn-outline" type="button" (click)="print()">Print / Export</button>
      </header>
      <form class="grid grid-cols-1 gap-3 rounded-lg border border-slate-200 bg-white p-4 md:grid-cols-5" (ngSubmit)="load()">
        <label class="text-sm text-slate-600">As of <input class="input mt-1 w-full" type="date" appCompanyDate [(ngModel)]="asOfDate" name="asOfDate"></label>
        <label *ngIf="mode === 'statement'" class="text-sm text-slate-600">Supplier ID <input class="input mt-1 w-full" type="number" [(ngModel)]="supplierId" name="supplierId"></label>
        <label *ngIf="mode === 'statement'" class="text-sm text-slate-600">From <input class="input mt-1 w-full" type="date" appCompanyDate [(ngModel)]="dateFrom" name="dateFrom"></label>
        <label *ngIf="mode === 'statement'" class="text-sm text-slate-600">To <input class="input mt-1 w-full" type="date" appCompanyDate [(ngModel)]="dateTo" name="dateTo"></label>
        <label *ngIf="mode !== 'statement'" class="text-sm text-slate-600">Status <select class="input mt-1 w-full" [(ngModel)]="status" name="status"><option value="">All</option><option value="open">Open</option><option value="partial">Partial</option><option value="paid">Paid</option></select></label>
        <label *ngIf="mode !== 'statement'" class="flex items-center gap-2 pt-6 text-sm text-slate-600"><input type="checkbox" [(ngModel)]="disputed" name="disputed"> Disputed only</label>
        <button class="btn btn-primary md:self-end" type="submit">Refresh</button>
      </form>
      <p *ngIf="error()" class="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">{{ error() }}</p>
      <div *ngIf="loading()" class="rounded-lg border border-slate-200 bg-white p-8 text-center text-slate-500">Loading report...</div>
      <ng-container *ngIf="!loading() && report() as data">
        <div class="grid grid-cols-2 gap-3 md:grid-cols-4" *ngIf="mode === 'aging'">
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Total outstanding</span><strong class="mt-1 block text-xl">{{ money(data.totalOutstanding) }}</strong></div>
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Current / Not Due</span><strong class="mt-1 block text-xl">{{ money(data.currentNotDue) }}</strong></div>
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">1-30 overdue</span><strong class="mt-1 block text-xl">{{ money(data.overdue1To30) }}</strong></div>
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">90+ overdue</span><strong class="mt-1 block text-xl">{{ money(data.overdue90Plus) }}</strong></div>
        </div>
        <div class="grid grid-cols-1 gap-3 md:grid-cols-3" *ngIf="mode === 'reconciliation' || mode === 'advance-reconciliation'">
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Subledger total</span><strong class="mt-1 block text-xl">{{ money(data.subledgerTotal) }}</strong></div>
          <div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">GL control balance</span><strong class="mt-1 block text-xl">{{ money(data.generalLedgerBalance) }}</strong></div>
          <div class="rounded-lg border bg-white p-4" [class.border-red-300]="data.reconciliationDifference !== 0"><span class="text-xs text-slate-500">Difference</span><strong class="mt-1 block text-xl">{{ money(data.reconciliationDifference) }}</strong></div>
        </div>
        <div *ngIf="mode === 'statement'" class="grid grid-cols-2 gap-3 md:grid-cols-5"><div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Opening</span><strong class="block">{{ money(data.openingBalance) }}</strong></div><div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">New payables</span><strong class="block">{{ money(data.newPayables) }}</strong></div><div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Payments</span><strong class="block">{{ money(data.supplierPayments) }}</strong></div><div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Applications</span><strong class="block">{{ money(data.supplierAdvanceApplications) }}</strong></div><div class="rounded-lg border bg-white p-4"><span class="text-xs text-slate-500">Closing</span><strong class="block">{{ money(data.closingBalance) }}</strong></div></div>
        <div *ngIf="data.exceptions?.legacyUnreconciledCount" class="rounded border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">Legacy or unreconciled records are excluded from authoritative AP totals.</div>
        <div class="overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <table *ngIf="mode !== 'statement' && mode !== 'reconciliation' && mode !== 'advance-reconciliation'" class="min-w-full text-sm"><thead class="bg-slate-50 text-left text-xs uppercase text-slate-500"><tr><th class="p-3">Supplier</th><th class="p-3">Source</th><th class="p-3">Due</th><th class="p-3">Original</th><th class="p-3">Payments</th><th class="p-3">Advances</th><th class="p-3">Outstanding</th><th class="p-3">Status</th></tr></thead><tbody><tr *ngFor="let row of data.rows" class="border-t"><td class="p-3">{{ row.supplierName }}</td><td class="p-3">{{ row.sourceReference }}</td><td class="p-3">{{ row.dueDate | appDate }}</td><td class="p-3">{{ money(row.originalPayableAmount) }}</td><td class="p-3">{{ money(row.effectivePaymentAllocations) }}</td><td class="p-3">{{ money(row.effectiveAdvanceUtilisations) }}</td><td class="p-3 font-semibold">{{ money(row.outstandingAmount) }}</td><td class="p-3">{{ row.status }}<span *ngIf="row.isDisputed" class="ml-2 text-amber-700">Disputed</span></td></tr></tbody></table>
          <table *ngIf="mode === 'statement'" class="min-w-full text-sm"><thead class="bg-slate-50 text-left text-xs uppercase text-slate-500"><tr><th class="p-3">Date</th><th class="p-3">Event</th><th class="p-3">Source</th><th class="p-3">Debit</th><th class="p-3">Credit</th><th class="p-3">Balance</th></tr></thead><tbody><tr *ngFor="let row of data.rows" class="border-t"><td class="p-3">{{ row.date | appDate }}</td><td class="p-3">{{ row.eventType }}</td><td class="p-3">{{ row.sourceReference }}</td><td class="p-3">{{ money(row.debit) }}</td><td class="p-3">{{ money(row.credit) }}</td><td class="p-3 font-semibold">{{ money(row.runningBalance) }}</td></tr></tbody></table>
          <div *ngIf="mode === 'reconciliation' || mode === 'advance-reconciliation'" class="p-6 text-sm text-slate-600">Reconciliation details remain independently calculated from the authoritative subledger and posted control-account journals.</div>
          <div *ngIf="!data.rows?.length && mode !== 'reconciliation' && mode !== 'advance-reconciliation'" class="p-8 text-center text-slate-500">No records for the selected filters.</div>
        </div>
      </ng-container>
    </section>
  `
})
export class ApReportComponent implements OnInit {
  readonly mode: string;
  readonly title: string;
  readonly report = signal<any>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  asOfDate = new Date().toISOString().slice(0, 10);
  dateFrom = new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().slice(0, 10);
  dateTo = new Date().toISOString().slice(0, 10);
  supplierId: number | null = null;
  status = '';
  disputed = false;

  constructor(private route: ActivatedRoute, private accounting: AccountingService, private errors: ErrorHandlerService, private currency: CurrencyService) { this.mode = this.route.snapshot.data['reportType'] as string; this.title = this.route.snapshot.data['title'] as string; }
  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true); this.error.set(null);
    const filters: ApReportFilter = { asOfDate: this.asOfDate, dateFrom: this.dateFrom, dateTo: this.dateTo, supplierId: this.supplierId || undefined, status: this.status || undefined, disputed: this.disputed ? true : undefined };
    const request: Observable<{ data: any }> = this.mode === 'aging' ? this.accounting.getApAging(filters) : this.mode === 'statement' ? this.accounting.getSupplierStatement(filters) : this.mode === 'reconciliation' ? this.accounting.getApReconciliation(filters) : this.mode === 'advance-reconciliation' ? this.accounting.getApReconciliation(filters, true) : this.accounting.getApSubledger(filters);
    request.subscribe({ next: response => { this.report.set(response.data); this.loading.set(false); }, error: error => { this.error.set(this.errors.extractErrorMessage(error)); this.loading.set(false); } });
  }
  money(value: number): string { return this.currency.format(value || 0); }
  print(): void { window.print(); }
}
