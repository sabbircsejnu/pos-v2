import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { CashSessionDetail, CashSessionService } from '../../services/cash-session.service';
import { CashSessionCloseComponent } from './cash-session-close.component';
import { CashSessionDetailComponent } from './cash-session-detail.component';
import { CashSessionListComponent } from './cash-session-list.component';
import { CashSessionOpenComponent } from './cash-session-open.component';
import { formatElapsed, stateLabel, stateVariant } from './cash-session.format';

const baseSession = {
  id: 7, outletId: 1, terminalId: 2, businessDate: '2026-11-04', openedAt: '2026-11-03T18:30:00Z', status: 'open',
  openedByUserId: 5, openingCashAmount: 100, isOverdue: false, maxDurationHours: 20, elapsedSeconds: 3600,
  state: 'open', timeZoneId: 'Asia/Dhaka', warningLevel: 'none'
};

function detail(overrides: Partial<CashSessionDetail> = {}): CashSessionDetail {
  return {
    session: baseSession as never,
    outletName: 'Main', terminalName: 'Counter 1', openedByName: 'Ayesha',
    summary: {
      sessionId: 7, outletId: 1, terminalId: 2, businessDate: '2026-11-04', openedAt: '', closingAt: '', openDurationSeconds: 0,
      openingCash: 100, cashSales: 50, cashTendered: 100, changeGiven: 50, cardSales: 20, mobileSales: 0, otherNonCashSales: 0,
      drawerExpenses: 0, cashRefunds: 0, corrections: 0, expectedClosingCash: 150, saleCount: 1, isOverdue: false
    },
    handovers: [], movements: [], attachments: [], sales: [], timeline: [],
    ...overrides
  };
}

const alerts = { success: vi.fn(), info: vi.fn(), error: vi.fn(), config: vi.fn().mockReturnValue(null) };

function configure(service: Partial<Record<keyof CashSessionService, unknown>>, permissions: string[] = []) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: CashSessionService, useValue: service },
      { provide: AlertService, useValue: alerts },
      { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '7' } } } }
    ]
  });
}

describe('cash session formatting helpers', () => {
  it('labels states and never relies on colour alone', () => {
    expect(stateLabel('overdue')).toBe('Overdue');
    expect(stateVariant('overdue')).toBe('danger');
    expect(stateVariant('closed')).toBe('neutral');
    expect(formatElapsed(19 * 3600 + 30 * 60)).toBe('19h 30m');
  });
});

describe('CashSessionService', () => {
  it('calls the dedicated /cash-sessions API with server-side filters', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(CashSessionService);

    service.list({ outletId: 3, state: 'overdue', page: 2, pageSize: 10 }).subscribe();
    const request = http.expectOne(r => r.url === `${environment.apiUrl}/cash-sessions`);
    expect(request.request.params.get('outletId')).toBe('3');
    expect(request.request.params.get('state')).toBe('overdue');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ data: { items: [], page: 2, pageSize: 10, totalCount: 0 } });

    service.close(7, { actualClosingCashAmount: 5 }).subscribe();
    const close = http.expectOne(`${environment.apiUrl}/cash-sessions/7/close`);
    expect(close.request.method).toBe('POST');
    close.flush({ data: {} });
    http.verify();
  });
});

