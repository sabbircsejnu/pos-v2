// angular import
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// apexChart
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-activity-line-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './activity-line-chart.component.html',
  styleUrl: './activity-line-chart.component.scss'
})
export class ActivityLineChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  selectType: string = 'today';
  themeColors = ['#52c41a', '#1677ff'];

  // constructor
  constructor() {
    effect(() => {
      this.updateChartColors(this.themeService.color());
    });
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle hook
  ngOnInit() {
    this.chartOptions = {
      chart: {
        type: 'line',
        height: 300,
        toolbar: {
          show: false
        }
      },
      colors: this.themeColors,
      dataLabels: {
        enabled: false
      },
      legend: {
        show: true,
        position: 'top'
      },
      markers: {
        size: 1,
        colors: ['#fff', '#fff', '#fff'],
        strokeColors: this.themeColors,
        strokeWidth: 1,
        shape: 'circle',
        hover: {
          size: 4
        }
      },
      stroke: {
        width: 3,
        curve: 'smooth'
      },
      grid: {
        strokeDashArray: 4,
        borderColor: '#f5f5f5'
      },
      series: [
        {
          name: 'Free Course',
          data: [20, 90, 65, 85, 20, 80, 30]
        },
        {
          name: 'Subscription',
          data: [70, 30, 40, 15, 60, 40, 95]
        }
      ],
      xaxis: {
        labels: {
          hideOverlappingLabels: true
        },
        axisBorder: {
          show: false
        },
        axisTicks: {
          show: false
        }
      }
    };
  }

  // private methods
  private updateChartColors(theme: string) {
    switch (theme) {
      case 'blue-theme':
      default:
        this.themeColors = ['#52c41a', '#4680ff'];
        break;
      case 'indigo-theme':
        this.themeColors = ['#52c41a', '#6610f2'];
        break;
      case 'purple-theme':
        this.themeColors = ['#52c41a', '#673ab7'];
        break;
      case 'pink-theme':
        this.themeColors = ['#52c41a', '#e83e8c'];
        break;
      case 'red-theme':
        this.themeColors = ['#52c41a', '#dc2626'];
        break;
      case 'orange-theme':
        this.themeColors = ['#52c41a', '#fd7e14'];
        break;
      case 'yellow-theme':
        this.themeColors = ['#52c41a', '#e58a00'];
        break;
      case 'green-theme':
        this.themeColors = ['#52c41a', '#2ca87f'];
        break;
      case 'teal-theme':
        this.themeColors = ['#52c41a', '#20c997'];
        break;
      case 'cyan-theme':
        this.themeColors = ['#52c41a', '#3ec9d6'];
        break;
    }
    this.chartOptions = { ...this.chartOptions, colors: this.themeColors };
  }

  private isDarkTheme(isDark: string) {
    const tooltipTheme = isDark === DARK ? DARK : LIGHT;
    const tooltip = { theme: tooltipTheme };
    const grid = { ...this.chartOptions.grid };
    grid.borderColor = isDark === DARK ? '#fafafa0d' : '#f5f5f5';
    this.chartOptions = { ...this.chartOptions, tooltip, grid };
  }

  // public methods
  onOptionSelected() {
    switch (this.selectType) {
      case 'today':
        this.chartOptions.series = [
          {
            name: 'Free Course',
            data: [20, 90, 65, 85, 20, 80, 30]
          },
          {
            name: 'Subscription',
            data: [70, 30, 40, 15, 60, 40, 95]
          }
        ];
        break;
      case 'week':
        this.chartOptions.series = [
          {
            name: 'Free Course',
            data: [30, 50, 65, 80, 35, 55, 20]
          },
          {
            name: 'Subscription',
            data: [95, 80, 55, 10, 25, 50, 60]
          }
        ];
        break;
      case 'month':
        this.chartOptions.series = [
          {
            name: 'Free Course',
            data: [55, 75, 25, 40, 65, 95, 20]
          },
          {
            name: 'Subscription',
            data: [10, 65, 85, 45, 20, 10, 35]
          }
        ];
        break;
    }
  }
}
