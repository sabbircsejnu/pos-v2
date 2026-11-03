// angular import
import { AfterViewInit, Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SelectionModel } from '@angular/cdk/collections';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { MatSort } from '@angular/material/sort';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

import { ProductService } from 'src/app/demo/pages/application/e-commerce/product.service';
import { Products } from 'src/app/@theme/types/product';
import { StarRatingComponent } from 'src/app/@theme/components/star-rating/star-rating.component';

@Component({
  selector: 'app-product-list',
  imports: [CommonModule, SharedModule, StarRatingComponent],
  templateUrl: './product-list.component.html',
  styleUrls: ['./product-list.component.scss']
})
export class ProductListComponent implements AfterViewInit, OnInit {
  private productService = inject(ProductService);

  // public props
  readonly paginator = viewChild(MatPaginator);
  readonly sort = viewChild(MatSort);
  dataSource: MatTableDataSource<Products>;
  selection: SelectionModel<Products>;
  columnsToDisplay = ['select', 'id', 'details', 'categories', 'price', 'qty', 'status', 'actions'];
  columnsToDisplayWithExpand = [...this.columnsToDisplay];

  // constructor
  constructor() {
    this.dataSource = new MatTableDataSource<Products>([]);
    this.selection = new SelectionModel<Products>(true, []);
  }

  // life cycle event
  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator()!;
    this.dataSource.sort = this.sort()!;
  }

  ngOnInit() {
    this.productService.getProduct();
    this.productService.product.subscribe((data) => {
      this.dataSource.data = data; // Update dataSource's data
    });
  }

  // public method
  /** Whether the number of selected elements matches the total number of rows. */
  isAllSelected() {
    const numSelected = this.selection.selected.length;
    const numRows = this.dataSource.data.length;
    return numSelected === numRows;
  }

  /** Selects all rows if they are not all selected; otherwise clear selection. */
  toggleAllRows() {
    if (this.isAllSelected()) {
      this.selection.clear();
      return;
    }

    this.selection.select(...this.dataSource.data);
  }

  /** The label for the checkbox on the passed row */
  checkboxLabel(row?: Products): string {
    if (!row) {
      return `${this.isAllSelected() ? 'deselect' : 'select'} all`;
    }
    return `${this.selection.isSelected(row) ? 'deselect' : 'select'} row `;
  }
}