describe('CashSessionCloseComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create(d: CashSessionDetail = detail(), close = vi.fn().mockReturnValue(of({ data: {} }))) {
    configure({ detail: vi.fn().mockReturnValue(of({ data: d })), close });
    const fixture = TestBed.createComponent(CashSessionCloseComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, component: fixture.componentInstance, close };
  }

  it('computes the signed variance and demands a reason only when it is not zero', async () => {
    const { component } = await create();

    component.counted.set(150);
    expect(component.variance()).toBe(0);
    expect(component.reasonError()).toBe('');

    component.counted.set(140.5);
    expect(component.variance()).toBe(-9.5);
    expect(component.reasonError()).toContain('reason');
    component.reason.set('short on change');
    expect(component.reasonError()).toBe('');
  });

  it('rejects denominations that do not match the count and totals them when they do', async () => {
    const { component } = await create();
    component.counted.set(150);
    component.setDenomination(0, 0);
    component.setDenomination(1, 0);
    component.setDenomination(2, 0);
    component.setDenomination(3, 1); // 100
    component.setDenomination(4, 1); // 50

    expect(component.denominationTotal()).toBe(150);
    expect(component.countError()).toBe('');

    component.counted.set(160);
    expect(component.countError()).toContain('denominations');
  });

  it('reconciles counted = handed over + retained and blocks over-handover', async () => {
    const { component } = await create();
    component.counted.set(150);
    component.addHandover();
    component.handovers.update(rows => [{ ...rows[0], amount: 100, recipientName: 'Manager' }]);

    expect(component.retained()).toBe(50);
    expect(component.handoverError()).toBe('');

    component.handovers.update(rows => [{ ...rows[0], amount: 200 }]);
    expect(component.handoverError()).toContain('cannot exceed');
    component.handovers.update(rows => [{ ...rows[0], amount: 20, method: 'bank_deposit', recipientName: '' }]);
    expect(component.handoverError()).toContain('bank');
  });

  it('only submits after a review and sends the evidence in the payload', async () => {
    const { component, close } = await create();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.counted.set(150);
    component.addHandover();
    component.handovers.update(rows => [{ ...rows[0], amount: 100, recipientName: 'Manager' }]);

    component.confirm();
    expect(close).not.toHaveBeenCalled();

    component.review();
    expect(component.reviewing()).toBe(true);
    component.confirm();

    expect(close).toHaveBeenCalledTimes(1);
    const [id, payload] = close.mock.calls[0];
    expect(id).toBe(7);
    expect(payload.actualClosingCashAmount).toBe(150);
    expect(payload.varianceReason).toBeUndefined();
    expect(payload.handovers).toEqual([expect.objectContaining({ method: 'cash_to_manager', amount: 100, recipientName: 'Manager' })]);
    expect(navigate).toHaveBeenCalledWith(['/cash-sessions', 7]);
  });

  it('shows the server error and stays on the page when the close is refused', async () => {
    const close = vi.fn().mockReturnValue(throwError(() => ({ error: { message: 'closed' }, message: 'closed' })));
    const { component } = await create(detail(), close);
    component.counted.set(150);
    component.review();
    component.confirm();

    expect(component.submitError()).toBeTruthy();
    expect(component.isSubmitting()).toBe(false);
  });

  async function typeInto(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }, input: HTMLInputElement | HTMLSelectElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('updates Handed over and clears the amount error when a handover amount is typed into the field', async () => {
    const { fixture, component } = await create();
    const root = fixture.nativeElement as HTMLElement;
    component.counted.set(150);
    component.addHandover();
    fixture.detectChanges();
    await fixture.whenStable();

    await typeInto(fixture, root.querySelector<HTMLInputElement>('.cs-handover-row input[type="number"]')!, '34.99');

    expect(component.handovers()[0].amount).toBe(34.99);
    expect(component.handedOver()).toBe(34.99);
    expect(component.retained()).toBe(115.01);
    expect(component.rowError(0, 'amount')).toBe('');
    expect(root.querySelector('[data-testid="handed-over"]')!.textContent).toContain('34.99');
    expect(root.textContent).not.toContain('Each handover line needs an amount above zero.');

    // The remaining rule is reported against its own field, and clears once the field is typed.
    expect(component.handoverError()).toContain('person who took the cash');
    await typeInto(fixture, root.querySelector<HTMLInputElement>('.cs-handover-row input[type="text"]')!, 'Manager');
    expect(component.handoverError()).toBe('');

    // Switching the method through the select re-validates against the bank-deposit rule.
    await typeInto(fixture, root.querySelector<HTMLSelectElement>('.cs-handover-row select')!, 'bank_deposit');
    expect(component.handovers()[0].method).toBe('bank_deposit');
    expect(component.handoverError()).toContain('bank');
    await typeInto(fixture, root.querySelector<HTMLInputElement>('.cs-handover-row input[type="text"]')!, 'City Bank');
    expect(component.handoverError()).toBe('');
  });

  it('keeps the existing handover rules: zero, empty and over-handover amounts are rejected', async () => {
    const { fixture, component } = await create();
    const root = fixture.nativeElement as HTMLElement;
    component.counted.set(50);
    component.addHandover();
    fixture.detectChanges();
    await fixture.whenStable();
    const amount = () => root.querySelector<HTMLInputElement>('.cs-handover-row input[type="number"]')!;

    await typeInto(fixture, root.querySelector<HTMLInputElement>('.cs-handover-row input[type="text"]')!, 'Owner');
    await typeInto(fixture, amount(), '0');
    expect(component.handoverError()).toBe('Each handover line needs an amount above zero.');
    expect(root.textContent).toContain('Each handover line needs an amount above zero.');

    await typeInto(fixture, amount(), '');
    expect(component.handovers()[0].amount).toBeNull();
    expect(component.handoverError()).toBe('Each handover line needs an amount above zero.');

    await typeInto(fixture, amount(), '60');
    expect(component.handoverError()).toContain('cannot exceed');
    expect(root.textContent).toContain('Handed-over cash cannot exceed the counted cash.');
  });

  it('does not flag untouched fields until Review, then shows every inline error', async () => {
    const { fixture, component } = await create();
    const root = fixture.nativeElement as HTMLElement;
    component.addHandover();
    fixture.detectChanges();
    expect(component.countFieldError()).toBe('');
    expect(component.rowError(0, 'amount')).toBe('');

    component.review();
    fixture.detectChanges();
    expect(component.reviewing()).toBe(false);
    expect(component.countFieldError()).toBe('Enter the counted cash.');
    expect(component.rowError(0, 'amount')).toContain('above zero');
    expect(root.textContent).toContain('Enter the counted cash.');
  });

  it('parses handover amounts without accepting non-numeric input', async () => {
    const { component } = await create();
    expect(component.parseAmount(34.99)).toBe(34.99);
    expect(component.parseAmount('34.99')).toBe(34.99);
    expect(component.parseAmount('')).toBeNull();
    expect(component.parseAmount(null)).toBeNull();
    expect(component.parseAmount(Number.NaN)).toBeNull();
    expect(component.parseAmount('abc')).toBeNull();
  });

  it('reviews in a dialog: Continue enables Confirm, Cancel withdraws the review', async () => {
    const { fixture, component, close } = await create();
    component.setCounted(150);
    component.review();
    fixture.detectChanges();
    expect(component.reviewDialogOpen()).toBe(true);
    expect(document.body.textContent).toContain('Review Closing Summary');

    component.cancelReview();
    expect(component.reviewing()).toBe(false);
    component.confirm(); // not reviewed: re-opens the review instead of submitting
    expect(close).not.toHaveBeenCalled();
    expect(component.reviewDialogOpen()).toBe(true);

    component.continueReview();
    expect(component.reviewDialogOpen()).toBe(false);
    expect(component.reviewing()).toBe(true);
    component.confirm();
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('keeps the denomination counter collapsed until requested and states the exact-match difference', async () => {
    const { fixture, component } = await create();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#cs-denominations')).toBeNull();
    component.denominationsOpen.set(true);
    fixture.detectChanges();
    expect(root.querySelectorAll('#cs-denominations input').length).toBe(10);

    component.setCounted(150);
    expect(component.varianceHint()).toContain('Exact Match');
    component.setCounted(140);
    expect(component.varianceHint()).toContain('reason is required');
  });

  it('keeps an overdue session closable and says so', async () => {
    const overdue = detail({ session: { ...baseSession, state: 'overdue', isOverdue: true } as never });
    const { fixture } = await create(overdue);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Overdue session');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Confirm');
  });
});

