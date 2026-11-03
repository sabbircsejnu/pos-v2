import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UI_KIT } from '../../components/shared/ui';
import { PosSettings, UpdatePosSettingsRequest } from '../../models/settings.model';

/**
 * Dedicated screen for the Business manual-discount limit. It is gated by sales.discount.configure
 * only, so holders of that permission can manage the limit without gaining access to Settings.
 */
@Component({
  selector: 'app-pos-discount-settings',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  templateUrl: './pos-discount-settings.component.html'
})
export class PosDiscountSettingsComponent implements OnInit {
  private readonly settingsService = inject(SettingsService);
  private readonly alertService = inject(AlertService);
  private readonly errorHandler = inject(ErrorHandlerService);

  readonly breadcrumbs = [{ label: 'Settings' }, { label: 'Discount limit' }];

  baseline = signal<PosSettings | null>(null);
  maxManualDiscountPercent = signal<number | null>(null);
  error = signal('');
  isLoading = signal(false);
  isSaving = signal(false);
  loadError = signal<string | null>(null);
  saveError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.settingsService.getPosSettings().subscribe({
      next: (res) => {
        if (res?.data) this.apply(res.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.loadError.set(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  update(value: number | string | null): void {
    this.maxManualDiscountPercent.set(value === null || value === '' ? null : Number(value));
    this.error.set('');
  }

  validate(): string {
    const value = this.maxManualDiscountPercent();
    return value === null || Number.isNaN(value) || value < 0 || value > 100
      ? 'Enter a percentage from 0 to 100.'
      : '';
  }

  /** Only the discount limit is ever sent; the API rejects other fields from a discount-only user. */
  buildPayload(): UpdatePosSettingsRequest {
    return { maxManualDiscountPercent: this.maxManualDiscountPercent()! };
  }

  save(): void {
    if (this.isSaving()) return;

    const error = this.validate();
    this.error.set(error);
    this.saveError.set(null);
    if (error) return;

    if (this.baseline()?.maxManualDiscountPercent === this.maxManualDiscountPercent()) {
      this.alertService.info('There are no discount limit changes to save.');
      return;
    }

    this.isSaving.set(true);
    this.settingsService.updatePosSettings(this.buildPayload()).subscribe({
      next: (res) => {
        if (res?.data) this.apply(res.data);
        this.alertService.success('Discount limit saved successfully');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.isSaving.set(false);
        if (err?.status === 403) {
          this.saveError.set('You do not have permission to change the discount limit (sales.discount.configure is required).');
          return;
        }
        this.saveError.set(this.errorHandler.extractErrorMessage(err));
        if (err?.status === 409) {
          this.reloadAfterConflict();
        }
      }
    });
  }

  /** The settings were changed elsewhere: show the latest stored limit so the user can retry on current data. */
  private reloadAfterConflict(): void {
    this.settingsService.getPosSettings().subscribe({
      next: (res) => {
        if (res?.data) this.apply(res.data);
      },
      error: () => undefined
    });
  }

  private apply(settings: PosSettings): void {
    this.baseline.set(settings);
    this.maxManualDiscountPercent.set(settings.maxManualDiscountPercent);
    this.error.set('');
  }
}
