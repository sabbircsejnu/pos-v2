// angular import
import { Component, EffectRef, OnDestroy, OnInit, effect, inject, input } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-earning-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './earning-chart.component.html',
  styleUrl: './earning-chart.component.scss'
})
export class EarningChartComponent implements OnInit, OnDestroy {
  private themeService = inject(ThemeLayoutService);
  private themeEffect!: EffectRef;

  // public props
  chartOptions: Partial<ApexOptions>;
  readonly styleInput = input<string>();
  readonly iconImage = input<string>();
  readonly headerTitle = input<string>();
  readonly earningValue = input<string>();
  readonly background = input<string>();
  readonly textColor = input<string>();
  readonly percentageValue = input<string>();
  readonly data = input.required<[]>();
  readonly color = input.required<[]>();

  // constructor
  constructor() {
    this.themeEffect = effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle
  ngOnInit() {
    this.chartOptions = {
      chart: { type: 'bar', background: 'transparent', height: 50, sparkline: { enabled: true } },
      plotOptions: { bar: { columnWidth: '80%' } },
      series: [
        {
          data: this.data()
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
        marker: { show: false },
        theme: LIGHT
      },
      colors: this.color(),
      theme: {
        mode: LIGHT
      }
    };
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