describe('CashSessionDetailComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  const withVariance = detail({
    session: { ...baseSession, state: 'closed', status: 'closed' } as never,
    variance: {
      id: 3, cashSessionId: 7, varianceAmount: -10, reason: 'short', status: 'pending', postingStatus: 'not_required',
      postingAttempts: 0, createdAtUtc: '2026-11-04T01:00:00Z'
    }
  });

  async function create(permissions: string[]) {
    const service = {
      detail: vi.fn().mockReturnValue(of({ data: withVariance })),
      reviewVariance: vi.fn().mockReturnValue(of({ data: {} }))
    };
    configure(service, permissions);
    const fixture = TestBed.createComponent(CashSessionDetailComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, service };
  }

  it('offers variance approval only with the approve permission', async () => {
    const without = await create(['cash_sessions.view']);
    expect((without.fixture.nativeElement as HTMLElement).textContent).not.toContain('Approve');
    TestBed.resetTestingModule();

    const withPermission = await create(['cash_sessions.view', 'cash_sessions.variance.approve']);
    const buttons = Array.from((withPermission.fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(b => b.textContent?.trim());
    expect(buttons).toContain('Approve');
  });

  function headerAction(fixture: { nativeElement: unknown }, text: string): HTMLElement | undefined {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('app-ui-page-header a, app-ui-page-header button'))
      .find(el => el.textContent?.includes(text)) as HTMLElement | undefined;
  }

  const shown = (el?: HTMLElement) => !!el && !el.classList.contains('cs-hidden');

  it('hides the close action for a closed session', async () => {
    const { fixture } = await create(['cash_sessions.view', 'cash_sessions.close']);
    expect(shown(headerAction(fixture, 'Close session'))).toBe(false);
  });

  it('renders Print Summary and Download PDF in the header only when a close snapshot exists', async () => {
    withVariance.closeSnapshot = { snapshotHash: 'ABC', countedCash: 90, variance: -10, expectedCash: 100 } as never;
    try {
      const { fixture } = await create(['cash_sessions.view']);
      expect(shown(headerAction(fixture, 'Print Summary'))).toBe(true);
      expect(shown(headerAction(fixture, 'Download PDF'))).toBe(true);
    } finally {
      withVariance.closeSnapshot = undefined;
    }

    TestBed.resetTestingModule();
    const { fixture: open } = await create(['cash_sessions.view']);
    expect(shown(headerAction(open, 'Download PDF'))).toBe(false);
  });
});

