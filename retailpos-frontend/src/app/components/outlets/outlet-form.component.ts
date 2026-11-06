import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { OutletService } from '../../services/outlet.service';
import { UserService } from '../../services/user.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { User } from '../../models/user.model';
import {
  CreateOutletRequest,
  Outlet,
  OutletSalesTargetConfiguration,
  OutletSalesTargetDay,
  OutletSalesTargetOverrideInput,
  OutletSalesTargetPeriod,
  OutletSalesTargetVersion,
  OutletSalesTargetsInput,
  OutletTerminal,
  OutletTerminalInput,
  SalesTargetPeriodType
} from '../../models/outlet.model';
import { CompanyDateService } from '../../services/company-date.service';
import { CurrencyService } from '../../services/currency.service';
import { UnsavedChangesAware, UnsavedChangesService } from '../../services/unsaved-changes.service';
import { UI_KIT } from '../shared/ui';
import { TARGET_PERIODS, TargetVersionLike, WEEKDAYS, isAmount, periodRange, previewPeriod } from './outlet-sales-target.logic';
import { WeekdayMultiSelectComponent } from './weekday-multi-select.component';

export const INVOICE_PREFIX_PATTERN = /^[A-Za-z][A-Za-z0-9]{0,9}$/;
export const TERMINAL_NAME_MAX_LENGTH = 120;
export const DEFAULT_TERMINAL_NAME = 'Counter 1';
export const PRINT_BRANCH_NAME_MAX_LENGTH = 150;
export const PRINT_REFERENCE_MAX_LENGTH = 50;
export const PRINT_POLICY_MAX_LENGTH = 2000;

export type TerminalGroup = FormGroup<{ name: FormControl<string>; isActive: FormControl<boolean> }>;

/** One editable terminal row; the persisted facts (code, history, open session) come from the API and never change locally. */
export interface TerminalRow {
  group: TerminalGroup;
  id: number | null;
  code: string | null;
  hasHistory: boolean;
  hasOpenSession: boolean;
}

interface FieldErrors {
  invoicePrefix?: string;
  timeZoneId?: string;
  terminals?: string;
  weeklyOffDays?: string;
  salesTargets?: string;
}

export type OutletFormTab = 'details' | 'invoice' | 'target';

const TAB_CONTROLS: Record<OutletFormTab, readonly string[]> = {
  details: ['name', 'address', 'contactNumber', 'managerId', 'weeklyOffDays', 'terminals'],
  invoice: ['invoicePrefix', 'timeZoneId', 'invoicePrintBranchName', 'binNumber', 'mushak', 'returnExchangePolicy', 'vatLawNotification'],
  target: ['salesTargets']
};

const FIELD_ERROR_TABS: Record<keyof FieldErrors, OutletFormTab> = {
  invoicePrefix: 'invoice',
  timeZoneId: 'invoice',
  terminals: 'details',
  weeklyOffDays: 'details',
  salesTargets: 'target'
};

const MAX_TARGET_AMOUNT = 9_999_999_999_999_999;
export const OVERRIDE_REASON_MAX_LENGTH = 500;

/** An override edit that has not been saved yet, kept while the user looks at other periods. */
type PendingOverride = OutletSalesTargetOverrideInput;

function money(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (value === null || value === undefined || value === '') return null;
  return isAmount(value) ? null : { money: true };
}

function notAllDaysOff(control: AbstractControl): ValidationErrors | null {
  return (control.value as number[] | null)?.length === WEEKDAYS.length ? { allDaysOff: true } : null;
}

export const ALL_DAYS_OFF_MESSAGE = 'An outlet cannot be closed on all seven days. Leave at least one working day.';

function normalizeName(value: unknown): string {
  return String(value ?? '').trim().toLowerCase();
}

function uniqueTerminalNames(control: AbstractControl): ValidationErrors | null {
  const names = (control as FormArray).controls
    .map(group => normalizeName(group.get('name')?.value))
    .filter(name => name.length > 0);
  return new Set(names).size === names.length ? null : { duplicateTerminalNames: true };
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim().length === 0 ? { required: true } : null;
}

/** Error body of the outlet API; the global error interceptor keeps the original response in `originalError`. */
export function extractApiErrorCode(error: any): string | null {
  return error?.originalError?.error?.errorCode ?? error?.error?.errorCode ?? null;
}

