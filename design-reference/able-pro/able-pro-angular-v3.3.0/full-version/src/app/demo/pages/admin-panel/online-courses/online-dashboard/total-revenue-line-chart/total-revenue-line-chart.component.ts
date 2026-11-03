// angular project
import { Component, OnInit, effect, inject, input } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-total-revenue-line-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './total-revenue-line-chart.component.html',
  styleUrl: './total-revenue-line-chart.component.scss'
})
export class TotalRevenueLineChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  readonly values = input.required<[]>();
  readonly color = input.required<string[]>();

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
        type: 'line',
        height: 60,
        sparkline: {
          enabled: true
        }
      },
      dataLabels: {
        enabled: false
      },
      colors: this.color(),
      stroke: {
        curve: 'straight',
        lineCap: 'round',
        width: 3
      },
      series: [
        {
          name: 'series1',
          data: this.values()
        }
      ],
      yaxis: {
        min: 0,
        max: 30
      },
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

  private isDarkTheme(isDarkMode: string) {
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDarkMode === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }
}
