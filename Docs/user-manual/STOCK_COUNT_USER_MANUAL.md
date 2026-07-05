# Stock Count User Manual

## 1. Overview and Purpose

The Stock Count module helps your business perform physical inventory counting by location (Outlet or Warehouse), compare physical counts against system snapshot stock, and keep an auditable stock count history.

This manual is for business users:

- Store/Outlet Managers
- Warehouse Managers
- Business Owners
- Other authorized users

This manual reflects currently implemented behavior.

---

## 2. Who Should Use This Module

Use Stock Count when you need to:

- Take a point-in-time inventory snapshot for a location
- Count stock physically using Excel or printed sheets
- Reconcile differences and move through approval workflow (if enabled)
- Generate an adjustment draft from approved differences

---

## 3. Key Concepts

- Stock Count is always tied to one location.
- Snapshot values (Product, Product Code, Variant, Current Stock) are fixed at creation time.
- One location can have many stock counts over time, but only one active stock count at a time.
- Active means status is Draft or Submitted.

---

## 4. Status Lifecycle

Implemented status flow:

1. Draft
2. Submitted
3. Rejected (optional branch)
4. Approved
5. AdjustmentGenerated
6. Completed

Allowed transitions:

- Draft -> Submitted
- Submitted -> Rejected
- Rejected -> Draft (Reopen)
- Submitted -> Approved
- Approved -> AdjustmentGenerated
- AdjustmentGenerated -> Completed (after generated stock adjustment is approved)

---

## 5. Role-Based Access and Responsibilities

Access is controlled by both:

1. Role/user permissions
2. Authorized location scope (own/default location vs all authorized locations)

### 5.1 Outlet Manager (typical)

What they can view:

- Stock counts for their authorized outlet location(s)

What they can create:

- Stock count for authorized outlet location(s)

What actions they can perform (if permission assigned):

- Download Excel
- Print
- Upload Excel
- Submit
- Reject
- Reopen

What they cannot do by default:

- View all locations unless StockCount.ViewAll is assigned
- Approve / Generate Stock Adjustment Draft unless StockCount.Approve is assigned and Phase 3 is enabled

Typical permissions:

- StockCount.ViewOwn
- StockCount.Create
- StockCount.Download
- StockCount.Print
- StockCount.Upload
- StockCount.Submit
- StockCount.Reject
- StockCount.Reopen

### 5.2 Warehouse Manager (typical)

What they can view:

- Stock counts for authorized warehouse location(s)

What they can create:

- Stock count for authorized warehouse location(s)

What actions they can perform (if permission assigned):

- Download Excel
- Print
- Upload Excel
- Submit
- Reject
- Reopen

What they cannot do by default:

- View all locations unless StockCount.ViewAll is assigned
- Approve / Generate Stock Adjustment Draft unless StockCount.Approve is assigned and Phase 3 is enabled

Typical permissions:

- StockCount.ViewOwn
- StockCount.Create
- StockCount.Download
- StockCount.Print
- StockCount.Upload
- StockCount.Submit
- StockCount.Reject
- StockCount.Reopen

### 5.3 Business Owner / Business Admin (typical)

What they can view:

- Own and all authorized locations (with ViewAll)

What they can create:

- Stock counts for any authorized location

What actions they can perform (if permission assigned):

- Download Excel
- Print
- Upload Excel
- Submit
- Approve
- Reject
- Reopen
- Generate Stock Adjustment Draft

What they cannot do:

- Access data outside authorized tenant/business scope

Typical permissions:

- StockCount.ViewOwn
- StockCount.ViewAll
- StockCount.Create
- StockCount.Download
- StockCount.Print
- StockCount.Upload
- StockCount.Submit
- StockCount.Approve
- StockCount.Reject
- StockCount.Reopen

### 5.4 Other Authorized Users

Access depends on exact permissions assigned. If a button is missing or API returns "permission denied", request the needed permission from your admin.

---

## 6. Role vs Action Matrix

Notes:

- "Required Permission" is mandatory.
- "Scope Rule" means location/tenant authorization still applies.
- Approve and Generate Draft also require Phase 3 feature to be enabled.

