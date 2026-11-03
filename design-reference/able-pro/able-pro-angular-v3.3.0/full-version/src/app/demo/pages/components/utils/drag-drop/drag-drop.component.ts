// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// angular cdk import
import { CdkDrag, CdkDragDrop, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';

@Component({
  selector: 'app-drag-drop',
  imports: [CommonModule, SharedModule],
  templateUrl: './drag-drop.component.html',
  styleUrls: ['./drag-drop.component.scss']
})
export class DragDropComponent {
  // public props
  todo = ['Get to work', 'Pick up groceries', 'Go home', 'Fall asleep'];

  done = ['Get up', 'Brush teeth', 'Take a shower', 'Check e-mail', 'Walk dog'];

  movies = [
    'Episode I - The Phantom Menace',
    'Episode II - Attack of the Clones',
    'Episode III - Revenge of the Sith',
    'Episode IV - A New Hope',
    'Episode V - The Empire Strikes Back',
    'Episode VI - Return of the Jedi'
  ];

  movies_pitcher = [
    {
      title: 'Episode I - The Phantom Menace',
      poster: 'https://upload.wikimedia.org/wikipedia/en/4/40/Star_Wars_Phantom_Menace_poster.jpg'
    },
    {
      title: 'Episode II - Attack of the Clones',
      poster: 'https://upload.wikimedia.org/wikipedia/en/3/32/Star_Wars_-_Episode_II_Attack_of_the_Clones_%28movie_poster%29.jpg'
    },
    {
      title: 'Episode III - Revenge of the Sith',
      poster: 'https://upload.wikimedia.org/wikipedia/en/9/93/Star_Wars_Episode_III_Revenge_of_the_Sith_poster.jpg'
    },
    {
      title: 'Episode IV - A New Hope',
      poster: 'https://upload.wikimedia.org/wikipedia/en/8/87/StarWarsMoviePoster1977.jpg'
    },
    {
      title: 'Episode V - The Empire Strikes Back',
      poster: 'https://upload.wikimedia.org/wikipedia/en/3/3f/The_Empire_Strikes_Back_%281980_film%29.jpg'
    },
    {
      title: 'Episode VI - Return of the Jedi',
      poster: 'https://upload.wikimedia.org/wikipedia/en/b/b2/ReturnOfTheJediPoster1983.jpg'
    },
    {
      title: 'Episode VII - The Force Awakens',
      poster: 'https://upload.wikimedia.org/wikipedia/en/a/a2/Star_Wars_The_Force_Awakens_Theatrical_Poster.jpg'
    },
    {
      title: 'Episode VIII - The Last Jedi',
      poster: 'https://upload.wikimedia.org/wikipedia/en/7/7f/Star_Wars_The_Last_Jedi.jpg'
    },
    {
      title: 'Episode IX – The Rise of Skywalker',
      poster: 'https://upload.wikimedia.org/wikipedia/en/a/af/Star_Wars_The_Rise_of_Skywalker_poster.jpg'
    }
  ];

  items = [
    { value: 'I can be dragged', disabled: false },
    { value: 'I cannot be dragged', disabled: true },
    { value: 'I can also be dragged', disabled: false }
  ];

  all = [1, 2, 3, 4, 5, 6, 7, 8, 9];
  even = [10];

  // public method
  drop(event: CdkDragDrop<string[]>) {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
    } else {
      transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    }
  }

  drop_1(event: CdkDragDrop<string[]>) {
    moveItemInArray(this.movies, event.previousIndex, event.currentIndex);
  }

  drop_2(event: CdkDragDrop<{ title: string; poster: string }[]>) {
    moveItemInArray(this.movies_pitcher, event.previousIndex, event.currentIndex);
  }

  drop_3(event: CdkDragDrop<string[]>) {
    moveItemInArray(this.items, event.previousIndex, event.currentIndex);
  }

  drop_4(event: CdkDragDrop<number[]>) {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
    } else {
      transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    }
  }

  evenPredicate(item: CdkDrag<number>) {
    return item.data % 2 === 0;
  }

  noReturnPredicate() {
    return false;
  }
}
