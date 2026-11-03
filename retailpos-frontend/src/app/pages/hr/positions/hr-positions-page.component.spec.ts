import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HrPositionsPageComponent } from './hr-positions-page.component';
import { AuthService } from '../../../services/auth.service';
import { HrOrgUnit, HrPosition } from '../../../services/hr-organisation.service';
import { environment } from '../../../../environments/environment';

describe('HR positions page (UI kit)', () => {
  const base = `${environment.apiUrl}/hr/v1`;
  afterEach(() => TestBed.resetTestingModule());

  const unit = (id: number, code: string, name: string): HrOrgUnit => ({ id, businessId: 1, code, name, unitType: 'Department',
    parentOrgUnitId: null, operationalLocationProjectionId: null, effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true, version: 1 });
  const position = (id: number, code: string, over: Partial<HrPosition> = {}): HrPosition => ({ id, businessId: 1, code, name: `Name ${code}`,
    orgUnitId: 1, operationalLocationProjectionId: null, reportsToPositionId: null, status: 'Vacant', effectiveFrom: '2026-02-01',
    effectiveTo: null, version: 3, ...over });

  async function create(opts: { manage?: boolean } = {}) {
    const manage = opts.manage ?? true;
    await TestBed.configureTestingModule({
      imports: [HrPositionsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => manage && p === 'hr.position.manage' } }]
    }).compileComponents();
    const fixture = TestBed.createComponent(HrPositionsPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    const flushLoad = async (positions: HrPosition[] = [], units: HrOrgUnit[] = [unit(1, 'OPS', 'Operations')]) => {
      http.expectOne(r => r.url === `${base}/positions` && r.method === 'GET').flush(positions);
      http.expectOne(`${base}/org-units`).flush(units);
      http.expectNone(`${base}/operational-locations`);
      await settle();
    };
    return { fixture, http, el, settle, flushLoad, page: fixture.componentInstance };
  }

  const bodyRows = (el: HTMLElement) => Array.from(el.querySelectorAll('tbody tr'));
  const buttonByText = (el: HTMLElement, text: string) =>
    Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  const control = (el: HTMLElement, name: string) => el.querySelector(`[name="${name}"]`) as HTMLInputElement & HTMLSelectElement;

  async function type(settle: () => Promise<void>, input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function choose(settle: () => Promise<void>, select: HTMLSelectElement, optionText: string) {
    const index = Array.from(select.options).findIndex(o => o.textContent?.includes(optionText));
    select.selectedIndex = index;
    select.dispatchEvent(new Event('change'));
    await settle();
  }

  it('renders one h1, a main landmark, breadcrumb and a named table region', async () => {
    const { el, flushLoad } = await create();
    await flushLoad([position(1, 'P1')]);
    expect(el.querySelector('main')).not.toBeNull();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Positions');
    expect(el.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe('/dashboard');
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Positions');
    const headers = Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim());
    expect(headers).toEqual(['Code', 'Name', 'Department / Team', 'Reports to', 'Status', 'Actions']);
  });

  it('shows a loading status and hides the form and table while loading', async () => {
    const { el, settle, flushLoad } = await create();
    await settle();
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Loading positions...');
    expect(el.querySelector('form')).toBeNull();
    expect(el.querySelector('table')).toBeNull();
    await flushLoad();
    expect(el.querySelector('form')).not.toBeNull();
  });

  it('shows department, reporting and status without legacy location or dates', async () => {
    const { el, flushLoad } = await create();
    await flushLoad([
      position(1, 'P1', { operationalLocationProjectionId: 10, status: 'Filled' }),
      position(2, 'P2', { reportsToPositionId: 1, status: 'TemporarilyFilled', effectiveTo: '2026-12-31' }),
      position(3, 'P3', { orgUnitId: 99, status: 'Inactive' })
    ]);
    const rows = bodyRows(el);
    const cells = (i: number) => Array.from(rows[i].querySelectorAll('td')).map(td => td.textContent?.replace(/\s+/g, ' ').trim());
    expect(cells(0).slice(0, 5)).toEqual(['P1', 'Name P1', 'OPS — Operations', '—', 'Filled']);
    expect(cells(1)[3]).toBe('P1 — Name P1');
    expect(cells(1)[4]).toBe('TemporarilyFilled');
    expect(el.textContent).not.toContain('2026-02-01');
    expect(el.textContent).not.toContain('Location scope');
    expect(cells(2)[2]).toBe('Unavailable');
    expect(rows[0].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Filled');
  });

  it('labels every form control and marks required ones', async () => {
    const { el, flushLoad } = await create();
    await flushLoad();
    const labels = Array.from(el.querySelectorAll('form label.ui-field__label'));
    expect(labels.map(l => l.textContent?.replace(/\s*\*?\s*\(required\)\s*$/, '').replace(/\s*\*$/, '').trim())).toEqual([
      'Code', 'Position Name', 'Department / Team', 'Reports to', 'Status']);
    for (const label of labels) {
      const target = el.querySelector(`#${label.getAttribute('for')}`);
      expect(target).not.toBeNull();
    }
    expect(control(el, 'code').getAttribute('aria-required')).toBe('true');
    expect(control(el, 'name').getAttribute('aria-required')).toBe('true');
    expect(control(el, 'orgUnitId').getAttribute('aria-required')).toBe('true');
    expect(el.querySelector('[name="locationId"], [name="effectiveFrom"], [name="effectiveTo"]')).toBeNull();
    expect(el.querySelector('h2')?.textContent).toContain('Create position');
  });

  it('hides the form and every row action without the manage permission', async () => {
    const { el, flushLoad } = await create({ manage: false });
    await flushLoad([position(1, 'P1')]);
    expect(el.querySelector('form')).toBeNull();
    expect(buttonByText(el, 'Edit')).toBeUndefined();
    expect(buttonByText(el, 'Deactivate')).toBeUndefined();
    expect(bodyRows(el).length).toBe(1);
  });

  it('shows named Edit and Deactivate actions only for non-inactive positions', async () => {
    const { el, flushLoad } = await create();
    await flushLoad([position(1, 'P1'), position(2, 'P2', { status: 'Inactive' })]);
    const rows = bodyRows(el);
    expect(rows[0].querySelector('button[aria-label="Edit P1"]')).not.toBeNull();
    expect(rows[0].querySelector('button[aria-label="Deactivate P1"]')).not.toBeNull();
    expect(rows[1].querySelectorAll('button').length).toBe(0);
  });

  it('creates a reusable position without location or dates and reloads', async () => {
    const { el, http, settle, flushLoad } = await create();
    await flushLoad([position(1, 'P1')]);
    await type(settle, control(el, 'code'), 'NEW');
    await type(settle, control(el, 'name'), 'New position');
    await choose(settle, control(el, 'orgUnitId'), 'OPS');
    await choose(settle, control(el, 'reportsToPositionId'), 'P1');
    buttonByText(el, 'Create position')!.click();
    const request = http.expectOne(r => r.url === `${base}/positions` && r.method === 'POST');
    expect(request.request.body).toEqual({ code: 'NEW', name: 'New position', orgUnitId: 1,
      reportsToPositionId: 1, status: 'Vacant' });
    request.flush(position(5, 'NEW'));
    await flushLoad([position(1, 'P1'), position(5, 'NEW')]);
    expect(bodyRows(el).length).toBe(2);
    http.verify();
  });

  it('blocks submission without a Department / Team', async () => {
    const { el, http, settle, flushLoad } = await create();
    await flushLoad();
    buttonByText(el, 'Create position')!.click();
    await settle();
    http.expectNone(r => r.method === 'POST');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Select a Department / Team.');
  });

  it('requires code and Position Name without requiring dates', async () => {
    const { el, http, settle, flushLoad } = await create();
    await flushLoad();
    await choose(settle, control(el, 'orgUnitId'), 'OPS');
    buttonByText(el, 'Create position')!.click();
    await settle();
    http.expectNone(r => r.method === 'POST');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Code and Position Name are required.');
  });

  it('shows a save failure as an alert and keeps the form', async () => {
    const { el, http, settle, flushLoad } = await create();
    await flushLoad();
    await choose(settle, control(el, 'orgUnitId'), 'OPS');
    await type(settle, control(el, 'code'), 'NEW');
    await type(settle, control(el, 'name'), 'New position');
    buttonByText(el, 'Create position')!.click();
    http.expectOne(r => r.url === `${base}/positions` && r.method === 'POST').flush({}, { status: 400, statusText: 'Bad' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to save the position.');
    expect(el.querySelector('form')).not.toBeNull();
  });

  it('edits a position: fills the form, switches labels, saves with the version and cancels', async () => {
    const { el, http, settle, flushLoad } = await create();
    await flushLoad([position(1, 'P1', {
      status: 'Filled', operationalLocationProjectionId: 10, effectiveTo: '2026-12-31'
    })]);
    (el.querySelector('button[aria-label="Edit P1"]') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('h2')?.textContent).toContain('Edit position');
    expect(control(el, 'code').value).toBe('P1');
    expect(control(el, 'status').value).toBe('Filled');
    expect(buttonByText(el, 'Save changes')).toBeDefined();

    buttonByText(el, 'Save changes')!.click();
    const request = http.expectOne(`${base}/positions/1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.expectedVersion).toBe(3);
    expect(request.request.body.data).toEqual({
      code: 'P1', name: 'Name P1', orgUnitId: 1, reportsToPositionId: null, status: 'Filled'
    });
    request.flush(position(1, 'P1'));
    await flushLoad([position(1, 'P1')]);
    expect(el.querySelector('h2')?.textContent).toContain('Create position');

    (el.querySelector('button[aria-label="Edit P1"]') as HTMLButtonElement).click();
    await settle();
    buttonByText(el, 'Cancel')!.click();
    await settle();
    expect(buttonByText(el, 'Cancel')).toBeUndefined();
    expect(control(el, 'code').value).toBe('');
  });

  it('excludes the position being edited from the reports-to choices', async () => {
    const { el, settle, flushLoad } = await create();
    await flushLoad([position(1, 'P1'), position(2, 'P2')]);
    const optionTexts = () => Array.from(control(el, 'reportsToPositionId').options).map(o => o.textContent?.trim());
    expect(optionTexts()).toEqual(['No reporting position', 'P1 — Name P1', 'P2 — Name P2']);
    (el.querySelector('button[aria-label="Edit P1"]') as HTMLButtonElement).click();
    await settle();
    expect(optionTexts()).toEqual(['No reporting position', 'P2 — Name P2']);
  });

  it('deactivates with the expected version and today as the end date', async () => {
    const { el, http, flushLoad } = await create();
    await flushLoad([position(1, 'P1')]);
    (el.querySelector('button[aria-label="Deactivate P1"]') as HTMLButtonElement).click();
    const request = http.expectOne(`${base}/positions/1/deactivate`);
    expect(request.request.body).toEqual({ expectedVersion: 3, effectiveTo: new Date().toISOString().slice(0, 10) });
    request.flush(position(1, 'P1', { status: 'Inactive' }));
    await flushLoad([position(1, 'P1', { status: 'Inactive' })]);
    expect(el.querySelector('button[aria-label="Edit P1"]')).toBeNull();
  });

  it('renders the load failure as an alert', async () => {
    const { el, http, settle } = await create();
    http.expectOne(r => r.url === `${base}/positions` && r.method === 'GET').flush({}, { status: 500, statusText: 'Server error' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load positions or their Department / Team references.');
  });

  it('keeps positions available and warns when organisation units cannot load', async () => {
    const { el, http, settle } = await create();
    http.expectOne(r => r.url === `${base}/positions` && r.method === 'GET').flush([position(1, 'P1')]);
    http.expectOne(`${base}/org-units`).flush({}, { status: 500, statusText: 'Server error' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Departments / Teams could not be loaded; position results remain available.');
    expect(bodyRows(el).length).toBe(1);
  });

  it('searches, sorts and paginates the loaded positions client-side', async () => {
    const { el, settle, flushLoad } = await create();
    await flushLoad(Array.from({ length: 30 }, (_, i) => position(i + 1, `C${String(i + 1).padStart(2, '0')}`)));
    expect(bodyRows(el).length).toBe(25);
    el.querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
    await settle();
    expect(bodyRows(el).length).toBe(5);

    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'C07';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(bodyRows(el).length).toBe(1);
    expect(bodyRows(el)[0].textContent).toContain('Name C07');

    input.value = '';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    const codeHeader = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Code'))!;
    codeHeader.querySelector<HTMLButtonElement>('button')!.click();
    codeHeader.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(codeHeader.getAttribute('aria-sort')).toBe('descending');
    expect(bodyRows(el)[0].textContent).toContain('C30');
  });

  it('shows the empty state text', async () => {
    const { el, flushLoad } = await create();
    await flushLoad([]);
    expect(el.textContent).toContain('No positions found.');
  });
});
