import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ac-setting',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-setting.component.html',
  styleUrls: ['../account-profile.scss', './ac-setting.component.scss']
})
export class AcSettingComponent {
  notification = [
    'News about PCT-themes products and feature updates',
    'Tips on getting more out of PCT-themes',
    'Things you missed since you last logged into PCT-themes',
    'News about products and other services',
    'Tips and Document business products'
  ];
}
