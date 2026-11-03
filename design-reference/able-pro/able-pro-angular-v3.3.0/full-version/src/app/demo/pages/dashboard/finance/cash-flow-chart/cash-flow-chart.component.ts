// angular import
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-cash-flow-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './cash-flow-chart.component.html',
  styleUrl: './cash-flow-chart.component.scss'
})
export class CashFlowChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  selectType = 'month';

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
        type: 'bar',
        height: 225,
        toolbar: {
          show: false
        }
      },
      plotOptions: {
        bar: {
          columnWidth: '70%',
          borderRadius: 2
        }
      },
      fill: {
        opacity: [1, 0.4]
      },
      stroke: {
        show: true,
        width: 3,
        colors: ['transparent']
      },
      dataLabels: {
        enabled: false
      },
      legend: {
        position: 'top',
        horizontalAlign: 'right',
        show: true,
        fontFamily: `'Public Sans', sans-serif`,
        offsetX: 10,
        offsetY: 10,
        labels: {
          useSeriesColors: false
        },
        markers: {
          offsetX: 2,
          offsetY: 2
        },
        itemMargin: {
          horizontal: 15,
          vertical: 5
        }
      },
      colors: ['#2196f3', '#2196f3'],
      series: [
        {
          name: 'Income',
          data: [180, 90, 135, 114, 120, 145, 180, 90, 135, 114, 120, 145]
        },
        {
          name: 'Expends',
          data: [120, 45, 78, 150, 168, 99, 120, 45, 78, 150, 168, 99]
        }
      ],
      grid: {
        show: true,
        borderColor: '#f0f0f0'
      },
      yaxis: {
        show: false
      },
      tooltip: {
        theme: 'light'
      }
    };
  }

  // private methods
  private updateChartColors(theme: string) {
    switch (theme) {
      case 'blue-theme':
      default:
        this.chartOptions.colors = ['#4680ff', '#4680ff'];
        break;
      case 'indigo-theme':
        this.chartOptions.colors = ['#6610f2', '#6610f2'];
        break;
      case 'purple-theme':
        this.chartOptions.colors = ['#673ab7', '#673ab7'];
        break;
      case 'pink-theme':
        this.chartOptions.colors = ['#e83e8c', '#e83e8c'];
        break;
      case 'red-theme':
        this.chartOptions.colors = ['#dc2626', '#dc2626'];
        break;
      case 'orange-theme':
        this.chartOptions.colors = ['#fd7e14', '#fd7e14'];
        break;
      case 'yellow-theme':
        this.chartOptions.colors = ['#e58a00', '#e58a00'];
        break;
      case 'green-theme':
        this.chartOptions.colors = ['#2ca87f', '#2ca87f'];
        break;
      case 'teal-theme':
        this.chartOptions.colors = ['#20c997', '#20c997'];
        break;
      case 'cyan-theme':
        this.chartOptions.colors = ['#3ec9d6', '#3ec9d6'];
        break;
    }
  }

  private isDarkTheme(isDark: string) {
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }

  // public methods
  onOptionSelected() {
    switch (this.selectType) {
      case 'today':
        this.chartOptions.series = [
          {
            name: 'Income',
            data: [90, 135, 114, 120, 145, 180, 180, 90, 145, 180, 90, 135]
          },
          {
            name: 'Expends',
            data: [150, 168, 99, 120, 45, 78, 150, 168, 78, 150, 168, 99]
          }
        ];
        break;
      case 'week':
        this.chartOptions.series = [
          {
            name: 'Income',
            data: [145, 180, 90, 114, 120, 145, 114, 120, 145, 114, 120, 145]
          },
          {
            name: 'Expends',
            data: [150, 168, 99, 150, 120, 45, 78, 45, 78, 150, 168, 99]
          }
        ];
        break;
      case 'month':
        this.chartOptions.series = [
          {
            name: 'Income',
            data: [180, 90, 135, 114, 120, 145, 180, 90, 135, 114, 120, 145]
          },
          {
            name: 'Expends',
            data: [120, 45, 78, 150, 168, 99, 120, 45, 78, 150, 168, 99]
          }
        ];
        break;
    }
  }
}
