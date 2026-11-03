// angular import
import { Component, effect, EffectRef, inject, OnDestroy } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-new-order-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './new-order-chart.component.html',
  styleUrl: './new-order-chart.component.scss'
})
export class NewOrderChartComponent implements OnDestroy {
  private themeService = inject(ThemeLayoutService);
  private themeEffect!: EffectRef;

  // public props
  chartOptions: Partial<ApexOptions>;
  chartOptionColor = ['var(--primary-500)'];

  // constructor
  constructor() {
    this.chartOptions = {
      chart: { type: 'bar', height: 80, background: 'transparent', sparkline: { enabled: true } },
      plotOptions: { bar: { borderRadius: 4, columnWidth: '80%' } },
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
