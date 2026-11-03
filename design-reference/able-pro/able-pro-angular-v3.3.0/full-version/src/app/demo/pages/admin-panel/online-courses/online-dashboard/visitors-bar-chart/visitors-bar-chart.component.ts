// angular project
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-visitors-bar-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './visitors-bar-chart.component.html',
  styleUrl: './visitors-bar-chart.component.scss'
})
export class VisitorsBarChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;

  // constructor
  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle
  ngOnInit() {
    this.chartOptions = {
      chart: {
        type: 'bar',
        height: 220,
        toolbar: {
          show: false
        }
      },
      colors: ['#52c41a'],
      dataLabels: {
        enabled: false
      },
      grid: {
        strokeDashArray: 4
      },
      yaxis: {
        tickAmount: 3
      },
      states: {
        hover: {
          filter: {
            type: 'lighten'
          }
        }
      },
      plotOptions: {
        bar: {
          borderRadius: 2,
          columnWidth: '50%'
        }
      },
      labels: ['2018', '2019', '2020', '2021', '2022', '2023'],
      series: [
        {
          data: [20, 15, 22, 25, 32, 50]
        }
      ]
    };
  }

  private isDarkTheme(isDark: string) {
    const tooltipTheme = isDark === DARK ? DARK : LIGHT;
    const tooltip = { theme: tooltipTheme };
    const grid = { ...this.chartOptions.grid };
    grid.borderColor = isDark === DARK ? '#fafafa0d' : '#f5f5f5';
    this.chartOptions = { ...this.chartOptions, tooltip, grid };
  }
}
