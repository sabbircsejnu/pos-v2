// angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-notification',
  imports: [SharedModule],
  templateUrl: './notification.component.html',
  styleUrl: './notification.component.scss'
})
export class NotificationComponent {
  notifications = [
    {
      title: 'Enrollment Notifications',
      description: 'Get notified via email whenever a student enrolls in your school and/or courses.',
      type: [
        {
          checked: true,
          name: 'When a new student joins the school'
        },
        {
          checked: true,
          name: 'When a student enrolls in a paid course'
        },
        {
          checked: false,
          name: 'When a student enrolls in a free course'
        }
      ]
    },
    {
      title: 'Comment Notifications',
      description: 'Get alerted via email whenever someone engages in a commenting action.',
      type: [
        {
          checked: true,
          name: 'When a new comment is posted that requires moderation'
        },
        {
          checked: false,
          name: 'When a new comment is posted on one of your courses'
        },
        {
          checked: true,
          name: "When a new comment is posted in a thread you've commented on"
        }
      ]
    },
    {
      title: 'Subscription Notifications',
      description: 'Get email notifications for specific subscription events.',
      type: [
        {
          checked: true,
          name: 'When a subscription payment fails or a subscription is canceled due to non-payment'
        },
        {
          checked: true,
          name: 'When a student cancels their subscription to one of your courses'
        }
      ]
    }
  ];
}