| Action | Required Permission | Outlet Manager (Typical) | Warehouse Manager (Typical) | Business Owner/Admin (Typical) | Scope Rule |
|---|---|---|---|---|---|
| View Own Stock Counts | StockCount.ViewOwn | Yes | Yes | Yes | Authorized own/default location scope |
| View All Stock Counts | StockCount.ViewAll | Usually No | Usually No | Yes | All authorized locations within business |
| Create Stock Count | StockCount.Create | Yes | Yes | Yes | Authorized locations only |
| Download Excel | StockCount.Download | Yes | Yes | Yes | Must have access to that stock count |
| Print | StockCount.Print | Yes | Yes | Yes | Must have access to that stock count |
| Upload Excel | StockCount.Upload | Yes | Yes | Yes | Draft status only + access to stock count |
| Submit | StockCount.Submit | Yes | Yes | Yes | Draft status only |
| Approve | StockCount.Approve | Usually No | Usually No | Yes | Submitted status + Phase 3 enabled |
| Reject | StockCount.Reject | Yes (if assigned) | Yes (if assigned) | Yes | Submitted status only |
| Reopen | StockCount.Reopen | Yes (if assigned) | Yes (if assigned) | Yes | Rejected status only |
| Generate Stock Adjustment Draft | StockCount.Approve | Usually No | Usually No | Yes | Approved status + Phase 3 enabled |

---

## 7. Workflow (End-to-End)

1. Create stock count snapshot (Draft)
2. Download/Print sheet
3. Perform physical count
4. Upload completed Excel
5. Review Physical Count and Difference
6. Submit
7. Approve or Reject
8. If approved, generate stock adjustment draft
9. Approve generated stock adjustment to complete lifecycle

---

## 8. Stock Count List Page

Purpose:

- Find, filter, and manage stock counts

Available columns:

- Stock Count No
- Date
- Location
- Total Items
- Status
- Created By
- Created At
- Actions

Filters:

- Search
- Status
- Date From / To
- Location Type / Location (if allowed)

List supports pagination.

Screenshot placeholder:

- [Screenshot Placeholder: Stock Count List with filters and action buttons]

---

## 9. Creating a Stock Count

Steps:

1. Go to Stock Counts -> Create Stock Count
2. Select Location Type and Location (if editable for your role)
3. Select Stock Count Date
4. Add optional remarks
5. Click Generate Snapshot

System behavior:

- Includes active products/variants
- Includes variants with zero stock
- Saves immutable snapshot fields
- Blocks creation if another active stock count exists for same location

Screenshot placeholder:

- [Screenshot Placeholder: Create Stock Count form]

---

## 10. Downloading and Printing the Excel Sheet

From list or details page:

1. Click Download Excel to get template/snapshot file
2. Click Print for print-friendly stock count sheet

Excel content includes:

- Header details
- SL, Product Name, Product Code, Variant, Current Stock, Physical Count, Difference, Remarks

Screenshot placeholders:

- [Screenshot Placeholder: Download button]
- [Screenshot Placeholder: Print preview]

---

## 11. Uploading Completed Excel

Pre-condition:

- Stock count must be in Draft
- You need StockCount.Upload

Steps:

1. Open stock count details
2. Click Upload Excel
3. Choose .xlsx file
4. System validates and updates Physical Count, Difference, and Remarks

Important validation rules:

- Only .xlsx supported
- File cannot be empty
- Serial number (SL) must match template line mapping
- Physical count must be numeric and non-negative
- Upload must contain meaningful changes

Screenshot placeholder:

- [Screenshot Placeholder: Upload Excel action and success message]

---

## 12. Reviewing Physical Count and Differences

In details page table, review:

- Current Stock (snapshot)
- Physical Count (uploaded)
- Difference (Physical - Current)
- Remarks

Also review movement warning:

- If transactions happened after generation, warning banner appears with movement count and last movement timestamp.

Screenshot placeholder:

- [Screenshot Placeholder: Details table with differences and movement warning]

---

## 13. Submitting a Stock Count

Pre-condition:

- Status must be Draft
- StockCount.Submit permission
- At least one physical count or remark uploaded

Steps:

1. Open details or list
2. Click Submit
3. Confirm action

Result:

- Status becomes Submitted

---

## 14. Approval Workflow

Pre-condition:

- Status is Submitted
- StockCount.Approve permission
- Phase 3 feature enabled
- All lines must have physical counts

Steps:

1. Open details or list
2. Click Approve
3. Confirm action

Result:

- Status becomes Approved

---

