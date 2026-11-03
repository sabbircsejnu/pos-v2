import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HrOrganisationPageComponent } from './hr-organisation-page.component';
import { HrOrgUnit } from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

describe('HR organisation page (UI kit)', () => {
  const url = `${environment.apiUrl}/hr/v1/org-units`;
  afterEach(() => TestBed.resetTestingModule());

  function unit(id: number, code: string, name: string, over: Partial<HrOrgUnit> = {}): HrOrgUnit {
    return { id, businessId: 1, code, name, unitType: 'Department', parentOrgUnitId: null,
      operationalLocationProjectionId: null, effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true, version: 1, ...over };
  }

  async function create(manage = true, view = true) {
    await TestBed.configureTestingModule({
      imports: [HrOrganisationPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (permission: string) =>
          permission === 'hr.organisation.view' ? view : permission === 'hr.organisation.manage' && manage } }]
    }).compileComponents();
    const fixture = TestBed.createComponent(HrOrganisationPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    return { fixture, http, el: fixture.nativeElement as HTMLElement, settle, page: fixture.componentInstance };
  }

  async function search(el: HTMLElement, settle: () => Promise<void>, value: string) {
    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
  }

  const bodyRows = (el: HTMLElement) => Array.from(el.querySelectorAll('tbody tr'));

  it('renders a single page header h1, a main landmark and a breadcrumb', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([]);
    await settle();
    expect(el.querySelector('main')).not.toBeNull();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Departments & Teams');
    expect(el.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe('/dashboard');
  });

  it('shows a loading status while the units are requested', async () => {
    const { http, el, settle } = await create();
    await settle();
    const status = el.querySelector('[role="status"]');
    expect(status?.textContent).toContain('Loading Departments & Teams...');
    http.expectOne(url).flush([]);
  });

  it('lists the units in a named, scrollable table region with the existing columns', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'HQ', 'Head office'), unit(2, 'FIN', 'Finance', { parentOrgUnitId: 1 })]);
    await settle();
    const region = el.querySelector('[role="region"]');
    expect(region?.getAttribute('aria-label')).toBe('Departments & Teams');
    const headers = Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim());
    expect(headers).toEqual(['Code', 'Name', 'Type', 'Parent', 'Status', 'Actions']);
    const rows = bodyRows(el);
    expect(rows.length).toBe(2);
    const first = Array.from(rows[0].querySelectorAll('td')).map(td => td.textContent?.trim());
    expect(first.slice(0, 4)).toEqual(['HQ', 'Head office', 'Department', '-']);
    expect(rows[1].querySelectorAll('td')[3].textContent?.trim()).toBe('Head office');
  });

  it('shows status as visible badge text for active and inactive units', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'A', 'Alpha'), unit(2, 'B', 'Beta', { isActive: false })]);
    await settle();
    const badges = Array.from(el.querySelectorAll('tbody app-ui-badge')).map(b => b.textContent?.trim());
    expect(badges).toEqual(['Active', 'Inactive']);
  });

  it('searches the loaded units client-side', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'HQ', 'Head office'), unit(2, 'FIN', 'Finance')]);
    await settle();
    await search(el, settle, 'fina');
    expect(bodyRows(el).length).toBe(1);
    expect(bodyRows(el)[0].textContent).toContain('Finance');
    http.verify();
  });

  it('sorts by name and exposes aria-sort', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'B', 'Zulu'), unit(2, 'A', 'Alpha')]);
    await settle();
    const nameHeader = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Name'))!;
    nameHeader.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(nameHeader.getAttribute('aria-sort')).toBe('ascending');
    expect(bodyRows(el)[0].textContent).toContain('Alpha');
  });

  it('paginates the loaded units with the table pager', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush(Array.from({ length: 30 }, (_, i) => unit(i + 1, `U${String(i + 1).padStart(2, '0')}`, `Unit ${i + 1}`)));
    await settle();
    expect(bodyRows(el).length).toBe(25);
    el.querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
    await settle();
    expect(bodyRows(el).length).toBe(5);
  });

  it('shows the empty state text', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([]);
    await settle();
    expect(el.textContent).toContain('No Departments / Teams found.');
  });

  it('shows the load failure as an alert with the existing message', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush({}, { status: 500, statusText: 'Server error' });
    await settle();
    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Unable to load Departments & Teams.');
  });

  const button = (el: HTMLElement, text: string) =>
    Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === text);

  async function type(el: HTMLElement, settle: () => Promise<void>, name: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`[name="${name}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function choose(el: HTMLElement, settle: () => Promise<void>, name: string, label: string) {
    const select = el.querySelector<HTMLSelectElement>(`[name="${name}"]`)!;
    select.selectedIndex = Array.from(select.options).findIndex(option => option.textContent?.includes(label));
    select.dispatchEvent(new Event('change'));
    await settle();
  }

  it('opens the shared dialog with only the normal fields and existing Department/Team values', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([]);
    await settle();
    expect(el.querySelector('form')).toBeNull();
    button(el, 'Add Department / Team')!.click();
    await settle();
    expect(el.querySelector('[role="dialog"]')?.getAttribute('aria-modal')).toBe('true');
    expect(Array.from(el.querySelectorAll('form [name]')).map(input => input.getAttribute('name')))
      .toEqual(['code', 'name', 'unitType', 'parentOrgUnitId', 'isActive']);
    expect(Array.from(el.querySelectorAll('[name="unitType"] option')).map(option => option.textContent?.trim()))
      .toEqual(['Department', 'Team']);
    expect(el.textContent).toContain('becomes effective when created');
    expect(button(el, 'Create')!.disabled).toBe(true);
    button(el, 'Cancel')!.click();
    await settle();
    expect(el.querySelector('[role="dialog"]')).toBeNull();
    http.verify();
  });

  it.each(['Department', 'Team'])('creates a %s without browser-supplied effective dates', async typeName => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'SALES', 'Sales')]);
    await settle();
    button(el, 'Add Department / Team')!.click();
    await settle();
    await type(el, settle, 'code', ' NEW ');
    await type(el, settle, 'name', ' New unit ');
    await choose(el, settle, 'unitType', typeName);
    if (typeName === 'Team') await choose(el, settle, 'parentOrgUnitId', 'SALES');
    button(el, 'Create')!.click();
    await settle();
    const request = http.expectOne(r => r.url === url && r.method === 'POST');
    expect(request.request.body).toEqual({
      code: 'NEW', name: 'New unit', unitType: typeName,
      parentOrgUnitId: typeName === 'Team' ? 1 : null, isActive: true
    });
    expect(button(el, 'Cancel')!.disabled).toBe(true);
    request.flush(unit(2, 'NEW', 'New unit', { unitType: typeName }));
    http.expectOne(url).flush([unit(1, 'SALES', 'Sales'), unit(2, 'NEW', 'New unit')]);
    await settle();
    expect(el.querySelector('[role="dialog"]')).toBeNull();
    expect(bodyRows(el).length).toBe(2);
    http.verify();
  });

  it('edits with expectedVersion and preserves hidden legacy end date and location without sending a start date', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([unit(1, 'SALES', 'Sales', {
      operationalLocationProjectionId: 7, effectiveFrom: '2020-01-01', effectiveTo: '2027-12-31', version: 5
    })]);
    await settle();
    el.querySelector<HTMLButtonElement>('button[aria-label="Edit SALES"]')!.click();
    await settle();
    await type(el, settle, 'name', 'Sales department');
    await choose(el, settle, 'isActive', 'Inactive');
    button(el, 'Save')!.click();
    await settle();
    const request = http.expectOne(`${url}/1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ expectedVersion: 5, data: {
      code: 'SALES', name: 'Sales department', unitType: 'Department',
      parentOrgUnitId: null, isActive: false, operationalLocationProjectionId: 7, effectiveTo: '2027-12-31'
    } });
    request.flush(unit(1, 'SALES', 'Sales department', { isActive: false, version: 6 }));
    http.expectOne(url).flush([unit(1, 'SALES', 'Sales department', { isActive: false })]);
    await settle();
    expect(el.textContent).toContain('Sales department');
    expect(el.querySelector('tbody app-ui-badge')?.textContent?.trim()).toBe('Inactive');
    http.verify();
  });

  it('hides Add and Edit without manage permission and prevents direct writes', async () => {
    const { http, el, settle, page } = await create(false);
    http.expectOne(url).flush([unit(1, 'SALES', 'Sales')]);
    await settle();
    expect(button(el, 'Add Department / Team')).toBeUndefined();
    expect(button(el, 'Edit')).toBeUndefined();
    page.add();
    page.form = { code: 'X', name: 'X', unitType: 'Department', parentOrgUnitId: null, isActive: true };
    page.save();
    expect(page.saveError()).toContain('permission');
    expect(page.dialogOpen()).toBe(false);
    http.expectNone(r => r.method === 'POST');
    http.verify();
  });

  it('does not request or render the list without view permission', async () => {
    const { http, el, settle } = await create(true, false);
    await settle();
    expect(el.querySelector('table')).toBeNull();
    expect(button(el, 'Add Department / Team')).toBeUndefined();
    expect(el.textContent).toContain('do not have permission to view');
    http.expectNone(url);
    http.verify();
  });

  it('excludes self, descendants and inactive parents and rejects forced circular choices', async () => {
    const { http, el, settle, page } = await create();
    const parent = unit(1, 'SALES', 'Sales');
    http.expectOne(url).flush([parent, unit(2, 'TEAM', 'Retail team', { unitType: 'Team', parentOrgUnitId: 1 }),
      unit(3, 'INACTIVE', 'Inactive department', { isActive: false }), unit(4, 'HR', 'HR')]);
    await settle();
    page.edit(parent);
    await settle();
    expect(page.parentOptions().map(option => option.id)).toEqual([4]);
    page.form.parentOrgUnitId = 2;
    page.save();
    expect(page.saveError()).toContain('circular hierarchy');
    http.expectNone(r => r.method === 'PUT');
    http.verify();
  });

  it('keeps the dialog and entered values on API validation or concurrency errors', async () => {
    const { http, el, settle, page } = await create();
    http.expectOne(url).flush([]);
    await settle();
    page.add();
    page.form.code = 'DUPLICATE';
    page.form.name = 'Sales';
    page.save();
    http.expectOne(r => r.method === 'POST').flush({ message: 'Code already exists in this Business.' },
      { status: 409, statusText: 'Conflict' });
    await settle();
    expect(el.querySelector('[role="dialog"]')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Code already exists');
    expect(page.form.code).toBe('DUPLICATE');
    expect(page.saving()).toBe(false);
    http.verify();
  });
});
