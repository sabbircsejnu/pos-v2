import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HrOperationalLocationsPageComponent } from './hr-operational-locations-page.component';
import { HrOperationalLocation } from '../../../services/hr-organisation.service';
import { environment } from '../../../../environments/environment';

describe('HR operational locations page (UI kit)', () => {
  const url = `${environment.apiUrl}/hr/v1/operational-locations`;
  afterEach(() => TestBed.resetTestingModule());

  function location(id: number, name: string, over: Partial<HrOperationalLocation> = {}): HrOperationalLocation {
    return { id, businessId: 1, sourceSystem: 'Retail', sourceEntityType: 'Outlet', sourceEntityId: String(id), name,
      address: `${name} street`, sourceVersion: 4, sourceUpdatedAtUtc: '2026-01-01T00:00:00Z',
      lastSyncedAtUtc: '2026-01-01T00:00:00Z', isSourceActive: true, isSourceDeleted: false, syncStatus: 'Current', ...over };
  }

  async function create() {
    await TestBed.configureTestingModule({
      imports: [HrOperationalLocationsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();
    const fixture = TestBed.createComponent(HrOperationalLocationsPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    return { fixture, http, el: fixture.nativeElement as HTMLElement, settle };
  }

  const bodyRows = (el: HTMLElement) => Array.from(el.querySelectorAll('tbody tr'));

  it('renders one h1 with the read-only explanation and a breadcrumb', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([]);
    await settle();
    expect(el.querySelector('main')).not.toBeNull();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Operational Locations');
    expect(el.textContent).toContain('Outlet and Warehouse records are read-only and owned by their source modules.');
    expect(el.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe('/dashboard');
  });

  it('shows the loading status then the named table region', async () => {
    const { http, el, settle } = await create();
    await settle();
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Loading operational locations...');
    http.expectOne(url).flush([location(1, 'Main')]);
    await settle();
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Operational locations');
    const headers = Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim());
    expect(headers).toEqual(['Type', 'Name', 'Address', 'Source version', 'Status']);
  });

  it('maps deleted, active and inactive sources to visible status badges', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([
      location(1, 'Alpha'),
      location(2, 'Beta', { isSourceActive: false }),
      location(3, 'Gamma', { isSourceDeleted: true, isSourceActive: true })
    ]);
    await settle();
    const badges = Array.from(el.querySelectorAll('tbody app-ui-badge')).map(b => b.textContent?.trim());
    expect(badges).toEqual(['Active', 'Inactive', 'Deleted']);
    expect(bodyRows(el)[0].textContent).toContain('Alpha street');
    expect(bodyRows(el)[0].textContent).toContain('4');
  });

  it('searches and sorts the loaded locations client-side', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([
      location(1, 'Zulu', { sourceEntityType: 'Warehouse' }),
      location(2, 'Alpha')
    ]);
    await settle();
    const nameHeader = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Name'))!;
    nameHeader.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(nameHeader.getAttribute('aria-sort')).toBe('ascending');
    expect(bodyRows(el)[0].textContent).toContain('Alpha');

    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'warehouse';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(bodyRows(el).length).toBe(1);
    expect(bodyRows(el)[0].textContent).toContain('Zulu');
  });

  it('shows the empty state text', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush([]);
    await settle();
    expect(el.textContent).toContain('No operational locations found.');
  });

  it('shows the load failure as an alert with the existing message', async () => {
    const { http, el, settle } = await create();
    http.expectOne(url).flush({}, { status: 500, statusText: 'Server error' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load operational locations.');
  });
});
