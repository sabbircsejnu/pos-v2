import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { CustomerDetailsComponent } from './customer-details.component';
import { CustomerService } from '../../services/customer.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';

const CUSTOMER = { id: 5, name: 'Rahim', phone: '0170', email: 'r@x.test', loyaltyPoints: 120, createdAt: '2026-01-01T00:00:00Z' };

describe('CustomerDetailsComponent legacy points', () => {
  let fixture: ComponentFixture<CustomerDetailsComponent>;
  const enabled = signal(false);
  const customerService = {
    getById: vi.fn(),
    getLoyaltyCapabilities: vi.fn(),
    addPoints: vi.fn(),
    legacyPointsMutationEnabled: enabled
  };
  const alerts = { success: vi.fn(), error: vi.fn() };

  async function setup(permissions: string[], flag: boolean) {
    enabled.set(flag);
    customerService.getById.mockReturnValue(of({ data: CUSTOMER }));
    customerService.getLoyaltyCapabilities.mockReturnValue(of({ legacyPointsMutationEnabled: flag }));
    TestBed.configureTestingModule({
      imports: [CustomerDetailsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CustomerService, useValue: customerService },
        { provide: AlertService, useValue: alerts },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '5' } } } },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } }
      ]
    });
    fixture = TestBed.createComponent(CustomerDetailsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const addButton = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
    .find(button => button.textContent?.includes('Add Points'));

  beforeEach(() => {
    Object.values(customerService).forEach(value => typeof value === 'function' && 'mockReset' in value && value.mockReset());
    alerts.success.mockReset();
    alerts.error.mockReset();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('requests the capabilities and shows the balance as read-only legacy data', async () => {
    await setup(['customers.view'], false);

    expect(customerService.getLoyaltyCapabilities).toHaveBeenCalledTimes(1);
    expect(text()).toContain('Legacy points (read-only)');
    expect(text()).toContain('120');
  });

  it('shows Add Points when the flag is enabled and the user has customers.edit', async () => {
    await setup(['customers.view', 'customers.edit'], true);
    expect(addButton()).toBeDefined();
  });

  it('hides Add Points (not just disables it) when the legacy flag is off', async () => {
    await setup(['customers.view', 'customers.edit'], false);
    expect(addButton()).toBeUndefined();
    expect(text()).toContain('120');
  });

  it('hides Add Points without customers.edit even when the flag is on', async () => {
    await setup(['customers.view'], true);
    expect(addButton()).toBeUndefined();
  });

  it('does not call the legacy endpoint when the action is not allowed', async () => {
    await setup(['customers.view', 'customers.edit'], false);
    fixture.componentInstance.pointsToAdd.set(10);
    fixture.componentInstance.onAddPoints();
    expect(customerService.addPoints).not.toHaveBeenCalled();
  });

  it('still adds points through the legacy endpoint when allowed', async () => {
    await setup(['customers.view', 'customers.edit'], true);
    customerService.addPoints.mockReturnValue(of({ data: { ...CUSTOMER, loyaltyPoints: 130 } }));
    fixture.componentInstance.pointsToAdd.set(10);
    fixture.componentInstance.onAddPoints();

    expect(customerService.addPoints).toHaveBeenCalledWith(5, 10);
    expect(fixture.componentInstance.customer()!.loyaltyPoints).toBe(130);
  });

});
