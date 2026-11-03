import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { errorInterceptor } from '../../interceptors/error.interceptor';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PosDiscountSettingsComponent } from './pos-discount-settings.component';
import { AlertService } from '../../services/alert.service';
import { environment } from '../../../environments/environment';
import { PosSettings } from '../../models/settings.model';

const POS_SETTINGS: PosSettings = {
  maxCashSessionDurationHours: 12,
  defaultTimeZoneId: 'Asia/Dhaka',
  taxEnabled: true,
  defaultTaxRate: 7.5,
  taxName: 'VAT',
  pricesIncludeTax: false,
  maxManualDiscountPercent: 10,
  version: 3,
  updatedAt: null,
  isPersisted: true,
  canApplyDiscount: true,
  canOverrideDiscountLimit: false
};

describe('PosDiscountSettingsComponent', () => {
  let fixture: ComponentFixture<PosDiscountSettingsComponent>;
  let http: HttpTestingController;
  const posUrl = `${environment.apiUrl}/settings/pos`;
  const alerts = { success: vi.fn(), info: vi.fn(), error: vi.fn(), config: vi.fn().mockReturnValue(null) };

  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const root = () => fixture.nativeElement as HTMLElement;
  const input = () => root().querySelector<HTMLInputElement>('input[name="maxManualDiscountPercent"]')!;
  const type = async (value: string) => {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    await settle();
  };
  const submit = async () => {
    root().querySelector<HTMLFormElement>('#pos-discount-form')!.dispatchEvent(new Event('submit'));
    await settle();
  };

  async function setup() {
    TestBed.configureTestingModule({
      imports: [PosDiscountSettingsComponent],
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), { provide: AlertService, useValue: alerts }]
    });
    fixture = TestBed.createComponent(PosDiscountSettingsComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(posUrl).flush({ data: POS_SETTINGS });
    await settle();
    await settle();
  }

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    alerts.success.mockReset();
    alerts.info.mockReset();
    alerts.error.mockReset();
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('shows only the manual-discount limit with its helper text', async () => {
    await setup();

    expect(input().value).toBe('10');
    expect(root().querySelectorAll('input').length).toBe(1);
    expect(root().querySelector(`label[for="${input().id}"]`)).not.toBeNull();
    expect(root().textContent).toContain('0 disables manual discounts');
    expect(root().textContent).toContain('sales.discount.override_limit');
  });

  it('sends only maxManualDiscountPercent and applies the saved settings', async () => {
    await setup();
    await type('15');
    await submit();

    const request = http.expectOne(posUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ maxManualDiscountPercent: 15 });
    request.flush({ data: { ...POS_SETTINGS, maxManualDiscountPercent: 15, version: 4 } });
    await settle();

    expect(fixture.componentInstance.baseline()!.version).toBe(4);
    expect(alerts.success).toHaveBeenCalled();
  });

  it('can set the limit to 0 to disable manual discounts', async () => {
    await setup();
    await type('0');
    await submit();

    const request = http.expectOne(posUrl);
    expect(request.request.body).toEqual({ maxManualDiscountPercent: 0 });
    request.flush({ data: { ...POS_SETTINGS, maxManualDiscountPercent: 0 } });
  });

  it('validates the range and does not call the API when nothing changed', async () => {
    await setup();
    await type('101');
    await submit();
    http.expectNone(posUrl);
    expect(root().textContent).toContain('Enter a percentage from 0 to 100.');

    await type('');
    await submit();
    http.expectNone(posUrl);

    await type('10');
    await submit();
    http.expectNone(posUrl);
    expect(alerts.info).toHaveBeenCalled();
  });

  it('shows an inline alert when the API answers 403', async () => {
    await setup();
    await type('20');
    await submit();

    http.expectOne(posUrl).flush({ message: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
    await settle();

    const alert = root().querySelector('app-ui-alert')!;
    expect(alert.textContent).toContain('do not have permission');
    expect(fixture.componentInstance.isSaving()).toBe(false);
  });

  it('shows an inline alert and reloads the latest settings on a 409 version conflict', async () => {
    await setup();
    await type('20');
    await submit();

    http.expectOne(posUrl).flush({ message: 'Settings were modified by another user.' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(posUrl).flush({ data: { ...POS_SETTINGS, maxManualDiscountPercent: 12, version: 4 } });
    await settle();

    expect(root().querySelector('app-ui-alert')!.textContent).toContain('Settings were modified by another user.');
    expect(fixture.componentInstance.baseline()!.version).toBe(4);
    expect(input().value).toBe('12');
    expect(fixture.componentInstance.isSaving()).toBe(false);
  });

  it('offers a retry when the limit cannot be loaded', async () => {
    TestBed.configureTestingModule({
      imports: [PosDiscountSettingsComponent],
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), { provide: AlertService, useValue: alerts }]
    });
    fixture = TestBed.createComponent(PosDiscountSettingsComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(posUrl).flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(root().querySelector('app-ui-state')).not.toBeNull();
    root().querySelector<HTMLButtonElement>('app-ui-state button')!.click();
    http.expectOne(posUrl).flush({ data: POS_SETTINGS });
    await settle();
    expect(input().value).toBe('10');
  });
});
