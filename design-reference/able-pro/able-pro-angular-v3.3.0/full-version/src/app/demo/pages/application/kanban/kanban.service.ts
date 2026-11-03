// angular import
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';

// project import
import {
  KanbanColumn,
  KanbanComment,
  KanbanItem,
  KanbanProfile,
  KanbanResponse,
  KanbanUserStory
} from '../../../../@theme/types/kanban-type';
import { environment } from 'src/environments/environment';

@Injectable({
  providedIn: 'root'
})
export class KanbanService {
  private http = inject(HttpClient);

  taskDetails = new Subject<KanbanItem>();
  editTask = this.taskDetails.asObservable();

  editDetails(task: KanbanItem) {
    this.taskDetails.next(task);
  }

  storyDetails = new Subject<KanbanUserStory>();
  editStory = this.storyDetails.asObservable();

  // eslint-disable-next-line
  editStoryDetails(userStoryId: any) {
    this.storyDetails.next(userStoryId);
  }

  // start
  private columnsSubject = new Subject<KanbanColumn[]>();
  columns: Observable<KanbanColumn[]> = this.columnsSubject.asObservable();
  columnData: KanbanColumn[] = [];

  private profileSubject = new Subject<KanbanProfile[]>();
  profiles: Observable<KanbanProfile[]> = this.profileSubject.asObservable();
  profile: KanbanProfile[] = [];

  private commentSubject = new Subject<KanbanComment[]>();
  comments: Observable<KanbanComment[]> = this.commentSubject.asObservable();
  comment: KanbanComment[] = [];

  private itemSubject = new Subject<KanbanItem[]>();
  items: Observable<KanbanItem[]> = this.itemSubject.asObservable();
  taskItems: KanbanItem[] = [];

  private userStorySubject = new Subject<KanbanUserStory[]>();
  userStory: Observable<KanbanUserStory[]> = this.userStorySubject.asObservable();
  userStories: KanbanUserStory[] = [];

  getColumns(): void {
    this.http.get<KanbanResponse>(`${environment.apiUrl}/api/kanban/columns`).subscribe((data) => {
      this.columnsSubject.next(data.columns);
      this.columnData = data.columns;
    });
  }

  getProfiles(): void {
    this.http.get<KanbanResponse>(`${environment.apiUrl}/api/kanban/profiles`).subscribe((data) => {
      this.profileSubject.next(data.profiles);
      this.profile = data.profiles;
    });
  }

  getComments(): void {
    this.http.get<KanbanResponse>(`${environment.apiUrl}/api/kanban/comments`).subscribe((data) => {
      this.commentSubject.next(data.comments);
      this.comment = data.comments;
    });
  }

  getItems(): void {
    this.http.get<KanbanResponse>(`${environment.apiUrl}/api/kanban/items`).subscribe((data) => {
      this.itemSubject.next(data.items);
      this.taskItems = data.items;
    });
  }

  getUserStory(): void {
    this.http.get<KanbanResponse>(`${environment.apiUrl}/api/kanban/userstory`).subscribe((data) => {
      this.userStorySubject.next(data.userStory);
      this.userStories = data.userStory;
    });
  }

  // delete columns
  deleteColumn(columnId: string) {
    this.http.get(`${environment.apiUrl}/api/kanban/delete-column`);
    return ((this.columnData = this.columnData.filter((x) => x.id !== columnId)), this.columnsSubject.next(this.columnData));
  }

  // delete items
  deleteItems(taskId: KanbanItem) {
    this.http.get(`${environment.apiUrl}/api/kanban/delete-item`);
    return ((this.taskItems = this.taskItems.filter((x) => x.id !== taskId.id)), this.itemSubject.next(this.taskItems));
  }

  // add new columns
  addNewColumn(column: KanbanColumn) {
    this.columnData.push(column);
    this.columnsSubject.next(this.columnData);
  }

