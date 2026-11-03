// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { SupportBarChartComponent } from './support-bar-chart/support-bar-chart.component';
import { SatisfactionChartComponent } from './satisfaction-chart/satisfaction-chart.component';

@Component({
  selector: 'app-helpdesk-dashboard',
  imports: [SharedModule, SupportBarChartComponent, SatisfactionChartComponent, CommonModule],
  templateUrl: './helpdesk-dashboard.component.html',
  styleUrl: './helpdesk-dashboard.component.scss'
})
export class HelpdeskDashboardComponent {
  // public method
  socialMedia = [
    {
      name: 'Facebook Source',
      color: 'primary',
      sourceList: [
        {
          title: 'Page Profile',
          value: 25
        },
        {
          title: 'Favorite',
          value: 85
        },
        {
          title: 'Like Story',
          value: 65
        }
      ]
    },
    {
      name: 'Twitter Source',
      color: 'warn',
      sourceList: [
        {
          title: 'Wall Profile',
          value: 85
        },
        {
          title: 'Favorite',
          value: 25
        },
        {
          title: 'Like Tweets',
          value: 65
        }
      ]
    }
  ];

  activityList = [
    {
      color: 'bg-primary-50 text-primary-500',
      icon: '#taskSquare',
      title: 'You have 3 pending tasks.'
    },
    {
      color: 'bg-warn-50 text-warn-500',
      icon: '#custom-shopping-cart',
      title: 'New order received'
    },
    {
      color: 'bg-success-50 text-success-500',
      icon: '#custom-document-text',
      title: 'You have 3 pending tasks.'
    },
    {
      color: 'bg-warning-50 text-warning-500',
      icon: '#taskSquare',
      title: 'New order received'
    },
    {
      color: 'bg-primary-50 text-primary-500',
      icon: '#custom-shopping-cart',
      title: 'You have 3 pending tasks.'
    },
    {
      color: 'bg-warn-50 text-warn-500',
      icon: '#custom-document-text',
      title: 'New order received'
    }
  ];
}
