// angular import
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-satisfaction-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './satisfaction-chart.component.html',
  styleUrl: './satisfaction-chart.component.scss'
})
export class SatisfactionChartComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  chartOptions!: Partial<ApexOptions>;

  // constructor
  constructor() {
    effect(() => {
      this.updateChartColors(this.themeService.color());
    });
  }

  // life cycle
  ngOnInit() {
    this.chartOptions = {
      chart: {
        height: 260,
        type: 'pie'
      },
      series: [66, 50, 40, 30],
      labels: ['Very Poor', 'Satisfied', 'Very Satisfied', 'Poor'],
      legend: {
        show: true,
        offsetY: 50
      },
      theme: {
        monochrome: {
          enabled: true,
          color: '#f27013'
        }
      },
      responsive: [
        {
          breakpoint: 768,
          options: {
            chart: {
              height: 320
            },
            legend: {
              position: 'bottom',
              offsetY: 0
            }
          }
        }
      ]
    };
  }

  private updateChartColors(theme: string) {
    const monochrome = { ...this.chartOptions.theme?.monochrome };
    switch (theme) {
      case 'blue-theme':
      default:
        monochrome.color = '#4680ff';
        break;
      case 'purple-theme':
        monochrome.color = '#6610f2';
        break;
      case 'pink-theme':
        monochrome.color = '#e83e8c';
        break;
      case 'red-theme':
        monochrome.color = '#fd7e14';
        break;
      case 'orange-theme':
        monochrome.color = '#3c64d0';
        break;
      case 'yellow-theme':
        monochrome.color = '#e58a00';
        break;
      case 'green-theme':
        monochrome.color = '#2ca87f';
        break;
      case 'teal-theme':
        monochrome.color = '#20c997';
        break;
      case 'cyan-theme':
        monochrome.color = '#3ec9d6';
        break;
    }
    this.chartOptions.theme = { monochrome };
  }
}
