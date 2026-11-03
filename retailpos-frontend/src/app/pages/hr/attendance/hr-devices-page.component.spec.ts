import { CompanyDateService } from '../../../services/company-date.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HrDevicesPageComponent } from './hr-devices-page.component';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { DeviceMappingRow, DeviceRow } from '../../../services/hr-device.service';
import { environment } from '../../../../environments/environment';

describe('Phase 2C device management', () => {
  const base = `${environment.apiUrl}/hr/v1/attendance`;
  afterEach(() => TestBed.resetTestingModule());
  async function create(manage = true, enabled = true) {
    await TestBed.configureTestingModule({ imports: [HrDevicesPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: AuthService, useValue: {
        hasPermission: (p: string) => p.endsWith('.view') || manage && (p === 'hr.device.manage' || p === 'hr.device_user_mapping.manage') } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: true,
      payrollEnabled: false, deviceIntegrationEnabled: enabled });
    const fixture = TestBed.createComponent(HrDevicesPageComponent); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    if (enabled) http.expectOne(`${base}/devices`).flush([]);
    return { fixture, page: fixture.componentInstance, http };
  }
  function device(): DeviceRow { return { id: 1, businessId: 1, deviceCode: 'D', serialNumber: 'S', vendorCode: 'V',
    model: 'M', operationalLocationProjectionId: 10, protocolType: 'ADMS', deviceTimeZoneId: 'Asia/Dhaka',
    status: 'Active', connectionHost: null, connectionPort: null, firmwareVersion: null, hasCredentialReference: true,
    lastSeenAtUtc: null, version: 3 }; }
  it('requires the Device capability and manage permission for writes', async () => {
    const { page, http, fixture } = await create(true, false); page.save(); http.expectNone(`${base}/devices`);
    expect(fixture.nativeElement.textContent).not.toContain('Save device'); http.verify();
  });
  it('hides writes and denies programmatic writes for view-only users', async () => {
    const { page, http, fixture } = await create(false); page.save(); page.createMapping();
    http.expectNone(r => r.method === 'POST'); expect(fixture.nativeElement.textContent).not.toContain('Save device'); http.verify();
  });
  it('preserves credential-reference name by default and requires explicit replacement', async () => {
    const { page, http } = await create(); page.edit(device());
    expect(page.data.credentialSecretReference).toBeNull(); page.data.credentialSecretReference = 'new-secret-name'; page.save();
    http.expectNone(r => r.method === 'PUT'); page.replaceCredentialReference = true; page.save();
    const request = http.expectOne(`${base}/devices/1`); expect(request.request.body.expectedVersion).toBe(3);
    expect(request.request.body.replaceCredentialReference).toBe(true);
    request.flush(device()); http.expectOne(`${base}/devices`).flush([]); http.verify();
  });
  it('sends explicit UTC mapping instants rather than browser-local offsets', async () => {
    const { page, http } = await create(); page.selected = device(); page.employeeId = 1; page.deviceUserId = '23';
    page.mappingFrom = '2027-01-01T18:00'; page.createMapping();
    const request = http.expectOne(`${base}/devices/1/mappings`);
    expect(request.request.body.effectiveFromUtc).toBe('2027-01-01T18:00:00.000Z'); request.flush({ id: 7 });
    http.expectOne(`${base}/devices/1/mappings`).flush([]); http.verify();
  });
  it('closes a versioned mapping with history retained and surfaces conflict', async () => {
    const { page, http } = await create();
    const m: DeviceMappingRow = { id: 7, businessId: 1, deviceId: 1, employeeId: 1, deviceUserId: '23',
      effectiveFromUtc: '2027-01-01T00:00:00Z', effectiveToUtc: null, isActive: true, version: 2 };
    page.close(m); http.expectNone(`${base}/device-user-mappings/7`);
    page.closeAt = '2027-02-01T00:00'; page.close(m);
    const request = http.expectOne(`${base}/device-user-mappings/7`); expect(request.request.method).toBe('DELETE');
    expect(request.request.body.expectedVersion).toBe(2);
    request.flush({ detail: 'Mapping changed since it was read.' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toContain('changed since'); http.verify();
  });
});

describe('Device page (UI kit)', () => {
  const base = `${environment.apiUrl}/hr/v1/attendance`;
  afterEach(() => TestBed.resetTestingModule());
  const device = (id: number, code: string, over: Partial<DeviceRow> = {}): DeviceRow => ({ id, businessId: 1, deviceCode: code, serialNumber: `S-${code}`,
    vendorCode: 'V', model: 'M', operationalLocationProjectionId: 10, protocolType: 'ADMS', deviceTimeZoneId: 'Asia/Dhaka', status: 'Active',
    connectionHost: null, connectionPort: null, firmwareVersion: null, hasCredentialReference: true, lastSeenAtUtc: null, version: 3, ...over });
  const mapping = (id: number, over: Partial<DeviceMappingRow> = {}): DeviceMappingRow => ({ id, businessId: 1, deviceId: 1, employeeId: 4,
    deviceUserId: '23', effectiveFromUtc: '2027-01-01T00:00:00Z', effectiveToUtc: null, isActive: true, version: 2, ...over });

  async function open(opts: { manage?: boolean; mappingsView?: boolean; mappingsManage?: boolean; enabled?: boolean } = {}) {
    const manage = opts.manage ?? true;
    const allowed = new Set<string>(['hr.device.view']);
    if (manage) allowed.add('hr.device.manage');
    if (opts.mappingsView ?? true) allowed.add('hr.device_user_mapping.view');
    if (opts.mappingsManage ?? manage) allowed.add('hr.device_user_mapping.manage');
    await TestBed.configureTestingModule({ imports: [HrDevicesPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => allowed.has(p) } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: true,
      payrollEnabled: false, deviceIntegrationEnabled: opts.enabled ?? true });
    const fixture = TestBed.createComponent(HrDevicesPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    const flushList = async (rows: DeviceRow[] = []) => { http.expectOne(`${base}/devices`).flush(rows); await settle(); };
    return { fixture, http, el, settle, flushList, page: fixture.componentInstance };
  }

  const bodyRows = (root: ParentNode) => Array.from(root.querySelectorAll('tbody tr'));
  const cellTexts = (row: Element) => Array.from(row.querySelectorAll('td')).map(td => td.textContent?.replace(/\s+/g, ' ').trim());
  const buttonByText = (el: ParentNode, text: string) =>
    Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  const byAria = (el: ParentNode, label: string) => el.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;
  const control = (el: ParentNode, name: string) => el.querySelector(`[name="${name}"]`) as HTMLInputElement & HTMLSelectElement;
  async function type(settle: () => Promise<void>, input: HTMLInputElement, value: string) {
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value; input.dispatchEvent(new Event('input')); await settle();
  }
  async function openMappings(ctx: Awaited<ReturnType<typeof open>>, rows: DeviceMappingRow[] = [mapping(7)]) {
    byAria(ctx.el, 'Mappings of device D1')!.click();
    ctx.http.expectOne(`${base}/devices/1/mappings`).flush(rows);
    await ctx.settle();
  }

  it('renders a single h1, landmark, breadcrumb, metadata notice and a named table', async () => {
    const { el, flushList } = await open();
    await flushList([device(1, 'D1')]);
    expect(el.querySelector('main')).not.toBeNull();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Attendance devices and user mappings');
    expect(el.textContent).toContain('Metadata only; no punch ingress is enabled here.');
    expect(el.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe('/dashboard');
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Attendance devices');
    expect(Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Device', 'Location / Timezone', 'Status', 'Version', 'Last seen', 'Secret reference', 'Actions']);
  });

  it('shows status badges and never the credential value, only whether a name is configured', async () => {
    const { el, flushList } = await open();
    await flushList([device(1, 'D1'), device(2, 'D2', { status: 'Error', hasCredentialReference: false, lastSeenAtUtc: '2026-10-01T00:00:00Z' })]);
    const rows = bodyRows(el);
    expect(cellTexts(rows[0]).slice(0, 6)).toEqual(['D1 / V / S-D1', '10 / Asia/Dhaka', 'Active', '3', 'Never', 'Configured name']);
    expect(cellTexts(rows[1]).slice(2, 6)).toEqual(['Error', '3', TestBed.inject(CompanyDateService).format('2026-10-01T00:00:00Z', true), 'Not configured']);
    expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Error');
  });

  it('labels every device form control, flags required ones and explains the secret name field', async () => {
    const { el, flushList } = await open();
    await flushList();
    expect(Array.from(el.querySelectorAll('app-ui-card h2')).some(h => h.textContent?.includes('New device'))).toBe(true);
    const labels = Array.from(el.querySelectorAll('form[aria-label="Device details"] label.ui-field__label'));
    expect(labels.length).toBe(12);
    for (const label of labels) expect(el.querySelector(`#${label.getAttribute('for')}`)).not.toBeNull();
    expect(labels.some(l => l.textContent?.includes('External secret NAME (not the secret)'))).toBe(true);
    expect(control(el, 'code').getAttribute('aria-required')).toBe('true');
    expect(control(el, 'host').getAttribute('aria-required')).toBeNull();
    expect(el.querySelector('[name="replace"]')).toBeNull();
  });

  it('shows named row actions by permission and device status', async () => {
    const { el, flushList } = await open();
    await flushList([device(1, 'D1'), device(2, 'D2', { status: 'Disabled' })]);
    const names = (i: number) => Array.from(bodyRows(el)[i].querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
    expect(names(0)).toEqual(['Edit device D1', 'Deactivate device D1', 'Mappings of device D1']);
    expect(names(1)).toEqual(['Edit device D2', 'Mappings of device D2']);
  });

  it('hides the form and manage actions for view-only callers but keeps Mappings when allowed', async () => {
    const { el, flushList } = await open({ manage: false });
    await flushList([device(1, 'D1')]);
    expect(el.querySelector('form')).toBeNull();
    expect(Array.from(bodyRows(el)[0].querySelectorAll('button')).map(b => b.textContent?.trim())).toEqual(['Mappings']);
  });

  it('offers no actions at all without mapping view and manage permissions', async () => {
    const { el, flushList } = await open({ manage: false, mappingsView: false });
    await flushList([device(1, 'D1')]);
    expect(bodyRows(el)[0].querySelectorAll('button').length).toBe(0);
  });

  it('edits a device from its row: shows the explicit replace checkbox and sends the version', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList([device(1, 'D1')]);
    byAria(el, 'Edit device D1')!.click();
    await settle();
    expect(Array.from(el.querySelectorAll('app-ui-card h2')).some(h => h.textContent?.includes('Edit device'))).toBe(true);
    expect(control(el, 'code').value).toBe('D1');
    expect(control(el, 'secretReference').value).toBe('');
    const replace = control(el, 'replace');
    expect(replace.closest('label')?.textContent).toContain('Explicitly replace/clear credential reference; otherwise preserve it');
    buttonByText(el, 'Save device')!.click();
    const request = http.expectOne(`${base}/devices/1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.expectedVersion).toBe(3);
    expect(request.request.body.replaceCredentialReference).toBe(false);
    request.flush(device(1, 'D1')); http.expectOne(`${base}/devices`).flush([]);
  });

  it('shows the credential-replacement rule as an alert when a secret name is typed without opting in', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList([device(1, 'D1')]);
    byAria(el, 'Edit device D1')!.click();
    await settle();
    await type(settle, control(el, 'secretReference'), 'new-secret-name');
    buttonByText(el, 'Save device')!.click();
    await settle();
    http.expectNone(r => r.method === 'PUT');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Select explicit credential-reference replacement');
  });

  it('deactivates a device from its row action', async () => {
    const { el, http, flushList } = await open();
    await flushList([device(1, 'D1')]);
    byAria(el, 'Deactivate device D1')!.click();
    const request = http.expectOne(r => r.method === 'POST' && r.url.startsWith(`${base}/devices/1`));
    request.flush(device(1, 'D1', { status: 'Disabled' }));
    http.expectOne(`${base}/devices`).flush([]);
  });

  it('creates a device from the labelled form with the typed values', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList();
    await type(settle, control(el, 'code'), 'NEW');
    await type(settle, control(el, 'serial'), 'SER');
    await type(settle, control(el, 'vendor'), 'ZK');
    await type(settle, control(el, 'model'), 'X1');
    await type(settle, control(el, 'location'), '10');
    buttonByText(el, 'Save device')!.click();
    const request = http.expectOne(r => r.method === 'POST' && r.url === `${base}/devices`);
    expect(request.request.body).toMatchObject({ deviceCode: 'NEW', serialNumber: 'SER', vendorCode: 'ZK', model: 'X1',
      operationalLocationProjectionId: 10, protocolType: 'ADMS', status: 'Active' });
    request.flush({ detail: 'Duplicate device code.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Duplicate device code.');
  });

  it('opens the mapping card with a labelled form, closure field and mapping table', async () => {
    const ctx = await open();
    await ctx.flushList([device(1, 'D1')]);
    await openMappings(ctx, [mapping(7), mapping(8, { isActive: false, effectiveToUtc: '2027-02-01T00:00:00Z' })]);
    const { el } = ctx;
    expect(Array.from(el.querySelectorAll('app-ui-card h2')).some(h => h.textContent?.includes('Mappings: D1'))).toBe(true);
    expect(el.textContent).toContain('UTC instants; half-open intervals [from, to).');
    const form = el.querySelector('form[aria-label="Device user mapping"]')!;
    const labels = Array.from(form.querySelectorAll('label.ui-field__label'));
    expect(labels.map(l => l.textContent?.replace(/\s*\*?\s*\(required\)$/, '').replace(/\s*\*$/, '').trim()))
      .toEqual(['Employee ID', 'Device user ID', 'Effective from UTC', 'Effective to UTC (optional)']);
    for (const label of labels) expect(el.querySelector(`#${label.getAttribute('for')}`)).not.toBeNull();
    expect(Array.from(el.querySelectorAll('label.ui-field__label')).some(l => l.textContent?.includes('Closure UTC instant'))).toBe(true);
    const table = el.querySelector('[role="region"][aria-label="Device user mappings"]')!;
    expect(Array.from(table.querySelectorAll('thead th')).map(th => th.textContent?.trim()))
      .toEqual(['User / Employee', 'UTC period', 'State', 'Version', 'Actions']);
    const rows = bodyRows(table);
    expect(cellTexts(rows[0]).slice(0, 4)).toEqual(['23 / 4', '01/01/2027 00:00:00 - Open', 'Open', '2']);
    expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Closed history');
    expect(rows[1].querySelectorAll('button').length).toBe(0);
  });

  it('closes a mapping with the explicit UTC instant and the expected version', async () => {
    const ctx = await open();
    await ctx.flushList([device(1, 'D1')]);
    await openMappings(ctx);
    const closure = Array.from(ctx.el.querySelectorAll('label.ui-field__label')).find(l => l.textContent?.includes('Closure UTC instant'))!;
    await type(ctx.settle, ctx.el.querySelector(`#${closure.getAttribute('for')}`) as HTMLInputElement, '2027-02-01T00:00');
    byAria(ctx.el, 'Close mapping of device user 23; retain history')!.click();
    const request = ctx.http.expectOne(`${base}/device-user-mappings/7`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body.expectedVersion).toBe(2);
    request.flush({}); ctx.http.expectOne(`${base}/devices/1/mappings`).flush([]);
  });

  it('shows the closure validation as an alert when no instant is given', async () => {
    const ctx = await open();
    await ctx.flushList([device(1, 'D1')]);
    await openMappings(ctx);
    byAria(ctx.el, 'Close mapping of device user 23; retain history')!.click();
    await ctx.settle();
    ctx.http.expectNone(`${base}/device-user-mappings/7`);
    expect(ctx.el.querySelector('[role="alert"]')?.textContent).toContain('Mapping manage access and explicit valid UTC closure instant required.');
  });

  it('hides mapping forms and close actions without mapping manage permission', async () => {
    const ctx = await open({ manage: false, mappingsView: true, mappingsManage: false });
    await ctx.flushList([device(1, 'D1')]);
    await openMappings(ctx);
    expect(ctx.el.querySelector('form')).toBeNull();
    expect(ctx.el.textContent).not.toContain('Closure UTC instant');
    expect(ctx.el.querySelector('[role="region"][aria-label="Device user mappings"]')?.textContent).toContain('23 / 4');
    expect(buttonByText(ctx.el, 'Close; retain history')).toBeUndefined();
  });

  it('creates a mapping with explicit UTC instants from the labelled form', async () => {
    const ctx = await open();
    await ctx.flushList([device(1, 'D1')]);
    await openMappings(ctx, []);
    expect(ctx.el.textContent).toContain('No mappings found.');
    await type(ctx.settle, control(ctx.el, 'employee'), '4');
    await type(ctx.settle, control(ctx.el, 'deviceUser'), '23');
    await type(ctx.settle, control(ctx.el, 'mappingFrom'), '2027-01-01T18:00');
    buttonByText(ctx.el, 'Create mapping')!.click();
    const request = ctx.http.expectOne(`${base}/devices/1/mappings`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({ employeeId: 4, deviceUserId: '23', effectiveFromUtc: '2027-01-01T18:00:00.000Z', effectiveToUtc: null });
    request.flush({}); ctx.http.expectOne(`${base}/devices/1/mappings`).flush([]);
  });

  it('shows loading while empty, the empty state, and the capability alert', async () => {
    const loading = await open();
    await loading.settle();
    expect(loading.el.querySelector('[role="status"]')?.textContent).toContain('Loading / saving...');
    await loading.flushList([]);
    expect(loading.el.textContent).toContain('No devices found.');
    TestBed.resetTestingModule();
    const disabled = await open({ enabled: false });
    await disabled.settle();
    expect(disabled.el.querySelector('[role="alert"]')?.textContent).toContain('HR/Attendance/Device capability or device view permission unavailable.');
    expect(disabled.el.querySelector('form')).toBeNull();
  });

  it('shows a device load failure as an alert', async () => {
    const { el, http, settle } = await open();
    http.expectOne(`${base}/devices`).flush({ detail: 'Not allowed.' }, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Not allowed.');
  });

  it('searches and sorts the loaded devices', async () => {
    const { el, settle, flushList } = await open();
    await flushList([device(1, 'ZZZ'), device(2, 'AAA')]);
    const header = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Device'))!;
    header.querySelector('button')!.click();
    await settle();
    expect(header.getAttribute('aria-sort')).toBe('ascending');
    expect(bodyRows(el)[0].textContent).toContain('AAA');
    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('zzz', (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'zzz'; input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(bodyRows(el).length).toBe(1);
    expect(bodyRows(el)[0].textContent).toContain('ZZZ');
  });
});
