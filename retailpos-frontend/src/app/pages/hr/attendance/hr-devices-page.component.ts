import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CompanyDateService } from '../../../services/company-date.service';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { DeviceData, DeviceMappingRow, DeviceRow, HrDeviceService } from '../../../services/hr-device.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-devices-page', standalone: true, imports: [FormsModule, ...UI_KIT],
  template: `<main class="container mx-auto px-4 py-3">
    <app-ui-page-header
      title="Attendance devices and user mappings"
      description="Metadata only; no punch ingress is enabled here. Location references are HR operational projections. Credentials remain in external secure configuration, never enter a credential value."
      [breadcrumbs]="breadcrumbs" />
    @if (error() || (busy() && rows().length > 0)) {
      <div class="dv-alerts">
        @if (error()) {
          <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
        }
        @if (busy() && rows().length > 0) {
          <app-ui-state kind="loading" [compact]="true" title="Loading / saving..." />
        }
      </div>
    }
    @if (canManage()) {
      <app-ui-card class="dv-card" [title]="editing ? 'Edit device' : 'New device'">
        <form (ngSubmit)="save()" class="dv-grid" aria-label="Device details">
          <app-ui-form-field label="Device code" [required]="true">
            <input appUiControl name="code" required maxlength="50" [(ngModel)]="data.deviceCode">
          </app-ui-form-field>
          <app-ui-form-field label="Serial" [required]="true">
            <input appUiControl name="serial" required maxlength="150" [(ngModel)]="data.serialNumber">
          </app-ui-form-field>
          <app-ui-form-field label="Vendor" [required]="true">
            <input appUiControl name="vendor" required maxlength="50" [(ngModel)]="data.vendorCode">
          </app-ui-form-field>
          <app-ui-form-field label="Model" [required]="true">
            <input appUiControl name="model" required maxlength="100" [(ngModel)]="data.model">
          </app-ui-form-field>
          <app-ui-form-field label="HR location projection ID" [required]="true">
            <input appUiControl name="location" type="number" min="1" required [(ngModel)]="data.operationalLocationProjectionId">
          </app-ui-form-field>
          <app-ui-form-field label="Protocol">
            <select appUiControl name="protocol" [(ngModel)]="data.protocolType">
              <option>ADMS</option>
              <option>Push</option>
              <option>API</option>
              <option>File</option>
            </select>
          </app-ui-form-field>
          <app-ui-form-field label="Timezone ID" [required]="true">
            <input appUiControl name="timezone" required [(ngModel)]="data.deviceTimeZoneId" placeholder="Asia/Dhaka">
          </app-ui-form-field>
          <app-ui-form-field label="Status">
            <select appUiControl name="status" [(ngModel)]="data.status">
              <option>Active</option>
              <option>Offline</option>
              <option>Disabled</option>
              <option>Error</option>
            </select>
          </app-ui-form-field>
          <app-ui-form-field label="Host/IP only">
            <input appUiControl name="host" [(ngModel)]="data.connectionHost">
          </app-ui-form-field>
          <app-ui-form-field label="Port">
            <input appUiControl name="port" type="number" min="1" max="65535" [(ngModel)]="data.connectionPort">
          </app-ui-form-field>
          <app-ui-form-field label="Firmware">
            <input appUiControl name="firmware" maxlength="100" [(ngModel)]="data.firmwareVersion">
          </app-ui-form-field>
          <app-ui-form-field label="External secret NAME (not the secret)">
            <input appUiControl name="secretReference" maxlength="100" [(ngModel)]="data.credentialSecretReference">
          </app-ui-form-field>
          @if (editing) {
            <label class="ui-check dv-wide">
              <input appUiControl name="replace" type="checkbox" [(ngModel)]="replaceCredentialReference">
              <span>Explicitly replace/clear credential reference; otherwise preserve it</span>
            </label>
          }
          <div class="dv-actions dv-wide">
            <button appUiButton type="submit" [disabled]="busy()">Save device</button>
            <button appUiButton type="button" variant="outline" (click)="reset()">Reset</button>
          </div>
        </form>
      </app-ui-card>
    }
    <app-ui-data-table
      ariaLabel="Attendance devices"
      density="compact"
      rowKey="id"
      [columns]="deviceColumns"
      [rows]="deviceRows()"
      [loading]="busy()"
      loadingLabel="Loading / saving..."
      emptyTitle="No devices found."
      searchLabel="Search devices"
      searchPlaceholder="Search code, vendor, serial…"
      [initialPageSize]="25">
      <ng-template uiTableRowActions let-row>
        <div class="dv-row-actions">
          @if (canManage()) {
            <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit device ' + row.row.deviceCode" (click)="edit(row.row)">Edit</button>
          }
          @if (canManage() && row.row.status !== 'Disabled') {
            <button appUiButton type="button" variant="ghost" size="sm" class="dv-danger" [attr.aria-label]="'Deactivate device ' + row.row.deviceCode" [disabled]="busy()" (click)="deactivate(row.row)">Deactivate</button>
          }
          @if (canViewMappings()) {
            <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Mappings of device ' + row.row.deviceCode" (click)="select(row.row)">Mappings</button>
          }
        </div>
      </ng-template>
    </app-ui-data-table>
    @if (selected) {
      <app-ui-card class="dv-card dv-card--spaced" [title]="'Mappings: ' + selected.deviceCode"
        subtitle="UTC instants; half-open intervals [from, to). Closed mappings remain historical evidence and grant no application access.">
        @if (canManageMappings()) {
          <form (ngSubmit)="createMapping()" class="dv-grid dv-mapping-form" aria-label="Device user mapping">
            <app-ui-form-field label="Employee ID" [required]="true">
              <input appUiControl name="employee" type="number" min="1" required [(ngModel)]="employeeId">
            </app-ui-form-field>
            <app-ui-form-field label="Device user ID" [required]="true">
              <input appUiControl name="deviceUser" required maxlength="100" [(ngModel)]="deviceUserId">
            </app-ui-form-field>
            <app-ui-form-field label="Effective from UTC" [required]="true">
              <input appUiControl name="mappingFrom" type="datetime-local" appCompanyDate required [(ngModel)]="mappingFrom">
            </app-ui-form-field>
            <app-ui-form-field label="Effective to UTC (optional)">
              <input appUiControl name="mappingTo" type="datetime-local" appCompanyDate [(ngModel)]="mappingTo">
            </app-ui-form-field>
            <div class="dv-actions dv-wide">
              <button appUiButton type="submit" [disabled]="busy()">Create mapping</button>
            </div>
          </form>
          <div class="dv-closure">
            <app-ui-form-field label="Closure UTC instant">
              <input appUiControl type="datetime-local" appCompanyDate [(ngModel)]="closeAt">
            </app-ui-form-field>
          </div>
        }
        <app-ui-data-table
          ariaLabel="Device user mappings"
          density="compact"
          rowKey="id"
          [columns]="mappingColumns"
          [rows]="mappingRows()"
          emptyTitle="No mappings found."
          searchLabel="Search mappings"
          searchPlaceholder="Search device user, employee…"
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            @if (canManageMappings() && row.row.isActive) {
              <button appUiButton type="button" variant="ghost" size="sm" class="dv-danger" [attr.aria-label]="'Close mapping of device user ' + row.row.deviceUserId + '; retain history'" [disabled]="busy()" (click)="close(row.row)">Close; retain history</button>
            }
          </ng-template>
        </app-ui-data-table>
      </app-ui-card>
    }
  </main>`,
  styles: [`
    .dv-alerts { display: grid; gap: var(--rp-space-3); margin-bottom: var(--rp-space-4); }
    .dv-card { display: block; margin-bottom: var(--rp-space-6); }
    .dv-card--spaced { margin-top: var(--rp-space-6); }
    .dv-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .dv-mapping-form { margin-bottom: var(--rp-space-4); }
    .dv-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .dv-closure { margin-bottom: var(--rp-space-4); max-width: 24rem; }
    .dv-row-actions { display: inline-flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: var(--rp-space-1); }
    .dv-danger { color: var(--rp-color-status-danger-fg); }
    @media (min-width: 768px) {
      .dv-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .dv-wide { grid-column: 1 / -1; }
    }
  `]
})
export class HrDevicesPageComponent implements OnInit {
  private readonly companyDates = inject(CompanyDateService);
  private readonly api = inject(HrDeviceService); private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly rows = signal<DeviceRow[]>([]); readonly mappings = signal<DeviceMappingRow[]>([]);
  readonly busy = signal(false); readonly error = signal<string | null>(null);
  data = this.empty(); editing: DeviceRow | null = null; selected: DeviceRow | null = null;
  replaceCredentialReference = false; employeeId = 0; deviceUserId = ''; mappingFrom = ''; mappingTo = ''; closeAt = '';
  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Attendance devices' }];
  readonly deviceColumns: UiTableColumn[] = [
    { key: 'device', header: 'Device', sortable: true },
    { key: 'locationZone', header: 'Location / Timezone', sortable: true },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: {
        Active: { label: 'Active', variant: 'success' },
        Offline: { label: 'Offline', variant: 'warning' },
        Disabled: { label: 'Disabled', variant: 'neutral' },
        Error: { label: 'Error', variant: 'danger' }
      }
    },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true },
    { key: 'lastSeen', type: 'date', header: 'Last seen' },
    { key: 'secretReference', header: 'Secret reference' }
  ];
  readonly mappingColumns: UiTableColumn[] = [
    { key: 'user', header: 'User / Employee', sortable: true },
    { key: 'period', header: 'UTC period', sortable: true, sortField: 'row.effectiveFromUtc' },
    {
      key: 'state', header: 'State', type: 'status', sortable: true,
      statusMap: { Open: { label: 'Open', variant: 'success' }, Closed: { label: 'Closed history', variant: 'neutral' } }
    },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true }
  ];
  readonly deviceRows = computed(() => this.rows().map(r => ({
    id: r.id, row: r,
    device: `${r.deviceCode} / ${r.vendorCode} / ${r.serialNumber}`,
    locationZone: `${r.operationalLocationProjectionId} / ${r.deviceTimeZoneId}`,
    status: r.status,
    version: r.version,
    lastSeen: r.lastSeenAtUtc || 'Never',
    secretReference: r.hasCredentialReference ? 'Configured name' : 'Not configured'
  })));
  readonly mappingRows = computed(() => this.mappings().map(m => ({
    id: m.id, row: m,
    user: `${m.deviceUserId} / ${m.employeeId}`,
    period: `${this.companyDates.format(m.effectiveFromUtc, true, 'UTC')} - ${this.companyDates.format(m.effectiveToUtc, true, 'UTC') || 'Open'}`,
    state: m.isActive ? 'Open' : 'Closed',
    version: m.version
  })));
  ngOnInit() { this.load(); }
  canManage() { return this.allowed('hr.device.manage'); }
  canViewMappings() { return this.allowed('hr.device_user_mapping.view'); }
  canManageMappings() { return this.allowed('hr.device_user_mapping.manage'); }
  private allowed(permission: string) { return this.capability.isEnabled('deviceintegration.enabled') && this.auth.hasPermission(permission); }
  load() {
    if (!this.allowed('hr.device.view')) { this.error.set('HR/Attendance/Device capability or device view permission unavailable.'); return; }
    this.busy.set(true); this.api.list().subscribe({
      next: rows => { this.rows.set(rows); this.busy.set(false); },
      error: e => { this.busy.set(false); this.error.set(e.error?.detail ?? 'Unable to load authorized devices.'); }
    });
  }
  save() {
    if (!this.canManage() || this.busy()) { this.error.set('Device manage access required.'); return; }
    if (this.editing && !this.replaceCredentialReference && this.data.credentialSecretReference) {
      this.error.set('Select explicit credential-reference replacement before entering a new secret name.'); return;
    }
    this.write(this.api.save({ ...this.data, connectionHost: this.data.connectionHost || null,
      firmwareVersion: this.data.firmwareVersion || null, credentialSecretReference: this.data.credentialSecretReference || null },
      this.editing, this.replaceCredentialReference), () => { this.reset(); this.load(); });
  }
  edit(row: DeviceRow) {
    if (!this.canManage()) return;
    this.editing = row; this.data = { deviceCode: row.deviceCode, serialNumber: row.serialNumber, vendorCode: row.vendorCode,
      model: row.model, operationalLocationProjectionId: row.operationalLocationProjectionId, protocolType: row.protocolType,
      deviceTimeZoneId: row.deviceTimeZoneId, status: row.status, connectionHost: row.connectionHost,
      connectionPort: row.connectionPort, firmwareVersion: row.firmwareVersion, credentialSecretReference: null };
    this.replaceCredentialReference = false;
  }
  deactivate(row: DeviceRow) { if (this.canManage()) this.write(this.api.deactivate(row), () => this.load()); }
  select(row: DeviceRow) {
    if (!this.canViewMappings()) { this.error.set('Mapping view access required.'); return; }
    this.selected = row; this.api.mappings(row.id).subscribe({
      next: m => this.mappings.set(m), error: e => this.error.set(e.error?.detail ?? 'Unable to load mapping history.')
    });
  }
  createMapping() {
    if (!this.canManageMappings() || !this.selected) { this.error.set('Select a device with mapping manage access.'); return; }
    const from = this.utc(this.mappingFrom); const to = this.mappingTo ? this.utc(this.mappingTo) : null;
    if (!from || this.mappingTo && !to) { this.error.set('Valid UTC mapping instants required.'); return; }
    const device = this.selected;
    this.write(this.api.map(device.id, { employeeId: this.employeeId, deviceUserId: this.deviceUserId,
      effectiveFromUtc: from, effectiveToUtc: to }), () => { this.deviceUserId = ''; if (this.canViewMappings()) this.select(device); });
  }
  close(row: DeviceMappingRow) {
    const end = this.utc(this.closeAt);
    if (!this.canManageMappings() || !end) { this.error.set('Mapping manage access and explicit valid UTC closure instant required.'); return; }
    this.write(this.api.close(row, end), () => { if (this.selected && this.canViewMappings()) this.select(this.selected); });
  }
  reset() { this.data = this.empty(); this.editing = null; this.replaceCredentialReference = false; }
  private write<T>(request: Observable<T>, next: () => void) {
    if (this.busy()) return;
    this.error.set(null); this.busy.set(true); request.subscribe({
      next: () => { this.busy.set(false); next(); },
      error: e => { this.busy.set(false); this.error.set(e.error?.detail ?? 'Device operation failed.'); }
    });
  }
  private utc(value: string) {
    if (!value) return null; const date = new Date(`${value}Z`); return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }
  private empty(): DeviceData { return { deviceCode: '', serialNumber: '', vendorCode: '', model: '',
    operationalLocationProjectionId: 0, protocolType: 'ADMS', deviceTimeZoneId: 'Asia/Dhaka', status: 'Active',
    connectionHost: null, connectionPort: null, firmwareVersion: null, credentialSecretReference: null }; }
}
