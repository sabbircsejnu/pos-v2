// Angular import
import { Component, effect, inject } from '@angular/core';

// project import
import { createAgGridTheme } from '../agGridTableStyle';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { RTL } from 'src/app/@theme/const';

// third party
import { AgGridAngular } from 'ag-grid-angular';
import {
  CellStyleModule,
  ClientSideRowModelModule,
  ColDef,
  GridReadyEvent,
  HighlightChangesModule,
  ModuleRegistry,
  RowApiModule
} from 'ag-grid-community';

ModuleRegistry.registerModules([RowApiModule, CellStyleModule, ClientSideRowModelModule, HighlightChangesModule]);

@Component({
  selector: 'app-highlighting-change',
  imports: [SharedModule, AgGridAngular],
  templateUrl: './highlighting-change.html',
  styleUrl: './highlighting-change.scss'
})
export class HighlightingChange {
  // private props
  private themeService = inject(ThemeLayoutService);
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

  // public methods
  columnDefs: ColDef[] = [
    { field: 'a', enableCellChangeFlash: true },
    { field: 'b', enableCellChangeFlash: true },
    { field: 'c', cellRenderer: 'agAnimateShowChangeCellRenderer' },
    { field: 'd', cellRenderer: 'agAnimateShowChangeCellRenderer' },
    { field: 'e', cellRenderer: 'agAnimateSlideCellRenderer' },
    { field: 'f', cellRenderer: 'agAnimateSlideCellRenderer' }
  ];
  defaultColDef: ColDef = {
    flex: 1,
    cellClass: 'align-right',
    valueFormatter: (params) => {
      return formatNumber(params.value);
    }
  };
  rowData: unknown[] | null = createRowData();

  onGridReady(params: GridReadyEvent) {
    const updateValues = () => {
      const rowCount = params.api!.getDisplayedRowCount();
      // pick 2 cells at random to update
      for (let i = 0; i < 2; i++) {
        const row = Math.floor(Math.random() * rowCount);
        const rowNode = params.api!.getDisplayedRowAtIndex(row)!;
        const col = ['a', 'b', 'c', 'd', 'e', 'f'][Math.floor(Math.random() * 6)];
        rowNode.setDataValue(col, Math.floor(Math.random() * 10000));
      }
    };
    setInterval(updateValues, 250);
  }
}

function formatNumber(number: number) {
  return Math.floor(number).toLocaleString();
}
function createRowData() {
  const rowData = [];
  for (let i = 0; i < 20; i++) {
    rowData.push({
      a: Math.floor(((i + 323) * 145045) % 10000),
      b: Math.floor(((i + 323) * 543020) % 10000),
      c: Math.floor(((i + 323) * 305920) % 10000),
      d: Math.floor(((i + 323) * 204950) % 10000),
      e: Math.floor(((i + 323) * 103059) % 10000),
      f: Math.floor(((i + 323) * 468276) % 10000)
    });
  }
  return rowData;
}
