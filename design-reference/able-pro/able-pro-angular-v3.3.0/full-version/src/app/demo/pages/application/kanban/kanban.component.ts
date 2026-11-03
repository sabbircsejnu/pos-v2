// angular import
import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { KanbanColumnsComponent } from './kanban-columns/kanban-columns.component';
import { KanbanBacklogsComponent } from './kanban-backlogs/kanban-backlogs.component';
import { KanbanColumn, KanbanComment, KanbanItem, KanbanProfile, KanbanUserStory } from 'src/app/@theme/types/kanban-type';
import { KanbanService } from 'src/app/demo/pages/application/kanban/kanban.service';
import { TaskDetailsComponent } from './task-details/task-details.component';
import { UserStoryDetailsComponent } from './user-story-details/user-story-details.component';
import { KanbanLayoutService } from './kanban-layout.service';

@Component({
  selector: 'app-kanban',
  imports: [CommonModule, SharedModule, TaskDetailsComponent, KanbanColumnsComponent, KanbanBacklogsComponent, UserStoryDetailsComponent],
  templateUrl: './kanban.component.html',
  styleUrls: ['./kanban.component.scss']
})
export class KanbanComponent implements OnInit {
  private kanbanService = inject(KanbanService);
  private layoutService = inject(KanbanLayoutService);

  // public props
  columns: KanbanColumn[];
  userComment: KanbanComment[];
  userStory: KanbanUserStory[];
  Profile: KanbanProfile[];
  items: KanbanItem[];
  columnAdd!: boolean;
  newColumns: string;

  // life cycle event
  ngOnInit(): void {
    // column data
    this.kanbanService.getColumns();
    this.kanbanService.columns.subscribe((columnData) => {
      this.columns = columnData;
    });

    // item data
    this.kanbanService.getItems();
    this.kanbanService.items.subscribe((itemsData) => {
      this.items = itemsData;
    });

    // Comment data
    this.kanbanService.getComments();
    this.kanbanService.comments.subscribe((commentData) => {
      this.userComment = commentData;
    });

    // Profile data
    this.kanbanService.getProfiles();
    this.kanbanService.profiles.subscribe((ProfileData) => {
      this.Profile = ProfileData;
    });

    // user story data
    this.kanbanService.getUserStory();
    this.kanbanService.userStory.subscribe((story) => {
      this.userStory = story;
    });

    this.kanbanService.editTask.subscribe(() => {
      if (!this.layoutService.storySliderClosed) {
        // close drawer for story
        this.layoutService.toggleStory();
      }

      if (this.layoutService.taskSliderClosed) {
        this.layoutService.toggleTask();
      }
    });

    this.kanbanService.editStory.subscribe(() => {
      if (!this.layoutService.taskSliderClosed) {
        // close drawer for task
        this.layoutService.toggleTask();
      }

      if (this.layoutService.storySliderClosed) {
        this.layoutService.toggleStory();
      }
    });
  }

  // form open for new task add
  toggleColumn() {
    this.columnAdd = !this.columnAdd;
  }

  // public method
  deleteColumn(columnId: string) {
    this.kanbanService.deleteColumn(columnId);
  }

  // new column add details
  columnSubmit() {
    this.columnAdd = false;
    const newColumn: KanbanColumn = {
      id: '',
      title: this.newColumns,
      itemIds: []
    };
    this.kanbanService.addNewColumn(newColumn);
  }
}
