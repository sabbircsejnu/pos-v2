// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// angular import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ac-account',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-account.component.html',
  styleUrls: ['../account-profile.scss', './ac-account.component.scss']
})
export class AcAccountComponent {
  // public props
  selectedOption = '1';
  selectedPassword = '1';

  // public method
  advance_setting = [
    {
      space: 'p-t-0',
      title: 'Secure Browsing',
      text: 'Browsing Securely ( https ) when its necessary'
    },
    {
      title: 'Login Notifications',
      text: 'Notify when login attempted from other place'
    },
    {
      space: 'p-b-0',
      title: 'Login Approvals',
      text: 'Approvals is not required when login from unrecognized devices.'
    }
  ];

  device = [
    {
      space: 'p-t-0',
      title: 'Celt Desktop',
      text: '4351 Deans Lane',
      status_type: 'text-success-500',
      status: 'Current Active'
    },
    {
      title: 'Imco Tablet',
      text: '4185 Michigan Avenue',
      status_type: 'text-muted',
      status: 'Active 5 days ago'
    },
    {
      space: 'p-b-0',
      title: 'Albs Mobile',
      text: '3462 Fairfax Drive',
      status_type: 'text-muted',
      status: 'Active 1 month ago'
    }
  ];

  sessions = [
    {
      space: 'p-t-0',
      title: 'Celt Desktop',
      text: '4351 Deans Lane'
    },
    {
      space: 'p-b-0',
      title: 'Moon Tablet',
      text: '4185 Michigan Avenue'
    }
  ];
}
