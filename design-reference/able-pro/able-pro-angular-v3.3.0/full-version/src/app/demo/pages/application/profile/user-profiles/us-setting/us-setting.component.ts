// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-us-setting',
  imports: [CommonModule, SharedModule],
  templateUrl: './us-setting.component.html',
  styleUrls: ['./us-setting.component.scss']
})
export class UsSettingComponent {
  settings = [
    {
      icon: 'ti ti-file-invoice',
      title: 'Order Confirmation',
      text: 'You will be notified when customer order any product'
    },
    {
      icon: 'ti ti-notification',
      title: 'Update System Notification',
      text: 'You will be notified when customer order any product'
    },
    {
      icon: 'ti ti-writing-sign',
      title: 'Language Change',
      text: 'You will be notified when customer order any product'
    },
    {
      icon: 'ti ti-mail',
      title: 'Setup Email Notification',
      text: 'You will be notified when customer order any product'
    }
  ];
}
