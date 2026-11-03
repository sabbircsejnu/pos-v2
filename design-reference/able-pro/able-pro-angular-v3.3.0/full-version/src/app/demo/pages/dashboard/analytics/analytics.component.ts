// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { NewOrderChartComponent } from '../../apex-chart/new-order-chart/new-order-chart.component';
import { NewUserChartComponent } from '../../apex-chart/new-user-chart/new-user-chart.component';
import { VisitorsChartComponent } from '../../apex-chart/visitors-chart/visitors-chart.component';
import { MonthlyOverviewChartComponent } from '../../apex-chart/monthly-overview-chart/monthly-overview-chart.component';
import { IncomeChartComponent } from '../../apex-chart/income-chart/income-chart.component';
import { LanguagesSupportChartComponent } from '../../apex-chart/languages-support-chart/languages-support-chart.component';
import { OverviewProductChartComponent } from '../../apex-chart/overview-product-chart/overview-product-chart.component';
import { TotalEarningChartComponent } from '../../apex-chart/total-earning-chart/total-earning-chart.component';

type SeriesData = { name: string; data: number[] }[];

@Component({
  selector: 'app-analytics',
  imports: [
    CommonModule,
    SharedModule,
    NewOrderChartComponent,
    NewUserChartComponent,
    LanguagesSupportChartComponent,
    VisitorsChartComponent,
    MonthlyOverviewChartComponent,
    IncomeChartComponent,
    OverviewProductChartComponent,
    TotalEarningChartComponent
  ],
  templateUrl: './analytics.component.html',
  styleUrls: ['../dashboard.scss', './analytics.component.scss']
})
export class AnalyticsComponent {
  activeTab = 1;

  tabs = [
    {
      id: 1,
      title: 'Overview',
      active: true
    },
    {
      id: 2,
      title: 'Marketing',
      active: false
    },
    {
      id: 3,
      title: 'Project',
      active: false
    },
    {
      id: 4,
      title: 'Order',
      active: false
    }
  ];

  chartDataMap = {
    1: [
      { name: 'Net Profit', data: [76, 85, 101, 98, 87, 105, 91] },
      { name: 'Revenue', data: [44, 55, 57, 56, 61, 58, 63] }
    ],
    2: [
      { name: 'Net Profit', data: [80, 101, 90, 65, 120, 105, 85] },
      { name: 'Revenue', data: [45, 30, 57, 45, 78, 48, 63] }
    ],
    3: [
      { name: 'Net Profit', data: [79, 85, 107, 95, 83, 115, 97] },
      { name: 'Revenue', data: [48, 56, 50, 54, 68, 53, 65] }
    ],
    4: [
      { name: 'Net Profit', data: [90, 111, 105, 55, 70, 65, 75] },
      { name: 'Revenue', data: [55, 80, 57, 45, 38, 48, 43] }
    ]
  };

  currentSeries = this.chartDataMap[1];

  setActiveTab(tab: number) {
    this.activeTab = tab;
    this.currentSeries = (this.chartDataMap as Record<number, SeriesData>)[tab] || [];
  }

  // public method
  payment = [
    {
      img: 'assets/images/widget/img-paypal.png',
      payment_name: 'Paypal',
      amount: '+210,000',
      color: 'text-success-500',
      percentage: '+ 30.6%'
    },
    {
      img: 'assets/images/widget/img-gpay.png',
      payment_name: 'Gpay',
      amount: '-$2,000',
      color: 'text-warn-500',
      percentage: '- 30.6%'
    },
    {
      img: 'assets/images/widget/img-phonepay.png',
      payment_name: 'Phone Pay',
      amount: '-$2,000',
      color: 'text-warn-500',
      percentage: '- 30.6%'
    }
  ];

  project_report = [
    {
      icon: 'ti ti-chart-bar',
      title: 'Total Sales',
      amount: '1,800',
      number: '- 245',
      percentage: '30.6%',
      color: 'text-warn-500',
      icon_2: 'ti ti-arrow-down-left'
    },
    {
      icon: 'ti ti-chart-arrows-vertical',
      title: 'Revenue',
      amount: '$5667',
      number: '+ $2100',
      percentage: '30.6%',
      color: 'text-success-500',
      icon_2: 'ti ti-arrow-up-right'
    },
    {
      icon: 'ti ti-shopping-cart',
      title: 'Abandon Cart',
      amount: '128',
      number: '- 26',
      percentage: '5%',
      color: 'text-warning-500',
      icon_2: 'ti ti-arrows-left-right'
    },
    {
      icon: 'ti ti-ad',
      title: 'Ads Spent',
      amount: '$2500',
      number: '$200',
      percentage: '10.6%',
      color: 'text-success-500',
      icon_2: 'ti ti-arrow-up-right'
    }
  ];
}
