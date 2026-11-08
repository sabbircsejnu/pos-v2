import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/**
 * Browser connectivity as a signal (navigator.onLine plus the window online/offline events).
 * Exchange is online-only (EX-28): screens disable their actions while offline and never queue requests.
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private readonly onlineState = signal(typeof navigator === 'undefined' ? true : navigator.onLine !== false);
  readonly online = this.onlineState.asReadonly();

  constructor() {
    if (typeof window === 'undefined') return;
    const goOnline = () => this.onlineState.set(true);
    const goOffline = () => this.onlineState.set(false);
    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
    });
  }
}
