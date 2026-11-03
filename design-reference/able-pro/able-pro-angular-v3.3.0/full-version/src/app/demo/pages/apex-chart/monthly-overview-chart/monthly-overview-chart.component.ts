// angular import
import { Component, effect, inject, EffectRef, OnDestroy, OnInit, input, OnChanges } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-monthly-overview-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './monthly-overview-chart.component.html',
  styleUrl: './monthly-overview-chart.component.scss'
})
export class MonthlyOverviewChartComponent implements OnInit, OnDestroy, OnChanges {
  themeService = inject(ThemeLayoutService);
  themeEffect!: EffectRef;

  readonly currentSeries = input<[]>();

  // public props
  chartOptions: Partial<ApexOptions>;
  overOptionsColor = ['var(--primary-500)', 'var(--primary-200)'];

  // constructor
  constructor() {
    this.themeEffect = effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  ngOnInit() {
    this.chartOptions = {
      chart: {
        height: 250,
        type: 'bar',
        toolbar: {
          show: false
        },
        background: 'transparent'
      },
      plotOptions: {
        bar: {
          horizontal: false,
          columnWidth: '55%',
          borderRadius: 4,
          borderRadiusApplication: 'end'
        }
      },
      legend: {
        show: true,
        position: 'top',
        horizontalAlign: 'left'
      },
      dataLabels: {
        enabled: false
      },
      stroke: {
        show: true,
        width: 3,
        colors: ['transparent']
      },
      fill: {
        opacity: [1, 0.5]
      },
      grid: {
        strokeDashArray: 4
      },
      series: this.currentSeries(),
      xaxis: {
        categories: ['Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug']
      },
      tooltip: {
        theme: LIGHT,
        y: {
          formatter: function (val) {
            return '$ ' + val + ' thousands';
          }
        }
      }
    };
  }

  ngOnChanges() {
    if (this.chartOptions) {
      this.chartOptions = { ...this.chartOptions, series: this.currentSeries() };
    }
  }

  ngOnDestroy() {
    if (this.themeEffect) {
      this.themeEffect.destroy();
    }
  }

  // private methods
  private isDarkTheme(isDark: string) {
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }

  // public method
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
