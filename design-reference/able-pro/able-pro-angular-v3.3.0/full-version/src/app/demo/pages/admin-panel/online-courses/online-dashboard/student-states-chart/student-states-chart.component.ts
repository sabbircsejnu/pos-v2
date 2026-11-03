// angular project
import { Component, OnInit, effect, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-student-states-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './student-states-chart.component.html',
  styleUrl: './student-states-chart.component.scss'
})
export class StudentStatesChartComponent implements OnInit {
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
        height: 275,
        type: 'donut'
      },
      dataLabels: {
        enabled: false
      },
      plotOptions: {
        pie: {
          donut: {
            size: '65%'
          }
        }
      },
      labels: ['Total Signups', 'Active Student'],
      series: [76.7, 30],
      legend: {
        show: true,
        position: 'bottom'
      },
      fill: {
        opacity: [1, 0.5]
      },
      colors: ['#4680ff', '#4680ff']
    };
  }

  private updateChartColors(theme: string) {
    switch (theme) {
      case 'blue-theme':
      default:
        this.chartOptions.colors = ['#4680ff', '#4680ff'];
        break;
      case 'indigo-theme':
        this.chartOptions.colors = ['#6610f2', '#6610f2'];
        break;
      case 'purple-theme':
        this.chartOptions.colors = ['#673ab7', '#673ab7'];
        break;
      case 'pink-theme':
        this.chartOptions.colors = ['#e83e8c', '#e83e8c'];
        break;
      case 'red-theme':
        this.chartOptions.colors = ['#dc2626', '#dc2626'];
        break;
      case 'orange-theme':
        this.chartOptions.colors = ['#fd7e14', '#fd7e14'];
        break;
      case 'yellow-theme':
        this.chartOptions.colors = ['#e58a00', '#e58a00'];
        break;
      case 'green-theme':
        this.chartOptions.colors = ['#2ca87f', '#2ca87f'];
        break;
      case 'teal-theme':
        this.chartOptions.colors = ['#20c997', '#20c997'];
        break;
      case 'cyan-theme':
        this.chartOptions.colors = ['#3ec9d6', '#3ec9d6'];
        break;
    }
  }
}
