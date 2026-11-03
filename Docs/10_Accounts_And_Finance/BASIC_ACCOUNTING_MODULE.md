# Implement Basic Accounting Module for IraniMart POS V2

Please review the existing IraniMart POS V2 codebase, database structure, business flows, permissions, and current modules before making any changes.

The goal is to implement a practical **basic double-entry accounting module** suitable for a retail POS and inventory business.

The accounting module must integrate with the existing:

* POS Sales
* Sales Returns and Refunds
* Purchase and GRN
* Inventory
* Stock Count and Stock Adjustment
* Cash Opening and Cash Closing
* Supplier Payments
* Outlet and Warehouse structure
* User, Role, and Permission system

Do not create a disconnected accounting system. Financial entries must be generated automatically from operational transactions.

---

# 1. Initial Review

Before implementation:

1. Review the existing database entities, migrations, services, repositories, APIs, Angular components, permissions, and audit implementation.
2. Identify how the following transactions currently work:

   * Sale completion
   * Sale refund and return
   * Purchase receipt or GRN
   * Supplier payment
   * Stock adjustment
   * Stock count approval
   * Expense entry, if any
   * Cash opening
   * Cash closing
   * Cash deposit and withdrawal
3. Identify existing fields such as:

   * BusinessId
   * OutletId
   * WarehouseId
   * TerminalId
   * CashSessionId
   * CreatedBy
   * ApprovedBy
   * TransactionDate
   * FinancialYear
4. Reuse existing architecture and coding conventions.
5. Do not duplicate existing functionality.
6. Document any gap, ambiguity, or risky existing implementation before making structural changes.

---

# 2. Accounting Principles

The module must use **double-entry accounting**.

For every posted journal:

```text
Total Debit = Total Credit
```

No financial transaction should directly manipulate account balances.

Account balances must be calculated from posted journal lines.

Operational modules must create accounting entries through a centralized accounting posting service.

Example:

```csharp
IAccountingPostingService
```

Possible responsibilities:

```csharp
PostSaleAsync(...)
PostSalesReturnAsync(...)
PostPurchaseReceiptAsync(...)
PostSupplierPaymentAsync(...)
PostExpenseAsync(...)
PostOtherIncomeAsync(...)
PostStockAdjustmentAsync(...)
PostCashTransferAsync(...)
ReverseTransactionAsync(...)
```

Avoid scattering journal creation logic across controllers and unrelated services.

---

# 3. Chart of Accounts

Implement a hierarchical Chart of Accounts.

Required account types:

```text
Asset
Liability
Equity
Income
CostOfGoodsSold
Expense
```

Suggested default accounts:

```text
Assets
- Cash
- Petty Cash
- Bank
- Inventory
- Supplier Advance
- Other Current Assets

Liabilities
- Supplier Payable
- Customer Advance
- Loan
- Other Current Liabilities

Equity
- Owner Capital
- Owner Drawings
- Retained Earnings

Income
- Sales
- Sales Discount
- Sales Return
- Other Income

Cost of Goods Sold
- Cost of Goods Sold
- Inventory Shortage
- Inventory Write-Off

Expenses
- Rent Expense
- Salary Expense
- Electricity Expense
- Internet Expense
- Transport Expense
- Packaging Expense
- Marketing Expense
- Cleaning Expense
- Bank Charge
- Miscellaneous Expense
```

Required account fields should include:

```text
Id
BusinessId
ParentAccountId
AccountCode
AccountName
AccountType
NormalBalanceType
IsSystemAccount
IsControlAccount
AllowManualPosting
IsActive
DisplayOrder
CreatedAt
CreatedBy
UpdatedAt
UpdatedBy
```

System accounts should not be deletable.

Account codes must be unique within a business.

Prevent deletion of any account that has journal transactions.

Allow disabling an unused account where appropriate.

---

# 4. Accounting Configuration

Create an accounting configuration or account mapping section.

