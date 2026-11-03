// angular import
import { Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDrawer } from '@angular/material/sidenav';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { KanbanColumn, KanbanComment, KanbanItem, KanbanProfile, KanbanUserStory } from 'src/app/@theme/types/kanban-type';
import { KanbanService } from 'src/app/demo/pages/application/kanban/kanban.service';
import { KanbanLayoutService } from '../kanban-layout.service';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

interface SelectedFile {
  file: File;
  data: string | ArrayBuffer | null;
}

@Component({
  selector: 'app-task-details',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './task-details.component.html',
  styleUrls: ['./task-details.component.scss']
})
export class TaskDetailsComponent implements OnInit {
  private layoutService = inject(KanbanLayoutService);
  private kanbanService = inject(KanbanService);

  // public props
  readonly task = viewChild<MatDrawer>('task');
  taskDetails: KanbanItem;
  selectedProfile: KanbanProfile;
  columnsDetails: KanbanColumn[];
  assignProfile: KanbanProfile[];
  userStory: KanbanUserStory[];
  userComment: KanbanComment[];
  selectUserStroy: KanbanUserStory;
  getColumnData: KanbanColumn;
  commentList: {
    comment: KanbanComment;
    profile: KanbanProfile;
  }[];
  taskDate: Date;
  newComment: string;
  selectedFiles: SelectedFile[] = [];
  commentCounter = 6;

  // life cycle event
  ngOnInit() {
    this.layoutService.taskSlider.subscribe(() => {
      this.task()!.toggle();
    });

    this.kanbanService.editTask.subscribe((task) => {
      this.taskDetails = task;
      // columns data
      this.columnsDetails = this.kanbanService.columnData;
      // comment Data
      this.userComment = this.kanbanService.comment;
      // profile data
      this.assignProfile = this.kanbanService.profile;
      // userStory data
      this.userStory = this.kanbanService.userStories;
      // date formatting
      this.taskDate = new Date(this.taskDetails.dueDate);
      // select profile
      this.selectedProfile = this.assignProfile.find((item) => item.id === this.taskDetails.assign)!;
      // select UserStory
      this.selectUserStroy = this.userStory.filter((item) => item.itemIds.includes(task.id))[0] || {};
      // select columns title
      this.getColumnData = this.columnsDetails.filter((item) => item.itemIds.includes(task.id))[0];
      // select User Comment
      if (this.taskDetails?.commentIds) {
        const commentList = [...this.taskDetails.commentIds].reverse().map((commentId) => {
          const commentData = this.userComment.filter((comment) => comment.id === commentId)[0];
          const profile = this.assignProfile.filter((item) => item.id === commentData.profileId)[0];
          return { comment: commentData, profile: profile };
        });
        this.commentList = commentList; // Assign to a component property for template access
      }
    });
  }

  // public method
  closeEditSlider() {
    this.layoutService.toggleTask();
  }

  // editTask delete
  deleteTask(taskId: KanbanItem) {
    this.kanbanService.deleteItems(taskId);
    this.layoutService.toggleTask();
  }

  // uploaded  file
  // eslint-disable-next-line
  onFileSelected(event: any): void {
    const files: FileList = event.target.files;
    if (files && files.length > 0) {
      for (const file of event.target.files) {
        const reader = new FileReader();
        // eslint-disable-next-line
        reader.onload = (e: any) => {
          this.selectedFiles.push({
            file: file,
            data: e.target.result
          });
        };
        reader.readAsDataURL(file as Blob);
      }
    }
  }

  // uploaded  file is image
  isImage(file: SelectedFile): boolean {
    return file.file.type.startsWith('image/');
  }

  // uploaded file remove
  removeFile(index: number): void {
    this.selectedFiles.splice(index, 1);
  }

  // change item details
  changeItemDetails() {
    const attachments = this.selectedFiles.map((selectedFile) => {
      return {
        fileName: selectedFile.file.name,
        fileType: selectedFile.file.type,
        data: selectedFile.data
      };
    });

    const itemToEdit: KanbanItem = {
      assign: this.taskDetails.assign,
      attachments: attachments.length > 0 ? attachments : [],
      commentIds: this.taskDetails.commentIds,
      description: this.taskDetails.description,
      dueDate: this.taskDate,
      id: this.taskDetails.id,
      image: false,
      priority: this.taskDetails.priority,
      title: this.taskDetails.title
    };
    this.kanbanService.editItem(itemToEdit, this.selectUserStroy, this.getColumnData);
  }

  newCommitSubmit() {
    const newCommentDetails: KanbanComment = {
      id: `comment-${this.commentCounter++}`,
      comment: this.newComment,
      profileId: 'profile-3'
    };

    this.kanbanService.addNewComment(newCommentDetails, this.taskDetails);
  }
}
