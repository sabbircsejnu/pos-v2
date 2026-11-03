// angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-membership-setting',
  imports: [SharedModule],
  templateUrl: './membership-setting.component.html',
  styleUrl: './membership-setting.component.scss'
})
export class MembershipSettingComponent {
  // public method
  memberPlan = [
    {
      title: 'Membership Plan',
      value: 'Addicted $150',
      more: 'See more Plan'
    },
    {
      title: 'Manage',
      value: 'Membership',
      more: 'Update, Cancel and more'
    },
    {
      title: 'Renewal Date',
      value: '120 November, 2024',
      more: 'View payment method'
    }
  ];
}