The business must be able to map operational transactions to ledger accounts.

Required mappings:

```text
Cash Account
Petty Cash Account
Default Bank Account
Inventory Account
Sales Account
Sales Discount Account
Sales Return Account
COGS Account
Inventory Shortage Account
Inventory Gain Account
Supplier Payable Account
Customer Advance Account
Other Income Account
Owner Capital Account
Owner Drawings Account
Rounding Difference Account
Cash Difference Gain Account
Cash Difference Loss Account
```

Support outlet-specific mappings where needed, particularly:

```text
Outlet Cash Account
Outlet Petty Cash Account
Outlet Bank Account
```

If outlet-specific mapping is missing, use the business-level default mapping.

Do not allow financial posting when a mandatory account mapping is missing.

Return a clear validation message showing which account mapping is required.

---

# 5. Journal Entry Structure

Create proper journal header and journal line entities.

Suggested structure:

## JournalEntry

```text
Id
BusinessId
JournalNumber
JournalType
TransactionDate
PostingDate
FinancialYearId
ReferenceType
ReferenceId
ReferenceNumber
Description
OutletId
WarehouseId
TerminalId
CashSessionId
Status
IsSystemGenerated
ReversalOfJournalEntryId
PostedAt
PostedBy
CreatedAt
CreatedBy
UpdatedAt
UpdatedBy
```

Suggested statuses:

```text
Draft
Posted
Reversed
Cancelled
```

## JournalEntryLine

```text
Id
JournalEntryId
AccountId
Description
DebitAmount
CreditAmount
OutletId
WarehouseId
SupplierId
CustomerId
PaymentMethodId
CostCenterId
CreatedAt
CreatedBy
```

Validation requirements:

1. Debit and credit cannot both contain an amount on the same line.
2. Debit and credit cannot both be zero.
3. Total debit must equal total credit.
4. Posted journals cannot be edited.
5. Posted journals cannot be physically deleted.
6. Corrections must be done through reversal and reposting.
7. Duplicate posting must be prevented.

Create an idempotency mechanism using fields such as:

```text
ReferenceType
ReferenceId
JournalType
```

The same operational transaction must not create duplicate journals if the API is retried.

---

# 6. Automatic Accounting Entries

Implement the following initial posting rules.

## 6.1 Cash Sale

```text
Cash / Bank / Payment Method Clearing    Dr
    Sales Income                              Cr
```

For inventory items:

```text
Cost of Goods Sold                       Dr
    Inventory                                 Cr
```

The payment account must depend on the payment method.

Examples:

```text
Cash payment       -> Outlet Cash
Card payment       -> Card Clearing or Bank
Bank transfer      -> Bank
Customer advance   -> Customer Advance
```

Support split payment.

The total debit from all payment methods must match the final sale amount.

---

## 6.2 Sales Discount

Prefer posting net sales where appropriate, but the design should support a separate Sales Discount account.

Recommended entry:

```text
Cash / Bank / Clearing                    Dr
Sales Discount                            Dr
    Sales Income                              Cr
```

Use the existing sale calculation rules as the source of truth.

Do not recalculate totals differently inside accounting.

---

## 6.3 Sales Return or Refund

Reverse the original financial effect.

```text
Sales Return                             Dr
    Cash / Bank / Payment Clearing           Cr
```

For stock returned in sellable condition:

```text
Inventory                                Dr
    Cost of Goods Sold                       Cr
```

If the returned item is damaged or not restocked, do not increase sellable inventory. Use the appropriate inventory loss or write-off flow.

Partial refund must create a proportional journal based on the actual returned items and refund amount.

---

## 6.4 Purchase Receipt or GRN

If purchased on credit:

```text
Inventory                                Dr
    Supplier Payable                         Cr
```

If paid immediately:

```text
Inventory                                Dr
    Cash / Bank                              Cr
```

Accounting should normally post when stock is received through GRN, not when a Purchase Order is merely created.

