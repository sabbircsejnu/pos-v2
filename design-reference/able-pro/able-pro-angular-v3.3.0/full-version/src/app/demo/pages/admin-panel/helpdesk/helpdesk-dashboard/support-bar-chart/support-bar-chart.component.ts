// angular import
import { Component, OnInit, effect, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-support-bar-chart',
  imports: [SharedModule, NgApexchartsModule, CommonModule],
  templateUrl: './support-bar-chart.component.html',
  styleUrl: './support-bar-chart.component.scss'
})
export class SupportBarChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  themeRTL!: string;

  readonly value = input.required<number>();
  readonly typeRequest = input.required<string>();
  readonly inputColor = input.required<string>();
  readonly backgroundColor = input.required<string>();
  readonly openValue = input.required<number>();
  readonly runningValue = input.required<number>();
  readonly solvedValue = input.required<number>();
  readonly chartColor = input.required<string[]>();

  // constructor
  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
    effect(() => {
      this.themeDirection(this.themeService.directionChange());
    });
  }

  // life cycle hook
  ngOnInit() {
    this.chartOptions = {
      chart: {
        type: 'area',
        height: 100,
        sparkline: {
          enabled: true
        }
      },
      dataLabels: {
        enabled: false
      },
      colors: this.chartColor(),
      stroke: {
        curve: 'smooth',
        width: 2
      },
      series: [
        {
          name: 'series1',
          data: [0, 20, 10, 45, 30, 55, 20, 30, 0]
        }
      ],
      tooltip: {
        theme: 'light',
        fixed: {
          enabled: false
        },
        x: {
          show: false
        },

        marker: {
          show: false
        }
      }
    };
  }

  private themeDirection(direction: string) {
    this.themeRTL = direction;
  }

  private isDarkTheme(isDark: string) {
    const tooltipTheme = isDark === DARK ? DARK : LIGHT;
    const tooltip = { theme: tooltipTheme };
    this.chartOptions = { ...this.chartOptions, tooltip };
  }
}