  // add new items
  addNewTask(task: KanbanItem, itemIds: string[]) {
    const matchingColumn = this.columnData.find((columnData) => {
      return columnData.itemIds.some((itemId) => itemIds.includes(itemId));
    });

    if (matchingColumn) {
      // Add the new task to the matching column
      matchingColumn.itemIds.push(task.id); // Assuming `task.id` is the identifier for the new task

      // Push the new task into the taskItems array
      this.taskItems.push(task);
      this.columnsSubject.next(this.columnData);
      // Update the subject with the updated task items
      this.itemSubject.next(this.taskItems);
    }
  }

  // add new comment
  addNewComment(comment: KanbanComment, item: KanbanItem) {
    this.comment.push(comment);
    this.commentSubject.next(this.comment);

    // Find the matching item in 'taskItems' array by ID.
    const matchingItem = this.taskItems.find((taskItem) => taskItem.id === item.id);
    if (matchingItem) {
      // Update the 'commentIds' property in the matching item.
      if (!matchingItem.commentIds) {
        matchingItem.commentIds = [];
      }
      matchingItem.commentIds.push(comment.id);
    }
    this.itemSubject.next(this.taskItems);
  }

  // change items value
  editItem(itemToEdit: KanbanItem, selectUserStroy: KanbanUserStory, getColumnData: KanbanColumn) {
    // userStory Data Update base User change
    {
      const userStoryIndex = this.userStories.findIndex((x) => x.id === selectUserStroy.id);
      if (userStoryIndex !== -1) {
        // Remove item from user story if it exists (based on id)
        this.userStories[userStoryIndex].itemIds = this.userStories[userStoryIndex].itemIds.filter((itemId) => itemId !== itemToEdit.id);

        // Push itemToEdit id to selected user story's itemsIds
        this.userStories[userStoryIndex].itemIds.push(itemToEdit.id);
      }
      // userStory object id match with selectUserStroy object id in not delete items id
      for (const userStory of this.userStories) {
        if (userStory.id !== selectUserStroy.id) {
          userStory.itemIds = userStory.itemIds.filter((itemId) => itemId !== itemToEdit.id);
        }
      }
    }

    // Columns Data Update base User change
    {
      const ColumnIndex = this.columnData.findIndex((x) => x.id === getColumnData.id);
      if (ColumnIndex !== -1) {
        // Remove item from ColumnsData if it exists (based on id)
        this.columnData[ColumnIndex].itemIds = this.columnData[ColumnIndex].itemIds.filter((x) => x !== itemToEdit.id);

        // Push itemToEdit id to selected ColumnsData itemsIds
        this.columnData[ColumnIndex].itemIds.push(itemToEdit.id);
      }
      // columns object id match with getColumnData object id in not delete items id
      for (const columns of this.columnData) {
        if (columns.id !== getColumnData.id) {
          columns.itemIds = columns.itemIds.filter((x) => x !== itemToEdit.id);
        }
      }
    }
    this.userStorySubject.next(this.userStories);
    this.columnsSubject.next(this.columnData);

    // change items data match id in changes
    const taskToUpdate = this.taskItems.find((item) => item.id === itemToEdit.id);
    if (taskToUpdate) {
      taskToUpdate!.dueDate = itemToEdit.dueDate;
      taskToUpdate!.attachments = itemToEdit.attachments;
      this.itemSubject.next(this.taskItems);
    }
  }

  // change story value
  editUserStory(storyDetails: KanbanUserStory, getColumnData: KanbanColumn) {
    const columnToUpdate = this.columnData.find((column) => column.id === getColumnData.id);

    if (columnToUpdate) {
      const storyToUpdate = this.userStories.find((story) => story.id === storyDetails.id);

      if (storyToUpdate) {
        // Update the user story's columnIDs
        const currentColumnIds = storyToUpdate.columnId.split(',');
        const newColumnId = getColumnData.id.toString();
        storyToUpdate!.dueDate = storyDetails.dueDate;

        if (!currentColumnIds.includes(newColumnId)) {
          const updatedColumnIds = currentColumnIds.map((id) => (id === storyDetails.columnId ? newColumnId : id));
          storyToUpdate.columnId = updatedColumnIds.join(',');

          // Emit the updated user stories
          this.userStorySubject.next(this.userStories);
        }
      }
    }
  }

  // get column title value
  getColumnFromTitle(title: string): KanbanColumn {
    return this.columnData.find((x) => x.title === title)!;
  }
}
