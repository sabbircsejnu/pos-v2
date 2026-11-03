// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';

// angular material import
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatInputModule } from '@angular/material/input';
import { MatGridListModule } from '@angular/material/grid-list';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatBadgeModule } from '@angular/material/badge';
import { MatRadioModule } from '@angular/material/radio';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialogModule } from '@angular/material/dialog';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSliderModule } from '@angular/material/slider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { CdkAccordionModule } from '@angular/cdk/accordion';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatTreeModule } from '@angular/material/tree';
import { DragDropModule } from '@angular/cdk/drag-drop';
import { LayoutModule } from '@angular/cdk/layout';
import { MatStepperModule } from '@angular/material/stepper';
import { MatTableModule } from '@angular/material/table';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBarModule } from '@angular/material/snack-bar';

// third party import
import { NgScrollbarModule } from 'ngx-scrollbar';
import { TranslateModule, TranslateLoader } from '@ngx-translate/core';

// project import
import { CustomTranslateLoader } from './custom-translate-loader';
import { CardComponent } from 'src/app/@theme/components/card/card.component';

const MaterialModules = [
  MatToolbarModule,
  MatSidenavModule,
  MatCheckboxModule,
  MatButtonModule,
  MatDividerModule,
  MatIconModule,
  MatMenuModule,
  MatInputModule,
  MatGridListModule,
  MatCardModule,
  MatListModule,
  MatSlideToggleModule,
  MatBadgeModule,
  MatRadioModule,
  MatPaginatorModule,
  MatFormFieldModule,
  MatProgressBarModule,
  MatDialogModule,
  MatNativeDateModule,
  MatSliderModule,
  MatProgressSpinnerModule,
  MatTabsModule,
  MatButtonToggleModule,
  MatTooltipModule,
  MatSelectModule,
  CdkAccordionModule,
  MatDatepickerModule,
  MatTreeModule,
  DragDropModule,
  LayoutModule,
  MatStepperModule,
  MatTableModule,
  MatAutocompleteModule,
  MatChipsModule,
  MatSnackBarModule
];

@NgModule({
  declarations: [],
  imports: [
    CommonModule,
    MaterialModules,
    FormsModule,
    ReactiveFormsModule,
    NgScrollbarModule,
    TranslateModule.forRoot({
      loader: {
        provide: TranslateLoader,
        useClass: CustomTranslateLoader
      }
    }),
    CardComponent
  ],
  exports: [MaterialModules, FormsModule, ReactiveFormsModule, NgScrollbarModule, TranslateModule, CardComponent]
})
export class SharedModule {}
