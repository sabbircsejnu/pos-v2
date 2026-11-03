import { CompanyDateService } from '../../../services/company-date.service';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { HrEmployeesPageComponent } from './hr-employees-page.component';
import { HrOrganisationService } from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';

const listItem = {
  id: 1, employeeCode: 'EMP-1', firstName: 'Alex', middleName: null, lastName: 'Employee',
  displayName: 'Alex Employee', workEmail: 'alex@example.test', currentEmploymentStatus: 'Active',
  currentEmploymentTypeId: 11, currentEmploymentTypeName: 'Permanent', joiningDate: '2026-10-01', version: 3
};

const profile = {
  ...listItem,
  currentPositionId: 7,
  currentOrgUnitId: null,
  confirmationDate: null,
  sensitiveFields: {
    dateOfBirth: '1990-01-02', genderCode: null, personalEmail: null, mobile: '555-0100',
    presentAddress: null, permanentAddress: null, emergencyContactName: null,
    emergencyContactPhone: null, nationalId: 'NID-998877', passportNo: 'P-445566'
  }
};

const pendingTransfer = {
  id: 'tr-1', employeeId: 1, targetLocationProjectionId: 5, locationName: 'Main Outlet',
  sourceSystem: 'Business', sourceEntityType: 'Outlet', sourceEntityId: '5', effectiveFrom: '2026-11-01',
  status: 'PendingApproval', reason: 'Move', requestedByUserId: 2, requestedAtUtc: '2026-10-01T00:00:00Z',
  approvalInstanceId: 44, version: 1
};

function page(items: unknown[], totalCount = items.length) {
  return { items, page: 1, pageSize: 25, totalCount };
}