@Component({
  selector: 'app-outlet-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, WeekdayMultiSelectComponent, ...UI_KIT],
  templateUrl: './outlet-form.component.html',
  styleUrls: ['./outlet-form.component.css']
})
export class OutletFormComponent implements OnInit, UnsavedChangesAware {
  private readonly auth = inject(AuthService);
  private readonly alertService = inject(AlertService);
  private readonly companyDates = inject(CompanyDateService);
  private readonly currency = inject(CurrencyService);
  private readonly unsavedChanges = inject(UnsavedChangesService);
  private readonly destroyRef = inject(DestroyRef);

  readonly tabs: readonly { id: OutletFormTab; label: string }[] = [
    { id: 'details', label: 'Outlet Details' },
    { id: 'invoice', label: 'Invoice Settings' },
    { id: 'target', label: 'Sales Target' }
  ];
  readonly weekdays = WEEKDAYS;
  readonly targetPeriods = TARGET_PERIODS;
  readonly overrideReasonMaxLength = OVERRIDE_REASON_MAX_LENGTH;
  readonly maxTargetAmount = MAX_TARGET_AMOUNT;

  activeTab = signal<OutletFormTab>('details');
  submitAttempted = signal(false);
  targetLoadError = signal<string | null>(null);
  isLoadingTargets = signal(false);
  targetToday = signal(this.companyDates.todayIso());
  targetHistory = signal<OutletSalesTargetVersion[]>([]);
  savedPeriods = signal<OutletSalesTargetPeriod[]>([]);
  referenceDate = signal(this.companyDates.todayIso());

  private latestVersion: OutletSalesTargetVersion | null = null;
  private baselineSignature = '';
  private readonly pendingOverrides = new Map<string, PendingOverride>();
  /** Amounts typed before a day was closed, shown (never restored) if it reopens. */
  private readonly closedAmounts = new Map<number, number>();
  private saved = false;

  readonly terminalNameMaxLength = TERMINAL_NAME_MAX_LENGTH;
  readonly printBranchNameMaxLength = PRINT_BRANCH_NAME_MAX_LENGTH;
  readonly printReferenceMaxLength = PRINT_REFERENCE_MAX_LENGTH;
  readonly printPolicyMaxLength = PRINT_POLICY_MAX_LENGTH;

  outletForm: FormGroup;
  terminalsArray = new FormArray<TerminalGroup>([], uniqueTerminalNames);
  terminalRows = signal<TerminalRow[]>([]);
  isEditMode = signal(false);
  outletId = signal<number | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  error = signal<string | null>(null);
  fieldErrors = signal<FieldErrors>({});
  managers = signal<User[]>([]);
  invoicePrefixLocked = signal(false);

  terminalToDelete = signal<TerminalRow | null>(null);
  deleteDialogOpen = signal(false);
  isDeletingTerminal = signal(false);

