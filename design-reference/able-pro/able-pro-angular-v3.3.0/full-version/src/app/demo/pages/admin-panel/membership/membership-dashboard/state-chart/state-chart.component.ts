// angular import
import { Component, OnInit } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// apexChart
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-state-chart',
  imports: [NgApexchartsModule, SharedModule],
  templateUrl: './state-chart.component.html',
  styleUrl: './state-chart.component.scss'
})
export class StateChartComponent implements OnInit {
  // public props
  chartOptions!: Partial<ApexOptions>;
  selectType: string = 'today';

  // life cycle hook
  ngOnInit(): void {
    this.chartOptions = {
      series: [76],
      chart: {
        type: 'radialBar',
        offsetY: -20,
        sparkline: {
          enabled: true
        }
      },
      colors: ['#1677ff'],
      plotOptions: {
        radialBar: {
          startAngle: -95,
          endAngle: 95,
          hollow: {
            margin: 15,
            size: '50%'
          },
          track: {
            background: '#1677ff25',
            strokeWidth: '97%',
            margin: 10
          },
          dataLabels: {
            name: {
              show: false
            },
            value: {
              offsetY: 0,
              fontSize: '20px'
            }
          }
        }
      },
      grid: {
        padding: {
          top: 10
        }
      },
      stroke: {
        lineCap: 'round'
      },
      labels: ['Average Results']
    };
  }

  // public methods
  onOptionSelected() {
    switch (this.selectType) {
      case 'today':
        this.chartOptions.series = [76];
        break;
      case 'week':
        this.chartOptions.series = [50];
        break;
      case 'month':
        this.chartOptions.series = [30];
        break;
    }
  }
}
