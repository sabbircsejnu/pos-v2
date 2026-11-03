// angular import
import { Component, OnInit, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ChartDB } from 'src/app/fake-data/chartDB';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// const
import { DARK, LIGHT } from 'src/app/@theme/const';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-apex-charts',
  imports: [CommonModule, SharedModule, NgApexchartsModule],
  templateUrl: './apex-charts.component.html',
  styleUrls: ['./apex-charts.component.scss']
})
export class ApexChartsComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);

  // public props
  barChart: Partial<ApexOptions>;
  barStackedChart: Partial<ApexOptions>;
  barHorizontalChart: Partial<ApexOptions>;
  barHStackChart: Partial<ApexOptions>;
  pieChart: Partial<ApexOptions>;
  donutChart: Partial<ApexOptions>;
  radialChart: Partial<ApexOptions>;
  customsAngleChart: Partial<ApexOptions>;
  lineChart: Partial<ApexOptions>;
  realTimeChart: Partial<ApexOptions>;
  areaChart: Partial<ApexOptions>;
  dateTimeChart: Partial<ApexOptions>;
  mixedChart: Partial<ApexOptions>;
  lineAreaChart: Partial<ApexOptions>;
  candlestickChart: Partial<ApexOptions>;
  bubbleChart: Partial<ApexOptions>;
  bubble3DChart: Partial<ApexOptions>;
  scatterChart: Partial<ApexOptions>;
  scatterDateTimeChart: Partial<ApexOptions>;
  heatmapChart: Partial<ApexOptions>;
  heatmapRoundedChart: Partial<ApexOptions>;
  // eslint-disable-next-line
  chartDB: any;

  // color change while theme color change
  preset = ['#0050B3', 'var(--primary-500)', '#52C41A'];
  barChartColor = ['var(--primary-500)', '#52c41a', '#faad14', '#13c2c2'];
  bHorizontalColor = ['var(--primary-500)', '#52c41a'];
  pie_color = ['#0050B3', 'var(--primary-500)', '#52C41A', '#FF4D4F', '#FAAD14'];
  radialColor = ['var(--primary-500)'];
  customs_color = ['#0050B3', 'var(--primary-500)', '#52C41A', '#FF4D4F'];

  // constructor
  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
    this.chartDB = ChartDB;
    const {
      barChart,
      bubbleChart,
      bubble3DChart,
      scatterChart,
      scatterDateTimeChart,
      heatmapChart,
      heatmapRoundedChart,
      lineAreaChart,
      candlestickChart,
      barStackedChart,
      barHorizontalChart,
      barHStackChart,
      pieChart,
      donutChart,
      radialChart,
      customsAngleChart,
      lineChart,
      realTimeChart,
      areaChart,
      dateTimeChart,
      mixedChart
    } = this.chartDB;

    // eslint-disable-next-line
    ((this.barChart = barChart),
      (this.barStackedChart = barStackedChart),
      (this.barHorizontalChart = barHorizontalChart),
      (this.barHStackChart = barHStackChart),
      (this.pieChart = pieChart),
      (this.donutChart = donutChart),
      (this.radialChart = radialChart),
      (this.customsAngleChart = customsAngleChart),
      (this.lineChart = lineChart),
      (this.realTimeChart = realTimeChart),
      (this.areaChart = areaChart),
      (this.dateTimeChart = dateTimeChart),
      (this.mixedChart = mixedChart),
      (this.lineAreaChart = lineAreaChart),
      (this.candlestickChart = candlestickChart),
      (this.bubbleChart = bubbleChart),
      (this.bubble3DChart = bubble3DChart),
      (this.scatterChart = scatterChart),
      (this.scatterDateTimeChart = scatterDateTimeChart),
      (this.heatmapChart = heatmapChart),
      (this.heatmapRoundedChart = heatmapRoundedChart));
  }

  // lifecycle hooks
  ngOnInit(): void {
    this.preset = ['#0050B3', 'var(--primary-500)', '#52C41A'];
    this.barChartColor = ['var(--primary-500)', '#52c41a', '#faad14', '#13c2c2'];
    this.bHorizontalColor = ['var(--primary-500)', '#52c41a'];
    this.pie_color = ['#0050B3', 'var(--primary-500)', '#52C41A', '#FF4D4F', '#FAAD14'];
    this.radialColor = ['var(--primary-500)'];
    this.customs_color = ['#0050B3', 'var(--primary-500)', '#52C41A', '#FF4D4F'];
  }

  // private methods
  private isDarkTheme(isDark: string) {
    const tooltipTheme = isDark === DARK ? DARK : LIGHT;
    const tooltip = { theme: tooltipTheme };

    this.barChart = { ...this.barChart, tooltip };
    this.barStackedChart = { ...this.barStackedChart, tooltip };
    this.barHorizontalChart = { ...this.barHorizontalChart, tooltip };
    this.barHStackChart = { ...this.barHStackChart, tooltip };
    this.lineChart = { ...this.lineChart, tooltip };
    this.realTimeChart = { ...this.realTimeChart, tooltip };
    this.areaChart = { ...this.areaChart, tooltip };
    this.dateTimeChart = { ...this.dateTimeChart, tooltip };
    this.mixedChart = { ...this.mixedChart, tooltip };
    this.lineAreaChart = { ...this.lineAreaChart, tooltip };
    this.candlestickChart = { ...this.candlestickChart, tooltip };
    this.bubbleChart = { ...this.bubbleChart, tooltip };
    this.bubble3DChart = { ...this.bubble3DChart, tooltip };
    this.scatterChart = { ...this.scatterChart, tooltip };
    this.scatterDateTimeChart = { ...this.scatterDateTimeChart, tooltip };
    this.heatmapChart = { ...this.heatmapChart, tooltip };
    this.heatmapRoundedChart = { ...this.heatmapRoundedChart, tooltip };
  }
}