describe('CashSessionOpenComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  const context = {
    outletId: 1, outletName: 'Main', timeZoneId: 'Asia/Dhaka', businessDate: '2026-11-04', serverNowUtc: '2026-11-03T18:30:00Z',
    maxDurationHours: 20, autoSelectedTerminalId: null,
    terminals: [{ id: 1, name: 'A', code: 'A', hasOpenSession: false }, { id: 2, name: 'B', code: 'B', hasOpenSession: false }]
  };

  async function create(open = vi.fn().mockReturnValue(of({ data: {} }))) {
    configure({ openContext: vi.fn().mockReturnValue(of({ data: context })), open });
    TestBed.overrideProvider(UserOutletAccessService, { useValue: { load: () => of({ data: { outlets: [{ id: 1, name: 'Main', type: 'outlet' }], defaultOutletId: 1 } }) } });
    const fixture = TestBed.createComponent(CashSessionOpenComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, open };
  }

  it('requires an explicit terminal when several are active and never sends a business date', async () => {
    const { component, open } = await create();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.openingCash = 25;
    component.submit();
    expect(open).not.toHaveBeenCalled();
    expect(component.submitError()).toContain('terminal');

    component.terminalId = 2;
    component.submit();
    const dto = open.mock.calls[0][0];
    expect(dto).toEqual(expect.objectContaining({ outletId: 1, terminalId: 2, openingCashAmount: 25 }));
    expect(dto.idempotencyKey).toBeTruthy();
    expect('businessDate' in dto).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/pos']);
  });

  it('refuses a negative opening cash', async () => {
    const { component, open } = await create();
    component.terminalId = 1;
    component.openingCash = -1;
    component.submit();

    expect(open).not.toHaveBeenCalled();
    expect(component.submitError()).toContain('zero or more');
  });

  it('reuses one idempotency key across retries of the same submission', async () => {
    const open = vi.fn().mockReturnValue(throwError(() => ({ error: { message: 'x' }, message: 'x' })));
    const { component } = await create(open);
    component.terminalId = 1;
    component.openingCash = 5;
    component.submit();
    component.submit();

    expect(open).toHaveBeenCalledTimes(2);
    expect(open.mock.calls[0][0].idempotencyKey).toBe(open.mock.calls[1][0].idempotencyKey);
  });
});