  constructor(
    private fb: FormBuilder,
    private outletService: OutletService,
    private userService: UserService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute
  ) {
    this.outletForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      address: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(200)]],
      contactNumber: ['', [Validators.pattern(/^[\d\s\-\+\(\)]+$/)]],
      managerId: [null],
      invoicePrefix: ['', [Validators.pattern(INVOICE_PREFIX_PATTERN)]],
      timeZoneId: [''],
      invoicePrintBranchName: ['', [Validators.maxLength(PRINT_BRANCH_NAME_MAX_LENGTH)]],
      binNumber: ['', [Validators.maxLength(PRINT_REFERENCE_MAX_LENGTH)]],
      mushak: ['', [Validators.maxLength(PRINT_REFERENCE_MAX_LENGTH)]],
      returnExchangePolicy: ['', [Validators.maxLength(PRINT_POLICY_MAX_LENGTH)]],
      vatLawNotification: ['', [Validators.maxLength(PRINT_POLICY_MAX_LENGTH)]],
      terminals: this.terminalsArray,
      weeklyOffDays: [[] as number[], [notAllDaysOff]],
      salesTargets: this.fb.group({
        isEnabled: [false],
        effectiveFrom: [this.companyDates.todayIso(), [Validators.required, this.notBeforeMinimumDate]],
        days: this.fb.group(Object.fromEntries(WEEKDAYS.map(day => [
          this.dayKey(day.value),
          [null as number | null, [Validators.required, Validators.min(0), Validators.max(MAX_TARGET_AMOUNT), money]]
        ]))),
        overrides: this.fb.group(Object.fromEntries(TARGET_PERIODS.map(period => [
          period.type,
          this.fb.group({
            amount: [null as number | null, [Validators.min(0), Validators.max(MAX_TARGET_AMOUNT), money]],
            reason: ['', [Validators.maxLength(OVERRIDE_REASON_MAX_LENGTH)]]
          })
        ])))
      })
    });

    this.syncTargetControls();
    this.baselineSignature = this.targetSignature();
    this.control('weeklyOffDays').valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.clearFieldError('weeklyOffDays');
        this.syncTargetControls();
      });
    this.control('salesTargets.isEnabled').valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncTargetControls());
  }

  private control(path: string): AbstractControl {
    return this.outletForm.get(path)!;
  }

  private dayKey(day: number): string {
    return `d${day}`;
  }

  dayControl(day: number): FormControl<number | null> {
    return this.control(`salesTargets.days.${this.dayKey(day)}`) as FormControl<number | null>;
  }

  overrideControl(type: SalesTargetPeriodType, field: 'amount' | 'reason'): FormControl {
    return this.control(`salesTargets.overrides.${type}.${field}`) as FormControl;
  }

  get offDays(): number[] {
    return (this.control('weeklyOffDays').value as number[]) ?? [];
  }

  get targetsEnabled(): boolean {
    return !!this.control('salesTargets.isEnabled').value;
  }

  get effectiveFrom(): string {
    return String(this.control('salesTargets.effectiveFrom').value ?? '');
  }

  /** A new version starts today at the earliest, and never before the newest saved one. */
  get minimumEffectiveDate(): string {
    const latest = this.latestVersion?.effectiveFrom ?? '';
    return latest > this.targetToday() ? latest : this.targetToday();
  }

  private readonly notBeforeMinimumDate = (control: AbstractControl): ValidationErrors | null => {
    const value = String(control.value ?? '');
    return value && value < this.minimumEffectiveDate ? { beforeMinimum: true } : null;
  };

  get offDaysError(): string {
    if (this.fieldErrors().weeklyOffDays) return this.fieldErrors().weeklyOffDays!;
    return this.control('weeklyOffDays').hasError('allDaysOff') ? ALL_DAYS_OFF_MESSAGE : '';
  }

  /** Off days never hold an amount, and disabled controls are skipped by validation, so only open days are required. */
  private syncTargetControls(): void {
    const enabled = this.targetsEnabled;
    const off = this.offDays;
    for (const day of WEEKDAYS) {
      const control = this.dayControl(day.value);
      if (off.includes(day.value)) {
        if (isAmount(control.value)) {
          this.closedAmounts.set(day.value, Number(control.value));
        }
        control.setValue(null, { emitEvent: false });
        control.disable({ emitEvent: false });
      } else if (enabled) {
        control.enable({ emitEvent: false });
      } else {
        control.disable({ emitEvent: false });
      }
    }
    const overrides = this.control('salesTargets.overrides');
    if (enabled) {
      overrides.enable({ emitEvent: false });
    } else {
      overrides.disable({ emitEvent: false });
    }
    this.control('salesTargets').updateValueAndValidity({ emitEvent: false });
  }

  isOffDay(day: number): boolean {
    return this.offDays.includes(day);
  }

  zeroMoney(): string {
    return this.currency.format(0);
  }

  formatMoney(value: number | null | undefined): string {
    return value === null || value === undefined ? 'N/A' : this.currency.format(value);
  }

  /** The last amount this weekday had, from this form or the saved history. */
  private previousAmount(day: number): number | null {
    if (this.closedAmounts.has(day)) return this.closedAmounts.get(day)!;
    for (const version of this.targetHistory()) {
      const amount = version.days.find(item => item.dayOfWeek === day)?.targetAmount;
      if (amount !== null && amount !== undefined) return amount;
    }
    return null;
  }

  private savedAmount(day: number): number | null {
    return this.latestVersion?.days.find(item => item.dayOfWeek === day)?.targetAmount ?? null;
  }

  /** Explains a change of schedule at the weekday it affects. An old target is shown, never restored silently. */
  dayNote(day: number): string {
    if (this.isOffDay(day)) {
      const saved = this.savedAmount(day);
      return saved === null
        ? ''
        : `Its ${this.formatMoney(saved)} target stops counting from the effective date. Past results do not change.`;
    }
    const previous = this.previousAmount(day);
    return this.dayControl(day).value === null && previous !== null && this.targetsEnabled
      ? `Previously ${this.formatMoney(previous)}. Enter the target to use from the effective date.`
      : '';
  }

  private draftDays(): OutletSalesTargetDay[] {
    return WEEKDAYS.map(day => {
      const isOffDay = this.isOffDay(day.value);
      const value = this.dayControl(day.value).value;
      return {
        dayOfWeek: day.value,
        isOffDay,
        targetAmount: !isOffDay && isAmount(value) ? Number(value) : null
      };
    });
  }

  private targetSignature(): string {
    const days = [...this.draftDays()].sort((left, right) => left.dayOfWeek - right.dayOfWeek);
    return JSON.stringify([this.targetsEnabled, days.map(day => [day.isOffDay, day.targetAmount])]);
  }

  /** Saved versions plus the values on this page, starting on the chosen effective date. */
  private get previewVersions(): TargetVersionLike[] {
    const saved: TargetVersionLike[] = this.targetHistory();
    if (this.targetSignature() === this.baselineSignature) return saved;
    return [
      ...saved,
      {
        versionNo: Number.MAX_SAFE_INTEGER,
        effectiveFrom: this.effectiveFrom || this.minimumEffectiveDate,
        isEnabled: this.targetsEnabled,
        days: this.draftDays()
      }
    ];
  }

  /** The Weekly Target of this configuration: every open day once, whichever dates it starts on. */
  get fullWeekTotal(): number {
    const paisa = this.draftDays().reduce((sum, day) => sum + Math.round((day.targetAmount ?? 0) * 100), 0);
    return paisa / 100;
  }

  /** The target summary: each calendar period containing the reference date, using the values on this page. */
  get periodRows() {
    const versions = this.previewVersions;
    return TARGET_PERIODS.map(period => {
      const range = periodRange(period.type, this.referenceDate());
      const preview = previewPeriod(versions, range.from, range.to);
      const overridden = this.overrideControl(period.type, 'amount').value;
      const isOverridden = overridden !== null && overridden !== undefined && overridden !== '';
      return {
        ...period,
        ...range,
        preview,
        isOverridden,
        target: isOverridden ? Number(overridden) : preview.total
      };
    });
  }

  private overrideKey(type: SalesTargetPeriodType, from: string): string {
    return `${type}|${from}`;
  }

  /** Remembers what differs from the saved values for the periods on screen. */
  private captureOverrideEdits(): void {
    for (const period of TARGET_PERIODS) {
      const from = periodRange(period.type, this.referenceDate()).from;
      const saved = this.savedPeriods().find(item => item.periodType === period.type && item.from === from);
      const rawAmount = this.overrideControl(period.type, 'amount').value;
      const amount = rawAmount === null || rawAmount === undefined || rawAmount === '' ? null : Number(rawAmount);
      const reason = String(this.overrideControl(period.type, 'reason').value ?? '').trim();
      const key = this.overrideKey(period.type, from);
      const unchanged = amount === (saved?.overrideAmount ?? null) && reason === (saved?.reason ?? '');
      if (unchanged || (amount === null && !saved?.isOverridden)) {
        this.pendingOverrides.delete(key);
      } else {
        this.pendingOverrides.set(key, { periodType: period.type, periodFrom: from, overrideAmount: amount, reason: reason || null });
      }
    }
  }

  private showOverridesForReferenceDate(): void {
    for (const period of TARGET_PERIODS) {
      const from = periodRange(period.type, this.referenceDate()).from;
      const pending = this.pendingOverrides.get(this.overrideKey(period.type, from));
      const saved = this.savedPeriods().find(item => item.periodType === period.type && item.from === from);
      const isEditing = !!pending;
      this.overrideControl(period.type, 'amount').setValue(
        isEditing ? pending!.overrideAmount : saved?.overrideAmount ?? null, { emitEvent: false });
      this.overrideControl(period.type, 'reason').setValue(
        isEditing ? pending!.reason ?? '' : saved?.reason ?? '', { emitEvent: false });
    }
  }

  resetOverride(type: SalesTargetPeriodType): void {
    this.overrideControl(type, 'amount').setValue(null);
    this.overrideControl(type, 'reason').setValue('');
    this.outletForm.markAsDirty();
  }

  onReferenceDateChange(value: string): void {
    if (!value || value === this.referenceDate()) return;
    this.captureOverrideEdits();
    this.referenceDate.set(value);
    const outletId = this.outletId();
    if (outletId === null) {
      this.showOverridesForReferenceDate();
      return;
    }
    this.loadSalesTargets(outletId, value);
  }

  loadSalesTargets(outletId: number, referenceDate?: string): void {
    this.isLoadingTargets.set(true);
    this.targetLoadError.set(null);
    this.outletService.getSalesTargets(outletId, referenceDate).subscribe({
      next: (response: any) => {
        this.isLoadingTargets.set(false);
        const configuration: OutletSalesTargetConfiguration | null = response?.data ?? null;
        if (referenceDate && configuration) {
          this.savedPeriods.set(configuration.periods ?? []);
          this.showOverridesForReferenceDate();
          return;
        }
        this.applyConfiguration(configuration);
      },
      error: (error) => {
        console.error('Error loading sales targets:', error);
        this.isLoadingTargets.set(false);
        this.targetLoadError.set('The sales targets could not be loaded, so this save will leave them unchanged.');
        // Nothing was loaded to edit, so the section must neither block the save nor overwrite anything.
        this.control('weeklyOffDays').disable({ emitEvent: false });
        this.control('salesTargets').disable({ emitEvent: false });
      }
    });
  }

  private applyConfiguration(configuration: OutletSalesTargetConfiguration | null): void {
    this.control('weeklyOffDays').enable({ emitEvent: false });
    this.control('salesTargets').enable({ emitEvent: false });
    this.latestVersion = configuration?.latest ?? null;
    this.targetHistory.set(configuration?.history ?? []);
    this.savedPeriods.set(configuration?.periods ?? []);
    if (configuration?.today) {
      this.targetToday.set(configuration.today);
      this.referenceDate.set(configuration.today);
    }

    const latest = this.latestVersion;
    this.control('weeklyOffDays').setValue(
      (latest?.days ?? []).filter(day => day.isOffDay).map(day => day.dayOfWeek),
      { emitEvent: false });
    this.control('salesTargets.isEnabled').setValue(!!latest?.isEnabled, { emitEvent: false });
    this.control('salesTargets.effectiveFrom').setValue(this.minimumEffectiveDate, { emitEvent: false });
    for (const day of WEEKDAYS) {
      this.dayControl(day.value).setValue(
        latest?.days.find(item => item.dayOfWeek === day.value)?.targetAmount ?? null,
        { emitEvent: false });
    }
    this.showOverridesForReferenceDate();
    this.closedAmounts.clear();
    this.syncTargetControls();
    this.baselineSignature = this.targetSignature();
    this.outletForm.markAsPristine();
  }

  /** The block is sent only for a real change, so saving another tab never creates a target version or a false conflict. */
  private buildSalesTargets(): OutletSalesTargetsInput | undefined {
    if (this.targetLoadError()) return undefined;
    this.captureOverrideEdits();
    const overrides = this.targetsEnabled
      ? [...this.pendingOverrides.values()]
      : [...this.pendingOverrides.values()].filter(item => item.overrideAmount === null);
    if (this.targetSignature() === this.baselineSignature && overrides.length === 0) return undefined;

    return {
      isEnabled: this.targetsEnabled,
      effectiveFrom: this.effectiveFrom,
      days: this.draftDays(),
      ...(overrides.length ? { overrides } : {}),
      expectedVersionNo: this.latestVersion?.versionNo ?? null
    };
  }

  // ---- Tabs ---------------------------------------------------------------------------------------------------------

  selectTab(tab: OutletFormTab): void {
    this.activeTab.set(tab);
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const next =
      event.key === 'ArrowRight' ? (index === last ? 0 : index + 1)
      : event.key === 'ArrowLeft' ? (index === 0 ? last : index - 1)
      : event.key === 'Home' ? 0
      : event.key === 'End' ? last
      : null;
    if (next === null) return;
    event.preventDefault();
    this.selectTab(this.tabs[next].id);
    const tablist = (event.currentTarget as HTMLElement).parentElement;
    tablist?.querySelectorAll<HTMLElement>('[role="tab"]')[next]?.focus();
  }

  /** Invalid fields show on the tab once the user has reached them or tried to save, as they do on the fields themselves. */
  tabHasError(tab: OutletFormTab): boolean {
    const fieldErrors = this.fieldErrors();
    if ((Object.keys(FIELD_ERROR_TABS) as (keyof FieldErrors)[]).some(field => FIELD_ERROR_TABS[field] === tab && !!fieldErrors[field])) {
      return true;
    }
    return TAB_CONTROLS[tab].some(name => {
      const control = this.control(name);
      return control.invalid && (this.submitAttempted() || control.touched || control.dirty);
    });
  }

  private showFirstInvalidTab(): void {
    const invalid = this.tabs.find(tab => this.tabHasError(tab.id));
    if (invalid) {
      this.selectTab(invalid.id);
    }
  }

  // ---- Unsaved changes ----------------------------------------------------------------------------------------------

  hasUnsavedChanges(): boolean {
    return !this.saved && this.outletForm.dirty;
  }

  getUnsavedChangesMessage(): string {
    return 'You have unsaved changes to this outlet. If you leave this page, they will be lost.';
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    this.unsavedChanges.applyBeforeUnload(event, this.hasUnsavedChanges());
  }

  /** Changing terminals needs outlets.terminals.manage; without it the editor is read-only and `terminals` is not sent. */
  get canManageTerminals(): boolean {
    return this.auth.hasPermission('outlets.terminals.manage');
  }

  get activeTerminalCount(): number {
    return this.terminalRows().filter(row => row.group.controls.isActive.value).length;
  }

  /** Example invoice number: prefix + yyMMdd + sequence, rendered without separators. */
  get invoiceExample(): string {
    const control = this.outletForm.get('invoicePrefix');
    const prefix = control?.valid && control.value ? String(control.value).toUpperCase() : 'DHK01';
    const now = new Date();
    const yy = String(now.getFullYear() % 100).padStart(2, '0');
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    const dd = String(now.getDate()).padStart(2, '0');
    return `${prefix}${yy}${mm}${dd}1`;
  }

  ngOnInit(): void {
    this.loadManagers();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.outletId.set(+id);
      this.loadOutlet(+id);
      this.loadSalesTargets(+id);
    } else if (this.canManageTerminals) {
      // Pre-filled for convenience only: nothing is persisted until the outlet is saved.
      this.addTerminal(DEFAULT_TERMINAL_NAME);
    }
  }

  loadManagers(): void {
    this.userService.getUsers().subscribe({
      next: (response: any) => {
        this.managers.set(response.data || []);
      },
      error: (error: any) => {
        console.error('Error loading managers:', error);
      }
    });
  }

  loadOutlet(id: number): void {
    this.isLoading.set(true);
    this.outletService.getOutletById(id).subscribe({
      next: (response: any) => {
        const outlet: Outlet = response.data;
        this.outletForm.patchValue({
          name: outlet.name,
          address: outlet.address,
          contactNumber: outlet.contactNumber,
          managerId: outlet.managerId,
          invoicePrefix: outlet.invoicePrefix ?? '',
          timeZoneId: outlet.timeZoneId ?? '',
          invoicePrintBranchName: outlet.invoicePrintBranchName ?? '',
          binNumber: outlet.binNumber ?? '',
          mushak: outlet.mushak ?? '',
          returnExchangePolicy: outlet.returnExchangePolicy ?? '',
          vatLawNotification: outlet.vatLawNotification ?? ''
        });
        this.setInvoicePrefixLocked(!!outlet.invoicePrefixLocked);
        this.setTerminalRows((outlet.terminals ?? []).map(terminal => this.createRow(terminal)));
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading outlet:', error);
        this.error.set('Failed to load outlet');
        this.isLoading.set(false);
      }
    });
  }

  private setInvoicePrefixLocked(locked: boolean): void {
    this.invoicePrefixLocked.set(locked);
    const control = this.outletForm.get('invoicePrefix')!;
    if (locked) {
      control.disable({ emitEvent: false });
    } else {
      control.enable({ emitEvent: false });
    }
  }

  private createRow(terminal: Partial<OutletTerminal> & { name: string }): TerminalRow {
    const row: TerminalRow = {
      group: this.fb.nonNullable.group({
        name: [terminal.name, [Validators.required, Validators.maxLength(TERMINAL_NAME_MAX_LENGTH), notBlank]],
        isActive: [terminal.isActive ?? true]
      }) as TerminalGroup,
      id: terminal.id ?? null,
      code: terminal.code ?? null,
      hasHistory: !!terminal.hasHistory,
      hasOpenSession: !!terminal.hasOpenSession
    };
    if (!this.canManageTerminals) {
      row.group.disable({ emitEvent: false });
    } else if (row.hasOpenSession) {
      // Deactivating a terminal with an open or overdue cash session is refused by the API.
      row.group.controls.isActive.disable({ emitEvent: false });
    }
    return row;
  }

  private setTerminalRows(rows: TerminalRow[]): void {
    this.terminalsArray.clear({ emitEvent: false });
    rows.forEach(row => this.terminalsArray.push(row.group, { emitEvent: false }));
    this.terminalsArray.updateValueAndValidity();
    this.terminalRows.set(rows);
  }

  addTerminal(name = ''): void {
    if (!this.canManageTerminals) return;
    this.setTerminalRows([...this.terminalRows(), this.createRow({ name })]);
    this.clearFieldError('terminals');
    // The suggested first terminal on a new outlet is not a change the user made.
    if (name !== DEFAULT_TERMINAL_NAME || this.isEditMode()) {
      this.outletForm.markAsDirty();
    }
  }

  /** Unsaved rows can simply be dropped; persisted rows are deactivated or deleted through the API instead. */
  removeUnsavedTerminal(row: TerminalRow): void {
    if (row.id !== null || !this.canManageTerminals) return;
    this.setTerminalRows(this.terminalRows().filter(candidate => candidate !== row));
    this.clearFieldError('terminals');
    this.outletForm.markAsDirty();
  }

  canDeleteTerminal(row: TerminalRow): boolean {
    return this.canManageTerminals && this.isEditMode() && row.id !== null && !row.hasHistory;
  }

  requestDeleteTerminal(row: TerminalRow): void {
    if (!this.canDeleteTerminal(row)) return;
    this.terminalToDelete.set(row);
    this.deleteDialogOpen.set(true);
  }

  cancelDeleteTerminal(): void {
    if (this.isDeletingTerminal()) return;
    this.terminalToDelete.set(null);
    this.deleteDialogOpen.set(false);
  }

  confirmDeleteTerminal(): void {
    const row = this.terminalToDelete();
    const outletId = this.outletId();
    if (!row || row.id === null || outletId === null || this.isDeletingTerminal()) return;

    this.isDeletingTerminal.set(true);
    this.outletService.deleteTerminal(outletId, row.id).subscribe({
      next: () => {
        this.setTerminalRows(this.terminalRows().filter(candidate => candidate !== row));
        this.isDeletingTerminal.set(false);
        this.terminalToDelete.set(null);
        this.deleteDialogOpen.set(false);
        this.clearFieldError('terminals');
      },
      error: (error) => {
        this.isDeletingTerminal.set(false);
        this.terminalToDelete.set(null);
        this.deleteDialogOpen.set(false);
        this.setFieldError('terminals', this.terminalErrorMessage(error));
      }
    });
  }

  onInvoicePrefixInput(event: Event): void {
    const element = event.target as HTMLInputElement;
    const upper = element.value.toUpperCase();
    if (upper !== element.value) {
      const caret = element.selectionStart;
      element.value = upper;
      element.setSelectionRange?.(caret, caret);
    }
    this.outletForm.get('invoicePrefix')!.setValue(upper);
    this.clearFieldError('invoicePrefix');
  }

  clearFieldError(field: keyof FieldErrors): void {
    if (this.fieldErrors()[field]) {
      this.fieldErrors.update(errors => ({ ...errors, [field]: undefined }));
    }
  }

  isDuplicateTerminalName(row: TerminalRow): boolean {
    const name = normalizeName(row.group.controls.name.value);
    if (!name) return false;
    return this.terminalRows().filter(candidate => normalizeName(candidate.group.controls.name.value) === name).length > 1;
  }

  terminalNameError(row: TerminalRow): string {
    return this.isDuplicateTerminalName(row) ? 'Another terminal at this outlet already has this name.' : '';
  }

  /** Request body: `terminals` is only sent for users who may manage them, and invoicePrefix only while it is still editable. */
  buildPayload(): CreateOutletRequest {
    const value = this.outletForm.getRawValue();
    const payload: CreateOutletRequest = {
      name: value.name,
      address: value.address,
      contactNumber: value.contactNumber,
      managerId: value.managerId
    };

    const prefix = String(value.invoicePrefix ?? '').trim().toUpperCase();
    if (!this.invoicePrefixLocked() && prefix) {
      payload.invoicePrefix = prefix;
    }

    const timeZone = String(value.timeZoneId ?? '').trim();
    if (this.isEditMode()) {
      // '' clears the outlet override so it inherits the Business default again.
      payload.timeZoneId = timeZone;
    } else if (timeZone) {
      payload.timeZoneId = timeZone;
    }

    const printFields = ['invoicePrintBranchName', 'binNumber', 'mushak', 'returnExchangePolicy', 'vatLawNotification'] as const;
    for (const field of printFields) {
      const text = String(value[field] ?? '').trim();
      // Same contract as the time zone: on edit '' clears the setting.
      if (this.isEditMode() || text) {
        payload[field] = text;
      }
    }

    if (this.canManageTerminals) {
      payload.terminals = this.terminalRows().map((row): OutletTerminalInput => {
        const terminal = { name: row.group.controls.name.value.trim(), isActive: row.group.controls.isActive.value };
        return row.id === null ? terminal : { id: row.id, ...terminal };
      });
    }

    const salesTargets = this.buildSalesTargets();
    if (salesTargets) {
      payload.salesTargets = salesTargets;
    }
    return payload;
  }

  onSubmit(): void {
    if (this.isSaving()) return;

    if (this.outletForm.invalid) {
      this.submitAttempted.set(true);
      this.outletForm.markAllAsTouched();
      this.showFirstInvalidTab();
      return;
    }

    this.isSaving.set(true);
    this.error.set(null);
    this.fieldErrors.set({});

    const payload = this.buildPayload();
    const warnNoActiveTerminal = this.canManageTerminals && this.activeTerminalCount === 0;
    const request = this.isEditMode()
      ? this.outletService.updateOutlet(this.outletId()!, payload)
      : this.outletService.createOutlet(payload);

    request.subscribe({
      next: () => {
        this.isSaving.set(false);
        this.saved = true;
        if (warnNoActiveTerminal) {
          this.alertService.warning(
            'The outlet was saved without an active terminal. Sales are blocked at this outlet until an active terminal exists.',
            'No active terminal'
          );
        }
        this.location.back();
      },
      error: (error) => {
        console.error('Error saving outlet:', error);
        this.applySaveError(error);
        this.isSaving.set(false);
      }
    });
  }

  /** Maps the API's stable error codes to the field they belong to; anything else stays a form-level message. */
  applySaveError(error: any): void {
    const code = extractApiErrorCode(error);
    const message: string | undefined = error?.message ?? error?.error?.error;

    switch (code) {
      case 'OUTLET_INVOICE_PREFIX_INVALID':
        this.setFieldError('invoicePrefix', 'Use 1–10 letters or digits, starting with a letter.');
        break;
      case 'OUTLET_INVOICE_PREFIX_EXISTS':
        this.setFieldError('invoicePrefix', 'Another outlet in this business already uses this prefix.');
        break;
      case 'OUTLET_INVOICE_PREFIX_LOCKED':
        this.setFieldError('invoicePrefix', 'The prefix cannot change because invoices were already issued for this outlet.');
        this.setInvoicePrefixLocked(true);
        break;
      case 'OUTLET_INVOICE_PREFIX_AMBIGUOUS':
        this.setFieldError(
          'invoicePrefix',
          'This prefix clashes with another outlet: one prefix cannot extend another using only digits (for example DHK and DHK01).'
        );
        break;
      case 'TERMINAL_NAME_EXISTS':
      case 'TERMINAL_INVALID':
      case 'TERMINAL_HAS_OPEN_SESSION':
      case 'TERMINAL_HAS_HISTORY':
        this.setFieldError('terminals', this.terminalErrorMessage(error));
        break;
      case 'OUTLET_TARGET_ALL_DAYS_OFF':
        this.setFieldError('weeklyOffDays', ALL_DAYS_OFF_MESSAGE);
        break;
      case 'OUTLET_TARGET_INVALID':
      case 'OUTLET_TARGET_EFFECTIVE_DATE_INVALID':
      case 'OUTLET_TARGET_OVERRIDE_INVALID':
      case 'OUTLET_TARGET_CONFLICT':
        this.setFieldError('salesTargets', message || 'The sales targets could not be saved.');
        break;
      case 'forbidden':
        this.error.set('You do not have permission to make this change.');
        break;
      default:
        this.error.set(message || 'Failed to save outlet');
    }
  }

  private terminalErrorMessage(error: any): string {
    switch (extractApiErrorCode(error)) {
      case 'TERMINAL_NAME_EXISTS':
        return 'Another terminal at this outlet already has this name. Terminal names must be unique.';
      case 'TERMINAL_INVALID':
        return `Every terminal needs a name of up to ${TERMINAL_NAME_MAX_LENGTH} characters.`;
      case 'TERMINAL_HAS_OPEN_SESSION':
        return 'A terminal with an open or overdue cash session cannot be deactivated. Close its session first.';
      case 'TERMINAL_HAS_HISTORY':
        return 'This terminal has sales or cash-session history, so it cannot be deleted. Deactivate it instead.';
      default:
        return error?.message || 'The terminal change could not be saved.';
    }
  }

  private setFieldError(field: keyof FieldErrors, message: string): void {
    this.fieldErrors.update(errors => ({ ...errors, [field]: message }));
    this.selectTab(FIELD_ERROR_TABS[field]);
  }

  cancel(): void {
    this.location.back();
  }
}
