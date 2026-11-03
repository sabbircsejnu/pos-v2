// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { EarningChartComponent } from '../../apex-chart/earning-chart/earning-chart.component';
import { RevenueChartComponent } from '../../apex-chart/revenue-chart/revenue-chart.component';
import { ProjectOverviewChartComponent } from '../../apex-chart/project-overview-chart/project-overview-chart.component';
import { TotalIncomeChartComponent } from '../../apex-chart/total-income-chart/total-income-chart.component';

// service
import { BuyNowLinkService } from 'src/app/@theme/services/buy-now-link.service';

@Component({
  selector: 'app-default',
  imports: [
    CommonModule,
    SharedModule,
    TotalIncomeChartComponent,
    ProjectOverviewChartComponent,
    EarningChartComponent,
    RevenueChartComponent
  ],
  templateUrl: './default.component.html',
  styleUrls: ['../dashboard.scss', './default.component.scss']
})
export class DefaultComponent {
  buyNowLinkService = inject(BuyNowLinkService);

  // public method
  project = [
    {
      title: 'Invoice Generator'
    },
    {
      title: 'Package Upgrades'
    },
    {
      title: 'Figma Auto Layout'
    },
    {
      title: 'Package Upgrades'
    }
  ];

  List_transaction = [
    {
      icon: 'AI',
      name: 'Apple Inc.',
      time: '#ABLE-PRO-T00232',
      amount: '$210,000',
      amount_position: 'ti ti-arrow-down-left',
      percentage: '10.6%',
      amount_type: 'text-warn-500'
    },
    {
      icon: 'SM',
      tooltip: '10,000 Tracks',
      name: 'Spotify Music',
      time: '#ABLE-PRO-T10232',
      amount: '- 10,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '30.6%',
      amount_type: 'text-success-500'
    },
    {
      icon: 'MD',
      bg: 'text-primary-500 bg-primary-50',
      tooltip: '143 Posts',
      name: 'Medium',
      time: '06:30 pm',
      amount: '-26',
      amount_position: 'ti ti-arrows-left-right',
      percentage: '5%',
      amount_type: 'text-warning-500'
    },
    {
      icon: 'U',
      tooltip: '143 Posts',
      name: 'Uber',
      time: '08:40 pm',
      amount: '+210,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '10.6%',
      amount_type: 'text-success-500'
    },
    {
      icon: 'OC',
      bg: 'text-warning-500 bg-warning-50',
      tooltip: '143 Posts',
      name: 'Ola Cabs',
      time: '07:40 pm',
      amount: '+210,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '10.6%',
      amount_type: 'text-success-500'
    }
  ];
}
