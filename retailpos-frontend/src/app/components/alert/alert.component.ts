import { Component, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AlertService } from '../../services/alert.service';

@Component({
  selector: 'app-alert',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (alertConfig()) {
      <div class="fixed inset-0 z-50 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
        <!-- Backdrop -->
        <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" (click)="onBackdropClick()"></div>

        <!-- Modal -->
        <div class="flex min-h-full items-center justify-center p-4">
          <div class="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-lg">
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <!-- Icon -->
                <div [ngClass]="iconWrapperClass()">
                  @if (alertConfig()!.type === 'success') {
                    <svg class="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                  } @else if (alertConfig()!.type === 'error') {
                    <svg class="h-6 w-6 text-red-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
                    </svg>
                  } @else if (alertConfig()!.type === 'warning') {
                    <svg class="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
                    </svg>
                  } @else if (alertConfig()!.type === 'info') {
                    <svg class="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M11.25 11.25l.041-.02a.75.75 0 011.063.852l-.708 2.836a.75.75 0 001.063.853l.041-.021M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9-3.75h.008v.008H12V8.25z" />
                    </svg>
                  } @else if (alertConfig()!.type === 'confirm') {
                    <svg class="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9 5.25h.008v.008H12v-.008z" />
                    </svg>
                  }
                </div>

                <!-- Content -->
                <div class="mt-3 text-center sm:ml-4 sm:mt-0 sm:text-left flex-1">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900" id="modal-title">
                    {{ alertConfig()!.title }}
                  </h3>
                  <div class="mt-2">
                    <p class="text-sm text-gray-500">
                      {{ alertConfig()!.message }}
                    </p>
                  </div>
                </div>
              </div>
            </div>

            <!-- Actions -->
            <div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6">
              @if (alertConfig()!.type === 'confirm') {
                <button type="button" (click)="onConfirm()" 
                  class="inline-flex w-full justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 sm:ml-3 sm:w-auto">
                  {{ alertConfig()!.confirmText }}
                </button>
                <button type="button" (click)="onCancel()"
                  class="mt-3 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto">
                  {{ alertConfig()!.cancelText }}
                </button>
              } @else {
                <button type="button" (click)="close()" [ngClass]="buttonClass()"
                  class="inline-flex w-full justify-center rounded-md px-3 py-2 text-sm font-semibold text-white shadow-sm sm:ml-3 sm:w-auto">
                  OK
                </button>
              }
            </div>
          </div>
        </div>
      </div>
    }
  `,
  styles: []
})
export class AlertComponent {
  alertConfig = computed(() => this.alertService.config());

  constructor(private alertService: AlertService) {}

  iconWrapperClass = computed(() => {
    const type = this.alertConfig()?.type;
    const baseClasses = 'mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full sm:mx-0 sm:h-10 sm:w-10';
    
    switch (type) {
      case 'success':
        return `${baseClasses} bg-green-100`;
      case 'error':
        return `${baseClasses} bg-red-100`;
      case 'warning':
        return `${baseClasses} bg-yellow-100`;
      case 'info':
      case 'confirm':
        return `${baseClasses} bg-blue-100`;
      default:
        return baseClasses;
    }
  });

  buttonClass = computed(() => {
    const type = this.alertConfig()?.type;
    
    switch (type) {
      case 'success':
        return 'bg-green-600 hover:bg-green-500';
      case 'error':
        return 'bg-red-600 hover:bg-red-500';
      case 'warning':
        return 'bg-yellow-600 hover:bg-yellow-500';
      case 'info':
        return 'bg-blue-600 hover:bg-blue-500';
      default:
        return 'bg-gray-600 hover:bg-gray-500';
    }
  });

  onConfirm(): void {
    const config = this.alertConfig();
    if (config?.onConfirm) {
      config.onConfirm();
    }
    this.close();
  }

  onCancel(): void {
    const config = this.alertConfig();
    if (config?.onCancel) {
      config.onCancel();
    }
    this.close();
  }

  onBackdropClick(): void {
    if (this.alertConfig()?.type !== 'confirm') {
      this.close();
    }
  }

  close(): void {
    this.alertService.close();
  }
}
