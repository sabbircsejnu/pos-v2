import { routes } from '../../../app.routes';
import { HrEmployeesPageComponent } from './hr-employees-page.component';

describe('HR employee routes', () => {
  it('exposes dedicated list, create, view and edit routes with matching permissions', () => {
    const employeeRoutes = routes.find(route => route.children?.some(
      child => child.component === HrEmployeesPageComponent
    ))?.children?.filter(route => route.component === HrEmployeesPageComponent) ?? [];
    const expected = new Map([
      ['hr/employees', ['hr.employee.view']],
      ['hr/employee/new', ['hr.employee.create']],
      ['hr/employee/:employeeId/view', ['hr.employee.view']],
      ['hr/employee/:employeeId/edit', ['hr.employee.view', 'hr.employee.edit']]
    ]);

    expect(employeeRoutes.map(route => route.path)).toEqual(Array.from(expected.keys()));
    for (const route of employeeRoutes) {
      expect(route.data?.['permissions']).toEqual(expected.get(route.path ?? ''));
      if (route.path === 'hr/employee/:employeeId/edit') {
        expect(route.data?.['permissionMode']).toBe('all');
      }
      expect(route.canActivate?.length).toBe(2);
    }
  });
});
