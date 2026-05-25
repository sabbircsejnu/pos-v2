import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuditService } from '../../services/audit.service';
import {
  AuditEventDetails,
  AuditEventListItem,
  AuditListFilter,
} from '../../models/audit.model';
import { AuditDetailsDrawerComponent } from './audit-details.component';

@Component({
  selector: 'app-audit-list',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, AuditDetailsDrawerComponent],
  template: `
    <div class="p-4 space-y-4">
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-semibold">Audit Log</h1>
        <button
          class="px-3 py-2 bg-indigo-600 text-white rounded hover:bg-indigo-700 disabled:opacity-50"
          [disabled]="isExporting()"
          (click)="exportCsv()">
          {{ isExporting() ? 'Exporting…' : 'Export CSV' }}
        </button>
      </div>

      <div class="bg-white rounded-lg shadow p-4 grid grid-cols-1 md:grid-cols-6 gap-3">
        <input
          class="border rounded px-2 py-1 md:col-span-2"
          placeholder="Search (user, summary, correlation id, entity id)"
          [(ngModel)]="filter.search"
          (keyup.enter)="reload()" />
        <input type="date" class="border rounded px-2 py-1" [(ngModel)]="filter.fromDate" />
        <input type="date" class="border rounded px-2 py-1" [(ngModel)]="filter.toDate" />
        <select class="border rounded px-2 py-1" [(ngModel)]="actionTypeOne">
          <option value="">All actions</option>
          <option *ngFor="let a of actionTypes" [value]="a">{{ a }}</option>
        </select>
        <select class="border rounded px-2 py-1" [(ngModel)]="moduleOne">
          <option value="">All modules</option>
          <option *ngFor="let m of modules" [value]="m">{{ m }}</option>
        </select>
        <select class="border rounded px-2 py-1" [(ngModel)]="sourceOne">
          <option value="">All sources</option>
          <option *ngFor="let s of sources" [value]="s">{{ s }}</option>
        </select>
        <select class="border rounded px-2 py-1" [(ngModel)]="filter.status">
          <option [ngValue]="undefined">Any status</option>
          <option value="Success">Success</option>
          <option value="Failed">Failed</option>
        </select>
        <button class="px-3 py-1 bg-slate-700 text-white rounded" (click)="reload()">Apply</button>
        <button class="px-3 py-1 bg-slate-200 rounded" (click)="resetFilters()">Reset</button>
      </div>

      <div class="bg-white rounded-lg shadow overflow-hidden">
        <table class="w-full text-sm">
          <thead class="bg-slate-100 text-left">
            <tr>
              <th class="px-3 py-2 w-44">Date &amp; Time</th>
              <th class="px-3 py-2">User</th>
              <th class="px-3 py-2">Action Summary</th>
              <th class="px-3 py-2">Primary Entity</th>
              <th class="px-3 py-2 text-center">Affected</th>
              <th class="px-3 py-2">Source</th>
              <th class="px-3 py-2">Action</th>
            </tr>
          </thead>
          <tbody>
            <ng-container *ngFor="let row of items()">
              <tr class="border-t hover:bg-slate-50">
                <td class="px-3 py-2 align-top whitespace-nowrap">
                  {{ row.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}
                </td>
                <td class="px-3 py-2 align-top">
                  <div class="flex items-center gap-2">
                    <span>{{ row.userName ?? '—' }}</span>
                    <span *ngIf="row.actingAsRole" class="text-xs px-2 py-0.5 rounded bg-amber-100 text-amber-800">
                      as {{ row.actingAsRole }}
                    </span>
                    <span *ngIf="row.status === 'Failed'"
                          class="text-xs px-2 py-0.5 rounded bg-red-100 text-red-700">FAILED</span>
                  </div>
                </td>
                <td class="px-3 py-2 align-top">{{ row.actionSummary }}</td>
                <td class="px-3 py-2 align-top whitespace-nowrap">
                  <ng-container *ngIf="row.primaryEntityType">
                    {{ row.primaryEntityType }}
                    <span *ngIf="row.primaryEntityId" class="text-slate-500">#{{ row.primaryEntityId }}</span>
                  </ng-container>
                </td>
                <td class="px-3 py-2 align-top text-center">
                  <button class="text-indigo-600 hover:underline" (click)="toggle(row.id)">
                    {{ expanded() === row.id ? '▼' : '▶' }} {{ row.affectedEntitiesCount }}
                  </button>
                </td>
                <td class="px-3 py-2 align-top">{{ row.source }}</td>
                <td class="px-3 py-2 align-top">
                  <button class="px-2 py-1 text-xs border rounded hover:bg-slate-100"
                          (click)="open(row.id)">View Details</button>
                </td>
              </tr>
              <tr *ngIf="expanded() === row.id" class="border-t bg-slate-50">
                <td colspan="7" class="px-6 py-2 text-xs">
                  <div *ngIf="!expandedDetails(); else loaded" class="text-slate-500">Loading…</div>
                  <ng-template #loaded>
                    <div class="flex flex-wrap gap-2">
                      <span *ngFor="let e of expandedDetails()!.affectedEntities"
                            class="px-2 py-1 rounded border bg-white">
                        <strong>{{ e.entityType }}</strong>#{{ e.entityId }}
                        <span class="text-slate-500">·{{ e.operationType }}·{{ e.fieldsChangedCount }} fields</span>
                      </span>
                    </div>
                  </ng-template>
                </td>
              </tr>
            </ng-container>
            <tr *ngIf="!items().length && !audit.isLoading()">
              <td colspan="7" class="text-center text-slate-500 py-6">No audit events match these filters.</td>
            </tr>
          </tbody>
        </table>

        <div class="flex items-center justify-between p-3 border-t bg-slate-50 text-sm">
          <div>{{ total() }} total events</div>
          <div class="flex items-center gap-2">
            <button class="px-2 py-1 border rounded" [disabled]="page() <= 1" (click)="prev()">Prev</button>
            <span>Page {{ page() }}</span>
            <button class="px-2 py-1 border rounded"
                    [disabled]="page() * (filter.pageSize ?? 50) >= total()"
                    (click)="next()">Next</button>
          </div>
        </div>
      </div>
    </div>

    <app-audit-details-drawer
      *ngIf="drawerEvent() as ev"
      [event]="ev"
      (closed)="drawerEvent.set(null)"></app-audit-details-drawer>
  `,
})
export class AuditListComponent implements OnInit {
  filter: AuditListFilter = { page: 1, pageSize: 50 };
  actionTypeOne = '';
  moduleOne = '';
  sourceOne = '';

