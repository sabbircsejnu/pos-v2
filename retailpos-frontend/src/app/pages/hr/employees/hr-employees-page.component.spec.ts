import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { HrEmployeesPageComponent } from './hr-employees-page.component';
import { HrOrganisationService } from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';

describe('HrEmployeesPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows only fields allowed by the caller permissions', async () => {
    const service = {
      getEmploymentTypes: vi.fn().mockReturnValue(of([])),
      searchEmployees: vi.fn().mockReturnValue(of({
        items: [{
          id: 1,
          employeeCode: 'EMP-1',
          firstName: 'Alex',
          middleName: null,
          lastName: 'Employee',
          displayName: 'Alex Employee',
          workEmail: null,
          currentEmploymentStatus: 'Active',
          currentEmploymentTypeId: 11,
          currentEmploymentTypeName: 'Permanent',
          joiningDate: '2026-10-01',
          version: 1
        }],
        page: 1,
        pageSize: 25,
        totalCount: 1
      })),
      getEmployee: vi.fn(),
      createEmployee: vi.fn(),
      updateEmployee: vi.fn()
    };
    const auth = {
      hasPermission: (permission: string) =>
        ['hr.employee.view', 'hr.employee.view_sensitive'].includes(permission)
    };
    await TestBed.configureTestingModule({
      imports: [HrEmployeesPageComponent],
      providers: [
        provideRouter([
          { path: 'hr/employees', component: HrEmployeesPageComponent },
          { path: 'hr/employee/:employeeId/view', component: HrEmployeesPageComponent }
        ]),
        { provide: HrOrganisationService, useValue: service },
        { provide: AuthService, useValue: auth }
      ]
    }).compileComponents();

    const harness = await RouterTestingHarness.create('/hr/employees');
    const fixture = harness.fixture;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Alex Employee');
    expect(fixture.nativeElement.textContent).not.toContain('National ID');
    expect(fixture.nativeElement.textContent).not.toContain('New employee');
    expect(service.searchEmployees).toHaveBeenCalledWith('', 1, 25);
  });

  it('shows linked Login User state and gates unlink controls by permission', async () => {
    const employee = {
      id: 1,
      employeeCode: 'EMP-1',
      firstName: 'Alex',
      middleName: null,
      lastName: 'Employee',
      displayName: 'Alex Employee',
      workEmail: null,
      currentEmploymentStatus: 'Active',
      currentEmploymentTypeId: 11,
      currentEmploymentTypeName: 'Permanent',
      joiningDate: '2026-10-01',
      currentPositionId: null,
      currentOrgUnitId: null,
      confirmationDate: null,
      version: 1,
      sensitiveFields: {
        dateOfBirth: null, genderCode: null, personalEmail: null, mobile: null,
        presentAddress: null, permanentAddress: null, emergencyContactName: null,
        emergencyContactPhone: null, nationalId: null, passportNo: null
      }
    };
    const permissions = ['hr.employee.view', 'hr.employee.link_user'];
    const service = {
      getEmploymentTypes: vi.fn().mockReturnValue(of([])),
      searchEmployees: vi.fn().mockReturnValue(of({
        items: [employee],
        page: 1,
        pageSize: 25,
        totalCount: 1
      })),
      getEmployee: vi.fn().mockReturnValue(of(employee)),
      getEmployeeUserLink: vi.fn().mockReturnValue(of({
        id: 10, employeeId: 1, identityUserId: 101, linkedAtUtc: '2026-10-01T00:00:00Z',
        linkedByUserId: 101, linkReason: 'Explicit link.', unlinkedAtUtc: null,
        unlinkedByUserId: null, unlinkReason: null, isActive: true, version: 1
      })),
      getEmployeeLifecycle: vi.fn().mockReturnValue(of([])),
      getEmployeePositionHistory: vi.fn().mockReturnValue(of([]))
    };
    const auth = { hasPermission: (permission: string) => permissions.includes(permission) };
    await TestBed.configureTestingModule({
      imports: [HrEmployeesPageComponent],
      providers: [
        provideRouter([
          { path: 'hr/employees', component: HrEmployeesPageComponent },
          { path: 'hr/employee/:employeeId/view', component: HrEmployeesPageComponent }
        ]),
        { provide: HrOrganisationService, useValue: service },
        { provide: AuthService, useValue: auth }
      ]
    }).compileComponents();

    const harness = await RouterTestingHarness.create('/hr/employees');
    const fixture = harness.fixture;
    fixture.detectChanges();
    const profileButton = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>
    ).find(button => button.textContent?.includes('View profile'));
    profileButton?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const loginLinkStep = Array.from(
      fixture.nativeElement.querySelectorAll('button.ui-stepper__header') as NodeListOf<HTMLButtonElement>
    ).find(button => button.textContent?.includes('Login User link'));
    loginLinkStep?.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Linked Login User ID: 101');
    expect(fixture.nativeElement.textContent).not.toContain('Unlink Login User');
    expect(fixture.nativeElement.textContent).not.toContain('Link existing Login User');
  });
});
