import { data } from './series-data';
import { candlestick_data } from './candlestick-data';

// const
import { LIGHT } from '../@theme/const';

export class ChartDB {
  static customerRateChart = {
    chart: {
      type: 'area',
      height: 230,
      toolbar: {
        show: false
      }
    },
    colors: ['#4680FF'],
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
    dataLabels: {
      enabled: false
    },
    stroke: {
      width: 1
    },
    plotOptions: {
      bar: {
        columnWidth: '45%',
        borderRadius: 4
      }
    },
    grid: {
      strokeDashArray: 4
    },
    series: [
      {
        data: [30, 60, 40, 70, 50, 90, 50, 55, 45, 60, 50, 65]
      }
    ],
    xaxis: {
      categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
      axisBorder: {
        show: false
      },
      axisTicks: {
        show: false
      }
    },
    tooltip: {
      theme: LIGHT
    }
  };
  static monthlyReportChart = {
    series: [
      {
        name: 'Deals',
        data: [44, 55, 41, 67, 52, 53, 13, 23, 20, 8, 13, 27]
      },
      {
        name: 'Income Report',
        data: [13, 23, 20, 8, 13, 27, 21, 7, 25, 13, 22, 8]
      },
      {
        name: 'Customer',
        data: [11, 17, 15, 15, 21, 14, 11, 17, 15, 15, 21, 14]
      },
      {
        name: 'Profits',
        data: [21, 7, 25, 13, 22, 3, 44, 55, 41, 67, 22, 12]
      }
    ],
    chart: {
      type: 'bar',
      height: 350,
      stacked: true,
      toolbar: {
        show: false
      }
    },
    colors: ['#4680FF', '#4680FF', '#4680FF', '#E58A00'],
    fill: {
      opacity: [1, 0.7, 0.4, 0.3]
    },
    grid: {
      strokeDashArray: 4
    },
    dataLabels: {
      enabled: false
    },
    plotOptions: {
      bar: {
        horizontal: false
      }
    },
    xaxis: {
      categories: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12']
    },
    legend: {
      show: false
    },
    tooltip: {
      theme: LIGHT
    }
  };
  static salesReportChart = {
    chart: {
      type: 'bar',
      height: 430,
      toolbar: {
        show: false
      }
    },
    plotOptions: {
      bar: {
        columnWidth: '30%',
        borderRadius: 4
      }
    },
    stroke: {
      show: true,
      width: 8,
      colors: ['transparent']
    },
    dataLabels: {
      enabled: false
    },
    legend: {
      position: 'top',
      horizontalAlign: 'right',
      show: true,
      fontFamily: `'Public Sans', sans-serif`,
      offsetX: 10,
      offsetY: 10,
      labels: {
        useSeriesColors: false
      },
      markers: {
        width: 10,
        height: 10,
        radius: '50%',
        offsexX: 2,
        offsexY: 2
      },
      itemMargin: {
        horizontal: 15,
        vertical: 5
      }
    },
    colors: ['#E58A00', '#4680FF'],
    series: [
      {
        name: 'Income',
        data: [180, 90, 135, 114, 120, 145]
      },
      {
        name: 'Cost Of Sales',
        data: [120, 45, 78, 150, 168, 99]
      }
    ],
    xaxis: {
      categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun']
    },
    yaxis: {
      title: {
        text: '$ (thousands)'
      }
    },
    fill: {
      opacity: 1
    },
    responsive: [
      {
        breakpoint: 600,
        options: {
          yaxis: {
            show: false
          }
        }
      }
    ],
    tooltip: {
      theme: LIGHT
    }
  };
  static acquisitionChart = {
    chart: {
      type: 'bar',
      height: 250,
      stacked: true,
      toolbar: {
        show: false
      }
    },
    legend: {
      show: true,
      position: 'bottom',
      horizontalAlign: 'left',
      offsetX: 10,
      markers: {
        width: 8,
        height: 8,
        radius: '50%'
      }
    },
    dataLabels: {
      enabled: false
    },
    grid: {
      show: false
    },
    stroke: {
      colors: ['transparent'],
      width: 1
    },
    colors: ['#0F172A', '#4680FF', '#4680FF'],
    fill: {
      opacity: [1, 0.6, 1]
    },
    series: [
      {
        name: 'Direct',
        data: [21, 17, 15, 13, 15, 13, 16, 13, 8, 14, 11, 9, 7, 5, 3, 3, 7]
      },
      {
        name: 'Referral',
        data: [28, 30, 20, 26, 18, 27, 22, 28, 20, 21, 15, 14, 12, 10, 8, 18, 16]
      },
      {
        name: 'Social',
        data: [50, 51, 60, 54, 53, 48, 55, 40, 44, 42, 44, 44, 42, 40, 42, 32, 16]
      }
    ],
    xaxis: {
      axisBorder: {
        show: false
      },
      axisTicks: {
        show: false
      },
      labels: {
        show: false
      }
    },
    yaxis: {
      axisBorder: {
        show: false
      },
      axisTicks: {
        show: false
      },
      labels: {
        show: false
      }
    },
    tooltip: {
      theme: LIGHT
    }
  };

