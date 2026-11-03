// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-under-constructor',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './under-constructor.component.html',
  styleUrls: ['../maintenance.scss', './under-constructor.component.scss']
})
export class UnderConstructorComponent {}
