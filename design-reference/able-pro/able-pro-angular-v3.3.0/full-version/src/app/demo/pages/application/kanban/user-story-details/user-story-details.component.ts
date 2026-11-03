// angular import
import { Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDrawer } from '@angular/material/sidenav';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { KanbanService } from 'src/app/demo/pages/application/kanban/kanban.service';
import { KanbanColumn, KanbanComment, KanbanProfile, KanbanUserStory } from 'src/app/@theme/types/kanban-type';
import { KanbanLayoutService } from '../kanban-layout.service';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

// type
interface SelectedFile {
  file: File;
  data: string | ArrayBuffer | null;
}

@Component({
  selector: 'app-user-story-details',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './user-story-details.component.html',
  styleUrls: ['./user-story-details.component.scss']
})
export class UserStoryDetailsComponent implements OnInit {
  private layoutService = inject(KanbanLayoutService);
  private kanbanService = inject(KanbanService);

  // public props
  readonly story = viewChild<MatDrawer>('story');
  selectedOption = '1';
  storyDetails: KanbanUserStory;
  selectedProfile: KanbanProfile;
  storyComment: KanbanComment;
  getColumnData: KanbanColumn;
  userComment: KanbanComment[];
  assignProfile: KanbanProfile[];
  columnsDetails: KanbanColumn[];
  commentList: {
    comment: KanbanComment;
    profile: KanbanProfile;
  }[];
  selectedStoryFiles: SelectedFile[] = [];
  storyDate: Date;

  // life cycle event
  ngOnInit() {
    this.layoutService.storySlider.subscribe(() => {
      this.story()!.toggle();
    });
    this.kanbanService.editStory.subscribe((userStoryId) => {
      // columns data
      this.columnsDetails = this.kanbanService.columnData;
      // comment data
      this.userComment = this.kanbanService.comment;
      // profile data
      this.assignProfile = this.kanbanService.profile;
      // select story data matching
      this.storyDetails = this.kanbanService.userStories.find((x) => x.id === userStoryId.toString())!;
      // story date formatting
      this.storyDate = new Date(this.storyDetails.dueDate);
      // stroy assign user profile
      this.selectedProfile = this.assignProfile.find((item) => item.id == this.storyDetails.assign)!;
      // select columns title
      this.getColumnData = this.columnsDetails.filter((item) => item.id === this.storyDetails.columnId)[0];
      // select User Comment
      if (this.storyDetails?.commentIds) {
        const commentList = [...this.storyDetails.commentIds].reverse().map((commentId) => {
          const commentData = this.userComment.filter((comment) => comment.id === commentId)[0];
          const profile = this.assignProfile.filter((item) => item.id === commentData.profileId)[0];
          return { comment: commentData, profile: profile };
        });
        this.commentList = commentList; // Assign to a component property for template access
      }
    });
  }

  // public method
  closeEditDetails() {
    this.layoutService.toggleStory();
  }

  storyForms() {
    const storyDetails: KanbanUserStory = {
      acceptance: this.storyDetails.acceptance,
      assign: this.storyDetails.assign,
      columnId: this.storyDetails.columnId,
      commentIds: this.storyDetails.commentIds,
      description: this.storyDetails.description,
      dueDate: this.storyDate,
      id: this.storyDetails.id,
      itemIds: this.storyDetails.itemIds,
      title: this.storyDetails.title,
      priority: this.storyDetails.priority
    };
    this.kanbanService.editUserStory(storyDetails, this.getColumnData);
  }
}
