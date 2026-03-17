import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AccountDto, CreateAccountDto, UpdateAccountDto,
  TransactionDto, CreateTransactionDto, TransactionSearchRequest,
  ExpenseDto, CreateExpenseDto, UpdateExpenseDto, ExpenseSearchRequest, ExpenseSummaryDto,
  BillDto, CreateBillDto, UpdateBillStatusDto, BillSearchRequest, BillSummaryDto
} from '../models/accounting.model';

@Injectable({
  providedIn: 'root'
})
export class AccountingService {
  private accountsUrl = `${environment.apiUrl}/accounts`;
  private transactionsUrl = `${environment.apiUrl}/transactions`;
  private expensesUrl = `${environment.apiUrl}/expenses`;
  private billsUrl = `${environment.apiUrl}/bills`;

  // Accounts signals
  accounts = signal<AccountDto[]>([]);
  // Transactions signals
  transactions = signal<TransactionDto[]>([]);
  transactionsTotalCount = signal<number>(0);
  // Expenses signals
  expenses = signal<ExpenseDto[]>([]);
  expensesTotalCount = signal<number>(0);
  expenseSummary = signal<ExpenseSummaryDto | null>(null);
  // Bills signals
  bills = signal<BillDto[]>([]);
  billsTotalCount = signal<number>(0);
  billSummary = signal<BillSummaryDto | null>(null);

  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  // ─── Accounts ────────────────────────────────────────────────────────────

  /** Get all accounts, optionally filtered by type */
  getAccounts(type?: string): Observable<any> {
    let params = new HttpParams();
    if (type) params = params.set('type', type);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.accountsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.accounts.set(res.data?.accounts || []);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load accounts');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get single account by ID */
  getAccountById(id: number): Observable<any> {
    return this.http.get<any>(`${this.accountsUrl}/${id}`);
  }

  /** Create a new account */
  createAccount(dto: CreateAccountDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.accountsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update an existing account */
  updateAccount(id: number, dto: UpdateAccountDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.accountsUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Delete an account */
  deleteAccount(id: number): Observable<any> {
    return this.http.delete<any>(`${this.accountsUrl}/${id}`);
  }

  // ─── Transactions ─────────────────────────────────────────────────────────

  /** Get transactions with optional query filters */
  getTransactions(filters?: { accountId?: number; startDate?: string; endDate?: string }): Observable<any> {
    let params = new HttpParams();
    if (filters?.accountId) params = params.set('accountId', filters.accountId.toString());
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.transactionsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.transactions.set(res.data?.transactions || []);
          this.transactionsTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load transactions');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search transactions with pagination */
  searchTransactions(dto: TransactionSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.transactionsUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.transactions.set(res.data?.transactions || res.data || []);
          this.transactionsTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search transactions');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single transaction by ID */
  getTransactionById(id: number): Observable<any> {
    return this.http.get<any>(`${this.transactionsUrl}/${id}`);
  }

  /** Create a new transaction */
  createTransaction(dto: CreateTransactionDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.transactionsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Get ledger for an account */
  getAccountLedger(accountId: number): Observable<any> {
    const params = new HttpParams().set('accountId', accountId.toString());
    return this.http.get<any>(`${this.transactionsUrl}/ledger`, { params });
  }

  // ─── Expenses ─────────────────────────────────────────────────────────────

  /** Get all expenses with optional filters */
  getExpenses(filters?: { category?: string; startDate?: string; endDate?: string; outletId?: number }): Observable<any> {
    let params = new HttpParams();
    if (filters?.category) params = params.set('category', filters.category);
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);
    if (filters?.outletId) params = params.set('outletId', filters.outletId.toString());

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.expensesUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.expenses.set(res.data?.expenses || []);
          this.expensesTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load expenses');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search expenses with pagination */
  searchExpenses(dto: ExpenseSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.expensesUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.expenses.set(res.data?.expenses || res.data || []);
          this.expensesTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search expenses');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single expense by ID */
  getExpenseById(id: number): Observable<any> {
    return this.http.get<any>(`${this.expensesUrl}/${id}`);
  }

  /** Create a new expense */
  createExpense(dto: CreateExpenseDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.expensesUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update an existing expense */
  updateExpense(id: number, dto: UpdateExpenseDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.put<any>(`${this.expensesUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Delete an expense */
  deleteExpense(id: number): Observable<any> {
    return this.http.delete<any>(`${this.expensesUrl}/${id}`);
  }

  /** Get expense summary */
  getExpenseSummary(): Observable<any> {
    return this.http.get<any>(`${this.expensesUrl}/summary`).pipe(
      tap({
        next: (res) => this.expenseSummary.set(res.data || null),
        error: () => this.expenseSummary.set(null)
      })
    );
  }

  // ─── Bills ────────────────────────────────────────────────────────────────

  /** Get all bills, optionally filtered by status */
  getBills(status?: string): Observable<any> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.billsUrl, { params }).pipe(
      tap({
        next: (res) => {
          this.bills.set(res.data?.bills || []);
          this.billsTotalCount.set(res.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load bills');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search bills with pagination */
  searchBills(dto: BillSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.billsUrl}/search`, dto).pipe(
      tap({
        next: (res) => {
          this.bills.set(res.data?.bills || res.data || []);
          this.billsTotalCount.set(res.data?.totalCount || res.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search bills');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get a single bill by ID */
  getBillById(id: number): Observable<any> {
    return this.http.get<any>(`${this.billsUrl}/${id}`);
  }

  /** Create a new bill */
  createBill(dto: CreateBillDto): Observable<any> {
    this.isLoading.set(true);
    return this.http.post<any>(this.billsUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => { this.error.set(err.message); this.isLoading.set(false); }
      })
    );
  }

  /** Update bill status */
  updateBillStatus(id: number, dto: UpdateBillStatusDto): Observable<any> {
    return this.http.put<any>(`${this.billsUrl}/${id}/status`, dto);
  }

  /** Get bill summary totals */
  getBillSummary(): Observable<any> {
    return this.http.get<any>(`${this.billsUrl}/summary`).pipe(
      tap({
        next: (res) => this.billSummary.set(res.data || null),
        error: () => this.billSummary.set(null)
      })
    );
  }
}
