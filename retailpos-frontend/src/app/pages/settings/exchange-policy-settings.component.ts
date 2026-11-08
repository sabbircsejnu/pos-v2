import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

import { UI_KIT } from '../../components/shared/ui';
import {
  ExchangePolicy,
  NegativeTargetAdjustmentMode,
  PhoneVerificationMode,
  SaveExchangePolicyRequest
} from '../../models/exchange.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ExchangeService } from '../../services/exchange.service';
import { readExchangeError } from '../exchange/exchange-errors';

export const PHONE_REQUIRED_UNAVAILABLE = 'Not available yet: customer phone numbers cannot currently be verified.';

/** Empty, or a positive amount with at most two decimals. */
const thresholdValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const raw = control.value;
  if (raw === null || raw === undefined || raw === '') return null;
  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) return { threshold: true };
  return /^\d+(\.\d{1,2})?$/.test(String(raw)) ? null : { threshold: true };
};

/** With re-exchange allowed, the replacement window and the chain limit are required (1–50). */
const reExchangeValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  if (!group.get('allowReplacementReExchange')?.value) return null;
  const errors: ValidationErrors = {};
  for (const key of ['replacementWindowDays', 'maxExchangeCountPerLineage']) {
    const value = group.get(key)?.value;
    const number = Number(value);
    if (value === null || value === '' || !Number.isInteger(number) || number < 1 || number > 50) errors[key] = true;
  }
  return Object.keys(errors).length > 0 ? errors : null;
};

/**
 * Business Exchange Policy (FR-EX-001..005, D-16). Readable with settings.view and saved with settings.edit; every save
 * creates a new version and is guarded by `expectedVersionNo`. Exchange stays unavailable until the first save.
 * "Required" phone verification is shown but cannot be chosen (DN-01).
 */
@Component({
  selector: 'app-exchange-policy-settings',
  standalone: true,
  imports: [ReactiveFormsModule, ...UI_KIT],
  templateUrl: './exchange-policy-settings.component.html'
})
export class ExchangePolicySettingsComponent implements OnInit {
  private readonly exchanges = inject(ExchangeService);
  private readonly alerts = inject(AlertService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  readonly canEdit = inject(AuthService).hasPermission('settings.edit');

  readonly breadcrumbs = [{ label: 'Settings' }, { label: 'Exchange policy' }];
  readonly phoneRequiredMessage = PHONE_REQUIRED_UNAVAILABLE;

  readonly policy = signal<ExchangePolicy | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly submitted = signal(false);

  readonly form = this.fb.group({
    isEnabled: this.fb.nonNullable.control(true),
    allowLowerPriceExchange: this.fb.nonNullable.control(false),
    exchangeWindowDays: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), Validators.max(3650), Validators.pattern(/^\d+$/)]),
    allowReplacementReExchange: this.fb.nonNullable.control(false),
    replacementWindowDays: this.fb.control<number | null>(null),
    maxExchangeCountPerLineage: this.fb.control<number | null>(null),
    phoneVerificationMode: this.fb.nonNullable.control<PhoneVerificationMode>('optional'),
    highValueApprovalThreshold: this.fb.control<number | null>(null, thresholdValidator),
    negativeTargetAdjustmentMode: this.fb.nonNullable.control<NegativeTargetAdjustmentMode>('track_only')
  }, { validators: reExchangeValidator });

  ngOnInit(): void {
    if (!this.canEdit) this.form.disable();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.exchanges.getPolicy().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.loading.set(false);
        if (res?.data) this.apply(res.data);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(readExchangeError(err).message);
      }
    });
  }

  fieldError(key: 'exchangeWindowDays' | 'replacementWindowDays' | 'maxExchangeCountPerLineage' | 'highValueApprovalThreshold'): string {
    const control = this.form.controls[key];
    const show = this.submitted() || control.touched || control.dirty;
    if (!show) return '';
    switch (key) {
      case 'exchangeWindowDays':
        return control.invalid ? 'Enter a whole number of days from 0 to 3650.' : '';
      case 'highValueApprovalThreshold':
        return control.invalid ? 'Leave empty to disable, or enter an amount above 0 with at most 2 decimals.' : '';
      default:
        return this.form.errors?.[key] ? 'Required when re-exchange is allowed: a whole number from 1 to 50.' : '';
    }
  }

  buildPayload(): SaveExchangePolicyRequest {
    const value = this.form.getRawValue();
    const reExchange = value.allowReplacementReExchange;
    const threshold = value.highValueApprovalThreshold;
    return {
      expectedVersionNo: this.policy()?.versionNo ?? null,
      isEnabled: value.isEnabled,
      allowLowerPriceExchange: value.allowLowerPriceExchange,
      exchangeWindowDays: Number(value.exchangeWindowDays),
      allowReplacementReExchange: reExchange,
      replacementWindowDays: reExchange ? Number(value.replacementWindowDays) : null,
      maxExchangeCountPerLineage: reExchange ? Number(value.maxExchangeCountPerLineage) : null,
      // DN-01: "required" is never sent while customer phones cannot be verified.
      phoneVerificationMode: 'optional',
      highValueApprovalThreshold: threshold === null || (threshold as unknown) === '' ? null : Number(threshold),
      negativeTargetAdjustmentMode: value.negativeTargetAdjustmentMode
    };
  }

  save(): void {
    if (this.saving() || !this.canEdit) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    this.saveError.set(null);
    if (this.form.invalid) return;
    this.saving.set(true);
    this.exchanges.savePolicy(this.buildPayload()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.saving.set(false);
        if (res?.data) this.apply(res.data);
        this.alerts.success('Exchange policy saved');
      },
      error: (err: unknown) => {
        this.saving.set(false);
        const info = readExchangeError(err);
        if (info.status === 409) {
          this.saveError.set('The policy was changed by someone else. The latest version has been loaded; review it and save again.');
          this.reloadAfterConflict();
          return;
        }
        this.saveError.set(info.status === 403
          ? 'You do not have permission to change the exchange policy (settings.edit is required).'
          : info.message);
      }
    });
  }

  private reloadAfterConflict(): void {
    this.exchanges.getPolicy().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => { if (res?.data) this.policy.set(res.data); },
      error: () => undefined
    });
  }

  private apply(policy: ExchangePolicy): void {
    this.policy.set(policy);
    this.submitted.set(false);
    // FR-EX-004: an unconfigured policy shows no numeric values; the owner enters and saves them.
    this.form.reset({
      isEnabled: policy.isConfigured ? policy.isEnabled : true,
      allowLowerPriceExchange: policy.isConfigured ? policy.allowLowerPriceExchange : false,
      exchangeWindowDays: policy.isConfigured ? policy.exchangeWindowDays : null,
      allowReplacementReExchange: policy.isConfigured ? policy.allowReplacementReExchange : false,
      replacementWindowDays: policy.replacementWindowDays,
      maxExchangeCountPerLineage: policy.maxExchangeCountPerLineage,
      phoneVerificationMode: 'optional',
      highValueApprovalThreshold: policy.highValueApprovalThreshold,
      negativeTargetAdjustmentMode: policy.isConfigured ? policy.negativeTargetAdjustmentMode : 'track_only'
    });
  }
}
