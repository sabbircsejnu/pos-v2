import { Role } from './role';

export class User {
  serviceToken!: string;
  user!: {
    firstName?: string;
    lastName?: string;
    id: string;
    email: string;
    password: string;
    name: string;
    role: Role;
  };
}