Purchase Order creation must not create a posted journal.

---

## 6.5 Supplier Payment

```text
Supplier Payable                         Dr
    Cash / Bank                              Cr
```

Support:

```text
Full payment
Partial payment
Advance payment
Multiple invoice allocation
Unallocated supplier payment
```

Do not allow payment allocation beyond the supplier’s outstanding amount unless it is explicitly treated as supplier advance.

---

## 6.6 Expense

```text
Expense Account                          Dr
    Cash / Bank / Payable                    Cr
```

Expense fields should include:

```text
ExpenseNumber
BusinessId
OutletId
ExpenseDate
ExpenseCategoryId
ExpenseAccountId
Amount
PaymentMethodId
PaymentAccountId
SupplierId
ReferenceNumber
Description
Attachment
Status
ApprovedBy
ApprovedAt
CreatedBy
CreatedAt
```

Suggested statuses:

```text
Draft
Submitted
Approved
Rejected
Cancelled
```

Accounting should post only after approval, unless the current business workflow explicitly allows immediate posting.

---

## 6.7 Other Income

```text
Cash / Bank                             Dr
    Other Income                            Cr
```

Fields should include:

```text
IncomeDate
OutletId
IncomeCategory
IncomeAccountId
Amount
PaymentMethod
PaymentAccountId
Reference
Description
Attachment
CreatedBy
```

---

## 6.8 Stock Shortage

When approved stock count finds a shortage:

```text
Inventory Shortage Expense              Dr
    Inventory                                Cr
```

---

## 6.9 Stock Gain

When approved stock count finds excess stock:

```text
Inventory                               Dr
    Inventory Gain / Adjustment Income      Cr
```

Use the stock valuation cost applicable to the inventory module.

Do not use selling price for inventory accounting.

---

## 6.10 Cash to Bank Transfer

```text
Bank                                    Dr
    Cash                                    Cr
```

---

## 6.11 Bank to Cash Transfer

```text
Cash                                    Dr
    Bank                                    Cr
```

---

## 6.12 Capital Injection

```text
Cash / Bank                             Dr
    Owner Capital                           Cr
```

---

## 6.13 Owner Withdrawal

```text
Owner Drawings                          Dr
    Cash / Bank                              Cr
```

Owner withdrawal must not be recorded as a business expense.

---

## 6.14 Cash Closing Difference

If actual cash is greater than expected:

```text
Cash                                    Dr
    Cash Difference Gain                   Cr
```

If actual cash is less than expected:

```text
Cash Difference Loss                    Dr
    Cash                                    Cr
```

The system should follow the existing cash closing approval process.

Do not automatically hide or silently ignore cash differences.

---

# 7. Inventory Costing and COGS

Review the existing inventory valuation method.

The accounting implementation must use the same inventory cost source as the inventory module.

Preferred initial costing method:

```text
Weighted Average Cost
```

If the existing system already uses another method, do not change it without documenting the impact.

COGS must be calculated using product cost, not selling price.

For product variants, valuation and COGS must be calculated at ProductVariant level.

Required reports must support:

```text
Main product
Product variant
Category
Outlet
Warehouse
Date range
```

The same stock movement must not update inventory accounting more than once.

---

# 8. Cash Opening and Closing Integration

The existing cash session should remain the operational source of truth.

Cash opening amount should establish the cashier’s physical cash responsibility but must be carefully reviewed before creating accounting revenue or expense entries.

Do not treat opening float as sales or income.

Cash closing should calculate:

```text
Opening Cash
+ Cash Sales
+ Other Cash Income
+ Cash Received
- Cash Refund
- Cash Expense
- Cash Transfer Out
= Expected Cash
```

Then compare:

```text
Expected Cash
Actual Cash
Difference
```

Required accounting and cash reports must be filterable by:

```text
Outlet
Terminal
Cashier
Cash Session
Opening Date
Closing Date
Business Date
```

