import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsPageComponent } from './settings-page.component';
import { CompanyDateService } from '../../services/company-date.service';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
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

describe('Company Settings date format', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('shows the configured dd/MM/yyyy selection and propagates a saved change to shared dates', async () => {
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: MenuService, useValue: {} }]
    });
    const fixture = TestBed.createComponent(SettingsPageComponent);
    const http = TestBed.inject(HttpTestingController);
    const dates = TestBed.inject(CompanyDateService);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/settings`).flush({
      data: { company: { ...fixture.componentInstance.company(), dateFormat: 'DD/MM/YYYY' } }
    });
    http.expectOne(`${environment.apiUrl}/settings/pos`).flush({ data: POS_SETTINGS });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const selects = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('select'));
    const format = selects.find(select => Array.from(select.options).some(option => option.value === 'dd/MM/yyyy'))!;
    expect(format.value).toBe('dd/MM/yyyy');
    expect(format.selectedOptions[0].textContent).toBe('dd/MM/yyyy');
    expect(dates.format('2026-04-05')).toBe('05/04/2026');

    format.value = 'MM/dd/yyyy';
    format.dispatchEvent(new Event('change'));
    fixture.componentInstance.saveCompany();
    const save = http.expectOne(`${environment.apiUrl}/settings/company`);
    expect(save.request.body.dateFormat).toBe('MM/dd/yyyy');
    save.flush({ data: { company: save.request.body } });
    expect(dates.format('2026-04-05')).toBe('04/05/2026');
  });
});

describe('POS settings tab', () => {
  let fixture: ComponentFixture<SettingsPageComponent>;
  let http: HttpTestingController;
  const posUrl = `${environment.apiUrl}/settings/pos`;

  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const root = () => fixture.nativeElement as HTMLElement;
  const input = (name: string) => root().querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
  const type = async (name: string, value: string) => {
    const el = input(name);
    el.value = value;
    el.dispatchEvent(new Event('input'));
    await settle();
  };
  const submit = async () => {
    root().querySelector<HTMLFormElement>('#pos-settings-form')!.dispatchEvent(new Event('submit'));
    await settle();
  };

  async function setup(permissions: string[], pos: PosSettings = POS_SETTINGS) {
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MenuService, useValue: {} },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } }
      ]
    });
    fixture = TestBed.createComponent(SettingsPageComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/settings`).flush({
      data: {
        company: { ...fixture.componentInstance.company() },
        invoiceNumber: { prefix: 'INV', includeDate: true, nextNumber: 42, padding: 6, separator: '-' }
      }
    });
    http.expectOne(posUrl).flush({ data: pos });
    fixture.componentInstance.activeTab.set('pos');
    await settle();
    await settle();
  }

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('shows the loaded Business POS settings with labelled controls', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);

    expect(input('maxCashSessionDurationHours').value).toBe('12');
    expect(input('defaultTimeZoneId').value).toBe('Asia/Dhaka');
    expect(input('taxEnabled').checked).toBe(true);
    expect(input('defaultTaxRate').value).toBe('7.5');
    expect(input('taxName').value).toBe('VAT');
    expect(root().textContent).toContain('Applied when the session lifecycle is enabled');
    expect(root().querySelector('datalist#pos-time-zones option[value="Asia/Dhaka"]')).not.toBeNull();
    for (const name of ['maxCashSessionDurationHours', 'defaultTimeZoneId', 'defaultTaxRate']) {
      expect(root().querySelector(`label[for="${input(name).id}"]`), name).not.toBeNull();
    }
  });

  it('sends only the changed settings fields', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);
    await type('maxCashSessionDurationHours', '8');
    await submit();

    const request = http.expectOne(posUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ maxCashSessionDurationHours: 8 });
    request.flush({ data: { ...POS_SETTINGS, maxCashSessionDurationHours: 8 } });
    await settle();
    expect(fixture.componentInstance.posBaseline()!.maxCashSessionDurationHours).toBe(8);
  });

  it('no longer edits the discount limit here and links to its own screen only for sales.discount.configure', async () => {
    await setup(['settings.edit']);
    expect(input('maxManualDiscountPercent')).toBeNull();
    expect(input('defaultTaxRate').disabled).toBe(false);
    expect(root().querySelector('a[href="/settings/discounts"]')).toBeNull();
    expect(root().textContent).toContain('sales.discount.configure');

    await type('defaultTaxRate', '5');
    await submit();

    const request = http.expectOne(posUrl);
    expect(request.request.body).toEqual({ defaultTaxRate: 5 });
    request.flush({ data: { ...POS_SETTINGS, defaultTaxRate: 5 } });
    await settle();
  });

  it('links to the discount limit screen for users who hold sales.discount.configure', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);
    expect(root().querySelector('a[href="/settings/discounts"]')).not.toBeNull();
  });

  it('is read-only without settings.edit', async () => {
    await setup(['sales.discount.configure']);
    expect(root().textContent).toContain('do not have permission to change');
    expect(root().querySelector('button[form="pos-settings-form"]')).toBeNull();
    for (const name of ['maxCashSessionDurationHours', 'defaultTimeZoneId', 'taxEnabled', 'pricesIncludeTax', 'taxName', 'defaultTaxRate']) {
      expect(input(name).disabled, name).toBe(true);
    }
    fixture.componentInstance.updatePosField('taxName', 'Changed');
    fixture.componentInstance.savePosSettings();
    http.expectNone(posUrl);
  });

  it('is read-only without any permission', async () => {
    await setup([]);
    expect(root().textContent).toContain('do not have permission to change');
    expect(root().querySelector('button[form="pos-settings-form"]')).toBeNull();
    fixture.componentInstance.savePosSettings();
    http.expectNone(posUrl);
  });

  it('validates the writable fields before sending anything', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);
    await type('maxCashSessionDurationHours', '21');
    await type('defaultTaxRate', '101');
    await submit();

    http.expectNone(posUrl);
    const text = root().textContent!;
    expect(text).toContain('Enter a whole number of hours from 1 to 20.');
    expect(text).toContain('Enter a rate from 0 to 100.');
  });

  it('does not call the API when nothing changed', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);
    await submit();
    http.expectNone(posUrl);
  });

  it('marks the legacy invoice-number block as read-only and points to outlet prefixes', async () => {
    await setup(['settings.edit', 'sales.discount.configure']);
    expect(root().textContent).toContain("Invoice numbers now use each outlet's invoice prefix (Outlets → Edit)");
    for (const name of ['legacyInvoicePrefix', 'legacyInvoiceNext', 'legacyInvoicePadding']) {
      expect(input(name).disabled, name).toBe(true);
    }
    expect(input('legacyInvoicePrefix').value).toBe('INV');
    expect(input('legacyInvoiceNext').value).toBe('42');
  });
});
