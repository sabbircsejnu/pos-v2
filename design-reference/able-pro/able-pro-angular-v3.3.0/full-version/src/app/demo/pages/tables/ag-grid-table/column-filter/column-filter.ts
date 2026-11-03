// Angular import
import { Component, effect, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// project import
import { IOlympicData } from '../agGridType';
import { createAgGridTheme } from '../agGridTableStyle';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { RTL } from 'src/app/@theme/const';

// third party
import { AgGridAngular } from 'ag-grid-angular';
import {
  ClientSideRowModelModule,
  ColDef,
  DateFilterModule,
  IDateFilterParams,
  ModuleRegistry,
  NumberFilterModule,
  TextFilterModule
} from 'ag-grid-community';
import { ColumnMenuModule, ContextMenuModule, MultiFilterModule, SetFilterModule } from 'ag-grid-enterprise';

ModuleRegistry.registerModules([
  ClientSideRowModelModule,
  TextFilterModule,
  NumberFilterModule,
  DateFilterModule,
  SetFilterModule,
  MultiFilterModule,
  ColumnMenuModule,
  ContextMenuModule
]);

@Component({
  selector: 'app-column-filter',
  imports: [SharedModule, AgGridAngular],
  templateUrl: './column-filter.html',
  styleUrl: './column-filter.scss'
})
export class ColumnFilter {
  // props
  private themeService = inject(ThemeLayoutService);
  private http = inject(HttpClient);

  // table theme apply
  theme = createAgGridTheme(this.themeService.isDarkMode());
  showGrid: boolean = true;
  direction: boolean;

  // constructor
  constructor() {
    effect(() => {
      const isDark = this.themeService.isDarkMode();
      const dir = this.themeService.directionChange();

      this.themeDirection(dir);
      this.theme = createAgGridTheme(isDark);
    });
  }

  public themeDirection(direction: string) {
    this.direction = direction === RTL;
    // Force grid to re-render
    this.showGrid = false;
    setTimeout(() => {
      this.showGrid = true;
    });
  }

  // table data
  rowData!: IOlympicData[];

  // public methods
  columnDefs: ColDef[] = [
    { field: 'athlete' },
    { field: 'age', filter: 'agNumberColumnFilter', maxWidth: 100 },
    {
      field: 'date',
      filter: 'agDateColumnFilter',
      filterParams: filterParams
    },
    { field: 'country', filter: 'agSetColumnFilter' },
    { field: 'sport', filter: 'agMultiColumnFilter' },
    { field: 'gold', filter: 'agNumberColumnFilter' },
    { field: 'silver', filter: 'agNumberColumnFilter' },
    { field: 'bronze', filter: 'agNumberColumnFilter' },
    { field: 'total', filter: false }
  ];
  defaultColDef: ColDef = {
    flex: 1,
    minWidth: 150,
    filter: 'agTextColumnFilter',
    suppressHeaderMenuButton: true,
    suppressHeaderContextMenu: true
  };

  onGridReady() {
    this.http.get<IOlympicData[]>('https://www.ag-grid.com/example-assets/olympic-winners.json').subscribe((data) => (this.rowData = data));
  }
}

// data filter
const filterParams: IDateFilterParams = {
  comparator: (filterLocalDateAtMidnight: Date, cellValue: string) => {
    const dateAsString = cellValue;
    if (dateAsString == null) return -1;
    const dateParts = dateAsString.split('/');
    const cellDate = new Date(Number(dateParts[2]), Number(dateParts[1]) - 1, Number(dateParts[0]));
    if (filterLocalDateAtMidnight.getTime() === cellDate.getTime()) {
      return 0;
    }
    if (cellDate < filterLocalDateAtMidnight) {
      return -1;
    }
    if (cellDate > filterLocalDateAtMidnight) {
      return 1;
    }
    return 0;
  }
};
