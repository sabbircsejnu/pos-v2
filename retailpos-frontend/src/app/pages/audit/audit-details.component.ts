import { CommonModule, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { AuditEventDetails } from '../../models/audit.model';

@Component({
  selector: 'app-audit-details-drawer',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="fixed inset-0 z-40 bg-black/40" (click)="closed.emit()"></div>
    <aside class="fixed top-0 right-0 z-50 h-full w-full md:w-[640px] bg-white shadow-2xl overflow-y-auto">
      <header class="px-5 py-3 border-b flex items-center justify-between sticky top-0 bg-white z-10">
        <div class="flex items-center gap-2">
          <h2 class="text-lg font-semibold">Audit Event Details</h2>
          <span
            class="text-xs px-2 py-0.5 rounded"
            [class.bg-green-100]="event.status === 'Success'"
            [class.text-green-800]="event.status === 'Success'"
            [class.bg-red-100]="event.status === 'Failed'"
            [class.text-red-800]="event.status === 'Failed'">
            {{ event.status }}
          </span>
        </div>
        <button class="text-2xl leading-none px-2" (click)="closed.emit()">×</button>
      </header>

      <section class="p-5 space-y-4 text-sm">
        <div class="text-base font-medium">{{ event.actionSummary }}</div>

        <div class="grid grid-cols-2 gap-3">
          <div>
            <div class="text-xs uppercase text-slate-500">Primary Entity</div>
            <div *ngIf="event.primaryEntityType; else noEntity">
              <span class="px-2 py-0.5 rounded bg-indigo-100 text-indigo-800">
                {{ event.primaryEntityType }}<ng-container *ngIf="event.primaryEntityId"> #{{ event.primaryEntityId }}</ng-container>
              </span>
            </div>
            <ng-template #noEntity><span class="text-slate-400">—</span></ng-template>
          </div>

          <div>
            <div class="text-xs uppercase text-slate-500">Total Affected</div>
            <div>{{ event.affectedEntities.length }} entities · {{ totalFieldChanges() }} field changes</div>
          </div>

          <div>
            <div class="text-xs uppercase text-slate-500">Performed By</div>
            <div>
              {{ event.realUserName ?? '—' }}
              <span *ngIf="event.actingAsRole" class="ml-1 text-xs px-2 py-0.5 rounded bg-amber-100 text-amber-800">
                acting as {{ event.actingAsRole }}
              </span>
            </div>
          </div>

          <div>
            <div class="text-xs uppercase text-slate-500">Timestamp</div>
            <div>{{ event.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</div>
          </div>

          <div class="col-span-2">
            <div class="text-xs uppercase text-slate-500">Correlation ID</div>
            <div class="flex items-center gap-2">
              <code class="text-xs">{{ event.correlationId }}</code>
              <button class="text-xs text-indigo-600" (click)="copy(event.correlationId)">Copy</button>
            </div>
          </div>

          <div>
            <div class="text-xs uppercase text-slate-500">Source</div>
            <div>{{ event.source }}</div>
          </div>

          <div>
            <div class="text-xs uppercase text-slate-500">Modules Involved</div>
            <div class="flex flex-wrap gap-1">
              <span *ngFor="let m of event.modulesInvolved" class="text-xs px-2 py-0.5 rounded bg-slate-100">{{ m }}</span>
            </div>
          </div>

          <div *ngIf="event.requestMethod || event.requestPath" class="col-span-2">
            <div class="text-xs uppercase text-slate-500">Request</div>
            <code class="text-xs">{{ event.requestMethod }} {{ event.requestPath }}</code>
          </div>

          <div class="col-span-2">
            <div class="text-xs uppercase text-slate-500">Client IP</div>
            <div class="text-slate-700">
              <ng-container *ngIf="event.isLocalRequest; else realIp">
                <span class="font-mono text-amber-700">Localhost ({{ event.ipAddress ?? '::1' }})</span>
                <ng-container *ngIf="event.localMachineIp">
                  <span class="text-slate-400 mx-1">·</span>
                  <span class="text-xs text-slate-500">Local Machine IP:</span>
                  <span class="font-mono ml-1 text-slate-700">{{ event.localMachineIp }}</span>
                </ng-container>
              </ng-container>
              <ng-template #realIp>
                <span class="font-mono">{{ event.ipAddress ?? '—' }}</span>
                <ng-container *ngIf="event.rawIp && event.rawIp !== event.ipAddress">
                  <span class="text-slate-400 mx-1">·</span>
                  <span class="text-xs text-slate-500">raw:</span>
                  <span class="font-mono ml-1 text-slate-400">{{ event.rawIp }}</span>
                </ng-container>
              </ng-template>
            </div>
            <div class="mt-0.5 text-xs text-slate-500">
              {{ event.device ?? '—' }} · {{ event.browser ?? '—' }} · {{ event.os ?? '—' }}
            </div>
          </div>

          <div *ngIf="event.errorMessage" class="col-span-2">
            <div class="text-xs uppercase text-red-700">Error</div>
            <pre class="text-xs whitespace-pre-wrap text-red-700 bg-red-50 p-2 rounded">{{ event.errorMessage }}</pre>
          </div>
        </div>

        <div class="pt-2">
          <h3 class="font-semibold mb-2">Affected Entities</h3>
          <div class="space-y-3">
            <div *ngFor="let e of event.affectedEntities" class="border rounded">
              <div class="flex items-center justify-between px-3 py-2 bg-slate-50 border-b">
                <div>
                  <strong>{{ e.entityType }}</strong>
                  <span class="text-slate-500"> #{{ e.entityId }}</span>
                  <span *ngIf="e.isInternalOperation"
                    class="ml-2 text-xs px-2 py-0.5 rounded bg-amber-100 text-amber-700">
                    Internal{{ e.internalOperationName ? ': ' + e.internalOperationName : '' }}
                  </span>
                </div>
                <div class="flex items-center gap-2">
                  <span
                    class="text-xs px-2 py-0.5 rounded"
                    [class.bg-green-100]="e.operationType === 'Insert'"
                    [class.text-green-800]="e.operationType === 'Insert'"
                    [class.bg-blue-100]="e.operationType === 'Update'"
                    [class.text-blue-800]="e.operationType === 'Update'"
                    [class.bg-red-100]="e.operationType === 'Delete'"
                    [class.text-red-800]="e.operationType === 'Delete'">
                    {{ e.operationType }}
                  </span>
                  <span class="text-xs text-slate-500">{{ e.fieldsChangedCount }} fields</span>
                </div>
              </div>
              <table *ngIf="e.fieldChanges.length" class="w-full text-xs">
                <thead class="bg-white text-left">
                  <tr>
                    <th class="px-3 py-1 w-1/3">Field</th>
                    <th class="px-3 py-1">Before</th>
                    <th class="px-3 py-1">After</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let f of e.fieldChanges" class="border-t align-top">
                    <td class="px-3 py-1">
                      <span class="font-medium">{{ f.displayLabel ?? prettify(f.fieldName) }}</span>
                      <span *ngIf="f.displayLabel" class="block text-slate-400 font-mono text-[10px]">{{ f.fieldName }}</span>
                    </td>
                    <td class="px-3 py-1">
                      <ng-container *ngIf="f.redacted">
                        <span class="text-red-400 italic">[redacted]</span>
                      </ng-container>
                      <ng-container *ngIf="!f.redacted">
                        <span *ngIf="f.isReferenceField && f.oldDisplayValue; else rawOld" class="text-slate-700">
                          {{ f.oldDisplayValue }}
                          <span class="block font-mono text-slate-400 text-[10px]">{{ render(f.oldValue) }}</span>
                        </span>
                        <ng-template #rawOld>
                          <span class="font-mono whitespace-pre-wrap">{{ render(f.oldValue) }}</span>
                        </ng-template>
                      </ng-container>
                    </td>
                    <td class="px-3 py-1">
                      <ng-container *ngIf="f.redacted">
                        <span class="text-red-400 italic">[redacted]</span>
                      </ng-container>
                      <ng-container *ngIf="!f.redacted">
                        <span *ngIf="f.isReferenceField && f.newDisplayValue; else rawNew" class="text-slate-700">
                          {{ f.newDisplayValue }}
                          <span class="block font-mono text-slate-400 text-[10px]">{{ render(f.newValue) }}</span>
                        </span>
                        <ng-template #rawNew>
                          <span class="font-mono whitespace-pre-wrap">{{ render(f.newValue) }}</span>
                        </ng-template>
                      </ng-container>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </section>
    </aside>
  `,
})
export class AuditDetailsDrawerComponent {
  @Input({ required: true }) event!: AuditEventDetails;
  @Output() closed = new EventEmitter<void>();

  totalFieldChanges(): number {
    return this.event.affectedEntities.reduce((sum, e) => sum + e.fieldsChangedCount, 0);
  }

  render(v: unknown): string {
    if (v === null || v === undefined) return '—';
    if (typeof v === 'string') return v;
    try { return JSON.stringify(v); } catch { return String(v); }
  }

  /** Converts camelCase/PascalCase field names to spaced labels, e.g. "SupplierId" → "Supplier Id" */
  prettify(fieldName: string): string {
    return fieldName
      .replace(/([A-Z])/g, ' $1')
      .replace(/^./, s => s.toUpperCase())
      .trim();
  }

  copy(s: string) {
    navigator.clipboard?.writeText(s);
  }
}
