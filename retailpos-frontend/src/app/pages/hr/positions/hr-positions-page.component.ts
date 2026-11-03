import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of } from 'rxjs';
import {
  HrOrgUnit,
  HrOrganisationService,
  HrPosition,
  HrPositionWriteData
} from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

interface PositionRow {
  id: number;
  position: HrPosition;
  code: string;
  name: string;
  orgUnitLabel: string;
  reportsToLabel: string;
  status: string;
}

@Component({
  selector: 'app-hr-positions-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .hp-alerts { display: grid; gap: var(--rp-space-3); margin-bottom: var(--rp-space-4); }
    .hp-card { display: block; margin-bottom: var(--rp-space-6); }
    .hp-form-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .hp-form-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .hp-actions { display: inline-flex; align-items: center; justify-content: center; gap: var(--rp-space-1); }
    .hp-danger { color: var(--rp-color-status-danger-fg); }
    .hp-wide { grid-column: 1 / -1; }
    @media (min-width: 768px) { .hp-form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header
        title="Positions"
        description="Manage reusable job roles, their Department / Team and reporting line."
        [breadcrumbs]="breadcrumbs" />

      @if (error() || referenceError()) {
        <div class="hp-alerts">
          @if (error()) {
            <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
          }
          @if (referenceError()) {
            <app-ui-alert variant="warning">{{ referenceError() }}</app-ui-alert>
          }
        </div>
      }

      @if (loading()) {
        <app-ui-state kind="loading" title="Loading positions..." />
      }

      @if (!loading() && auth.hasPermission('hr.position.manage')) {
        <app-ui-card class="hp-card" [title]="editing() ? 'Edit position' : 'Create position'">
          <form (ngSubmit)="save()" class="hp-form-grid" aria-label="Position details">
            <app-ui-form-field label="Code" [required]="true">
              <input appUiControl name="code" required maxlength="50" [(ngModel)]="form.code">
            </app-ui-form-field>
            <app-ui-form-field label="Position Name" [required]="true">
              <input appUiControl name="name" required maxlength="150" [(ngModel)]="form.name">
            </app-ui-form-field>
            <app-ui-form-field label="Department / Team" [required]="true">
              <select appUiControl name="orgUnitId" required [(ngModel)]="form.orgUnitId">
                <option [ngValue]="null">Select a Department / Team</option>
                @for (unit of orgUnits(); track unit.id) {
                  <option [ngValue]="unit.id">{{ unit.code }} — {{ unit.name }}</option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Reports to">
              <select appUiControl name="reportsToPositionId" [(ngModel)]="form.reportsToPositionId">
                <option [ngValue]="null">No reporting position</option>
                @for (position of reportablePositions(); track position.id) {
                  <option [ngValue]="position.id">{{ position.code }} — {{ position.name }}</option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Status">
              <select appUiControl name="status" [(ngModel)]="form.status">
                <option value="Vacant">Vacant</option>
                <option value="Filled">Filled</option>
                <option value="TemporarilyFilled">Temporarily filled</option>
              </select>
            </app-ui-form-field>
            <div class="hp-form-actions hp-wide">
              <button appUiButton type="submit">{{ editing() ? 'Save changes' : 'Create position' }}</button>
              @if (editing()) {
                <button appUiButton type="button" variant="outline" (click)="resetForm()">Cancel</button>
              }
            </div>
          </form>
        </app-ui-card>
      }

      @if (!loading()) {
        <app-ui-data-table
          ariaLabel="Positions"
          density="compact"
          rowKey="id"
          [columns]="columns"
          [rows]="rows()"
          emptyTitle="No positions found."
          searchLabel="Search positions"
          searchPlaceholder="Search code, name, Department / Team…"
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            @if (auth.hasPermission('hr.position.manage') && row.position.status !== 'Inactive') {
              <div class="hp-actions">
                <button appUiButton type="button" variant="ghost" size="sm"
                  [attr.aria-label]="'Edit ' + row.code" (click)="edit(row.position)">Edit</button>
                <button appUiButton type="button" variant="ghost" size="sm" class="hp-danger"
                  [attr.aria-label]="'Deactivate ' + row.code" (click)="deactivate(row.position)">Deactivate</button>
              </div>
            }
          </ng-template>
        </app-ui-data-table>
      }
    </main>
  `
})
export class HrPositionsPageComponent implements OnInit {
  private readonly service = inject(HrOrganisationService);
  readonly auth = inject(AuthService);
  readonly positions = signal<HrPosition[]>([]);
  readonly orgUnits = signal<HrOrgUnit[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly referenceError = signal<string | null>(null);
  readonly editing = signal<HrPosition | null>(null);
  form = this.emptyForm();

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Positions' }];
  readonly columns: UiTableColumn[] = [
    { key: 'code', header: 'Code', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'orgUnitLabel', header: 'Department / Team', sortable: true },
    { key: 'reportsToLabel', header: 'Reports to', sortable: true },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: {
        Vacant: { label: 'Vacant', variant: 'warning' },
        Filled: { label: 'Filled', variant: 'success' },
        TemporarilyFilled: { label: 'TemporarilyFilled', variant: 'info' },
        Inactive: { label: 'Inactive', variant: 'neutral' }
      }
    }
  ];
  readonly rows = computed<PositionRow[]>(() => this.positions().map(position => ({
    id: position.id,
    position,
    code: position.code,
    name: position.name,
    orgUnitLabel: this.unitName(position.orgUnitId),
    reportsToLabel: this.positionName(position.reportsToPositionId),
    status: position.status
  })));

  ngOnInit(): void {
    this.load();
  }

  save(): void {
    this.error.set(null);
    if (!this.auth.hasPermission('hr.position.manage')) {
      this.error.set('You do not have permission to manage positions.');
      return;
    }
    if (this.form.orgUnitId === null) {
      this.error.set('Select a Department / Team.');
      return;
    }
    if (!this.form.code.trim() || !this.form.name.trim()) {
      this.error.set('Code and Position Name are required.');
      return;
    }
    const data: HrPositionWriteData = {
      ...this.form,
      orgUnitId: Number(this.form.orgUnitId),
      reportsToPositionId: this.form.reportsToPositionId === null
        ? null : Number(this.form.reportsToPositionId)
    };
    const current = this.editing();
    const request = current
      ? this.service.updatePosition(current.id, current.version, data)
      : this.service.createPosition(data);
    request.subscribe({
      next: () => {
        this.resetForm();
        this.load();
      },
      error: () => this.error.set('Unable to save the position. Check access, Department / Team, code, and reporting hierarchy.')
    });
  }

  edit(position: HrPosition): void {
    if (!this.auth.hasPermission('hr.position.manage')) return;
    this.error.set(null);
    this.editing.set(position);
    this.form = {
      code: position.code,
      name: position.name,
      orgUnitId: position.orgUnitId,
      reportsToPositionId: position.reportsToPositionId,
      status: position.status === 'Inactive' ? 'Vacant' : position.status
    };
  }

  deactivate(position: HrPosition): void {
    if (!this.auth.hasPermission('hr.position.manage')) return;
    this.error.set(null);
    this.service.deactivatePosition(position.id, position.version, this.today()).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Unable to deactivate the position. It may have changed or the end date may be invalid.')
    });
  }

  resetForm(): void {
    this.editing.set(null);
    this.form = this.emptyForm();
    this.error.set(null);
  }

  unitName(id: number): string {
    const unit = this.orgUnits().find(item => item.id === id);
    return unit ? `${unit.code} — ${unit.name}` : 'Unavailable';
  }

  positionName(id: number | null): string {
    if (id === null) return '—';
    const position = this.positions().find(item => item.id === id);
    return position ? `${position.code} — ${position.name}` : 'Unavailable';
  }

  reportablePositions(): HrPosition[] {
    const currentId = this.editing()?.id;
    return this.positions().filter(item => item.id !== currentId && item.status !== 'Inactive');
  }

  private load(): void {
    this.loading.set(true);
    this.referenceError.set(null);
    forkJoin({
      positions: this.service.getPositions(),
      orgUnits: this.service.getOrgUnits().pipe(catchError(() => {
        this.referenceError.set('Departments / Teams could not be loaded; position results remain available.');
        return of([] as HrOrgUnit[]);
      }))
    }).subscribe({
      next: result => {
        this.positions.set(result.positions);
        this.orgUnits.set(result.orgUnits);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load positions or their Department / Team references.');
        this.loading.set(false);
      }
    });
  }

  private emptyForm(): {
    code: string;
    name: string;
    orgUnitId: number | null;
    reportsToPositionId: number | null;
    status: 'Vacant' | 'Filled' | 'TemporarilyFilled';
  } {
    return {
      code: '',
      name: '',
      orgUnitId: null,
      reportsToPositionId: null,
      status: 'Vacant'
    };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
