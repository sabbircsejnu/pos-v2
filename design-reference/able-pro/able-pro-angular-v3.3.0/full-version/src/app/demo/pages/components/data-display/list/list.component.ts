// Angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

export interface Section {
  name: string;
  updated: Date;
}

@Component({
  selector: 'app-list',
  imports: [CommonModule, SharedModule],
  templateUrl: './list.component.html',
  styleUrls: ['./list.component.scss']
})
export class ListComponent {
  // public method
  folders: Section[] = [
    {
      name: 'Photos',
      updated: new Date('1/1/16')
    },
    {
      name: 'Recipes',
      updated: new Date('1/17/16')
    },
    {
      name: 'Work',
      updated: new Date('1/28/16')
    }
  ];
  notes: Section[] = [
    {
      name: 'Vacation Itinerary',
      updated: new Date('2/20/16')
    },
    {
      name: 'Kitchen Remodel',
      updated: new Date('1/18/16')
    }
  ];
  typesOfShoes: string[] = ['Boots', 'Clogs', 'Loafers', 'Moccasins', 'Sneakers'];
  typesOfFlower: string[] = ['Rose', 'Peony', 'Orchids', 'Aster', 'Sunflower'];
}