Respect the existing rule that a cash session may continue after midnight while transactions still belong to the opening business date.

Respect the existing maximum 15-hour cash-session safeguard.

---

# 9. Financial Year and Accounting Period

Implement:

```text
FinancialYear
AccountingPeriod
```

Suggested FinancialYear fields:

```text
Id
BusinessId
Name
StartDate
EndDate
IsCurrent
IsClosed
ClosedAt
ClosedBy
```

Suggested AccountingPeriod fields:

```text
Id
FinancialYearId
PeriodNumber
Name
StartDate
EndDate
Status
ClosedAt
ClosedBy
```

Statuses:

```text
Open
Closed
Locked
```

Prevent posting into a closed or locked accounting period.

Backdated posting must follow permission and accounting-period rules.

---

# 10. Manual Journal

Implement manual journal entry only for authorized accounting users.

Required permissions should include:

```text
Accounting.Journal.View
Accounting.Journal.Create
Accounting.Journal.Post
Accounting.Journal.Reverse
Accounting.ManualJournal.Create
Accounting.ManualJournal.Post
```

Manual journal requirements:

1. Draft before posting.
2. Debit and credit must balance.
3. User must provide description and reference.
4. Only accounts with `AllowManualPosting = true` can be used.
5. Posted journals are immutable.
6. Reversal requires reason.
7. Full audit history is mandatory.

Cashier and salesperson roles must not have access to manual journals.

---

# 11. Required Reports

Implement the following reports.

## 11.1 Profit and Loss

Structure:

```text
Sales
Less: Sales Return
Less: Sales Discount
Net Sales

Less: Cost of Goods Sold
Gross Profit

Other Income

Operating Expenses
Net Profit or Loss
```

Filters:

```text
Date range
Business date
Outlet
Account
Expense category
Comparison period
```

---

## 11.2 Trial Balance

Columns:

```text
Account Code
Account Name
Opening Debit
Opening Credit
Period Debit
Period Credit
Closing Debit
Closing Credit
```

Total debit must equal total credit.

---

## 11.3 General Ledger

Columns:

```text
Date
Journal Number
Reference
Description
Debit
Credit
Running Balance
Outlet
Created By
```

Filters:

```text
Account
Date range
Outlet
Journal type
Reference type
```

---

## 11.4 Balance Sheet

Structure:

```text
Assets
Liabilities
Equity
```

It must be generated as of a selected date.

---

## 11.5 Cash Book

Columns:

```text
Date
Reference
Description
Cash In
Cash Out
Balance
Outlet
Terminal
Cash Session
```

---

## 11.6 Expense Report

Required views:

```text
Expense Summary
Expense Details
Expense by Category
Expense by Outlet
Expense by Payment Method
```

---

## 11.7 Other Income Report

Required filters:

```text
Date range
Outlet
Income category
Payment method
```

---

## 11.8 COGS Report

Required columns:

```text
Date
Sale Number
Product
Main Product Code
Variant
Quantity
Unit Cost
Total Cost
Outlet
```

---

## 11.9 Inventory Valuation

Required columns:

```text
Product
Main Product Code
Variant
Quantity on Hand
Average Unit Cost
Inventory Value
Outlet or Warehouse
```

---

## 11.10 Cash Closing Report

Required fields:

```text
Cash Session
Opening Time
Closing Time
Business Date
Cashier
Outlet
Terminal
Opening Cash
Cash Sales
Cash Refund
Cash Expense
Expected Cash
Actual Cash
Difference
Status
Approved By
```

---

# 12. Permissions

Integrate with the existing permission system.

Suggested permissions:

