export interface chatPersonType {
  id: number;
  name: string;
  company: string;
  role: string;
  work_email: string;
  personal_email: string;
  work_phone: string;
  personal_phone: string;
  location: string;
  avatar: string;
  status: string;
  lastMessage: string;
  birthdayText: string;
  unReadChatCount: number;
  online_status: string;
}

export interface chatHistory {
  id: number;
  from: string;
  to: string;
  text: string;
  time: string;
}
