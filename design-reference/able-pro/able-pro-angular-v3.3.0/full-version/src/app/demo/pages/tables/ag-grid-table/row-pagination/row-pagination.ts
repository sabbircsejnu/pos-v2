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
import { ClientSideRowModelModule, ColDef, ModuleRegistry, PaginationModule } from 'ag-grid-community';

ModuleRegistry.registerModules([PaginationModule, ClientSideRowModelModule]);

@Component({
  selector: 'app-row-pagination',
  imports: [SharedModule, AgGridAngular],
  templateUrl: './row-pagination.html',
  styleUrl: './row-pagination.scss'
})
export class RowPagination {
  // private props
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
    {
      field: 'athlete',
      minWidth: 170
    },
    { field: 'age' },
    { field: 'country' },
    { field: 'date' },
    { field: 'total' }
  ];

  defaultColDef: ColDef = {
    flex: 1,
    minWidth: 100
  };

  onGridReady() {
    this.http.get<IOlympicData[]>('https://www.ag-grid.com/example-assets/olympic-winners.json').subscribe((data) => (this.rowData = data));
  }
}
