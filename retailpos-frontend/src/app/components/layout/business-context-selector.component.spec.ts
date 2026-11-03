import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { BusinessContextService } from '../../services/business-context.service';
import { SKIP_BUSINESS_CONTEXT } from '../../services/business-context.store';
import { BusinessService } from '../../services/business.service';
import { BusinessContextSelectorComponent } from './business-context-selector.component';

describe('BusinessContextSelectorComponent presentation', () => {
  let fixture: ComponentFixture<BusinessContextSelectorComponent>;
  let superAdmin: boolean;
  let selected$: BehaviorSubject<{ businessId: number; businessName?: string } | null>;
  let restoring: ReturnType<typeof signal<boolean>>;
  let context: { selected$: typeof selected$; restoring: typeof restoring; select: ReturnType<typeof vi.fn> };
  let getBusinesses: ReturnType<typeof vi.fn>;
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement>(selector: string) => root().querySelector(selector) as T;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  async function create(isSuperAdmin: boolean) {
    superAdmin = isSuperAdmin;
    selected$ = new BehaviorSubject<{ businessId: number; businessName?: string } | null>(null);
    restoring = signal(false);
    context = { selected$, restoring, select: vi.fn(() => of({})) };
    getBusinesses = vi.fn(() => of({
      data: [
        { businessId: 1, businessName: 'Alpha Retail', isActive: true },
        { businessId: 2, businessName: 'Beta Foods', isActive: true },
        { businessId: 3, businessName: 'Closed Co', isActive: false }
      ]
    }));

    TestBed.configureTestingModule({
      imports: [BusinessContextSelectorComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser$: new BehaviorSubject(null), isSuperAdmin: () => superAdmin } },
        { provide: BusinessService, useValue: { getBusinesses } },
        { provide: BusinessContextService, useValue: context }
      ]
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(BusinessContextSelectorComponent);
    await settle();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders nothing and requests no Business list for a regular user', async () => {
    await create(false);
    expect(root().querySelector('[data-testid="business-context-selector"]')).toBeNull();
    expect(getBusinesses).not.toHaveBeenCalled();
  });

  it('renders a labelled group for a Super Admin and lists only active Businesses', async () => {
    await create(true);
    expect(q('[data-testid="business-context-selector"]').getAttribute('aria-label')).toBe('Business context');
    expect(q('label[for="business-context-select"]').textContent).toContain('Business context');
    const options = Array.from(q<HTMLSelectElement>('#business-context-select').options).map(o => o.textContent!.trim());
    expect(options).toEqual(['Platform (no Business selected)', 'Alpha Retail', 'Beta Foods']);
  });

  it('lists Businesses without sending the current Business context', async () => {
    await create(true);
    expect(getBusinesses).toHaveBeenCalledTimes(1);
    const httpContext = getBusinesses.mock.calls[0][2];
    expect(httpContext.get(SKIP_BUSINESS_CONTEXT)).toBe(true);
  });

  it('never selects a Business automatically and keeps Apply disabled until the choice changes', async () => {
    await create(true);
    expect(context.select).not.toHaveBeenCalled();
    expect(q<HTMLButtonElement>('.rp-context__apply').disabled).toBe(true);
    expect(q('[data-testid="business-context-active"]').textContent).toContain('Active: Platform');

    const select = q<HTMLSelectElement>('#business-context-select');
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    await settle();
    expect(q<HTMLButtonElement>('.rp-context__apply').disabled).toBe(false);
    expect(context.select).not.toHaveBeenCalled();
  });

  it('applies the chosen Business through the verified service and returns to the dashboard', async () => {
    await create(true);
    const select = q<HTMLSelectElement>('#business-context-select');
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    await settle();

    q<HTMLButtonElement>('.rp-context__apply').click();
    expect(context.select).toHaveBeenCalledWith(2);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows the active Business and keeps the control disabled while a restore is in progress', async () => {
    await create(true);
    selected$.next({ businessId: 1, businessName: 'Alpha Retail' });
    restoring.set(true);
    await settle();

    expect(q('[data-testid="business-context-active"]').textContent).toContain('Active: Alpha Retail');
    expect(q<HTMLSelectElement>('#business-context-select').disabled).toBe(true);
    expect(q<HTMLButtonElement>('.rp-context__apply').disabled).toBe(true);
  });

  it('announces a rejected change as an alert, stays on the page and restores the previous choice', async () => {
    await create(true);
    selected$.next({ businessId: 1, businessName: 'Alpha Retail' });
    await settle();
    context.select.mockReturnValue(throwError(() => ({ error: { message: 'Business not permitted' } })));

    const select = q<HTMLSelectElement>('#business-context-select');
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    await settle();
    q<HTMLButtonElement>('.rp-context__apply').click();
    await settle();

    expect(q('[role="alert"]').textContent).toContain('Business not permitted');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(q('[data-testid="business-context-active"]').textContent).toContain('Active: Alpha Retail');
  });
});
