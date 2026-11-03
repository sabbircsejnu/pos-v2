// angular import
import { Component, effect, EffectRef, inject, OnDestroy, OnInit } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

@Component({
  selector: 'app-revenue-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './revenue-chart.component.html',
  styleUrl: './revenue-chart.component.scss'
})
export class RevenueChartComponent implements OnInit, OnDestroy {
  private themeService = inject(ThemeLayoutService);
  private themeEffect!: EffectRef;

  // public props
  chartOptions: Partial<ApexOptions>;
  monthlyColor = ['var(--primary-500)'];

  // constructor
  constructor() {
    this.themeEffect = effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  ngOnInit() {
    this.chartOptions = {
      chart: {
        fontFamily: 'Inter var, sans-serif',
        type: 'area',
        height: 300,
        background: 'transparent',
        toolbar: {
          show: false
        }
      },
      fill: {
        type: 'gradient',
        gradient: {
          shadeIntensity: 1,
          type: 'vertical',
          inverseColors: false,
          opacityFrom: 0.2,
          opacityTo: 0
        }
      },
      dataLabels: {
        enabled: false
      },
      stroke: { curve: 'smooth', width: 2 },
      plotOptions: {
        bar: {
          columnWidth: '45%',
          borderRadius: 4
        }
      },
      grid: {
        show: true,
        borderColor: '#F3F5F7',
        strokeDashArray: 2
      },
      series: [{ data: [20, 70, 40, 70, 70, 90, 50, 55, 45, 60, 50, 65] }],
      xaxis: {
        categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
        axisBorder: {
          show: false
        },
        axisTicks: {
          show: false
        }
      },
      theme: {
        mode: LIGHT
      }
    };
  }

  ngOnDestroy() {
    // destroy the reactive effect so it doesn’t run after component is gone
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
