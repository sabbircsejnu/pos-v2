// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { StatisticsChartComponent } from '../../../apex-chart/statistics-chart/statistics-chart.component';
import { InvitesGoalChartComponent } from './invites-goal-chart/invites-goal-chart.component';
import { CourseReportBarChartComponent } from './course-report-bar-chart/course-report-bar-chart.component';
import { TotalRevenueLineChartComponent } from './total-revenue-line-chart/total-revenue-line-chart.component';
import { StudentStatesChartComponent } from './student-states-chart/student-states-chart.component';
import { ActivityLineChartComponent } from './activity-line-chart/activity-line-chart.component';
import { activityData } from 'src/app/fake-data/activity_data';
import { VisitorsBarChartComponent } from './visitors-bar-chart/visitors-bar-chart.component';
import { EarningCoursesLineChartComponent } from './earning-courses-line-chart/earning-courses-line-chart.component';
import { courseStatesData } from 'src/app/fake-data/courseStates_data';

export interface activity_Data {
  image: string;
  name: string;
  qualification: string;
  rating: string;
}

export interface courseStates_data {
  name: string;
  teacher: string;
  rating: number;
  earning: string;
  sale: string;
}

const activity_Data = activityData;
const courseStates_data = courseStatesData;

@Component({
  selector: 'app-online-dashboard',
  imports: [
    SharedModule,
    CommonModule,
    StatisticsChartComponent,
    InvitesGoalChartComponent,
    CourseReportBarChartComponent,
    TotalRevenueLineChartComponent,
    StudentStatesChartComponent,
    ActivityLineChartComponent,
    VisitorsBarChartComponent,
    EarningCoursesLineChartComponent
  ],
  templateUrl: './online-dashboard.component.html',
  styleUrl: './online-dashboard.component.scss'
})
export class OnlineDashboardComponent {
  // public props
  selected: Date | null;

  activity: string[] = ['Name', 'Qualification', 'Rating'];
  activitySource = activity_Data;

  courseStates: string[] = ['Name', 'Teacher', 'Rating', 'Earning', 'Sale', 'Action'];
  courseSource = courseStates_data;

  // public methods
  dashboard_summary = [
    {
      icon: '#custom-profile-2user-outline',
      background: 'bg-primary-50 text-primary-500',
      title: 'New Students',
      value: '400+',
      percentage: '30.6%',
      color: 'text-success'
    },
    {
      icon: '#book',
      background: 'bg-warning-50 text-warning-500',
      title: 'Total Course',
      value: '520+',
      percentage: '30.6%',
      color: 'text-warning-500'
    },
    {
      icon: '#custom-eye',
      background: 'bg-success-50 text-success-500',
      title: 'New Visitor',
      value: '800+',
      percentage: '30.6%',
      color: 'text-success-500'
    },
    {
      icon: '#custom-card',
      background: 'bg-warn-50 text-warn-500',
      title: 'Total sale',
      value: '1065',
      percentage: '30.6%',
      color: 'text-warn-500'
    }
  ];

  course_list = [
    {
      title: 'Bootstrap 5 Beginner Course',
      image: 'assets/images/admin/img-bootstrap.svg'
    },
    {
      title: 'PHP Training Course',
      image: 'assets/images/admin/img-php.svg'
    },
    {
      title: 'UI/UX Training Course',
      image: 'assets/images/admin/img-ux.svg'
    },
    {
      title: 'Web Designing Course',
      image: 'assets/images/admin/img-web.svg'
    }
  ];

  queriesList = [
    {
      image: 'assets/images/user/avatar-2.png',
      title: 'Python $ Data Manage'
    },
    {
      image: 'assets/images/user/avatar-1.png',
      title: 'Website Error'
    },
    {
      image: 'assets/images/user/avatar-3.png',
      title: 'How to Illustrate'
    },
    {
      image: 'assets/images/user/avatar-4.jpg',
      title: 'PHP Learning'
    }
  ];

  trendingCourse = [
    {
      image: 'assets/images/admin/img-bootstrap.svg',
      title: 'Bootstrap 5 Beginner Course'
    },
    {
      image: 'assets/images/admin/img-php.svg',
      title: 'PHP Training Course'
    },
    {
      image: 'assets/images/admin/img-ux.svg',
      title: 'UI/UX Training Course'
    },
    {
      image: 'assets/images/admin/img-web.svg',
      title: 'Web Designing Course'
    },
    {
      image: 'assets/images/admin/img-c.svg',
      title: 'C Training Course'
    }
  ];

  notificationList = [
    {
      image: 'assets/images/user/avatar-1.png',
      title: 'Report Successfully',
      time: 'Today | 9:00 AM'
    },
    {
      image: 'assets/images/user/avatar-5.jpg',
      title: 'Reminder: Test time',
      time: 'Yesterday | 6:30 PM'
    },
    {
      image: 'assets/images/user/avatar-3.png',
      title: 'Send course pdf',
      time: '05 Feb | 3:45 PM'
    },
    {
      image: 'assets/images/user/avatar-2.png',
      title: 'Report Successfully',
      time: '05 Feb | 4:00 PM'
    }
  ];
}