  actionTypes = [
    'Create','Update','Delete','Approve','Cancel','Login','Logout','RoleSwitch',
    'Export','Import','Payment','Adjustment','PermissionChange','Reverse','Hold','Resume','Print'
  ];
  modules = ['Sales','Purchase','Inventory','Accounts','Admin','Settings','Reports','Auth','System'];
  sources = ['UI','API','SystemJob','Import','Integration'];

  items = signal<AuditEventListItem[]>([]);
  total = signal(0);
  page = signal(1);
  expanded = signal<number | null>(null);
  expandedDetails = signal<AuditEventDetails | null>(null);
  drawerEvent = signal<AuditEventDetails | null>(null);
  isExporting = signal(false);

  constructor(public audit: AuditService) {}

  ngOnInit() {
    this.reload();
  }

  reload() {
    this.filter.actionType = this.actionTypeOne ? [this.actionTypeOne] : undefined;
    this.filter.module = this.moduleOne ? [this.moduleOne] : undefined;
    this.filter.source = this.sourceOne ? [this.sourceOne] : undefined;
    this.filter.page = this.page();
    this.audit.list(this.filter).subscribe((res) => {
      this.items.set(res.items);
      this.total.set(res.total);
    });
  }

  resetFilters() {
    this.filter = { page: 1, pageSize: 50 };
    this.actionTypeOne = '';
    this.moduleOne = '';
    this.sourceOne = '';
    this.page.set(1);
    this.reload();
  }

  prev() {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.reload();
    }
  }

  next() {
    this.page.update((p) => p + 1);
    this.reload();
  }

  toggle(id: number) {
    if (this.expanded() === id) {
      this.expanded.set(null);
      this.expandedDetails.set(null);
      return;
    }
    this.expanded.set(id);
    this.expandedDetails.set(null);
    this.audit.details(id).subscribe((d) => this.expandedDetails.set(d));
  }

  open(id: number) {
    this.audit.details(id).subscribe((d) => this.drawerEvent.set(d));
  }

  exportCsv() {
    this.isExporting.set(true);
    this.audit.export({ ...this.filter, format: 'csv' }).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `audit-events-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        this.isExporting.set(false);
        this.reload();
      },
      error: () => this.isExporting.set(false),
    });
  }
}
