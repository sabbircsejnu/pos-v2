// angular project
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-course-report-bar-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './course-report-bar-chart.component.html',
  styleUrl: './course-report-bar-chart.component.scss'
})
export class CourseReportBarChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  themeColors = ['#4680ff', '#faad14'];

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
  ngOnInit(): void {
    this.chartOptions = {
      chart: {
        type: 'bar',
        height: '190px',
        toolbar: {
          show: false
        }
      },
      plotOptions: {
        bar: {
          columnWidth: '50%',
          borderRadius: 3
        }
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
      colors: this.themeColors,
      series: [
        {
          name: 'Net Profit',
          data: [180, 90, 135, 114, 120, 145, 180, 90, 135, 114, 120, 145]
        },
        {
          name: 'Revenue',
          data: [120, 45, 78, 150, 168, 99, 120, 45, 78, 150, 168, 99]
        }
      ],
      grid: {
        borderColor: '#00000010'
      },
      yaxis: {
        show: false
      }
    };
  }

  private updateChartColors(theme: string) {
    switch (theme) {
      case 'blue-theme':
      default:
        this.themeColors = ['#4680ff', '#faad14'];
        break;
      case 'indigo-theme':
        this.themeColors = ['#6610f2', '#faad14'];
        break;
      case 'purple-theme':
        this.themeColors = ['#673ab7', '#faad14'];
        break;
      case 'pink-theme':
        this.themeColors = ['#e83e8c', '#faad14'];
        break;
      case 'red-theme':
        this.themeColors = ['#dc2626', '#faad14'];
        break;
      case 'orange-theme':
        this.themeColors = ['#fd7e14', '#faad14'];
        break;
      case 'yellow-theme':
        this.themeColors = ['#e58a00', '#faad14'];
        break;
      case 'green-theme':
        this.themeColors = ['#2ca87f', '#faad14'];
        break;
      case 'teal-theme':
        this.themeColors = ['#20c997', '#faad14'];
        break;
      case 'cyan-theme':
        this.themeColors = ['#3ec9d6', '#faad14'];
        break;
    }
    this.chartOptions = { ...this.chartOptions, colors: this.themeColors };
  }

  private isDarkTheme(isDark: string) {
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }
}
