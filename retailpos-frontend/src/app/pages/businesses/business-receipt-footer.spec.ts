import { Location } from '@angular/common';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { BusinessService } from '../../services/business.service';
import { BusinessFormComponent } from './business-form.component';
import { BusinessSetupComponent } from './business-setup.component';

const BUSINESS = {
  businessId: 7,
  businessName: 'Shop',
  isActive: true,
  userCount: 1,
  outletCount: 1,
  warehouseCount: 0,
  showPoweredByOnReceipt: true,
  poweredByText: 'Powered by: Irani Mart Technology'
};

function configure(superAdmin: boolean) {
  const businessService = {
    createBusiness: vi.fn(() => of({ success: true, message: 'ok' })),
    getBusinessById: vi.fn(() => of({ success: true, data: BUSINESS })),
    updateReceiptFooter: vi.fn(() => of({ success: true, data: { ...BUSINESS, showPoweredByOnReceipt: false } }))
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '7' }), queryParams: {} } } },
      { provide: Location, useValue: { back: vi.fn() } },
      { provide: AuthService, useValue: { isSuperAdmin: () => superAdmin, hasPermission: () => true } },
      { provide: BusinessService, useValue: businessService }
    ]
  });
  return businessService;
}

async function render<T>(type: Type<T>) {
  const fixture = TestBed.createComponent(type);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

describe('Platform receipt footer controls', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  describe('Create Business', () => {
    it('shows the controls to a Platform Super Admin and sends their values', async () => {
      const service = configure(true);
      const { fixture, cmp, root } = await render(BusinessFormComponent);
      expect(root.textContent).toContain('Show Powered By on Receipt');

      const toggle = root.querySelector<HTMLInputElement>('input[type="checkbox"][name="showPoweredByOnReceipt"]')!;
      toggle.click();
      const text = root.querySelector<HTMLInputElement>('input[name="poweredByText"]')!;
      text.value = '  Powered by: X  ';
      text.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      cmp.submit();

      expect(service.createBusiness).toHaveBeenCalledWith(
        expect.objectContaining({ showPoweredByOnReceipt: true, poweredByText: 'Powered by: X' })
      );
    });

    it('hides the controls from anyone else and never sends the fields', async () => {
      const service = configure(false);
      const { cmp, root } = await render(BusinessFormComponent);

      expect(root.textContent).not.toContain('Powered By');
      cmp.submit();

      const payload = (service.createBusiness.mock.calls as unknown[][])[0][0] as object;
      expect(payload).not.toHaveProperty('showPoweredByOnReceipt');
      expect(payload).not.toHaveProperty('poweredByText');
    });
  });

  describe('Business Setup', () => {
    const openSettings = async (superAdmin: boolean) => {
      const service = configure(superAdmin);
      const rendered = await render(BusinessSetupComponent);
      (rendered.cmp as unknown as { activeTab: { set(tab: string): void } }).activeTab.set('settings');
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();
      rendered.fixture.detectChanges();
      return { service, ...rendered };
    };

    it('loads the saved values and saves the edited ones through the dedicated API', async () => {
      const { service, fixture, cmp, root } = await openSettings(true);
      const text = root.querySelector<HTMLInputElement>('input[name="poweredByText"]')!;
      expect(root.querySelector<HTMLInputElement>('input[name="showPoweredByOnReceipt"]')!.checked).toBe(true);
      expect(text.value).toBe('Powered by: Irani Mart Technology');

      root.querySelector<HTMLInputElement>('input[name="showPoweredByOnReceipt"]')!.click();
      text.value = '';
      text.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      cmp.saveReceiptFooter();

      expect(service.updateReceiptFooter).toHaveBeenCalledWith(7, { showPoweredByOnReceipt: false, poweredByText: null });
      expect(cmp.business()?.showPoweredByOnReceipt).toBe(false);
      expect(cmp.receiptFooterSaving()).toBe(false);
    });

    it('saves true when the toggle is switched on through its label, and shows the saved state', async () => {
      const service = configure(true);
      service.getBusinessById.mockReturnValue(of({ success: true, data: { ...BUSINESS, showPoweredByOnReceipt: false } }) as never);
      service.updateReceiptFooter.mockReturnValue(of({ success: true, data: BUSINESS }) as never);
      const rendered = await render(BusinessSetupComponent);
      rendered.cmp.activeTab.set('settings');
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();
      rendered.fixture.detectChanges();
      const status = () => rendered.root.querySelector('#receipt-footer-status')?.textContent?.trim();
      expect(status()).toBe('Saved: not printing on receipts');

      rendered.root.querySelector<HTMLLabelElement>('label.ui-check')!.click();
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();
      expect(status()).toBe('Saved: not printing on receipts');

      rendered.cmp.saveReceiptFooter();
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();
      rendered.fixture.detectChanges();

      expect(service.updateReceiptFooter).toHaveBeenCalledWith(7, {
        showPoweredByOnReceipt: true,
        poweredByText: 'Powered by: Irani Mart Technology'
      });
      expect(status()).toBe('Saved: printing on receipts');
    });

    it('shows what the server saved after a save, not what the form held', async () => {
      const { service, fixture, cmp, root } = await openSettings(true);
      service.updateReceiptFooter.mockReturnValue(
        of({ success: true, data: { ...BUSINESS, showPoweredByOnReceipt: false, poweredByText: null } }) as never
      );

      cmp.saveReceiptFooter();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cmp.showPoweredBy()).toBe(false);
      expect(cmp.poweredByText()).toBe('');
      expect(root.querySelector('#receipt-footer-status')?.textContent?.trim()).toBe('Saved: not printing on receipts');
    });

    it('explains that nothing prints when enabled without text', async () => {
      const { fixture, root } = await openSettings(true);
      const text = root.querySelector<HTMLInputElement>('input[name="poweredByText"]')!;
      text.value = '   ';
      text.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(root.textContent).toContain('Nothing prints until text is entered.');
    });

    it('keeps the entered values and shows the error when saving is refused', async () => {
      const { service, cmp } = await openSettings(true);
      const alerts = TestBed.inject(AlertService);
      const error = vi.spyOn(alerts, 'error').mockImplementation(() => undefined as never);
      service.updateReceiptFooter.mockReturnValue(throwError(() => ({ status: 403, message: 'denied' })) as never);

      cmp.poweredByText.set('Unsaved text');
      cmp.saveReceiptFooter();

      expect(error).toHaveBeenCalled();
      expect(cmp.poweredByText()).toBe('Unsaved text');
      expect(cmp.receiptFooterSaving()).toBe(false);
    });

    it('hides the controls from anyone else and refuses to call the API', async () => {
      const { service, cmp, root } = await openSettings(false);

      expect(root.textContent).not.toContain('Powered By');
      cmp.saveReceiptFooter();

      expect(service.updateReceiptFooter).not.toHaveBeenCalled();
    });
  });
});