  static totalEarningChart = {
    series: [30],
    chart: {
      height: 150,
      type: 'radialBar'
    },
    plotOptions: {
      radialBar: {
        hollow: {
          margin: 0,
          size: '60%',
          background: 'transparent',
          imageOffsetX: 0,
          imageOffsetY: 0,
          position: 'front'
        },
        track: {
          background: '#4680FF50',
          strokeWidth: '50%'
        },

        dataLabels: {
          show: true,
          name: {
            show: false
          },
          value: {
            formatter: function (val: string) {
              return parseInt(val);
            },
            offsetY: 7,
            color: '#4680FF',
            fontSize: '20px',
            fontWeight: '700',
            show: true
          }
        }
      }
    },
    colors: ['#4680FF'],
    fill: {
      type: 'solid'
    },
    stroke: {
      lineCap: 'round'
    }
  };

  // apex-chart page chart data

  static barChart = {
    chart: {
      height: 350,
      type: 'bar'
    },
    plotOptions: {
      bar: {
        horizontal: false,
        columnWidth: '55%'
      }
    },
    dataLabels: {
      enabled: false
    },
    stroke: {
      show: true,
      width: 2,
      colors: ['transparent']
    },
    series: [
      {
        name: 'Net Profit',
        data: [44, 55, 57, 56, 61, 58, 63]
      },
      {
        name: 'Revenue',
        data: [76, 85, 101, 98, 87, 105, 91]
      },
      {
        name: 'Free Cash Flow',
        data: [35, 41, 36, 26, 45, 48, 52]
      }
    ],
    xaxis: {
      categories: ['Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug']
    },
    yaxis: {
      title: {
        text: '$ (thousands)'
      }
    },
    fill: {
      opacity: 1
    },
    tooltip: {
      y: {
        formatter: function (val: string) {
          return '$ ' + val + ' thousands';
        }
      }
    }
  };
  static barStackedChart = {
    chart: {
      height: 350,
      type: 'bar',
      stacked: true,
      toolbar: {
        show: true
      },
      zoom: {
        enabled: true
      }
    },
    responsive: [
      {
        breakpoint: 480,
        options: {
          legend: {
            position: 'bottom',
            offsetX: -10,
            offsetY: 0
          }
        }
      }
    ],
    plotOptions: {
      bar: {
        horizontal: false
      }
    },
    series: [
      {
        name: 'PRODUCT A',
        data: [44, 55, 41, 67, 22, 43]
      },
      {
        name: 'PRODUCT B',
        data: [13, 23, 20, 8, 13, 27]
      },
      {
        name: 'PRODUCT C',
        data: [11, 17, 15, 15, 21, 14]
      },
      {
        name: 'PRODUCT D',
        data: [21, 7, 25, 13, 22, 8]
      }
    ],
    tooltip: {
      theme: LIGHT
    },
    xaxis: {
      type: 'datetime',
      categories: ['01/01/2011 GMT', '01/02/2011 GMT', '01/03/2011 GMT', '01/04/2011 GMT', '01/05/2011 GMT', '01/06/2011 GMT']
    },
    fill: {
      opacity: 1
    }
  };
  static barHorizontalChart = {
    chart: {
      height: 350,
      type: 'bar'
    },
    plotOptions: {
      bar: {
        horizontal: true,
        dataLabels: {
          position: 'top'
        }
      }
    },
    colors: ['#1890ff', '#52c41a'],
    dataLabels: {
      enabled: true,
      offsetX: -6,
      style: {
        fontSize: '12px',
        colors: ['#fff']
      }
    },
    tooltip: {
      theme: LIGHT
    },
    stroke: {
      show: true,
      width: 1,
      colors: ['#fff']
    },
    series: [
      {
        data: [44, 55, 41, 64, 22, 43, 21]
      },
      {
        data: [53, 32, 33, 52, 13, 44, 32]
      }
    ],
    xaxis: {
      categories: [2001, 2002, 2003, 2004, 2005, 2006, 2007]
    }
  };
  static barHStackChart = {
    chart: {
      height: 350,
      type: 'bar',
      stacked: true,
      stackType: '100%'
    },
    plotOptions: {
      bar: {
        horizontal: true
      }
    },
    colors: ['#1890ff', '#13c2c2', '#52c41a', '#faad14', '#ff4d4f'],
    stroke: {
      width: 1,
      colors: ['#fff']
    },
    series: [
      {
        name: 'Marine Sprite',
        data: [44, 55, 41, 37, 22, 43, 21]
      },
      {
        name: 'Striking Calf',
        data: [53, 32, 33, 52, 13, 43, 32]
      },
      {
        name: 'Tank Picture',
        data: [12, 17, 11, 9, 15, 11, 20]
      },
      {
        name: 'Bucket Slope',
        data: [9, 7, 5, 8, 6, 9, 4]
      },
      {
        name: 'Reborn Kid',
        data: [25, 12, 19, 32, 25, 24, 10]
      }
    ],
    title: {
      text: '100% Stacked Bar'
    },
    xaxis: {
      categories: [2008, 2009, 2010, 2011, 2012, 2013, 2014]
    },

    tooltip: {
      y: {
        formatter: function (val: string) {
          return val + 'K';
        }
      }
    },
    fill: {
      opacity: 1
    },

    legend: {
      position: 'top',
      horizontalAlign: 'left',
      offsetX: 40
    }
  };
  static pieChart = {
    chart: {
      height: 320,
      type: 'pie'
    },
    labels: ['Team A', 'Team B', 'Team C', 'Team D', 'Team E'],
    series: [44, 55, 13, 43, 22],
    legend: {
      show: true,
      position: 'bottom'
    },
    dataLabels: {
      enabled: true,
      dropShadow: {
        enabled: false
      }
    },
    responsive: [
      {
        breakpoint: 480,
        options: {
          legend: {
            position: 'bottom'
          }
        }
      }
    ]
  };
  static donutChart = {
    chart: {
      height: 320,
      type: 'donut'
    },
    series: [44, 55, 41, 17, 15],
    legend: {
      show: true,
      position: 'bottom'
    },
    plotOptions: {
      pie: {
        donut: {
          labels: {
            show: true,
            name: {
              show: true
            },
            value: {
              show: true
            }
          }
        }
      }
    },
    dataLabels: {
      enabled: true,
      dropShadow: {
        enabled: false
      }
    },
    responsive: [
      {
        breakpoint: 480,
        options: {
          legend: {
            position: 'bottom'
          }
        }
      }
    ]
  };
  static radialChart = {
    chart: {
      height: 350,
      type: 'radialBar'
    },
    plotOptions: {
      radialBar: {
        hollow: {
          size: '70%'
        }
      }
    },
    series: [70],
    labels: ['Cricket']
  };
  static customsAngleChart = {
    chart: {
      height: 350,
      type: 'radialBar'
    },
    plotOptions: {
      radialBar: {
        offsetY: -30,
        startAngle: 0,
        endAngle: 270,
        hollow: {
          margin: 5,
          size: '30%',
          background: 'transparent',
          image: undefined
        },
        dataLabels: {
          name: {
            show: false
          },
          value: {
            show: false
          }
        }
      }
    },
    series: [76, 67, 61, 90],
    labels: ['Vimeo', 'Messenger', 'Facebook', 'LinkedIn'],
    legend: {
      show: true,
      floating: true,
      fontSize: '16px',
      position: 'left',
      offsetX: 0,
      offsetY: 0,
      labels: {
        useSeriesColors: true
      },
      formatter: function (
        seriesName: string,
        opts: { w: { globals: { series: { [x: string]: string } } }; seriesIndex: string | number }
      ) {
        return seriesName + ':  ' + opts.w.globals.series[opts.seriesIndex];
      },
      itemMargin: {
        horizontal: 1
      }
    },
    responsive: [
      {
        breakpoint: 480,
        options: {
          legend: {
            show: false
          }
        }
      }
    ]
  };
  static lineChart = {
    chart: {
      height: 300,
      type: 'line',
      zoom: {
        enabled: false
      }
    },
    tooltip: {
      theme: LIGHT
    },
    dataLabels: {
      enabled: false
    },
    stroke: {
      curve: 'straight'
    },
    colors: ['#1890ff'],
    series: [
      {
        name: 'Desktops',
        data: [10, 41, 35, 51, 49, 62, 69, 91, 148]
      }
    ],
    title: {
      text: 'Product Trends by Month',
      align: 'left'
    },
    grid: {
      row: {
        colors: ['#f3f6ff', 'transparent'], // takes an array which will be repeated on columns
        opacity: 0.5
      }
    },
    xaxis: {
      categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep']
    }
  };
  static realTimeChart = {
    chart: {
      height: 300,
      type: 'line',
      zoom: {
        enabled: false
      }
    },
    dataLabels: {
      enabled: false
    },
    stroke: {
      width: [5, 7, 5],
      curve: 'straight',
      dashArray: [0, 8, 5]
    },
    colors: ['#52c41a', '#faad14', '#ff4d4f'],
    series: [
      {
        name: 'Session Duration',
        data: [45, 52, 38, 24, 33, 26, 21, 20, 6, 8, 15, 10]
      },
      {
        name: 'Page Views',
        data: [35, 41, 62, 42, 13, 18, 29, 37, 36, 51, 32, 35]
      },
      {
        name: 'Total Visits',
        data: [87, 57, 74, 99, 75, 38, 62, 47, 82, 56, 45, 47]
      }
    ],
    title: {
      text: 'Page Statistics',
      align: 'left'
    },
    markers: {
      size: 0,

      hover: {
        sizeOffset: 6
      }
    },
    xaxis: {
      categories: ['01 Jan', '02 Jan', '03 Jan', '04 Jan', '05 Jan', '06 Jan', '07 Jan', '08 Jan', '09 Jan', '10 Jan', '11 Jan', '12 Jan']
    },
    tooltip: {
      y: [
        {
          title: {
            formatter: (val: string) => val + ' (mins)'
          }
        },
        {
          title: {
            formatter: (val: string) => val + ' per session'
          }
        },
        {
          title: {
            formatter: (val: string) => val
          }
        }
      ]
    },
    grid: {
      borderColor: '#f1f1f1'
    }
  };
  static areaChart = {
    chart: {
      height: 350,
      type: 'area'
    },
    dataLabels: {
      enabled: false
    },
    stroke: {
      curve: 'smooth'
    },
    colors: ['#0050B3', '#4680FF'],
    series: [
      {
        name: 'series1',
        data: [31, 40, 28, 51, 42, 109, 100]
      },
      {
        name: 'series2',
        data: [11, 32, 45, 32, 34, 52, 41]
      }
    ],

    xaxis: {
      type: 'datetime',
      categories: [
        '2018-09-19T00:00:00',
        '2018-09-19T01:30:00',
        '2018-09-19T02:30:00',
        '2018-09-19T03:30:00',
        '2018-09-19T04:30:00',
        '2018-09-19T05:30:00',
        '2018-09-19T06:30:00'
      ]
    },
    tooltip: {
      x: {
        format: 'dd/MM/yy HH:mm'
      }
    }
  };
  static dateTimeChart = {
    series: [
      {
        data: data
      }
    ],
    chart: {
      type: 'area',
      height: 350
    },
    annotations: {
      yaxis: [
        {
          y: 30,
          borderColor: '#999',
          label: {
            text: 'Support',
            style: {
              color: '#fff',
              background: '#00E396'
            }
          }
        }
      ],
      xaxis: [
        {
          x: new Date('14 Nov 2012').getTime(),
          borderColor: '#999',
          label: {
            text: 'Rally',
            style: {
              color: '#fff',
              background: '#775DD0'
            }
          }
        }
      ]
    },
    dataLabels: {
      enabled: false
    },
    markers: {
      size: 0
    },
    xaxis: {
      type: 'datetime',
      min: new Date('01 Mar 2012').getTime(),
      tickAmount: 6
    },
    tooltip: {
      x: {
        format: 'dd MMM yyyy'
      }
    },
    fill: {
      type: 'gradient',
      gradient: {
        shadeIntensity: 1,
        opacityFrom: 0.7,
        opacityTo: 0.9,
        stops: [0, 100]
      }
    }
  };
  static mixedChart = {
    chart: {
      height: 350,
      type: 'line'
    },
    series: [
      {
        name: 'Website Blog',
        type: 'column',
        data: [440, 505, 414, 671, 227, 413, 201, 352, 752, 320, 257, 160]
      },
      {
        name: 'Social Media',
        type: 'line',
        data: [23, 42, 35, 27, 43, 22, 17, 31, 22, 22, 12, 16]
      }
    ],
    stroke: {
      width: [0, 4]
    },
    colors: ['#1890ff', '#ff4d4f'],
    title: {
      text: 'Traffic Sources'
    },
    tooltip: {
      theme: LIGHT
    },
    labels: [
      '01 Jan 2001',
      '02 Jan 2001',
      '03 Jan 2001',
      '04 Jan 2001',
      '05 Jan 2001',
      '06 Jan 2001',
      '07 Jan 2001',
      '08 Jan 2001',
      '09 Jan 2001',
      '10 Jan 2001',
      '11 Jan 2001',
      '12 Jan 2001'
    ],
    xaxis: {
      type: 'datetime'
    },
    yaxis: [
      {
        title: {
          text: 'Website Blog'
        }
      },
      {
        opposite: true,
        title: {
          text: 'Social Media'
        }
      }
    ]
  };
  static lineAreaChart = {
    chart: {
      height: 350,
      type: 'line',
      stacked: false
    },
    stroke: {
      width: [0, 2, 5],
      curve: 'smooth'
    },
    colors: ['#4680FF'],
    plotOptions: {
      bar: {
        columnWidth: '50%'
      }
    },
    series: [
      {
        name: 'Facebook',
        type: 'column',
        data: [23, 11, 22, 27, 13, 22, 37, 21, 44, 22, 30]
      },
      {
        name: 'Vine',
        type: 'area',
        data: [44, 55, 41, 67, 22, 43, 21, 41, 56, 27, 43]
      },
      {
        name: 'Dribbble',
        type: 'line',
        data: [30, 25, 36, 30, 45, 35, 64, 52, 59, 36, 39]
      }
    ],
    fill: {
      opacity: [0.85, 0.25, 1],
      gradient: {
        inverseColors: false,
        shade: LIGHT,
        type: 'vertical',
        opacityFrom: 0.85,
        opacityTo: 0.55,
        stops: [0, 100, 100, 100]
      }
    },
    labels: [
      '01/01/2003',
      '02/01/2003',
      '03/01/2003',
      '04/01/2003',
      '05/01/2003',
      '06/01/2003',
      '07/01/2003',
      '08/01/2003',
      '09/01/2003',
      '10/01/2003',
      '11/01/2003'
    ],
    markers: {
      size: 0
    },
    xaxis: {
      type: 'datetime'
    },
    yaxis: {
      title: {
        text: 'Points'
      },
      min: 0
    },
    tooltip: {
      shared: true,
      intersect: false,
      y: {
        formatter: function (y: number) {
          if (typeof y !== 'undefined') {
            return y.toFixed(0) + ' views';
          }
          return y;
        }
      }
    }
  };
  static candlestickChart = {
    chart: {
      height: 350,
      type: 'candlestick'
    },
    series: [
      {
        data: candlestick_data
      }
    ],
    title: {
      text: 'CandleStick Chart',
      align: 'left'
    },
    colors: ['#52c41a', '#ff4d4f'],
    xaxis: {
      type: 'datetime'
    },
    tooltip: {
      theme: LIGHT
    },
    yaxis: {
      tooltip: {
        enabled: true
      }
    }
  };
  static bubbleChart = {
    chart: {
      height: 350,
      type: 'bubble'
    },
    dataLabels: {
      enabled: false
    },
    series: [
      {
        name: 'Bubble1',
        data: generateData(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Bubble2',
        data: generateData(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Bubble3',
        data: generateData(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Bubble4',
        data: generateData(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      }
    ],
    colors: ['#1890ff', '#52c41a', '#faad14', '#ff4d4f'],
    fill: {
      opacity: 0.8
    },
    title: {
      text: 'Simple Bubble Chart'
    },
    xaxis: {
      tickAmount: 12,
      type: 'category'
    },
    yaxis: {
      max: 70
    },
    tooltip: {
      theme: LIGHT
    }
  };
  static bubble3DChart = {
    chart: {
      height: 350,
      type: 'bubble'
    },
    dataLabels: {
      enabled: false
    },
    series: [
      {
        name: 'Product1',
        data: generateData3D(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Product2',
        data: generateData3D(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Product3',
        data: generateData3D(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'Product4',
        data: generateData3D(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      }
    ],
    fill: {
      type: 'gradient'
    },
    colors: ['#1890ff', '#52c41a', '#faad14', '#ff4d4f'],
    title: {
      text: '3D Bubble Chart'
    },
    xaxis: {
      tickAmount: 12,
      type: 'datetime',

      labels: {
        rotate: 0
      }
    },
    yaxis: {
      max: 70
    },
    tooltip: {
      theme: LIGHT
    },
    theme: {
      palette: 'palette2'
    }
  };
  static scatterChart = {
    chart: {
      height: 350,
      type: 'scatter',
      zoom: {
        enabled: true,
        type: 'xy'
      }
    },
    colors: ['#1890ff', '#52c41a', '#ff4d4f', '#faad14', '#13c2c2'],
    series: [
      {
        name: 'SAMPLE A',
        data: [
          [16.4, 5.4],
          [21.7, 2],
          [25.4, 3],
          [19, 2],
          [10.9, 1],
          [13.6, 3.2],
          [10.9, 7.4],
          [10.9, 0],
          [10.9, 8.2],
          [16.4, 0],
          [16.4, 1.8],
          [13.6, 0.3],
          [13.6, 0],
          [29.9, 0],
          [27.1, 2.3],
          [16.4, 0],
          [13.6, 3.7],
          [10.9, 5.2],
          [16.4, 6.5],
          [10.9, 0],
          [24.5, 7.1],
          [10.9, 0],
          [8.1, 4.7],
          [19, 0],
          [21.7, 1.8],
          [27.1, 0],
          [24.5, 0],
          [27.1, 0],
          [29.9, 1.5],
          [27.1, 0.8],
          [22.1, 2]
        ]
      },
      {
        name: 'SAMPLE B',
        data: [
          [36.4, 13.4],
          [1.7, 11],
          [5.4, 8],
          [9, 17],
          [1.9, 4],
          [3.6, 12.2],
          [1.9, 14.4],
          [1.9, 9],
          [1.9, 13.2],
          [1.4, 7],
          [6.4, 8.8],
          [3.6, 4.3],
          [1.6, 10],
          [9.9, 2],
          [7.1, 15],
          [1.4, 0],
          [3.6, 13.7],
          [1.9, 15.2],
          [6.4, 16.5],
          [0.9, 10],
          [4.5, 17.1],
          [10.9, 10],
          [0.1, 14.7],
          [9, 10],
          [12.7, 11.8],
          [2.1, 10],
          [2.5, 10],
          [27.1, 10],
          [2.9, 11.5],
          [7.1, 10.8],
          [2.1, 12]
        ]
      },
      {
        name: 'SAMPLE C',
        data: [
          [21.7, 3],
          [23.6, 3.5],
          [24.6, 3],
          [29.9, 3],
          [21.7, 20],
          [23, 2],
          [10.9, 3],
          [28, 4],
          [27.1, 0.3],
          [16.4, 4],
          [13.6, 0],
          [19, 5],
          [22.4, 3],
          [24.5, 3],
          [32.6, 3],
          [27.1, 4],
          [29.6, 6],
          [31.6, 8],
          [21.6, 5],
          [20.9, 4],
          [22.4, 0],
          [32.6, 10.3],
          [29.7, 20.8],
          [24.5, 0.8],
          [21.4, 0],
          [21.7, 6.9],
          [28.6, 7.7],
          [15.4, 0],
          [18.1, 0],
          [33.4, 0],
          [16.4, 0]
        ]
      }
    ],
    xaxis: {
      tickAmount: 10,
      labels: {
        formatter: function (val: string) {
          return parseFloat(val).toFixed(1);
        }
      }
    },
    yaxis: {
      tickAmount: 7
    }
  };
  static scatterDateTimeChart = {
    chart: {
      height: 350,
      type: 'scatter',
      zoom: {
        type: 'xy'
      }
    },
    series: [
      {
        name: 'TEAM 1',
        data: generateDayWiseTimeSeries(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'TEAM 2',
        data: generateDayWiseTimeSeries(new Date('11 Feb 2017 GMT').getTime(), 20, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'TEAM 3',
        data: generateDayWiseTimeSeries(new Date('11 Feb 2017 GMT').getTime(), 30, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'TEAM 4',
        data: generateDayWiseTimeSeries(new Date('11 Feb 2017 GMT').getTime(), 10, {
          min: 10,
          max: 60
        })
      },
      {
        name: 'TEAM 5',
        data: generateDayWiseTimeSeries(new Date('11 Feb 2017 GMT').getTime(), 30, {
          min: 10,
          max: 60
        })
      }
    ],
    dataLabels: {
      enabled: false
    },
    colors: ['#1890ff', '#52c41a', '#ff4d4f', '#faad14', '#13c2c2'],
    grid: {
      xaxis: {
        lines: {
          show: true
        }
      },
      yaxis: {
        lines: {
          show: true
        }
      }
    },
    tooltip: {
      theme: LIGHT
    },
    xaxis: {
      type: 'datetime'
    },
    yaxis: {
      max: 70
    }
  };
  static heatmapChart = {
    chart: {
      height: 350,
      type: 'heatmap'
    },
    dataLabels: {
      enabled: false
    },
    colors: ['#1890ff'],
    series: [
      {
        name: 'Metric1',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric2',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric3',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric4',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric5',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric6',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric7',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric8',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric9',
        data: generateDataHeatmap(12, {
          min: 0,
          max: 90
        })
      }
    ],
    title: {
      text: 'HeatMap Chart (Single color)'
    },
    tooltip: {
      theme: LIGHT
    }
  };
  static heatmapRoundedChart = {
    chart: {
      height: 350,
      type: 'heatmap'
    },
    stroke: {
      width: 0
    },
    tooltip: {
      theme: LIGHT
    },
    plotOptions: {
      heatmap: {
        radius: 30,
        enableShades: false,
        colorScale: {
          ranges: [
            {
              from: 0,
              to: 50,
              color: '#faad14'
            },
            {
              from: 51,
              to: 100,
              color: '#ff4d4f'
            }
          ]
        }
      }
    },
    dataLabels: {
      enabled: true,
      style: {
        colors: ['#fff']
      }
    },
    colors: ['#1890ff', '#13c2c2', '#52c41a', '#faad14', '#ff4d4f'],
    xaxis: {
      type: 'category'
    },
    title: {
      text: 'Rounded (Range without Shades)'
    },
    series: [
      {
        name: 'Metric1',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric2',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric3',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric4',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric5',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric6',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric7',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric8',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      },
      {
        name: 'Metric8',
        data: generateDataRounded(15, {
          min: 0,
          max: 90
        })
      }
    ]
  };
}

// bubble chart
function generateData(baseval: number, count: number, yrange: { max: number; min: number }) {
  let i = 0;
  const series = [];
  while (i < count) {
    const x = Math.floor(Math.random() * (750 - 1 + 1)) + 1;
    const y = Math.floor(Math.random() * (yrange.max - yrange.min + 1)) + yrange.min;
    const z = Math.floor(Math.random() * (75 - 15 + 1)) + 15;

    series.push([x, y, z]);
    baseval += 86400000;
    i++;
  }
  return series;
}

// bubble chart 3D
function generateData3D(baseval: number, count: number, yrange: { min: number; max: number }) {
  let i = 0;
  const series = [];
  while (i < count) {
    //var x =Math.floor(Math.random() * (750 - 1 + 1)) + 1;;
    const y = Math.floor(Math.random() * (yrange.max - yrange.min + 1)) + yrange.min;
    const z = Math.floor(Math.random() * (75 - 15 + 1)) + 15;

    series.push([baseval, y, z]);
    baseval += 86400000;
    i++;
  }
  return series;
}

// scatter-datetime
function generateDayWiseTimeSeries(baseval: number, count: number, yrange: { min: number; max: number }) {
  let i = 0;
  const series = [];
  while (i < count) {
    const y = Math.floor(Math.random() * (yrange.max - yrange.min + 1)) + yrange.min;

    series.push([baseval, y]);
    baseval += 86400000;
    i++;
  }
  return series;
}

// HeatMap Chart
function generateDataHeatmap(count: number, yrange: { min: number; max: number }) {
  let i = 0;
  const series = [];
  while (i < count) {
    const x = 'w' + (i + 1).toString();
    const y = Math.floor(Math.random() * (yrange.max - yrange.min + 1)) + yrange.min;

    series.push({
      x: x,
      y: y
    });
    i++;
  }
  return series;
}

//Rounded HeatMap Chart
function generateDataRounded(count: number, yrange: { min: number; max: number }) {
  let i = 0;
  const series = [];
  while (i < count) {
    const x = (i + 1).toString();
    const y = Math.floor(Math.random() * (yrange.max - yrange.min + 1)) + yrange.min;

    series.push({
      x: x,
      y: y
    });
    i++;
  }
  return series;
}
