import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';
import { AccountingService } from '../../services/accounting.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { VatReportFilter } from '../../models/accounting.model';

@Component({ selector: 'app-vat-report', standalone: true, imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule], template: `
<section class="p-6 space-y-6"><header class="flex items-start justify-between gap-4"><div><p class="text-xs font-semibold uppercase text-slate-500">VAT / Tax Reporting</p><h1 class="text-2xl font-semibold">{{ title }}</h1></div><button class="btn btn-outline" (click)="print()">Print / Export</button></header>
<form class="grid grid-cols-1 md:grid-cols-4 gap-3 rounded-lg border bg-white p-4" (ngSubmit)="load()"><label class="text-sm">From<input class="input mt-1 w-full" type="date" appCompanyDate [(ngModel)]="dateFrom" name="dateFrom"></label><label class="text-sm">To<input class="input mt-1 w-full" type="date" appCompanyDate [(ngModel)]="dateTo" name="dateTo"></label><label class="text-sm">Direction<select class="input mt-1 w-full" [(ngModel)]="direction" name="direction"><option value="">Output + Input</option><option>Output</option><option>Input</option></select></label><button class="btn btn-primary md:self-end">Refresh</button></form>
<p *ngIf="error" class="rounded border border-red-200 bg-red-50 p-3 text-red-700">{{ error }}</p><div *ngIf="loading" class="rounded border bg-white p-8 text-center text-slate-500">Loading report...</div>
<ng-container *ngIf="!loading && data"><div *ngIf="mode === 'summary'" class="grid grid-cols-2 md:grid-cols-5 gap-3"><div class="stat">Sales Output {{ money(data.outputVatFromSales) }}</div><div class="stat">Other Income {{ money(data.otherIncomeOutputVat) }}</div><div class="stat">Input VAT {{ money(data.totalInputVat) }}</div><div class="stat">Net VAT {{ money(data.netVatPosition) }}</div><div class="stat">Position {{ data.position }}</div></div>
<div *ngIf="mode === 'reconciliation'" class="grid grid-cols-1 md:grid-cols-3 gap-3"><div class="stat">Output difference {{ money(data.outputDifference) }}</div><div class="stat">Input difference {{ money(data.inputDifference) }}</div><div class="stat">Overall difference {{ money(data.overallDifference) }}</div></div>
<div *ngIf="mode === 'exceptions' && data.items?.length" class="rounded border border-amber-300 bg-amber-50 p-3 text-amber-900">Incomplete VAT snapshots are excluded from authoritative totals.</div>
<div class="overflow-x-auto rounded-lg border bg-white"><table *ngIf="mode === 'details'" class="min-w-full text-sm"><thead class="bg-slate-50 text-left"><tr><th class="p-3">Date</th><th class="p-3">Source</th><th class="p-3">Reference</th><th class="p-3">Direction</th><th class="p-3">Rate</th><th class="p-3">Taxable</th><th class="p-3">VAT</th></tr></thead><tbody><tr *ngFor="let row of data" class="border-t"><td class="p-3">{{ row.postingDate | appDate }}</td><td class="p-3">{{ row.sourceType }}</td><td class="p-3">{{ row.sourceReference }}</td><td class="p-3">{{ row.vatDirection }}</td><td class="p-3">{{ row.vatRate || '-' }}</td><td class="p-3">{{ money(row.taxableAmount) }}</td><td class="p-3">{{ money(row.vatAmount) }}</td></tr></tbody></table><table *ngIf="mode === 'exceptions'" class="min-w-full text-sm"><tbody><tr *ngFor="let row of data.items" class="border-t"><td class="p-3">{{ row.postingDate | appDate }}</td><td class="p-3">{{ row.sourceType }}</td><td class="p-3">{{ row.sourceReference }}</td><td class="p-3">{{ row.reason }}</td></tr></tbody></table><p *ngIf="mode === 'details' && !data?.length || mode === 'exceptions' && !data?.items?.length" class="p-8 text-center text-slate-500">No records for the selected period.</p></div></ng-container></section>` })
export class VatReportComponent implements OnInit {
  readonly mode: string; readonly title: string;
  dateFrom = new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().slice(0, 10); dateTo = new Date().toISOString().slice(0, 10); direction = ''; data: any = null; loading = false; error: string | null = null;
  constructor(private route: ActivatedRoute, private accounting: AccountingService, private currency: CurrencyService, private errors: ErrorHandlerService) { this.mode = this.route.snapshot.data['reportType'] as string; this.title = this.route.snapshot.data['title'] as string; }
  ngOnInit(): void { this.load(); }
  load(): void { this.loading = true; this.error = null; const f: VatReportFilter = { dateFrom: this.dateFrom, dateTo: this.dateTo, vatDirection: this.direction || undefined }; const request: Observable<{ data: any }> = this.mode === 'summary' ? this.accounting.getVatSummary(f) : this.mode === 'details' ? this.accounting.getVatDetails(f) : this.mode === 'reconciliation' ? this.accounting.getVatReconciliation(f) : this.accounting.getVatExceptions(f); request.subscribe({ next: x => { this.data = x.data; this.loading = false; }, error: e => { this.error = this.errors.extractErrorMessage(e); this.loading = false; } }); }
  money(value: number): string { return this.currency.format(value || 0); }
  print(): void { window.print(); }
}