async function setup(
  permissions: string[],
  overrides: Record<string, unknown> = {},
  initialUrl = '/hr/employees'
) {
  const service = {
    getEmploymentTypes: vi.fn().mockReturnValue(of([{ id: 11, name: 'Permanent' }])),
    searchEmployees: vi.fn().mockReturnValue(of(page([listItem]))),
    getEmployee: vi.fn().mockReturnValue(of(profile)),
    createEmployee: vi.fn().mockReturnValue(of({})),
    updateEmployee: vi.fn().mockReturnValue(of({})),
    getEmployeeUserLink: vi.fn().mockReturnValue(of(null)),
    linkEmployeeUser: vi.fn().mockReturnValue(of({ id: 1, identityUserId: 5, linkedAtUtc: '2026-10-01T00:00:00Z', linkReason: 'Because', version: 1 })),
    unlinkEmployeeUser: vi.fn().mockReturnValue(of({})),
    getEmployeeLifecycle: vi.fn().mockReturnValue(of([])),
    getEmployeePositionHistory: vi.fn().mockReturnValue(of([])),
    getEmployeeLocationHistory: vi.fn().mockReturnValue(of([])),
    getEmployeeLocationTransfers: vi.fn().mockReturnValue(of([])),
    getOperationalLocations: vi.fn().mockReturnValue(of([])),
    getPositions: vi.fn().mockReturnValue(of([])),
    transitionEmployeeLifecycle: vi.fn().mockReturnValue(of({})),
    decideEmployeeLocationTransfer: vi.fn().mockReturnValue(of({})),
    cancelEmployeeLocationTransfer: vi.fn().mockReturnValue(of({})),
    ...overrides
  };
  await TestBed.configureTestingModule({
    imports: [HrEmployeesPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([
        { path: 'hr/employees', component: HrEmployeesPageComponent },
        { path: 'hr/employee/new', component: HrEmployeesPageComponent },
        { path: 'hr/employee/:employeeId/view', component: HrEmployeesPageComponent },
        { path: 'hr/employee/:employeeId/edit', component: HrEmployeesPageComponent }
      ]),
      { provide: HrOrganisationService, useValue: service },
      { provide: AuthService, useValue: { hasPermission: (value: string) => permissions.includes(value) } }
    ]
  }).compileComponents();
  const harness = await RouterTestingHarness.create(initialUrl);
  const fixture = harness.fixture;
  await settle(fixture);
  return {
    fixture,
    service,
    component: harness.routeDebugElement!.componentInstance as HrEmployeesPageComponent,
    el: fixture.nativeElement as HTMLElement
  };
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function button(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return Array.from(root.querySelectorAll('button')).find(item => item.textContent?.trim() === text);
}

function control(root: ParentNode, label: string): HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement {
  const labelElement = Array.from(root.querySelectorAll('label'))
    .find(item => item.textContent?.trim().startsWith(label));
  expect(labelElement, `label "${label}"`).toBeTruthy();
  const target = document.getElementById(labelElement!.getAttribute('for') ?? '');
  expect(target, `control for "${label}"`).toBeTruthy();
  return target as HTMLInputElement;
}

function fill(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string): void {
  element.value = element.hasAttribute('appCompanyDate') || element.closest('app-ui-date-picker')
    ? TestBed.inject(CompanyDateService).inputText(value, (element.getAttribute('placeholder') ?? '').includes('HH:mm'))
    : value;
  element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input'));
}

async function openProfile(fixture: ComponentFixture<unknown>, el: HTMLElement): Promise<void> {
  button(el, 'View profile')!.click();
  await settle(fixture);
}

async function openProfileSection(
  fixture: ComponentFixture<unknown>,
  el: HTMLElement,
  title: string
): Promise<void> {
  const header = Array.from(el.querySelectorAll<HTMLButtonElement>('.hr-profile-stepper .ui-stepper__header'))
    .find(item => item.textContent?.includes(title));
  expect(header, `profile step "${title}"`).toBeTruthy();
  header!.click();
  await settle(fixture);
}

describe('HrEmployeesPageComponent kit presentation', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('employee list', () => {
    it('renders one page heading, breadcrumbs and a named table region with status text', async () => {
      const { el } = await setup(['hr.employee.view']);
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(el.querySelector('h1')?.textContent).toContain('Employees');
      expect(el.querySelectorAll('main').length).toBe(1);
      expect(el.querySelector('nav[aria-label="Breadcrumb"]')?.textContent).toContain('HR');
      const region = el.querySelector('[role="region"][aria-label="Employees"]');
      expect(region).toBeTruthy();
      const headers = Array.from(region!.querySelectorAll('th')).map(item => item.textContent?.trim());
      expect(headers).toEqual(expect.arrayContaining([
        'Employee code', 'Name', 'Work email', 'Employment type', 'Status', 'Joining date', 'Action']));
      expect(region!.querySelector('app-ui-badge')?.textContent).toContain('Active');
      expect(region!.textContent).toContain('alex@example.test');
      expect(region!.querySelectorAll('th[aria-sort]').length).toBe(0);
    });

    it('gives each row action an accessible name that starts with its visible text', async () => {
      const { el } = await setup(['hr.employee.view']);
      const action = button(el, 'View profile')!;
      expect(action.getAttribute('aria-label')).toBe('View profile for Alex Employee');
    });

    it('searches only through the explicit Search button and Enter key with the trimmed term', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view']);
      const search = control(el, 'Search employees') as HTMLInputElement;
      expect(search.type).toBe('search');
      fill(search, '  Alex  ');
      await settle(fixture);
      expect(service.searchEmployees).toHaveBeenCalledTimes(1);

      button(el, 'Search')!.click();
      await settle(fixture);
      expect(service.searchEmployees).toHaveBeenLastCalledWith('Alex', 1, 25);

      search.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
      await settle(fixture);
      expect(service.searchEmployees).toHaveBeenCalledTimes(3);
    });

    it('pages through the existing server pagination with a fixed 25-row page', async () => {
      const { fixture, service, component, el } = await setup(['hr.employee.view'], {
        searchEmployees: vi.fn().mockReturnValue(of(page([listItem], 60)))
      });
      const size = el.querySelector('select[aria-label="Rows per page"]') as HTMLSelectElement;
      expect(Array.from(size.options).map(option => option.value)).toEqual(['25']);
      expect(el.querySelector('nav[aria-label="Pagination"]')?.textContent).toContain('Page 1 of 3');
      expect((el.querySelector('button[aria-label="Previous page"]') as HTMLButtonElement).disabled).toBe(true);

      (el.querySelector('button[aria-label="Next page"]') as HTMLButtonElement).click();
      await settle(fixture);
      expect(service.searchEmployees).toHaveBeenLastCalledWith('', 2, 25);
      expect(component.page).toBe(2);

      (el.querySelector('button[aria-label="Previous page"]') as HTMLButtonElement).click();
      await settle(fixture);
      expect(service.searchEmployees).toHaveBeenLastCalledWith('', 1, 25);
    });

    it('shows a loading status, then the rows, without a separate stale table', async () => {
      const pending = new Subject<ReturnType<typeof page>>();
      const { fixture, el } = await setup(['hr.employee.view'], {
        searchEmployees: vi.fn().mockReturnValue(pending.asObservable())
      });
      expect(el.querySelector('[role="status"]')?.textContent).toContain('Loading employees...');
      expect(el.textContent).not.toContain('Alex Employee');
      pending.next(page([listItem]));
      await settle(fixture);
      expect(el.querySelector('[role="status"]')).toBeNull();
      expect(el.textContent).toContain('Alex Employee');
    });

    it('announces a load failure once and shows the empty table text', async () => {
      const { el } = await setup(['hr.employee.view'], {
        searchEmployees: vi.fn().mockReturnValue(throwError(() => new Error('boom')))
      });
      const alerts = el.querySelectorAll('[role="alert"]');
      expect(alerts.length).toBe(1);
      expect(alerts[0].textContent).toContain('Unable to load employees. Check HR access and Business scope.');
      expect(el.textContent).toContain('No employees found.');
    });

    it('shows the empty state when no employees exist', async () => {
      const { el } = await setup(['hr.employee.view'], {
        searchEmployees: vi.fn().mockReturnValue(of(page([])))
      });
      expect(el.textContent).toContain('No employees found.');
    });

    it('links to the opening import only with hr.import.view', async () => {
      const without = await setup(['hr.employee.view']);
      expect(without.el.querySelector('a[href="/hr/opening-imports"]')).toBeNull();
      TestBed.resetTestingModule();
      const withImport = await setup(['hr.employee.view', 'hr.import.view']);
      const link = withImport.el.querySelector('a[href="/hr/opening-imports"]');
      expect(link?.textContent).toContain('Opening Employee Import');
    });
  });

  describe('create and edit form', () => {
    it('opens the create form only for hr.employee.create, with labelled required controls', async () => {
      const { fixture, el } = await setup(['hr.employee.view', 'hr.employee.create']);
      button(el, 'New employee')!.click();
      await settle(fixture);
      expect(el.querySelector('h2')?.textContent).toContain('Create employee');
      expect(button(el, 'New employee')).toBeUndefined();
      expect(el.querySelector('app-ui-data-table')).toBeNull();
      for (const label of ['Employee code', 'First name', 'Display name']) {
        const field = control(el, label);
        expect(field.hasAttribute('required'), label).toBe(true);
        expect(field.getAttribute('aria-required'), label).toBe('true');
      }
      expect(control(el, 'Work email').hasAttribute('required')).toBe(false);
      expect(el.querySelectorAll('.ui-stepper__item')).toHaveLength(2);
      expect(el.querySelector('.ui-stepper')?.getAttribute('data-orientation')).toBe('vertical');
      expect(el.querySelectorAll('.ui-field[data-appearance="filled"]').length).toBeGreaterThan(0);
      expect(el.querySelector('app-ui-date-picker')).toBeNull();
      expect(el.textContent).not.toContain('Restricted personal information');
      expect(el.textContent).not.toContain('Restricted identity information');

      button(el, 'Next')!.click();
      await settle(fixture);
      expect(el.textContent).toContain('Enter the employee code');
      fill(control(el, 'Employee code'), 'EMP-1');
      fill(control(el, 'First name'), 'Alex');
      fill(control(el, 'Display name'), 'Alex Employee');
      button(el, 'Next')!.click();
      await settle(fixture);
      expect(el.querySelector('.ui-stepper__item[data-state="active"]')?.textContent).toContain('Employment details');
      expect(control(el, 'Employment type').getAttribute('aria-required')).toBe('true');
      expect((control(el, 'Joining date') as HTMLInputElement).placeholder).toBe('dd/MM/yyyy');
      expect(el.querySelector('app-ui-date-picker')).toBeTruthy();
    });

    it('shows the restricted fieldsets only with the matching permissions', async () => {
      const { fixture, el } = await setup([
        'hr.employee.view', 'hr.employee.create', 'hr.employee.view_sensitive']);
      button(el, 'New employee')!.click();
      await settle(fixture);
      fill(control(el, 'Employee code'), 'EMP-2');
      fill(control(el, 'First name'), 'Alex');
      fill(control(el, 'Display name'), 'Alex Employee');
      button(el, 'Next')!.click();
      await settle(fixture);
      expect(Array.from(el.querySelectorAll('legend')).map(item => item.textContent))
        .toEqual(['Restricted personal information']);
      expect(control(el, 'Mobile')).toBeTruthy();
      TestBed.resetTestingModule();

      const identity = await setup(['hr.employee.view', 'hr.employee.create', 'hr.employee.view_identity']);
      button(identity.el, 'New employee')!.click();
      await settle(identity.fixture);
      fill(control(identity.el, 'Employee code'), 'EMP-3');
      fill(control(identity.el, 'First name'), 'Alex');
      fill(control(identity.el, 'Display name'), 'Alex Employee');
      button(identity.el, 'Next')!.click();
      await settle(identity.fixture);
      expect(Array.from(identity.el.querySelectorAll('legend')).map(item => item.textContent))
        .toEqual(['Restricted identity information']);
      expect(control(identity.el, 'National ID')).toBeTruthy();
    });

    it('accepts a company-formatted joining date and submits ISO without changing other values', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view', 'hr.employee.create']);
      button(el, 'New employee')!.click();
      await settle(fixture);
      fill(control(el, 'Employee code'), ' EMP-9 ');
      fill(control(el, 'First name'), 'Sam');
      fill(control(el, 'Display name'), 'Sam Smith');
      button(el, 'Next')!.click();
      await settle(fixture);
      fill(control(el, 'Employment type'), '11');
      const joiningDate = control(el, 'Joining date') as HTMLInputElement;
      joiningDate.value = '05/10/2026';
      joiningDate.dispatchEvent(new Event('input'));
      await settle(fixture);
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await settle(fixture);
      expect(service.createEmployee).toHaveBeenCalledWith(expect.objectContaining({
        employeeCode: 'EMP-9', firstName: 'Sam', displayName: 'Sam Smith', middleName: null,
        employmentTypeId: 11, employmentStatus: 'Active', joiningDate: '2026-10-05'
      }));
      expect(el.querySelector('form')).toBeNull();
      expect(el.querySelector('app-ui-data-table')).toBeTruthy();
    });

    it('cancels back to the list without calling the API', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view', 'hr.employee.create']);
      button(el, 'New employee')!.click();
      await settle(fixture);
      button(el, 'Cancel')!.click();
      await settle(fixture);
      expect(service.createEmployee).not.toHaveBeenCalled();
      expect(el.querySelector('form')).toBeNull();
      expect(button(el, 'New employee')).toBeTruthy();
    });

    it('edits the open profile with the code hidden and sends the update', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view', 'hr.employee.edit', 'hr.employee.view_sensitive']);
      await openProfile(fixture, el);
      button(el, 'Edit Employee')!.click();
      await settle(fixture);
      expect(el.querySelector('h2')?.textContent).toContain('Edit employee');
      expect(Array.from(el.querySelectorAll('label')).some(item => item.textContent?.includes('Employee code'))).toBe(false);
      expect((control(el, 'First name') as HTMLInputElement).value).toBe('Alex');
      expect(el.querySelectorAll('.ui-stepper__item')).toHaveLength(2);
      button(el, 'Next')!.click();
      await settle(fixture);
      const dateOfBirth = control(el, 'Date of birth') as HTMLInputElement;
      expect(dateOfBirth.placeholder).toBe('dd/MM/yyyy');
      expect(dateOfBirth.value).toBe('02/01/1990');
      dateOfBirth.value = '07/01/1990';
      dateOfBirth.dispatchEvent(new Event('input'));
      await settle(fixture);
      button(el, 'Previous')!.click();
      await settle(fixture);
      fill(control(el, 'First name'), 'Alexander');
      await settle(fixture);
      button(el, 'Next')!.click();
      await settle(fixture);
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await settle(fixture);
      expect(service.updateEmployee).toHaveBeenCalledWith(1, 3, expect.objectContaining({
        firstName: 'Alexander', dateOfBirth: '1990-01-07'
      }));
    });

    it('offers a permission-gated edit icon directly from the employee list', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view', 'hr.employee.edit']);
      const edit = el.querySelector<HTMLButtonElement>('[aria-label="Edit employee Alex Employee"]');
      expect(edit?.getAttribute('data-action')).toBe('edit');
      edit!.click();
      await settle(fixture);
      expect(service.getEmployee).toHaveBeenCalledWith(1);
      expect(el.querySelector('h2')?.textContent).toContain('Edit employee');
      expect(control(el, 'First name')).toBeTruthy();

      TestBed.resetTestingModule();
      const noEdit = await setup(['hr.employee.view']);
      expect(noEdit.el.querySelector('[aria-label="Edit employee Alex Employee"]')).toBeNull();
    });
  });

  describe('profile', () => {
    it('loads a directly opened employee view from its route ID and returns to the list state', async () => {
      const { fixture, service, el } = await setup(
        ['hr.employee.view'],
        {
          getEmployee: vi.fn().mockReturnValue(of({ ...profile, id: 42 })),
          searchEmployees: vi.fn().mockReturnValue(of(page([listItem], 60)))
        },
        '/hr/employee/42/view?search=Alex&page=3'
      );
      expect(service.getEmployee).toHaveBeenCalledWith(42);
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Alex Employee');

      button(el, 'Back to Employees')!.click();
      await settle(fixture);
      expect(TestBed.inject(Router).url).toBe('/hr/employees?search=Alex&page=3');
      expect(el.querySelector('app-ui-data-table')).toBeTruthy();
      expect((control(el, 'Search employees') as HTMLInputElement).value).toBe('Alex');
    });

    it('loads route changes by the new ID and ignores a stale response from the prior employee', async () => {
      const firstResponse = new Subject<typeof profile>();
      const secondResponse = new Subject<typeof profile>();
      const { fixture, service, el } = await setup(
        ['hr.employee.view'],
        { getEmployee: vi.fn((id: number) => id === 1 ? firstResponse : secondResponse) },
        '/hr/employee/1/view'
      );
      await TestBed.inject(Router).navigateByUrl('/hr/employee/2/view');
      await settle(fixture);
      expect(service.getEmployee).toHaveBeenNthCalledWith(2, 2);
      secondResponse.next({ ...profile, id: 2, displayName: 'Second Employee' });
      await settle(fixture);
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Second Employee');
      firstResponse.next({ ...profile, id: 1, displayName: 'Stale Employee' });
      await settle(fixture);
      expect(el.textContent).not.toContain('Stale Employee');
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Second Employee');
    });

    it('does not request malformed employee IDs and shows not-found state', async () => {
      const { service, el } = await setup(
        ['hr.employee.view'],
        {},
        '/hr/employee/not-an-id/view'
      );
      expect(service.getEmployee).not.toHaveBeenCalled();
      expect(el.textContent).toContain('Employee not found');
      expect(el.textContent).not.toContain('Restricted personal information');
    });

    it('shows a scoped authorization error without exposing details or navigating out', async () => {
      const { service, el } = await setup(
        ['hr.employee.view'],
        { getEmployee: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 }))) },
        '/hr/employee/1/view'
      );
      expect(service.getEmployee).toHaveBeenCalledWith(1);
      expect(el.textContent).toContain('not authorized');
      expect(el.textContent).not.toContain('NID-998877');
      expect(TestBed.inject(Router).url).toBe('/hr/employee/1/view');
    });

    it('loads a directly opened edit page from the route ID without requiring prior list selection', async () => {
      const { service, el } = await setup(
        ['hr.employee.view', 'hr.employee.edit'],
        { getEmployee: vi.fn().mockReturnValue(of({ ...profile, id: 42 })) },
        '/hr/employee/42/edit'
      );
      expect(service.getEmployee).toHaveBeenCalledWith(42);
      expect(control(el, 'First name')).toBeTruthy();
      expect(el.textContent).not.toContain('Restricted personal information');
      expect(el.querySelector('app-ui-data-table')).toBeNull();
    });

    it('cancels an edit back to that employee view without submitting changes', async () => {
      const { fixture, service, el } = await setup(
        ['hr.employee.view', 'hr.employee.edit'],
        {},
        '/hr/employee/1/edit'
      );
      button(el, 'Cancel')!.click();
      await settle(fixture);
      expect(TestBed.inject(Router).url).toBe('/hr/employee/1/view');
      expect(service.updateEmployee).not.toHaveBeenCalled();
      expect(el.querySelector('form')).toBeNull();
      expect(el.querySelector('.hr-profile-stepper')).toBeTruthy();
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Alex Employee');
    });

    it('prevents duplicate create submissions while the first save is pending', async () => {
      const pending = new Subject<unknown>();
      const { fixture, service, el } = await setup(
        ['hr.employee.view', 'hr.employee.create'],
        { createEmployee: vi.fn().mockReturnValue(pending) }
      );
      button(el, 'New employee')!.click();
      await settle(fixture);
      fill(control(el, 'Employee code'), 'EMP-12');
      fill(control(el, 'First name'), 'Taylor');
      fill(control(el, 'Display name'), 'Taylor Employee');
      button(el, 'Next')!.click();
      await settle(fixture);
      fill(control(el, 'Employment type'), '11');
      button(el, 'Create employee')!.click();
      await settle(fixture);
      expect(service.createEmployee).toHaveBeenCalledTimes(1);
      expect((button(el, 'Create employee') as HTMLButtonElement).disabled).toBe(true);
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      expect(service.createEmployee).toHaveBeenCalledTimes(1);
      pending.next(profile);
      pending.complete();
      await settle(fixture);
      expect(TestBed.inject(Router).url).toBe('/hr/employees');
    });

    it('shows the summary as a card with a definition list and closes it', async () => {
      const { fixture, el } = await setup(['hr.employee.view']);
      await openProfile(fixture, el);
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Alex Employee');
      const terms = Array.from(el.querySelectorAll('dt')).map(item => item.textContent);
      expect(terms).toEqual([
        'Joining date', 'Confirmation date', 'Work email', 'Position',
        'Department / Team', 'Reports to', 'Primary work location'
      ]);
      expect(el.querySelector('dl')?.textContent).toContain("01/10/2026");
      expect(button(el, 'Edit Employee')).toBeUndefined();

      button(el, 'Back to Employees')!.click();
      await settle(fixture);
      expect(el.querySelector('dl')).toBeNull();
      expect(el.querySelector('app-ui-card h2')).toBeNull();
    });

    it('presents the authorized profile sections in the vertical stepper', async () => {
      const permissions = [
        'hr.employee.view', 'hr.employee.view_sensitive', 'hr.employee.view_identity',
        'hr.employee_lifecycle.view', 'hr.employee_transfer.view', 'hr.position.view',
        'hr.employee_document.view'
      ];
      const { fixture, el } = await setup(permissions);
      await openProfile(fixture, el);
      const steps = Array.from(el.querySelectorAll('.hr-profile-stepper .ui-stepper__title'))
        .map(step => step.textContent?.trim());
      expect(steps).toEqual([
        'Employee overview', 'Personal information', 'Login User link', 'Employment and lifecycle',
        'Work locations', 'Position and manager', 'Employee documents'
      ]);
      expect(el.querySelector('.hr-profile-stepper .ui-stepper')?.getAttribute('data-orientation')).toBe('vertical');
    });

    it('hides every confidential field without the sensitive and identity permissions', async () => {
      const { fixture, el } = await setup(['hr.employee.view']);
      await openProfile(fixture, el);
      expect(el.textContent).not.toContain('Restricted personal information');
      expect(el.textContent).not.toContain('Restricted identity information');
      expect(el.textContent).not.toContain('1990-01-02');
      expect(el.textContent).not.toContain('555-0100');
      expect(el.textContent).not.toContain('NID-998877');
      expect(el.textContent).not.toContain('P-445566');
      expect(el.querySelector('app-employee-documents')).toBeNull();
    });

    it('shows personal and identity cards independently', async () => {
      const personal = await setup(['hr.employee.view', 'hr.employee.view_sensitive']);
      await openProfile(personal.fixture, personal.el);
      await openProfileSection(personal.fixture, personal.el, 'Personal information');
      expect(personal.el.textContent).toContain('Restricted personal information');
      expect(personal.el.textContent).toContain('555-0100');
      expect(personal.el.textContent).not.toContain('NID-998877');
      TestBed.resetTestingModule();

      const identity = await setup(['hr.employee.view', 'hr.employee.view_identity']);
      await openProfile(identity.fixture, identity.el);
      await openProfileSection(identity.fixture, identity.el, 'Personal information');
      expect(identity.el.textContent).toContain('Restricted identity information');
      expect(identity.el.textContent).toContain('NID-998877');
      expect(identity.el.textContent).toContain('P-445566');
      expect(identity.el.textContent).not.toContain('555-0100');
    });

    it('embeds the documents card only with hr.employee_document.view', async () => {
      const { fixture, el } = await setup(['hr.employee.view', 'hr.employee_document.view']);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Employee documents');
      const http = TestBed.inject(HttpTestingController);
      http.match(request => request.url.endsWith('/employee-document-types')).forEach(request => request.flush([]));
      http.match(request => request.url.endsWith('/employees/1/documents')).forEach(request => request.flush([]));
      await settle(fixture);
      const documents = el.querySelector('app-employee-documents');
      expect(documents?.querySelector('h3')?.textContent).toContain('Employee documents');
      expect(documents?.textContent).toContain('No employee documents.');
    });

    it('links a Login User through labelled fields and trims the reason', async () => {
      const { fixture, service, el } = await setup(['hr.employee.view', 'hr.employee.link_user']);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Login User link');
      expect(el.textContent).toContain('No active Login User link.');
      fill(control(el, 'Existing Login User ID'), '5');
      fill(control(el, 'Reason'), '  Because  ');
      await settle(fixture);
      const form = Array.from(el.querySelectorAll('form')).find(item => item.textContent?.includes('Link existing Login User'))!;
      form.dispatchEvent(new Event('submit'));
      await settle(fixture);
      expect(service.linkEmployeeUser).toHaveBeenCalledWith(1, 5, 'Because');
      expect(el.textContent).toContain('Linked Login User ID: 5');
    });

    it('offers unlink only with hr.employee.unlink_user when a link exists', async () => {
      const link = { id: 10, identityUserId: 101, linkedAtUtc: '2026-10-01T00:00:00Z', linkReason: 'Explicit', version: 4 };
      const allowed = await setup(['hr.employee.view', 'hr.employee.unlink_user'], {
        getEmployeeUserLink: vi.fn().mockReturnValue(of(link))
      });
      await openProfile(allowed.fixture, allowed.el);
      await openProfileSection(allowed.fixture, allowed.el, 'Login User link');
      expect(button(allowed.el, 'Unlink Login User')).toBeTruthy();
      fill(control(allowed.el, 'Unlink reason'), 'Left company');
      await settle(allowed.fixture);
      Array.from(allowed.el.querySelectorAll('form'))
        .find(item => item.textContent?.includes('Unlink Login User'))!.dispatchEvent(new Event('submit'));
      await settle(allowed.fixture);
      expect(allowed.service.unlinkEmployeeUser).toHaveBeenCalledWith(1, 4, 'Left company');
      TestBed.resetTestingModule();

      const denied = await setup(['hr.employee.view'], { getEmployeeUserLink: vi.fn().mockReturnValue(of(link)) });
      await openProfile(denied.fixture, denied.el);
      await openProfileSection(denied.fixture, denied.el, 'Login User link');
      expect(denied.el.textContent).toContain('Linked Login User ID: 101');
      expect(button(denied.el, 'Unlink Login User')).toBeUndefined();
    });
  });

  describe('lifecycle', () => {
    const lifecycleEvent = {
      id: 1, employeeId: 1, employmentPeriodId: 1, eventType: 'Confirmed', effectiveDate: '2026-09-01',
      previousStateJson: '{}', newStateJson: '{}', reason: 'Probation passed', actorUserId: 9,
      occurredAtUtc: '2026-09-01T10:00:00Z'
    };
    const permissions = ['hr.employee.view', 'hr.employee_lifecycle.view', 'hr.employee_lifecycle.manage'];

    it('lists history in a named table and refreshes it from the button', async () => {
      const { fixture, service, el } = await setup(permissions, {
        getEmployeeLifecycle: vi.fn().mockReturnValue(of([lifecycleEvent]))
      });
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Employment and lifecycle');
      const region = el.querySelector('[role="region"][aria-label="Employee lifecycle history"]')!;
      expect(region.textContent).toContain('Confirmed');
      expect(region.textContent).toContain('Probation passed');
      expect(region.textContent).toContain("01/09/2026");
      const calls = service.getEmployeeLifecycle.mock.calls.length;
      button(el, 'Refresh history')!.click();
      await settle(fixture);
      expect(service.getEmployeeLifecycle.mock.calls.length).toBe(calls + 1);
    });

    it('shows the empty message when there are no events', async () => {
      const { fixture, el } = await setup(permissions);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Employment and lifecycle');
      expect(el.textContent).toContain('No lifecycle events recorded.');
    });

    it('submits a lifecycle action with the employee version', async () => {
      const { fixture, service, el } = await setup(permissions);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Employment and lifecycle');
      fill(control(el, 'Action'), 'Suspended');
      fill(control(el, 'Reason'), 'Policy review');
      await settle(fixture);
      Array.from(el.querySelectorAll('form'))
        .find(item => item.textContent?.includes('Save lifecycle action'))!.dispatchEvent(new Event('submit'));
      await settle(fixture);
      expect(service.transitionEmployeeLifecycle).toHaveBeenCalledWith(1, 3, expect.objectContaining({
        eventType: 'Suspended', reason: 'Policy review'
      }));
    });

    it('swaps the action form for the rehire form once the employee has exited', async () => {
      const exited = { ...profile, currentEmploymentStatus: 'Exited' };
      const rehire = await setup([...permissions, 'hr.employee_lifecycle.rehire'], {
        getEmployee: vi.fn().mockReturnValue(of(exited))
      });
      await openProfile(rehire.fixture, rehire.el);
      await openProfileSection(rehire.fixture, rehire.el, 'Employment and lifecycle');
      expect(button(rehire.el, 'Rehire employee')).toBeTruthy();
      expect(button(rehire.el, 'Save lifecycle action')).toBeUndefined();
      TestBed.resetTestingModule();

      const noRehire = await setup(permissions, { getEmployee: vi.fn().mockReturnValue(of(exited)) });
      await openProfile(noRehire.fixture, noRehire.el);
      await openProfileSection(noRehire.fixture, noRehire.el, 'Employment and lifecycle');
      expect(button(noRehire.el, 'Rehire employee')).toBeUndefined();
      expect(button(noRehire.el, 'Save lifecycle action')).toBeUndefined();
    });

    it('hides the whole section without hr.employee_lifecycle.view', async () => {
      const { fixture, el } = await setup(['hr.employee.view']);
      await openProfile(fixture, el);
      expect(el.textContent).not.toContain('Lifecycle history');
    });
  });

  describe('operational location assignments', () => {
    const assignment = {
      id: 3, employeeId: 1, operationalLocationProjectionId: 5, locationName: 'Main Outlet',
      sourceSystem: 'Business', sourceEntityType: 'Outlet', sourceEntityId: '5', locationSourceVersion: 1,
      assignmentType: 'Primary', isTemporary: false, effectiveFrom: '2026-01-01', effectiveTo: null,
      status: 'Effective', reason: 'Initial', requestedByUserId: 1, requestedAtUtc: '2026-01-01T00:00:00Z',
      approvedByUserId: null, approvedAtUtc: null, approvalInstanceId: null, version: 1
    };
    const base = ['hr.employee.view', 'hr.employee_transfer.view'];

    it('renders the history table with status text and the current primary location', async () => {
      const { fixture, el } = await setup(base, {
        getEmployeeLocationHistory: vi.fn().mockReturnValue(of([assignment]))
      });
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Work locations');
      const region = el.querySelector('[role="region"][aria-label="Operational location assignment history"]')!;
      expect(region.textContent).toContain('Main Outlet · Outlet');
      expect(region.textContent).toContain("01/01/2026 – Open");
      expect(region.querySelector('app-ui-badge')?.textContent).toContain('Effective');
      expect(el.textContent).toContain('Current Primary location: Main Outlet');
    });

    it('shows the empty message when there is no visible history', async () => {
      const { fixture, el } = await setup(base);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Work locations');
      expect(el.textContent).toContain('No visible location assignment history.');
      expect(el.textContent).toContain('Current Primary location: Not assigned');
    });

    it('gates approve and reject by hr.employee_transfer.approve and cancel by create', async () => {
      const transfers = { getEmployeeLocationTransfers: vi.fn().mockReturnValue(of([pendingTransfer])) };
      const approver = await setup([...base, 'hr.employee_transfer.approve'], transfers);
      await openProfile(approver.fixture, approver.el);
      await openProfileSection(approver.fixture, approver.el, 'Work locations');
      expect(approver.el.textContent).toContain('Primary transfer to Main Outlet');
      expect(approver.el.textContent).toContain('PendingApproval');
      expect(button(approver.el, 'Approve')).toBeTruthy();
      expect(button(approver.el, 'Reject')).toBeTruthy();
      expect(button(approver.el, 'Cancel request')).toBeUndefined();
      TestBed.resetTestingModule();

      const requester = await setup([...base, 'hr.employee_transfer.create'], transfers);
      await openProfile(requester.fixture, requester.el);
      await openProfileSection(requester.fixture, requester.el, 'Work locations');
      expect(button(requester.el, 'Approve')).toBeUndefined();
      expect(button(requester.el, 'Cancel request')).toBeTruthy();
    });

    it('requires an action reason before a decision is sent', async () => {
      const { fixture, service, el } = await setup([...base, 'hr.employee_transfer.approve'], {
        getEmployeeLocationTransfers: vi.fn().mockReturnValue(of([pendingTransfer]))
      });
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Work locations');
      button(el, 'Approve')!.click();
      await settle(fixture);
      expect(service.decideEmployeeLocationTransfer).not.toHaveBeenCalled();
      fill(control(el, 'Action reason'), 'Looks right');
      await settle(fixture);
      button(el, 'Approve')!.click();
      await settle(fixture);
      expect(service.decideEmployeeLocationTransfer).toHaveBeenCalledWith(
        1, 'tr-1', expect.any(String), 'Looks right', true);
    });

    it('offers the assignment forms only with create permission and an active employee', async () => {
      const create = await setup([...base, 'hr.employee_transfer.create'], {
        getEmployeeLocationHistory: vi.fn().mockReturnValue(of([assignment]))
      });
      await openProfile(create.fixture, create.el);
      await openProfileSection(create.fixture, create.el, 'Work locations');
      expect(button(create.el, 'Save location assignment')).toBeTruthy();
      expect(button(create.el, 'Request transfer approval')).toBeTruthy();
      TestBed.resetTestingModule();

      const suspended = await setup([...base, 'hr.employee_transfer.create'], {
        getEmployee: vi.fn().mockReturnValue(of({ ...profile, currentEmploymentStatus: 'Suspended' }))
      });
      await openProfile(suspended.fixture, suspended.el);
      await openProfileSection(suspended.fixture, suspended.el, 'Work locations');
      expect(button(suspended.el, 'Save location assignment')).toBeUndefined();
      TestBed.resetTestingModule();

      const readOnly = await setup(base);
      await openProfile(readOnly.fixture, readOnly.el);
      await openProfileSection(readOnly.fixture, readOnly.el, 'Work locations');
      expect(button(readOnly.el, 'Save location assignment')).toBeUndefined();
    });

    it('adds the temporary end date field only for temporary assignments', async () => {
      const { fixture, el } = await setup([...base, 'hr.employee_transfer.create']);
      await openProfile(fixture, el);
      await openProfileSection(fixture, el, 'Work locations');
      expect(Array.from(el.querySelectorAll('label')).some(item => item.textContent?.includes('Effective to'))).toBe(false);
      fill(control(el, 'Assignment type'), 'Temporary');
      await settle(fixture);
      expect(control(el, 'Effective to').hasAttribute('required')).toBe(true);
    });
  });

  describe('position and manager history', () => {
    it('renders the history table and gates the assignment form by both position permissions', async () => {
      const history = [{
        id: 1, employeeId: 1, positionId: 2, positionCode: 'P2', positionName: 'Cashier', orgUnitId: 1,
        orgUnitName: 'Front', reportsToEmployeeId: null, reportsToEmployeeName: null,
        assignmentType: 'Primary', effectiveFrom: '2026-02-01', effectiveTo: null, reason: null,
        isCurrent: true, version: 1
      }];
      const manage = await setup(['hr.employee.view', 'hr.position.view', 'hr.position.manage'], {
        getEmployeePositionHistory: vi.fn().mockReturnValue(of(history))
      });
      await openProfile(manage.fixture, manage.el);
      await openProfileSection(manage.fixture, manage.el, 'Position and manager');
      const region = manage.el.querySelector('[role="region"][aria-label="Position and manager history"]')!;
      expect(region.textContent).toContain('Cashier');
      expect(region.textContent).toContain("01/02/2026 – Current");
      expect(button(manage.el, 'Save position assignment')).toBeTruthy();
      TestBed.resetTestingModule();

      const viewOnly = await setup(['hr.employee.view', 'hr.position.view'], {
        getEmployeePositionHistory: vi.fn().mockReturnValue(of(history))
      });
      await openProfile(viewOnly.fixture, viewOnly.el);
      await openProfileSection(viewOnly.fixture, viewOnly.el, 'Position and manager');
      expect(viewOnly.el.textContent).toContain('Cashier');
      expect(button(viewOnly.el, 'Save position assignment')).toBeUndefined();
    });
  });

  it('keeps the profile errors announced through a single alert', async () => {
    const failing: Observable<never> = throwError(() => new Error('no'));
    const { fixture, el } = await setup(['hr.employee.view'], { getEmployee: vi.fn().mockReturnValue(failing) });
    await openProfile(fixture, el);
    const alerts = el.querySelectorAll('[role="alert"]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('Unable to load employee profile');
  });
});
