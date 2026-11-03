import { TestBed } from '@angular/core/testing';
import { Route, Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { AccountingReportsComponent } from './accounting-reports.component';
import { routes } from '../../app.routes';
import { MenuItem, NAV_SECTIONS } from '../../models/menu.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';

const REPORT_PATHS = ['reports/general-ledger', 'reports/trial-balance', 'reports/profit-loss'];

function flatItems(items: MenuItem[]): MenuItem[] {
  return items.flatMap(item => [item, ...flatItems(item.children ?? [])]);
}

describe('AccountingReportsComponent navigation', () => {
  async function setup(initialUrl: string) {
    const accounting = {
      getGeneralLedger: vi.fn().mockReturnValue(of({ data: { entries: [] } })),
      getTrialBalance: vi.fn().mockReturnValue(of({ data: { entries: [] } })),
      getProfitLoss: vi.fn().mockReturnValue(of({ data: { entries: [] } }))
    };
    TestBed.configureTestingModule({
      providers: [
        // Same paths/component as app.routes, without guards (guards are covered elsewhere).
        provideRouter(REPORT_PATHS.map(path => ({ path, component: AccountingReportsComponent }))),
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: { error: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
        { provide: AuthService, useValue: { hasPermission: () => true, hasAnyPermission: () => true } }
      ]
    });
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(initialUrl, AccountingReportsComponent);
    return { harness, component, accounting, router: TestBed.inject(Router) };
  }

  it('maps the sidebar Trial Balance item to its own route rendered by this component', () => {
    const menuItem = NAV_SECTIONS.flatMap(s => flatItems(s.items)).find(i => i.id === 'trial-balance');
    expect(menuItem?.route).toBe('/reports/trial-balance');
    const route = routes.flatMap(r => [r, ...(r.children ?? [])]).find((r: Route) => r.path === 'reports/trial-balance');
    expect(route?.component).toBe(AccountingReportsComponent);
    expect(route?.data?.['permissions']).toEqual(['accounting.reports.trial_balance']);
  });

  it('shows and loads Trial Balance when navigating to /reports/trial-balance from General Ledger', async () => {
    const { harness, accounting, router } = await setup('/reports/general-ledger');
    expect(accounting.getGeneralLedger).toHaveBeenCalledTimes(1);

    const component = await harness.navigateByUrl('/reports/trial-balance', AccountingReportsComponent);

    expect(router.url).toBe('/reports/trial-balance');
    expect(component.view()).toBe('trial-balance');
    expect(accounting.getTrialBalance).toHaveBeenCalledTimes(1);
    expect(accounting.getGeneralLedger).toHaveBeenCalledTimes(1);
    const active = harness.routeNativeElement!.querySelector('button.bg-slate-900');
    expect(active?.textContent?.trim()).toBe('Trial Balance');
  });

  it('opens directly on Trial Balance / Profit & Loss instead of defaulting to General Ledger', async () => {
    const { component, accounting } = await setup('/reports/trial-balance');
    expect(component.view()).toBe('trial-balance');
    expect(accounting.getTrialBalance).toHaveBeenCalledTimes(1);
    expect(accounting.getGeneralLedger).not.toHaveBeenCalled();
  });

  it('report tabs change the URL so the page and address bar stay in sync', async () => {
    const { harness, accounting, router } = await setup('/reports/general-ledger');
    const tab = Array.from(harness.routeNativeElement!.querySelectorAll('button'))
      .find(b => b.textContent?.trim() === 'Trial Balance') as HTMLButtonElement;

    tab.click();
    await harness.fixture.whenStable();

    expect(router.url).toBe('/reports/trial-balance');
    expect(accounting.getTrialBalance).toHaveBeenCalledTimes(1);

    const profit = await harness.navigateByUrl('/reports/profit-loss', AccountingReportsComponent);
    expect(profit.view()).toBe('profit-loss');
    expect(accounting.getProfitLoss).toHaveBeenCalledTimes(1);
  });
});