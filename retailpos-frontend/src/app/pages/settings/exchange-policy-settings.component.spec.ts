import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ExchangePolicy } from '../../models/exchange.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ExchangeService } from '../../services/exchange.service';
import { ExchangePolicySettingsComponent, PHONE_REQUIRED_UNAVAILABLE } from './exchange-policy-settings.component';

const unconfigured: ExchangePolicy = {
  isConfigured: false, versionNo: null, isEnabled: false, allowLowerPriceExchange: false, exchangeWindowDays: null,
  allowReplacementReExchange: false, replacementWindowDays: null, maxExchangeCountPerLineage: null,
  phoneVerificationMode: 'optional', requiredPhoneVerificationAvailable: false, highValueApprovalThreshold: null,
  negativeTargetAdjustmentMode: 'track_only', updatedAtUtc: null
};
const configured: ExchangePolicy = { ...unconfigured, isConfigured: true, versionNo: 3, isEnabled: true, exchangeWindowDays: 7 };

describe('ExchangePolicySettingsComponent', () => {
  let fixture: ComponentFixture<ExchangePolicySettingsComponent>;
  let page: ExchangePolicySettingsComponent;
  const exchanges = { getPolicy: vi.fn(), savePolicy: vi.fn() };
  const alerts = { success: vi.fn(), error: vi.fn(), config: () => null };

  async function setup(policy: ExchangePolicy, canEdit = true): Promise<void> {
    vi.clearAllMocks();
    exchanges.getPolicy.mockReturnValue(of({ success: true, data: policy }));
    await TestBed.configureTestingModule({
      imports: [ExchangePolicySettingsComponent],
      providers: [
        { provide: ExchangeService, useValue: exchanges },
        { provide: AlertService, useValue: alerts },
        { provide: AuthService, useValue: { hasPermission: (key: string) => canEdit || key !== 'settings.edit' } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExchangePolicySettingsComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  const root = () => fixture.nativeElement as HTMLElement;

  it('shows the not-configured state and the safe defaults, with no numeric value prefilled', async () => {
    await setup(unconfigured);
    expect(root().querySelector('[data-testid="not-configured"]')?.textContent).toContain('exchange unavailable');
    const value = page.form.getRawValue();
    expect(value.allowLowerPriceExchange).toBe(false);
    expect(value.negativeTargetAdjustmentMode).toBe('track_only');
    expect(value.exchangeWindowDays).toBeNull();
  });

  it('shows "Required" phone verification as disabled with the explanation and never sends it', async () => {
    await setup(configured);
    const required = root().querySelector<HTMLInputElement>('[data-testid="phone-required"]')!;
    expect(required.disabled).toBe(true);
    expect(root().textContent).toContain(PHONE_REQUIRED_UNAVAILABLE);

    page.form.controls.phoneVerificationMode.setValue('required');
    expect(page.buildPayload().phoneVerificationMode).toBe('optional');
  });

  it('validates the window, re-exchange values and the threshold', async () => {
    await setup(unconfigured);
    page.save();
    expect(exchanges.savePolicy).not.toHaveBeenCalled();
    expect(page.fieldError('exchangeWindowDays')).toContain('0 to 3650');

    page.form.patchValue({ exchangeWindowDays: 3651 });
    expect(page.form.controls.exchangeWindowDays.invalid).toBe(true);
    page.form.patchValue({ exchangeWindowDays: 7, allowReplacementReExchange: true });
    expect(page.form.invalid).toBe(true);
    expect(page.fieldError('replacementWindowDays')).toContain('1 to 50');

    page.form.patchValue({ replacementWindowDays: 7, maxExchangeCountPerLineage: 51 });
    expect(page.fieldError('maxExchangeCountPerLineage')).toContain('1 to 50');
    page.form.patchValue({ maxExchangeCountPerLineage: 2 });
    expect(page.form.valid).toBe(true);

    page.form.patchValue({ highValueApprovalThreshold: 0 });
    expect(page.form.controls.highValueApprovalThreshold.invalid).toBe(true);
    page.form.patchValue({ highValueApprovalThreshold: 10.555 });
    expect(page.form.controls.highValueApprovalThreshold.invalid).toBe(true);
    page.form.patchValue({ highValueApprovalThreshold: 5000.5 });
    expect(page.form.valid).toBe(true);
  });

  it('saves with the expected version for optimistic concurrency', async () => {
    await setup(configured);
    exchanges.savePolicy.mockReturnValue(of({ success: true, data: { ...configured, versionNo: 4 } }));
    page.form.patchValue({ exchangeWindowDays: 10 });
    page.save();
    expect(exchanges.savePolicy).toHaveBeenCalledWith({
      expectedVersionNo: 3, isEnabled: true, allowLowerPriceExchange: false, exchangeWindowDays: 10,
      allowReplacementReExchange: false, replacementWindowDays: null, maxExchangeCountPerLineage: null,
      phoneVerificationMode: 'optional', highValueApprovalThreshold: null, negativeTargetAdjustmentMode: 'track_only'
    });
    expect(page.policy()?.versionNo).toBe(4);
    expect(alerts.success).toHaveBeenCalled();
  });

  it('reloads the latest version after a concurrency conflict', async () => {
    await setup(configured);
    exchanges.savePolicy.mockReturnValue(throwError(() => ({ status: 409, message: 'conflict', originalError: { error: { errorCode: 'CONCURRENCY' } } })));
    exchanges.getPolicy.mockReturnValue(of({ success: true, data: { ...configured, versionNo: 5 } }));
    page.save();
    expect(page.saveError()).toContain('changed by someone else');
    expect(page.policy()?.versionNo).toBe(5);
  });

  it('is read-only without settings.edit', async () => {
    await setup(configured, false);
    expect(page.form.disabled).toBe(true);
    page.save();
    expect(exchanges.savePolicy).not.toHaveBeenCalled();
  });
});
