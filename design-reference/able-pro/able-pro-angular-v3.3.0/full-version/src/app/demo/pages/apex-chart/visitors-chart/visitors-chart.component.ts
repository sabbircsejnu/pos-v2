// angular import
import { Component, effect, inject, OnDestroy, EffectRef } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-visitors-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './visitors-chart.component.html',
  styleUrl: './visitors-chart.component.scss'
})
export class VisitorsChartComponent implements OnDestroy {
  themeService = inject(ThemeLayoutService);
  themeEffect!: EffectRef;

  chartOptions: Partial<ApexOptions>;

  constructor() {
    this.chartOptions = {
      chart: {
        type: 'rangeBar',
        height: 80,
        sparkline: { enabled: true },
        toolbar: {
          show: false
        },
        background: 'transparent'
      },
      series: [
        {
          data: [
            {
              x: 'Rejected',
              y: [1, 6]
            },
            {
              x: 'Pending',
              y: [3, 7]
            },
            {
              x: 'New',
              y: [4, 8]
            },
            {
              x: 'Verified',
              y: [5, 9]
            },
            {
              x: 'Store',
              y: [4, 8]
            },
            {
              x: 'Deleted',
              y: [4, 7]
            },
            {
              x: 'Block',
              y: [2, 5]
            }
          ]
        }
      ],
      colors: ['#E58A00'],
      plotOptions: {
        bar: {
          columnWidth: '30%',
          borderRadius: 5,
          horizontal: false
        }
      },
      yaxis: {
        show: false
      },
      grid: {
        show: false,
        padding: {
          top: 0,
          right: 0,
          bottom: 0,
          left: 0
        }
      },
      dataLabels: {
        enabled: false
      },
      theme: {
        mode: LIGHT
      }
    };
    this.themeEffect = effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  ngOnDestroy() {
    if (this.themeEffect) {
      this.themeEffect.destroy();
    }
  }

  private isDarkTheme(isDark: string) {
    const theme = { ...this.chartOptions.theme };
    theme.mode = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, theme };
  }
}
