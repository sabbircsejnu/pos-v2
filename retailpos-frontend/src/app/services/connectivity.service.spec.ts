import { TestBed } from '@angular/core/testing';
import { ConnectivityService } from './connectivity.service';

describe('ConnectivityService', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('starts from navigator.onLine', () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    expect(TestBed.inject(ConnectivityService).online()).toBe(false);
  });

  it('follows the window online and offline events', () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const service = TestBed.inject(ConnectivityService);
    expect(service.online()).toBe(true);

    window.dispatchEvent(new Event('offline'));
    expect(service.online()).toBe(false);

    window.dispatchEvent(new Event('online'));
    expect(service.online()).toBe(true);
  });
});
