// angular import
import { Component, effect, inject, EffectRef, OnDestroy } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-income-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './income-chart.component.html',
  styleUrl: './income-chart.component.scss'
})
export class IncomeChartComponent implements OnDestroy {
  themeService = inject(ThemeLayoutService);
  themeEffect!: EffectRef;

  // public props
  chartOptions: Partial<ApexOptions>;

  // constructor
  constructor() {
    this.chartOptions = {
      chart: {
        type: 'bar',
        background: 'transparent',
        height: 80,
        sparkline: { enabled: true }
      },
      colors: ['#2CA87F'],
      plotOptions: { bar: { columnWidth: '80%' } },
      series: [
        {
          data: [10, 30, 40, 20, 60, 50, 20, 15, 20, 25, 30, 25]
        }
      ],
      xaxis: { crosshairs: { width: 1 } },
      tooltip: {
        fixed: { enabled: false },
        x: { show: false },
        y: {
          title: {
            formatter: function () {
              return '';
            }
          }
        },
        marker: { show: false }
      },
      theme: {
        mode: LIGHT
      }
    };

    this.themeEffect = effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle
  ngOnDestroy() {
    if (this.themeEffect) {
      this.themeEffect.destroy();
    }
  }

  // private methods
  private isDarkTheme(isDark: string) {
    const theme = { ...this.chartOptions.theme };
    theme.mode = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, theme };
  }
}
