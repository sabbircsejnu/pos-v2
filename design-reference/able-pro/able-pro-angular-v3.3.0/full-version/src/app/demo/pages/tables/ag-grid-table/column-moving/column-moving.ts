// angular import
import { Component, effect, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// project import
import { createAgGridTheme } from '../agGridTableStyle';
import { IOlympicData } from '../agGridType';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { RTL } from 'src/app/@theme/const';

// third party
import { AgGridAngular } from 'ag-grid-angular';
import { ClientSideRowModelModule, ColDef, ColumnApiModule, GridApi, GridReadyEvent, ModuleRegistry } from 'ag-grid-community';

ModuleRegistry.registerModules([ColumnApiModule, ClientSideRowModelModule]);

@Component({
  selector: 'app-column-moving',
  imports: [SharedModule, AgGridAngular],
  templateUrl: './column-moving.html',
  styleUrl: './column-moving.scss'
})
export class ColumnMoving {
  // private props
  private gridApi!: GridApi<IOlympicData>;
  private themeService = inject(ThemeLayoutService);
  private http = inject(HttpClient);

  showGrid: boolean = true;
  direction: boolean;

  // table theme apply
  theme = createAgGridTheme(this.themeService.isDarkMode());

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
    { field: 'age' },
    { field: 'country' },
    { field: 'year' },
    { field: 'date' },
    { field: 'sport' },
    { field: 'gold' },
    { field: 'silver' },
    { field: 'bronze' },
    { field: 'total' }
  ];
  defaultColDef: ColDef = {
    width: 150
  };

  onMedalsFirst() {
    this.gridApi.moveColumns(['gold', 'silver', 'bronze', 'total'], 0);
  }

  onMedalsLast() {
    this.gridApi.moveColumns(['gold', 'silver', 'bronze', 'total'], 6);
  }

  onCountryFirst() {
    this.gridApi.moveColumns(['country'], 0);
  }

  onSwapFirstTwo() {
    this.gridApi.moveColumnByIndex(0, 1);
  }

  onGridReady(params: GridReadyEvent<IOlympicData>) {
    this.gridApi = params.api;

    this.http.get<IOlympicData[]>('https://www.ag-grid.com/example-assets/olympic-winners.json').subscribe((data) => (this.rowData = data));
  }
}
