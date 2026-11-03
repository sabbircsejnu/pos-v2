import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { HrOrgUnit, HrOrgUnitWriteData, HrOrganisationService } from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-organisation-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .ho-form { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .ho-wide { grid-column: 1 / -1; }
    @media (min-width: 768px) { .ho-form { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header
        title="Departments & Teams"
        description="Manage departments, teams and their organisational hierarchy."
        [breadcrumbs]="breadcrumbs">
        @if (canManage()) {
          <button uiPagePrimaryActions appUiButton type="button" [disabled]="loading() || !!error()"
            (click)="add()">Add Department / Team</button>
        }
      </app-ui-page-header>

      @if (auth.hasPermission('hr.organisation.view')) {
      <app-ui-data-table
        ariaLabel="Departments & Teams"
        density="compact"
        rowKey="id"
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [error]="error()"
        [retryable]="false"
        errorTitle="Could not load Departments & Teams"
        loadingLabel="Loading Departments & Teams..."
        emptyTitle="No Departments / Teams found."
        searchLabel="Search Departments & Teams"
        searchPlaceholder="Search code, name, type…"
        [initialPageSize]="25">
        <ng-template uiTableRowActions let-row>
          @if (canManage()) {
            <button appUiButton type="button" variant="ghost" size="sm"
              [attr.aria-label]="'Edit ' + row.code" (click)="edit(row.unit)">Edit</button>
          }
        </ng-template>
      </app-ui-data-table>
      } @else {
        <app-ui-alert variant="danger">You do not have permission to view Departments & Teams.</app-ui-alert>
      }

      @if (dialogOpen() && canManage()) {
        <app-ui-dialog [(open)]="dialogOpen" size="lg" [dismissible]="!saving()"
          [title]="editing() ? 'Edit Department / Team' : 'Add Department / Team'">
          <form id="org-unit-form" class="ho-form" #details="ngForm" (ngSubmit)="save()"
            aria-label="Department / Team details">
            @if (saveError()) {
              <app-ui-alert class="ho-wide" variant="danger">{{ saveError() }}</app-ui-alert>
            }
            <app-ui-form-field label="Code" [required]="true">
              <input appUiControl name="code" required maxlength="50" data-autofocus [(ngModel)]="form.code">
            </app-ui-form-field>
            <app-ui-form-field label="Name" [required]="true">
              <input appUiControl name="name" required maxlength="150" [(ngModel)]="form.name">
            </app-ui-form-field>
            <app-ui-form-field label="Type" [required]="true">
              <select appUiControl name="unitType" required [(ngModel)]="form.unitType">
                <option value="Department">Department</option>
                <option value="Team">Team</option>
                @if (editing() && editing()!.unitType !== 'Department' && editing()!.unitType !== 'Team') {
                  <option [value]="editing()!.unitType">{{ editing()!.unitType }} (existing type)</option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Parent">
              <select appUiControl name="parentOrgUnitId" [(ngModel)]="form.parentOrgUnitId">
                <option [ngValue]="null">No parent (top-level)</option>
                @for (parent of parentOptions(); track parent.id) {
                  <option [ngValue]="parent.id">{{ parent.code }} - {{ parent.name }}</option>
                }
                @if (hasUnavailableParent()) {
                  <option [ngValue]="form.parentOrgUnitId" disabled>Unavailable parent - select a valid parent</option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Status">
              <select appUiControl name="isActive" [(ngModel)]="form.isActive">
                <option [ngValue]="true">Active</option>
                <option [ngValue]="false">Inactive</option>
              </select>
            </app-ui-form-field>
            @if (!editing()) {
              <p class="ho-wide">Department/Team becomes effective when created.</p>
            }
          </form>
          <button uiDialogFooter appUiButton type="button" variant="outline" [disabled]="saving()"
            (click)="dialogOpen.set(false)">Cancel</button>
          <button uiDialogFooter appUiButton type="submit" form="org-unit-form" [loading]="saving()"
            [disabled]="saving() || !!details.invalid">{{ editing() ? 'Save' : 'Create' }}</button>
        </app-ui-dialog>
      }
    </main>
  `
})
export class HrOrganisationPageComponent implements OnInit {
  private readonly service = inject(HrOrganisationService);
  readonly auth = inject(AuthService);
  readonly units = signal<HrOrgUnit[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<HrOrgUnit | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  form: HrOrgUnitWriteData = this.emptyForm();

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Departments & Teams' }];
  readonly columns: UiTableColumn[] = [
    { key: 'code', header: 'Code', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'unitType', header: 'Type', sortable: true },
    { key: 'parentLabel', header: 'Parent', emptyText: '-' },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: {
        Active: { label: 'Active', variant: 'success' },
        Inactive: { label: 'Inactive', variant: 'neutral' }
      }
    }
  ];
  readonly rows = computed(() => this.units().map(unit => ({
    ...unit, unit,
    parentLabel: unit.parentOrgUnitId === null ? null
      : this.units().find(parent => parent.id === unit.parentOrgUnitId)?.name ?? 'Unavailable',
    status: unit.isActive ? 'Active' : 'Inactive'
  })));

  ngOnInit(): void {
    if (!this.auth.hasPermission('hr.organisation.view')) {
      this.loading.set(false);
      return;
    }
    this.load();
  }

  canManage(): boolean {
    return this.auth.hasPermission('hr.organisation.view') && this.auth.hasPermission('hr.organisation.manage');
  }

  add(): void {
    if (!this.canManage()) {
      this.error.set('You do not have permission to manage Departments & Teams.');
      return;
    }
    this.editing.set(null);
    this.form = this.emptyForm();
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  edit(unit: HrOrgUnit): void {
    if (!this.canManage()) {
      this.error.set('You do not have permission to manage Departments & Teams.');
      return;
    }
    this.editing.set(unit);
    this.form = {
      code: unit.code, name: unit.name, unitType: unit.unitType,
      parentOrgUnitId: unit.parentOrgUnitId, isActive: unit.isActive
    };
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  parentOptions(): HrOrgUnit[] {
    const currentId = this.editing()?.id;
    return this.units().filter(unit => {
      if (!unit.isActive) return false;
      const visited = new Set<number>();
      let next: HrOrgUnit | undefined = unit;
      while (next) {
        if (next.id === currentId || visited.has(next.id)) return false;
        visited.add(next.id);
        next = this.units().find(parent => parent.id === next?.parentOrgUnitId);
      }
      return true;
    });
  }

  hasUnavailableParent(): boolean {
    return this.form.parentOrgUnitId !== null
      && !this.parentOptions().some(parent => parent.id === this.form.parentOrgUnitId);
  }

  save(): void {
    if (this.saving()) return;
    this.saveError.set(null);
    if (!this.canManage()) {
      this.saveError.set('You do not have permission to manage Departments & Teams.');
      return;
    }
    if (!this.form.code.trim() || !this.form.name.trim()
      || this.form.code.trim().length > 50 || this.form.name.trim().length > 150) {
      this.saveError.set('Code and Name are required (maximum 50 and 150 characters).');
      return;
    }
    const current = this.editing();
    if (!['Department', 'Team'].includes(this.form.unitType) && this.form.unitType !== current?.unitType) {
      this.saveError.set('Select Department or Team.');
      return;
    }
    if (this.hasUnavailableParent()) {
      this.saveError.set('Select an active parent without creating a circular hierarchy.');
      return;
    }
    const data: HrOrgUnitWriteData = {
      ...this.form, code: this.form.code.trim(), name: this.form.name.trim(),
      ...(current ? {
        operationalLocationProjectionId: current.operationalLocationProjectionId,
        effectiveTo: current.effectiveTo
      } : {})
    };
    this.saving.set(true);
    const request = current
      ? this.service.updateOrgUnit(current.id, current.version, data)
      : this.service.createOrgUnit(data);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogOpen.set(false);
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const message = error instanceof HttpErrorResponse ? error.error?.message : null;
        this.saveError.set(typeof message === 'string' ? message
          : 'Unable to save the Department / Team. Check access, code and parent, or reload if it has changed.');
      }
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getOrgUnits().subscribe({
      next: units => {
        this.units.set(units);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load Departments & Teams.');
        this.loading.set(false);
      }
    });
  }

  private emptyForm(): HrOrgUnitWriteData {
    return { code: '', name: '', unitType: 'Department', parentOrgUnitId: null, isActive: true };
  }
}
