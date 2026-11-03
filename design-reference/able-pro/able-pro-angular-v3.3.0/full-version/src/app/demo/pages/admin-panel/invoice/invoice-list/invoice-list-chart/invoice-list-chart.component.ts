// angular import
import { Component, OnInit, effect, inject, input } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// rxjs
import { DARK, LIGHT } from 'src/app/@theme/const';

// apexChart
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-invoice-list-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './invoice-list-chart.component.html',
  styleUrl: './invoice-list-chart.component.scss'
})
export class InvoiceListChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;
  readonly seriesData = input.required<[]>();
  readonly colors = input.required<string[]>();

  // constructor
  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle hook
  ngOnInit(): void {
    this.chartOptions = {
      chart: {
        type: 'area',
        height: 55,
        sparkline: {
          enabled: true
        }
      },
      series: [
        {
          data: this.seriesData()
        }
      ],
      fill: {
        type: 'gradient',
        gradient: {
          shadeIntensity: 1,
          type: 'vertical',
          inverseColors: false,
          opacityFrom: 0.5,
          opacityTo: 0
        }
      },
      stroke: {
        curve: 'smooth',
        width: 2
      },
      grid: {
        show: false
      },
      tooltip: {
        fixed: {
          enabled: false
        },
        x: {
          show: false
        },
        marker: {
          show: false
        }
      },
      colors: this.colors()
    };
  }

  private isDarkTheme(isDark: string) {
    const tooltip = { ...this.chartOptions.tooltip };
    tooltip.theme = isDark === DARK ? DARK : LIGHT;
    this.chartOptions = { ...this.chartOptions, tooltip };
  }
}