describe('CashSessionListComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('lists history for the default outlet and hides open/close actions without permission', async () => {
    const list = vi.fn().mockReturnValue(of({ data: { items: [], page: 1, pageSize: 20, totalCount: 0, serverNowUtc: '2026-11-03T18:30:00Z' } }));
    const businessDaySales = vi.fn().mockReturnValue(of({ data: null }));
    const openContext = vi.fn().mockReturnValue(of({ data: { terminals: [] } }));
    const businessDayTarget = vi.fn().mockReturnValue(of({ data: { outletId: 4, businessDate: '2026-11-06', netSales: 0, target: null, variance: null, achievementPercent: null, status: 'not_configured', serverNowUtc: '2026-11-06T04:00:00Z' } }));
    configure({ list, businessDaySales, businessDayTarget, openContext }, ['cash_sessions.view']);
    TestBed.overrideProvider(UserOutletAccessService, { useValue: { load: () => of({ data: { outlets: [{ id: 4, name: 'Main', type: 'outlet' }], defaultOutletId: 4 } }) } });
    const fixture = TestBed.createComponent(CashSessionListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ outletId: 4 }));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Not configured');
    expect(text).not.toContain('Open Cash Session');
  });

  describe('daily target cards', () => {
    async function render(target: Record<string, unknown>) {
      const empty = of({ data: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
      configure({
        list: vi.fn().mockReturnValue(empty),
        businessDaySales: vi.fn().mockReturnValue(of({ data: null })),
        businessDayTarget: vi.fn().mockReturnValue(of({ data: { outletId: 4, businessDate: '2026-11-06', serverNowUtc: '2026-11-06T04:00:00Z', ...target } })),
        openContext: vi.fn().mockReturnValue(of({ data: { terminals: [] } }))
      }, ['cash_sessions.view']);
      TestBed.overrideProvider(UserOutletAccessService, { useValue: { load: () => of({ data: { outlets: [{ id: 4, name: 'Main', type: 'outlet' }], defaultOutletId: 4 } }) } });
      const fixture = TestBed.createComponent(CashSessionListComponent);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    it('shows the target, percentage and a shortfall below target', async () => {
      const text = await render({ netSales: 600, target: 1000, variance: -400, achievementPercent: 60, status: 'ok' });
      expect(text).toContain('60% achieved');
      expect(text).toContain('Below target');
    });

    it('shows when sales are above target', async () => {
      expect(await render({ netSales: 1500, target: 1000, variance: 500, achievementPercent: 150, status: 'ok' })).toContain('Above target');
    });

    it('shows on target at exactly zero variance', async () => {
      expect(await render({ netSales: 1000, target: 1000, variance: 0, achievementPercent: 100, status: 'ok' })).toContain('On target');
    });

    it('shows a scheduled off day with no variance', async () => {
      const text = await render({ netSales: 500, target: 0, variance: null, achievementPercent: null, status: 'off_day' });
      expect(text).toContain('Scheduled Off Day');
      expect(text).toContain('No target applies on an off day.');
      expect(text).not.toContain('Below target');
    });
  });
});
