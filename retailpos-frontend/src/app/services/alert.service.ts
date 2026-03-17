import { Injectable, signal } from '@angular/core';

export interface AlertConfig {
  type: 'success' | 'error' | 'warning' | 'info' | 'confirm';
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  onConfirm?: () => void;
  onCancel?: () => void;
}

@Injectable({
  providedIn: 'root'
})
export class AlertService {
  private alertConfig = signal<AlertConfig | null>(null);
  
  get config() {
    return this.alertConfig.asReadonly();
  }

  success(message: string, title: string = 'Success'): void {
    this.alertConfig.set({
      type: 'success',
      title,
      message
    });
  }

  error(message: string, title: string = 'Error'): void {
    this.alertConfig.set({
      type: 'error',
      title,
      message
    });
  }

  warning(message: string, title: string = 'Warning'): void {
    this.alertConfig.set({
      type: 'warning',
      title,
      message
    });
  }

  info(message: string, title: string = 'Information'): void {
    this.alertConfig.set({
      type: 'info',
      title,
      message
    });
  }

  confirm(
    message: string,
    onConfirm: () => void,
    title: string = 'Confirm',
    confirmText: string = 'Confirm',
    cancelText: string = 'Cancel'
  ): void {
    this.alertConfig.set({
      type: 'confirm',
      title,
      message,
      confirmText,
      cancelText,
      onConfirm
    });
  }

  close(): void {
    this.alertConfig.set(null);
  }
}
