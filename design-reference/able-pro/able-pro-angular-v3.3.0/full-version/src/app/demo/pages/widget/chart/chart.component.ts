// angular import
import { Component, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ChartDB } from 'src/app/fake-data/chartDB';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { EarningChartComponent } from '../../apex-chart/earning-chart/earning-chart.component';
import { NewOrderChartComponent } from '../../apex-chart/new-order-chart/new-order-chart.component';
import { NewUserChartComponent } from '../../apex-chart/new-user-chart/new-user-chart.component';
import { VisitorsChartComponent } from '../../apex-chart/visitors-chart/visitors-chart.component';
import { MonthlyOverviewChartComponent } from '../../apex-chart/monthly-overview-chart/monthly-overview-chart.component';
import { IncomeChartComponent } from '../../apex-chart/income-chart/income-chart.component';
import { LanguagesSupportChartComponent } from '../../apex-chart/languages-support-chart/languages-support-chart.component';
import { ProjectOverviewChartComponent } from '../../apex-chart/project-overview-chart/project-overview-chart.component';
import { OverviewProductChartComponent } from '../../apex-chart/overview-product-chart/overview-product-chart.component';
import { TotalIncomeChartComponent } from '../../apex-chart/total-income-chart/total-income-chart.component';
import { TotalEarningChartComponent } from '../../apex-chart/total-earning-chart/total-earning-chart.component';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

type SeriesData = { name: string; data: number[] }[];

@Component({
  selector: 'app-chart',
  imports: [
    CommonModule,
    SharedModule,
    NgApexchartsModule,
    LanguagesSupportChartComponent,
    EarningChartComponent,
    NewOrderChartComponent,
    IncomeChartComponent,
    NewUserChartComponent,
    VisitorsChartComponent,
    MonthlyOverviewChartComponent,
    ProjectOverviewChartComponent,
    OverviewProductChartComponent,
    TotalIncomeChartComponent,
    TotalEarningChartComponent
  ],
  templateUrl: './chart.component.html',
  styleUrls: ['./chart.component.scss']
})
export class WidgetChartComponent {
  themeService = inject(ThemeLayoutService);

  // public props
  customerRateChart: Partial<ApexOptions>;
  monthlyReportChart: Partial<ApexOptions>;
  salesReportChart: Partial<ApexOptions>;
  acquisitionChart: Partial<ApexOptions>;
  activeTab = 1;

  preset = ['var(--primary-500)'];
  salesReportColor = ['#E58A00', 'var(--primary-500)'];
  // eslint-disable-next-line
  chartDB: any;

  // constructor
  constructor() {
    this.chartDB = ChartDB;
    const { customerRateChart, monthlyReportChart, salesReportChart, acquisitionChart } = this.chartDB;
    this.customerRateChart = customerRateChart;
    this.monthlyReportChart = monthlyReportChart;
    this.salesReportChart = salesReportChart;
    this.acquisitionChart = acquisitionChart;
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  private isDarkTheme(isDarkMode: string) {
    const tooltip = {
      ...this.customerRateChart.tooltip,
      ...this.monthlyReportChart.tooltip,
      ...this.salesReportChart.tooltip,
      ...this.acquisitionChart.tooltip
    };
    tooltip.theme = isDarkMode === DARK ? DARK : LIGHT;
    this.customerRateChart = { ...this.customerRateChart, tooltip };
    this.monthlyReportChart = { ...this.monthlyReportChart, tooltip };
    this.salesReportChart = { ...this.salesReportChart, tooltip };
    this.acquisitionChart = { ...this.acquisitionChart, tooltip };
  }

  // public method
  acquisition = [
    {
      title: 'Top Channels',
      icon: 'ti ti-device-analytics',
      time: 'Today, 2:00 AM',
      background: 'bg-accent-200'
    },
    {
      title: 'Top pages',
      icon: 'ti ti-file-text',
      time: 'Today 6:00 AM',
      background: 'bg-primary-50 text-primary-500'
    }
  ];

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

  currentSeries = this.chartDataMap[1];

  setActiveTab(tab: number) {
    this.activeTab = tab;
    this.currentSeries = (this.chartDataMap as Record<number, SeriesData>)[tab] || [];
  }
}
