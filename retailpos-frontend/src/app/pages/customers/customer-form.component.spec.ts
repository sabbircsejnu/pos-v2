import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Location } from '@angular/common';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { CustomerFormComponent } from './customer-form.component';
import { CustomerService } from '../../services/customer.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';

const CUSTOMER = { id: 5, name: 'Rahim', phone: '0170', email: 'r@x.test', loyaltyPoints: 120, createdAt: '2026-01-01T00:00:00Z' };

describe('CustomerFormComponent legacy points', () => {
  let fixture: ComponentFixture<CustomerFormComponent>;
  const enabled = signal(false);
  const customerService = {
    getById: vi.fn(),
    getLoyaltyCapabilities: vi.fn(),
    addPoints: vi.fn(),
    legacyPointsMutationEnabled: enabled
  };

  async function setup(permissions: string[], flag: boolean) {
    enabled.set(flag);
    customerService.getById.mockReturnValue(of({ data: CUSTOMER }));
    customerService.getLoyaltyCapabilities.mockReturnValue(of({ legacyPointsMutationEnabled: flag }));
    TestBed.configureTestingModule({
      imports: [CustomerFormComponent],
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: customerService },
        { provide: AlertService, useValue: { success: vi.fn(), error: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '5' } } } },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } }
      ]
    });
    fixture = TestBed.createComponent(CustomerFormComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const addButton = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
    .find(button => button.textContent?.includes('Add Points'));

  beforeEach(() => {
    customerService.getById.mockReset();
    customerService.getLoyaltyCapabilities.mockReset();
    customerService.addPoints.mockReset();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('labels the balance as read-only legacy data and requests the capabilities', async () => {
    await setup(['customers.edit'], false);
    expect(customerService.getLoyaltyCapabilities).toHaveBeenCalledTimes(1);
    expect(text()).toContain('Legacy points (read-only)');
    expect(text()).toContain('120');
  });

  it('shows Add Points only when the flag is on and the user has customers.edit', async () => {
    await setup(['customers.edit'], true);
    expect(addButton()).toBeDefined();
  });

  it('hides Add Points when the legacy flag is off', async () => {
    await setup(['customers.edit'], false);
    expect(addButton()).toBeUndefined();
  });

  it('hides Add Points without customers.edit', async () => {
    await setup(['customers.view'], true);
    expect(addButton()).toBeUndefined();
  });

  it('does not call the legacy endpoint when the action is not allowed', async () => {
    await setup(['customers.edit'], false);
    fixture.componentInstance.pointsToAdd.set(10);
    fixture.componentInstance.onAddPoints();
    expect(customerService.addPoints).not.toHaveBeenCalled();
  });
});
