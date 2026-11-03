//  angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { InvoiceChartComponent } from './invoice-chart/invoice-chart.component';
import { TotalExpensesChartComponent } from './total-expenses-chart/total-expenses-chart.component';

@Component({
  selector: 'app-invoice-dashboard',
  imports: [SharedModule, InvoiceChartComponent, TotalExpensesChartComponent],
  templateUrl: './invoice-dashboard.component.html',
  styleUrl: './invoice-dashboard.component.scss'
})
export class InvoiceDashboardComponent {
  // public method
  invoiceCard = [
    {
      title: 'All Invoices',
      icon: '#custom-document-text',
      background: 'bg-primary-500'
    },
    {
      title: 'Reports',
      icon: '#custom-archive-book',
      background: 'bg-cyan-500'
    },
    {
      title: 'Paid',
      icon: '#custom-dollar-circle',
      background: 'bg-success-500'
    },
    {
      title: 'Pending',
      icon: '#custom-document-filter',
      background: 'bg-warning-500'
    },
    {
      title: 'Cancel',
      icon: '#custom-close-circle',
      background: 'bg-warn-500'
    },
    {
      title: 'Draft',
      icon: '#custom-shopping-bag',
      background: 'bg-primary-500'
    }
  ];

  UserList = [
    {
      avatar: 'assets/images/user/avatar-10.jpg',
      name: 'David Jones',
      value: '$329.20',
      time: '5 min ago'
    },
    {
      avatar: 'assets/images/user/avatar-8.jpg',
      name: 'Jenny Jones',
      value: '$182.89',
      time: '1 day ago'
    },
    {
      avatar: 'assets/images/user/avatar-6.jpg',
      name: 'Harry Ben',
      value: '3 week ago',
      time: '5 min ago'
    },
    {
      avatar: 'assets/images/user/avatar-5.jpg',
      name: 'Jenifer Vintage',
      value: '$182.89',
      time: '3 week ago'
    },
    {
      avatar: 'assets/images/user/avatar-3.png',
      name: 'Stebin Ben',
      value: '3 week ago',
      time: '1 month ago'
    }
  ];

  notificationList = [
    {
      title: 'Johnny sent you an invoice billed',
      link: true,
      linkValue: '$1,000',
      date: '2 August',
      icon: 'download',
      background: 'bg-success-50 text-success-500',
      text: false
    },
    {
      title: 'Sent an invoice to Aida Bugg amount of',
      link: true,
      linkValue: '$200',
      date: '7 hours ago',
      icon: 'file-text',
      background: 'bg-primary-50 text-primary-500',
      text: false
    },
    {
      title: 'Cristina danny invited to you join Meetingp',
      link: false,
      date: '6 hours ago',
      background: 'bg-warning-50 text-warning-500',
      text: true
    },
    {
      title: 'There was a failure to your setup',
      link: false,
      date: '5 hours ago',
      icon: 'settings',
      background: 'bg-warn-50 text-warn-500',
      text: false
    },
    {
      title: 'Cristina danny invited to you join Meetingp',
      link: false,
      date: '5 hours ago',
      background: 'bg-primary-50 text-primary-500',
      text: true
    }
  ];
}
