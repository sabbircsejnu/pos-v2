// angular import
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

// project imports
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { StatisticsChartComponent } from '../../../apex-chart/statistics-chart/statistics-chart.component';
import { StateChartComponent } from './state-chart/state-chart.component';
import { ActivityChartComponent } from './activity-chart/activity-chart.component';
import { signupData } from 'src/app/fake-data/latest_signup_data';

export interface signup_Data {
  image: string;
  name: string;
  email: string;
  date: string;
}

const signup_Data = signupData;

@Component({
  selector: 'app-membership-dashboard',
  imports: [SharedModule, CommonModule, StatisticsChartComponent, StateChartComponent, ActivityChartComponent],
  templateUrl: './membership-dashboard.component.html',
  styleUrl: './membership-dashboard.component.scss'
})
export class MembershipDashboardComponent {
  signup: string[] = ['Name', 'Email', 'Date'];
  signupSource = signup_Data;

  // public method
  membership = [
    {
      title: 'Registrations',
      value: '980+',
      date: 'May 23 - June 01 (2018)',
      icon: '#bookmark',
      background: 'bg-primary-50 text-primary-500'
    },
    {
      title: 'Renewals',
      value: '1,563',
      date: 'May 23 - June 01 (2018)',
      icon: '#refreshCircle',
      background: 'bg-success-50 text-success-500'
    },
    {
      title: 'Revenue',
      value: '42.6%',
      date: 'May 23 - June 01 (2018)',
      icon: '#money',
      background: 'bg-warn-50 text-warn-500'
    },
    {
      title: 'Cancellations',
      value: '30.7%',
      date: 'May 23 - June 01 (2018)',
      icon: '#profile-delete',
      background: 'bg-warn-50 text-warn-500'
    }
  ];

  tasks = [
    {
      title: 'Realize offers!',
      time: '16:00',
      border_color: 'border-success'
    },
    {
      title: 'Add new members.',
      time: '14:00',
      border_color: 'border-warning'
    },
    {
      title: 'Add new benefit list.',
      time: '13:00',
      border_color: 'border-primary'
    },
    {
      title: 'Second offer is end!',
      time: '09:00',
      border_color: 'border-warn'
    }
  ];

  notifications = [
    {
      image: 'assets/images/user/avatar-1.png',
      title: 'Brieley join casual membership..',
      date: 'Today | 9:00 AM'
    },
    {
      image: 'assets/images/user/avatar-5.jpg',
      title: 'Ashton end membership planing',
      date: 'Yesterday | 6:30 PM'
    },
    {
      image: 'assets/images/user/avatar-2.png',
      title: 'Airi canceled in 2 months membership',
      date: '05 Feb | 1:45 PM'
    },
    {
      image: 'assets/images/user/avatar-3.png',
      title: 'Colleen join Addicted membership',
      date: '05 Feb | 2:45 PM'
    },
    {
      image: 'assets/images/user/avatar-4.jpg',
      title: 'Airi canceled in 2 months membership',
      date: '05 Feb | 3:45 PM'
    },
    {
      image: 'assets/images/user/avatar-6.jpg',
      title: 'Airi canceled in 2 months membership',
      date: '05 Feb | 4:00 PM'
    }
  ];
}
