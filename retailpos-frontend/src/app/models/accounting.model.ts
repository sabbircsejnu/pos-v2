export interface AccountDto {
  id: number;
  name: string;
  type: string; // asset, liability, expense, revenue
  balance: number;
}

export interface CreateAccountDto {
  name: string;
  type: string;
}

export interface UpdateAccountDto {
  name: string;
  type: string;
}

export interface TransactionDto {
  id: number;
  accountId: number;
  accountName: string;
  amount: number;
  type: string; // debit, credit
  description?: string;
  transactionDate: string;
  referenceType?: string;
  referenceId?: number;
}

export interface CreateTransactionDto {
  accountId: number;
  amount: number;
  type: string;
  description?: string;
  transactionDate: string;
  referenceType?: string;
  referenceId?: number;
}

export interface TransactionSearchRequest {
  accountId?: number;
  startDate?: string;
  endDate?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface ExpenseDto {
  id: number;
  category: string;
  amount: number;
  description?: string;
  expenseDate: string;
  outletId?: number;
  outletName?: string;
}

export interface CreateExpenseDto {
  category: string;
  amount: number;
  description?: string;
  expenseDate: string;
  outletId?: number;
}

export interface UpdateExpenseDto {
  category: string;
  amount: number;
  description?: string;
  expenseDate: string;
  outletId?: number;
}

export interface ExpenseSearchRequest {
  category?: string;
  startDate?: string;
  endDate?: string;
  outletId?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface ExpenseSummaryDto {
  totalAmount: number;
  byCategory: { [key: string]: number };
  count: number;
}

export interface BillDto {
  id: number;
  supplierId: number;
  supplierName: string;
  poId?: number;
  poNumber?: string;
  amountDue: number;
  dueDate: string;
  status: string; // unpaid, partial, paid
  isOverdue: boolean;
}

export interface CreateBillDto {
  supplierId: number;
  poId?: number;
  amountDue: number;
  dueDate: string;
}

export interface UpdateBillStatusDto {
  status: string;
}

export interface BillSearchRequest {
  status?: string;
  supplierId?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface BillSummaryDto {
  totalUnpaid: number;
  totalOverdue: number;
  unpaidCount: number;
  overdueCount: number;
}
