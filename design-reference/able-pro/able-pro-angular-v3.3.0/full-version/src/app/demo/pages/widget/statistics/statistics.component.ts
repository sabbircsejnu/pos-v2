// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-statistics',
  imports: [CommonModule, SharedModule],
  templateUrl: './statistics.component.html',
  styleUrls: ['./statistics.component.scss']
})
export class StatisticsComponent {
  // public method
  card = [
    {
      number: '$30200',
      text: 'All Earnings',
      icon: 'ti ti-chart-bar text-accent-600'
    },
    {
      number: '145',
      text: 'Task',
      icon: 'ti ti-calendar-event text-warn-500'
    },
    {
      number: '290+',
      text: 'Page View',
      icon: 'ti ti-file-text text-success-500'
    },
    {
      number: '500',
      text: 'Downloads',
      icon: 'ti ti-download text-primary-500'
    }
  ];

  social_card = [
    {
      number: '1165+',
      text: 'Facebook Users',
      icon: 'fab fa-facebook',
      background: 'bg-primary-500'
    },
    {
      number: '780+',
      text: 'Twitter Users',
      icon: 'fab fa-twitter',
      background: 'bg-info'
    },
    {
      number: '998+',
      text: 'Linked In Users',
      icon: 'fab fa-linkedin-in',
      background: 'bg-dark'
    },
    {
      number: '650+',
      text: 'Youtube Videos',
      icon: 'fab fa-youtube',
      background: 'bg-warn-500'
    }
  ];

  percentage_card = [
    {
      text: 'Total Page Views',
      number: '4,42,236',
      percentage: '59.3%',
      sale: '35,000',
      icon: 'ti ti-trending-up',
      text_color: 'text-primary-500'
    },
    {
      text: 'Total Users',
      number: '78,250',
      percentage: ' 70.5%',
      sale: '8,900',
      icon: 'ti ti-trending-up',
      color: 'success',
      text_color: 'text-success-500'
    },
    {
      text: 'Total Order',
      number: '18,800',
      percentage: ' 27.4%',
      sale: '1,943',
      color: 'warning',
      icon: 'ti ti-trending-down',
      text_color: 'text-warning-500'
    },
    {
      text: 'Total Sales',
      number: '$35,078',
      percentage: '27.4%',
      sale: '$20,395 ',
      color: 'warn',
      icon: 'ti ti-trending-down',
      text_color: 'text-success-500'
    }
  ];

  progressData = [
    { value: 25, color: 'primary' },
    { value: 50, color: 'accent' },
    { value: 75, color: 'warn' }
  ];
}
