// ==============================|| TYPES - KANBAN  ||============================== //

export interface KanbanResponse {
  columns: KanbanColumn[];
  columnsOrder: string[];
  comments: KanbanComment[];
  items: KanbanItem[];
  profiles: KanbanProfile[];
  selectedItem: string | false;
  userStory: KanbanUserStory[];
  userStoryOrder: string[];
  error: object | string | null;
}

export type KanbanColumn = {
  id: string;
  title: string;
  itemIds: string[];
};

export type KanbanComment = {
  id: string;
  comment: string;
  profileId: string;
};

export type KanbanItem = {
  assign: string;
  // eslint-disable-next-line
  attachments: any;
  commentIds?: string[];
  description: string;
  dueDate: Date;
  id: string;
  image: string | false;
  priority: 'low' | 'medium' | 'high';
  title: string;
};

export type KanbanProfile = {
  id: string;
  name: string;
  avatar: string;
  time: string;
};

export type KanbanUserStory = {
  acceptance: string;
  assign?: string;
  columnId: string;
  commentIds?: string[];
  description: string;
  dueDate: Date;
  id: string;
  itemIds: string[];
  title: string;
  priority: string;
};

export interface editItem {
  columnId: string;
  columns: KanbanColumn[];
  item: KanbanItem;
  items: KanbanItem[];
  storyId: string;
  userStory: KanbanUserStory[];
}