## 15. Rejection and Reopening

### Reject

Pre-condition:

- Status is Submitted
- StockCount.Reject permission
- Rejection reason is required

Steps:

1. Click Reject
2. Enter reason
3. Confirm

Result:

- Status becomes Rejected

### Reopen

Pre-condition:

- Status is Rejected
- StockCount.Reopen permission

Steps:

1. Click Reopen
2. Confirm

Result:

- Status becomes Draft again

---

## 16. Generate Stock Adjustment Draft

Pre-condition:

- Status is Approved
- StockCount.Approve permission
- Phase 3 feature enabled
- At least one non-zero difference exists

Steps:

1. Click Generate Draft
2. Confirm

Result:

- Stock count status becomes AdjustmentGenerated
- A linked Stock Adjustment draft is created (reason: StockCountCorrection)

Duplicate prevention:

- Only one linked draft can exist per stock count
- Repeated or concurrent generate requests are blocked

Completion behavior:

- When the linked stock adjustment is approved in Stock Adjustment module, stock count status changes to Completed

---

## 17. Business Rules (Implemented)

1. One active stock count per location (Draft/Submitted)
2. Snapshot fields are immutable in workflow
3. Location/tenant authorization enforced server-side for all actions
4. Upload is Draft-only
5. Submit is Draft-only
6. Reject/Approve are Submitted-only
7. Reopen is Rejected-only
8. Generate Draft is Approved-only
9. Post-generation movement warning on view/list payload

---

## 18. Common Validation Messages

Examples you may see:

- You do not have permission to perform this action
- Stock Count Phase 3 actions are currently disabled
- An active stock count already exists for this location
- Excel upload is only allowed while stock count is in Draft status
- Only .xlsx files are supported for stock count upload
- Invalid serial number at row X
- Invalid physical count value at row X
- Physical count cannot be negative at row X
- Uploaded Excel does not contain any changes to physical count or remarks
- Upload completed Excel before submitting stock count
- All lines must have physical counts before approval
- Stock adjustment draft can only be generated from Approved stock counts
- Adjustment draft has already been generated for this stock count
- No stock differences found to generate adjustment draft

---

## 19. Best Practices

1. Download the latest template before counting
2. Do not edit SL, product, variant, or current stock columns
3. Enter physical counts for all lines before approval step
4. Use clear rejection reasons for auditability
5. Review movement warning before approving
6. Avoid parallel operations on same stock count by multiple users
7. Keep role permissions minimal (least privilege)

---

## 20. Future Enhancements

The following items are not documented as currently available for end users:

- Mobile stock count
- Barcode-assisted counting workflow in stock count UI
- RFID support
- Inventory freeze controls in stock count flow
- Dedicated in-page approve-screen movement analysis beyond current warnings

These are treated as future enhancement areas.

---

## 21. FAQ

### Q1. Why can’t I create a new stock count for a location?
A: There is already an active stock count (Draft or Submitted) for that location.

### Q2. Why is Approve button not visible?
A: You may be missing StockCount.Approve permission, status may not be Submitted, or Phase 3 is disabled for your environment/business.

### Q3. Why is Generate Draft not visible?
A: Status must be Approved, you need StockCount.Approve, and Phase 3 must be enabled.

### Q4. Why upload failed though file was selected?
A: Common reasons are wrong file type, invalid SL mapping, invalid numeric values, negative physical count, or no meaningful changes in file.

### Q5. Can I reject without a reason?
A: No. Rejection reason is mandatory.

### Q6. What happens after Generate Draft?
A: Status becomes AdjustmentGenerated, and one linked Stock Adjustment draft is created.

### Q7. When does status become Completed?
A: After the linked stock adjustment draft is submitted and approved in the Stock Adjustment module.

### Q8. Can I access other outlets/warehouses?
A: Only if your location scope and permissions allow it (for example, ViewAll and authorized locations).

---

## 22. Quick Permission Reference

- StockCount.ViewOwn: view own/authorized scope
- StockCount.ViewAll: view all authorized locations
- StockCount.Create: create snapshot
- StockCount.Download: download excel
- StockCount.Print: print sheet
- StockCount.Upload: upload completed excel
- StockCount.Submit: submit draft
- StockCount.Approve: approve + generate adjustment draft
- StockCount.Reject: reject submitted
- StockCount.Reopen: reopen rejected
