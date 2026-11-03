// angular import
import { Component, effect, inject, OnDestroy, EffectRef } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-new-user-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './new-user-chart.component.html',
  styleUrl: './new-user-chart.component.scss'
})
export class NewUserChartComponent implements OnDestroy {
  private themeService = inject(ThemeLayoutService);
  private themeEffect!: EffectRef;

  //public props
  chartOptions: Partial<ApexOptions>;

  // constructor
  constructor() {
    this.chartOptions = {
      chart: { type: 'area', height: 80, background: 'transparent', sparkline: { enabled: true } },
      colors: ['#2CA87F'],
      stroke: {
        width: 1
      },
      fill: {
        type: 'gradient',
        gradient: {
          shadeIntensity: 1,
          type: 'vertical',
          shade: LIGHT,
          inverseColors: false,
          opacityFrom: 0.5,
          opacityTo: 0
        }
      },
      dataLabels: {
        enabled: false
      },
      series: [
        {
          data: [1, 1, 60, 1, 1, 50, 1, 1, 40, 1, 1, 25, 0]
        }
      ],
      tooltip: {
        theme: LIGHT,
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
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }
}
