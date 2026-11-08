import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RoleService } from '../../services/role.service';
import { PERMISSION_DETAILS, RoleFormComponent } from './role-form.component';

const exchangeKeys = [
  'pos.exchange.create', 'pos.exchange.refund', 'pos.exchange.override_window', 'pos.exchange.override_chain', 'pos.exchange.reverse'
];

describe('RoleFormComponent Product Exchange permissions', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RoleFormComponent],
      providers: [
        provideRouter([]),
        {
          provide: RoleService,
          useValue: {
            getAllPermissions: vi.fn(() => of([...exchangeKeys, 'sales.view'])),
            getRoleById: vi.fn(() => of({ id: 3, name: 'Manager', permissions: [] })),
            updateRole: vi.fn(() => of({})),
            createRole: vi.fn(() => of({}))
          }
        }
      ]
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('offers exactly the five exchange permissions in a "Product Exchange" group', () => {
    const fixture = TestBed.createComponent(RoleFormComponent);
    fixture.detectChanges();
    const groups = new Map(fixture.componentInstance.visiblePermissionCategories.map(category => [category.name, category.permissions]));

    expect(groups.get('Product Exchange')).toEqual(exchangeKeys);
    const allExchange = fixture.componentInstance.permissionCategories
      .flatMap(category => category.permissions)
      .filter(key => key.startsWith('pos.exchange.'));
    expect(allExchange).toEqual(exchangeKeys);
    expect(allExchange).not.toContain('pos.exchange.view');
    expect(allExchange).not.toContain('pos.exchange.approve_high_value');
  });

  it('describes the window override as covering high-value approval', () => {
    expect(PERMISSION_DETAILS['pos.exchange.override_window'].label).toBe('Approve expired windows and high-value exchanges');
    exchangeKeys.forEach(key => expect(PERMISSION_DETAILS[key]).toBeDefined());
  });
});