```text
Accounting.Dashboard.View

Accounting.ChartOfAccounts.View
Accounting.ChartOfAccounts.Create
Accounting.ChartOfAccounts.Update

Accounting.Configuration.View
Accounting.Configuration.Update

Accounting.Journal.View
Accounting.Journal.Create
Accounting.Journal.Post
Accounting.Journal.Reverse

Accounting.Expense.View
Accounting.Expense.Create
Accounting.Expense.Update
Accounting.Expense.Submit
Accounting.Expense.Approve
Accounting.Expense.Reject
Accounting.Expense.Cancel

Accounting.Income.View
Accounting.Income.Create
Accounting.Income.Update
Accounting.Income.Cancel

Accounting.Reports.ProfitLoss
Accounting.Reports.TrialBalance
Accounting.Reports.GeneralLedger
Accounting.Reports.BalanceSheet
Accounting.Reports.CashBook
Accounting.Reports.Expense
Accounting.Reports.Income
Accounting.Reports.COGS
Accounting.Reports.InventoryValuation
Accounting.Reports.CashClosing

Accounting.FinancialYear.View
Accounting.FinancialYear.Manage
Accounting.Period.Close
Accounting.Period.Reopen
```

Suggested access:

```text
Super Admin
- Full access

Business Owner
- Full business-level financial access

Accountant
- Accounting configuration, journals, expenses, income, reports, period management

Main Warehouse Manager
- Relevant inventory and operational reports
- No manual financial posting unless separately permitted

Outlet Manager
- Own outlet expenses, cash reports, and permitted financial reports

Cashier / Salesperson
- Own operational cash session
- No Chart of Accounts
- No manual journal
- No company-wide Profit and Loss
```

All outlet-scoped roles must only see data for their permitted outlet unless explicitly granted wider access.

---

# 13. Audit and Data Integrity

Every financial action must be auditable.

Audit:

```text
Created
Updated
Submitted
Approved
Rejected
Posted
Reversed
Cancelled
Accounting period closed
Accounting period reopened
Configuration changed
```

Record:

```text
User
Timestamp
Action
Old value
New value
Reason
Reference
IP or device information if existing audit supports it
```

Do not hard-delete posted financial data.

Use database transactions when:

```text
Operational transaction
Inventory movement
Journal entry
Payment record
Cash movement
```

must be saved together.

If accounting posting fails, the operational transaction must not be left in an inconsistent partially completed state.

---

# 14. UI Requirements

Add an Accounts menu with:

```text
Accounting Dashboard
Chart of Accounts
Expenses
Other Income
Journal Entries
Manual Journal
Cash and Bank Transfers
Financial Years
Accounting Configuration
Reports
```

Follow the existing application UI/UX, table actions, filter layout, pagination, export, date-time formatting, permission checks, responsive behavior, and list-state preservation standards.

All list pages must retain:

```text
Search text
Filters
Sort order
Page number
Page size
Selected tab
```

when navigating to details or edit pages and returning.

Use the application’s existing date-time standard.

Show date and time where a transaction timestamp is relevant.

Use clear status badges and consistent action menus.

---

# 15. Dashboard

Create a basic accounting dashboard with:

```text
Today Sales
Today Expenses
Today Other Income
Today Gross Profit
Current Month Sales
Current Month Expenses
Current Month Net Profit
Cash Balance
Bank Balance
Inventory Value
Supplier Outstanding
Cash Closing Difference
```

All dashboard figures must respect:

```text
Business permission
Outlet permission
Selected date
Selected outlet
```

Do not calculate financial figures from separate inconsistent queries when the journal ledger is the appropriate source.

---

# 16. API and Backend Requirements

Use clean service boundaries.

Suggested components:

```text
AccountingPostingService
JournalService
ChartOfAccountService
ExpenseService
OtherIncomeService
AccountingReportService
FinancialYearService
AccountingConfigurationService
InventoryValuationService
```

Requirements:

1. Controllers should remain thin.
2. Business rules should remain in services.
3. Use DTOs rather than directly exposing entities.
4. Use server-side filtering, sorting, and pagination.
5. Use asynchronous database calls.
6. Avoid N+1 queries.
7. Add indexes for common report and journal filters.
8. Use decimal types consistently for financial amounts.
9. Define a consistent monetary precision.
10. Protect all endpoints with permissions.
11. Validate BusinessId and outlet access on the server.
12. Do not rely only on frontend permission checks.

