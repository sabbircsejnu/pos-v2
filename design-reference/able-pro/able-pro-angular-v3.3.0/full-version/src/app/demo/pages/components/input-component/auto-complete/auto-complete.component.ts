// angular import
import { Component, OnInit, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormControl } from '@angular/forms';
import { Observable } from 'rxjs';
import { map, startWith } from 'rxjs/operators';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { GroupData } from 'src/app/fake-data/group-data';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { RTL } from 'src/app/@theme/const';

export interface User {
  name: string;
}

export interface StateGroup {
  letter: string;
  names: string[];
}

export interface State {
  flag: string;
  name: string;
  population: string;
}

export const _filter = (opt: string[], value: string): string[] => {
  const filterValue = value.toLowerCase();

  return opt.filter((item) => item.toLowerCase().includes(filterValue));
};

@Component({
  selector: 'app-auto-complete',
  imports: [CommonModule, SharedModule],
  templateUrl: './auto-complete.component.html',
  styleUrls: ['./auto-complete.component.scss']
})
export class AutoCompleteComponent implements OnInit {
  private _formBuilder = inject(FormBuilder);
  private themeService = inject(ThemeLayoutService);

  // public props
  myControl = new FormControl('');
  myControl_2 = new FormControl<string | User>('');
  stateCtrl = new FormControl('');
  options: string[] = ['One', 'Two', 'Three'];
  options_2: User[] = [{ name: 'Mary' }, { name: 'Shelley' }, { name: 'Igor' }];
  filteredOptions: Observable<string[]>;
  filteredOptions_2: Observable<User[]>;
  stateGroupOptions: Observable<StateGroup[]>;
  filteredStates: Observable<State[]>;
  stateGroups: StateGroup[] = GroupData;
  themeDirection: boolean = false;

  // user select
  displayFn(user: User): string {
    return user && user.name ? user.name : '';
  }

  // group select
  stateForm = this._formBuilder.group({
    stateGroup: ''
  });

  // constructor
  constructor() {
    this.filteredStates = this.stateCtrl.valueChanges.pipe(
      startWith(''),
      map((state) => (state ? this._filterStates(state) : this.states.slice()))
    );

    effect(() => {
      this.themeMode(this.themeService.directionChange());
    });
  }

  // life cycle event
  ngOnInit() {
    this.filteredOptions = this.myControl.valueChanges.pipe(
      startWith(''),
      map((value) => this._filter(value || ''))
    );
    this.filteredOptions_2 = this.myControl_2.valueChanges.pipe(
      startWith(''),
      map((value) => {
        const name = typeof value === 'string' ? value : value?.name;
        return name ? this._filterUsers(name as string) : this.options_2.slice();
      })
    );

    this.stateGroupOptions = this.stateForm.get('stateGroup')!.valueChanges.pipe(
      startWith(''),
      map((value) => this._filterGroup(value || ''))
    );
  }

  // private method
  private _filter(value: string): string[] {
    const filterValue = value.toLowerCase();

    return this.options.filter((option) => option.toLowerCase().includes(filterValue));
  }

  private _filterUsers(value: string): User[] {
    const filterValue = value.toLowerCase();

    return this.options_2.filter((user) => user.name.toLowerCase().includes(filterValue));
  }

  private _filterGroup(value: string): StateGroup[] {
    if (value) {
      return this.stateGroups
        .map((group) => ({ letter: group.letter, names: _filter(group.names, value) }))
        .filter((group) => group.names.length > 0);
    }

    return this.stateGroups;
  }

  private _filterStates(value: string): State[] {
    const filterValue = value.toLowerCase();

    return this.states.filter((state) => state.name.toLowerCase().includes(filterValue));
  }

  private themeMode(dir: string) {
    this.themeDirection = dir === RTL ? true : false;
  }

  // state data
  states: State[] = [
    {
      name: 'Arkansas',
      population: '2.978M',
      flag: 'https://upload.wikimedia.org/wikipedia/commons/9/9d/Flag_of_Arkansas.svg'
    },
    {
      name: 'California',
      population: '39.14M',
      flag: 'https://upload.wikimedia.org/wikipedia/commons/0/01/Flag_of_California.svg'
    },
    {
      name: 'Florida',
      population: '20.27M',
      flag: 'https://upload.wikimedia.org/wikipedia/commons/f/f7/Flag_of_Florida.svg'
    },
    {
      name: 'Texas',
      population: '27.47M',
      flag: 'https://upload.wikimedia.org/wikipedia/commons/f/f7/Flag_of_Texas.svg'
    }
  ];
}
