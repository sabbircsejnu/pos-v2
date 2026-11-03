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
  selector: 'app-languages-support-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './languages-support-chart.component.html',
  styleUrl: './languages-support-chart.component.scss'
})
export class LanguagesSupportChartComponent implements OnDestroy {
  themeService = inject(ThemeLayoutService);
  themeEffect!: EffectRef;

  // public props
  chartOptions: Partial<ApexOptions>;

  // constructor
  constructor() {
    this.chartOptions = {
      chart: { type: 'area', height: 130, sparkline: { enabled: true } },
      plotOptions: { bar: { columnWidth: '80%' } },
      series: [
        {
          data: [100, 140, 100, 250, 115, 125, 90, 100, 140, 100, 230, 115, 215, 90, 190, 100, 120, 180]
        }
      ],
      colors: ['#4680FF'],
      xaxis: { crosshairs: { width: 1 } },
      stroke: {
        width: 1.5
      },
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

  // public method
  languages = [
    {
      name: 'React'
    },
    {
      name: 'Angular'
    },
    {
      name: 'Bootstrap'
    },
    {
      name: 'MUI'
    }
  ];
}