Suggested indexes:

```text
JournalEntry:
BusinessId, PostingDate
BusinessId, ReferenceType, ReferenceId
BusinessId, JournalNumber
BusinessId, OutletId, PostingDate
Status, PostingDate

JournalEntryLine:
JournalEntryId
AccountId
AccountId, OutletId
SupplierId
CustomerId
```

---

# 17. Migration and Existing Data

Do not automatically create accounting journals for historical transactions without a migration plan.

Provide a recommendation for one of these options:

```text
Option A:
Start accounting from a configured opening date.

Option B:
Generate opening balances only.

Option C:
Backfill historical journals through a controlled migration.
```

The preferred initial approach is:

```text
Configure an accounting start date
Create approved opening balances
Post all new transactions after that date automatically
```

Opening balances must be entered through a balanced opening journal.

---

# 18. Testing Requirements

Add unit, integration, and permission tests.

Minimum test cases:

```text
Cash sale journal balances
Card sale journal balances
Split-payment sale journal balances
Discounted sale journal balances
Full return journal
Partial return journal
Purchase receipt on credit
Immediate cash purchase
Supplier payment
Supplier advance
Expense approval and posting
Other income posting
Stock shortage
Stock gain
Cash transfer
Capital injection
Owner withdrawal
Cash closing surplus
Cash closing shortage
Duplicate API retry does not duplicate journal
Posted journal cannot be edited
Posted journal can be reversed
Closed period blocks posting
Unauthorized outlet cannot view another outlet
Trial balance debit equals credit
P&L totals agree with ledger
Inventory valuation agrees with inventory stock
COGS agrees with sale item cost
```

Run:

```text
Backend build
Frontend build
Database migration
Unit tests
Integration tests
Permission tests
Relevant regression tests
```

Fix all relevant errors before marking the task complete.

---

# 19. Delivery Approach

Do not implement the entire module blindly in one large change.

First provide:

1. Existing-system review.
2. Gap analysis.
3. Proposed entity and database changes.
4. Posting-event matrix.
5. Permission matrix.
6. API list.
7. UI page list.
8. Reporting calculation source.
9. Migration plan.
10. Implementation phases.

Recommended implementation phases:

## Phase 1

```text
Chart of Accounts
Accounting Configuration
Journal Engine
Financial Year
Opening Balances
Expense
Other Income
Manual Journal
General Ledger
Trial Balance
Basic Profit and Loss
```

## Phase 2

```text
POS Sale integration
Sales Return integration
Purchase and GRN integration
Supplier Payment integration
Cash Closing integration
Stock Adjustment integration
COGS report
Inventory Valuation report
Cash Book
```

## Phase 3

```text
Balance Sheet
Accounting Dashboard
Accounting Period Closing
Advanced outlet reports
Bank reconciliation
Accounts Payable enhancements
Accounts Receivable, if required
```

Before starting each phase, verify that the previous phase is stable and tested.

---

# 20. Important Restrictions

1. Do not directly change account balances.
2. Do not physically delete posted journals.
3. Do not allow unbalanced journals.
4. Do not duplicate journals on API retry.
5. Do not calculate COGS using selling price.
6. Do not post Purchase Orders before stock receipt.
7. Do not treat owner withdrawal as expense.
8. Do not treat cash opening float as income.
9. Do not bypass outlet-level data security.
10. Do not create a separate accounting calculation inconsistent with existing sale or inventory totals.
11. Do not silently correct accounting differences.
12. Do not make large architecture changes without first documenting the reason and impact.

Start by reviewing the current implementation and producing the gap analysis and phased technical plan. After that, implement Phase 1 only unless the existing project documentation explicitly authorizes the later phases.
