// angular import
import { Component, OnInit, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDragDrop, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { KanbanService } from 'src/app/demo/pages/application/kanban/kanban.service';
import { KanbanColumn, KanbanItem, KanbanUserStory } from 'src/app/@theme/types/kanban-type';

@Component({
  selector: 'app-kanban-columns',
  imports: [CommonModule, SharedModule],
  templateUrl: './kanban-columns.component.html',
  styleUrls: ['./kanban-columns.component.scss']
})
export class KanbanColumnsComponent implements OnInit {
  private kanbanService = inject(KanbanService);

  // public props
  items: KanbanItem[];
  taskItems: KanbanItem[];
  updatedTask: KanbanItem[];
  userStory: KanbanUserStory[];
  columnData: KanbanColumn;
  userStoryId: number | string;
  taskAdd!: boolean;
  inputDetails: string;
  title = input<string>();

  // life cycle event
  ngOnInit(): void {
    this.kanbanService.getItems();
    this.columnData = this.kanbanService.getColumnFromTitle(this.title()!);
    this.kanbanService.items.subscribe((itemsData) => {
      this.taskItems = itemsData.filter((x) => this.columnData?.itemIds.includes(x.id));
    });
  }

  // public method

  // for task item drag and drop
  drop(event: CdkDragDrop<{ header_title: string; story_number: string; img: string }[]>) {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
    } else {
      transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    }
  }

  // form open for new task add
  toggleTask() {
    this.taskAdd = !this.taskAdd;
  }

  // userStory id get
  getStoryId(taskId: string) {
    this.userStoryId = this.kanbanService.userStories.filter((item2) => item2.itemIds.includes(taskId))[0]?.id;
    return this.userStoryId;
  }

  // task-item for delete
  deleteTask(taskId: KanbanItem) {
    this.kanbanService.deleteItems(taskId);
  }

  // for task-details slider
  editTaskOpen(task: KanbanItem) {
    this.kanbanService.editDetails(task);
  }

  // for story-details slider
  editStoryOpen(taskId: string) {
    this.kanbanService.editStoryDetails(taskId);
  }

  // new task add details
  taskSubmit() {
    this.taskAdd = false;
    const newTask: KanbanItem = {
      assign: '',
      attachments: [],
      commentIds: [],
      description: '',
      dueDate: new Date(),
      id: `${Math.floor(1000 + Math.random() * 9999)}`,
      image: false,
      priority: 'low',
      title: this.inputDetails
    };

    this.kanbanService.addNewTask(newTask, this.columnData.itemIds);
  }
}
