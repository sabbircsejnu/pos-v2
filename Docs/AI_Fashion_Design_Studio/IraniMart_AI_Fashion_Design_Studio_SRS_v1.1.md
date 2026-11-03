<!-- IraniMart RetailPOS | AI Fashion Design Studio | SRS v1.1 -->

# AI Fashion Design Studio

*Software Requirements Specification (SRS)  |  IraniMart RetailPOS / Future SaaS*

| Document control | Value |
| --- | --- |
| Version / status | 1.1 • Consolidated architecture and navigation baseline |
| Date | 13 October 2026 |
| Scope | AI-assisted apparel concept generation, review, approval and product handoff |
| System of record | Existing RetailPOS is authoritative for operational transactions; independent AI Studio DB for design/jobs; private file objects in configured Local or S3 storage |
| Design principle | AI workloads must not degrade POS checkout or any core ERP functions |

Decision statement: Optional, business-scoped AI Design Studio in the existing RetailPOS monorepo; independent AI metadata database and dedicated worker, durable queue and configurable private Local/S3 storage. No AI generation on POS transaction path.

## 1. Purpose and business context

IraniMart designs and sells modest women’s apparel including abayas, jilbabs and embroidered outfits. Current design ideation is difficult to scale. The proposed module transforms inspiration photos and written briefs into original visual concepts, tracks iterations and approvals, captures production-facing specifications, and hands approved designs to the existing Product Master workflow. AI-generated content is exploratory until reviewed by human designers and manufacturing staff.

Existing RetailPOS capabilities and constraints considered: .NET Clean Architecture with Application/CQRS-MediatR, Angular client, PostgreSQL persistence, business- and location-aware authorization, sensitive-data access, audit v2, durable-delivery prerequisites, Product Master with main product design code and barcode-unique colour×size variants, inventory/POS/accounting modules, and prospective multi-tenant SaaS positioning. Exact repository names, infrastructure and interfaces must be validated during discovery.

## 2. Goals, non-goals and success indicators

### 2.1 Goals

- Generate diverse, original, brand-fit garment concepts from text and optional reference images.
- Allow a business to choose among authorised image generation providers and compare outputs.
- Create a governed history of briefs, inputs, outputs, edits, ratings, decisions and approvals.
- Prepare reviewed design packages and initiate, but not silently publish, corresponding Product Master drafts.
- Isolate expensive AI jobs from checkout, stock movement, financial posting and other operational workflows.
- Support future SaaS tenants with strict business data isolation, spend controls and provider configuration.

### 2.2 Out of scope for initial release

- Autonomous sewing patterns, guaranteed accurate measurements, physically valid fabric simulation or automatic manufacturing orders.
- Training a proprietary foundation model or fine-tuning on customer files.
- Automatic inventory creation, stock posting, accounting journals or live SKU publication by AI.
- Unrestricted internet scraping/copying of competitors’ garment designs.
- Automatic prediction of customer demand based only on visual imagery.

### 2.3 Proposed success criteria (pilot targets, not existing performance claims)

| Metric | Proposed pilot target |
| --- | --- |
| Concept throughput | Produce and archive ≥10 concepts in a supervised session |
| Human acceptance | ≥90% of completed jobs produce viewable assets, excluding policy/provider failures |
| Traceability | 100% of approved designs trace to an original brief, input revisions and approving user |
| Isolation | Under saturated AI workload, core POS performance stays within agreed baseline guardrails |
| Cost visibility | 100% of billable generation attempts attributed to business, provider and design job |

## 3. Actors and access control

| Actor / permission group | Abilities |
| --- | --- |
| Business Admin / AI Admin | Enable capability, configure allowed providers, budgets, access, retention; view business costs |
| Fashion Designer | Create drafts, upload reference assets, generate, edit, compare, submit designs |
| Design Reviewer / Manager | Rate, comment, approve/reject and request sample |
| Production / Merchandising | Review technical pack, costing, mark sample outcomes; propose product handoff |
| Product Manager | Accept draft product proposal and manage actual master product/variants |
| Finance / Owner | Read AI cost reports, subject to permission and business isolation |
| Sales Cashier | No AI Studio access by default; normal POS permissions remain unchanged |

Authorisation must enforce Business ID on every request and query; location scope applies where concepts are associated with outlets. Permissions are checked server-side for asset download, provider use, export, approvals and product handoff. Permissions must not be inferred from Angular navigation visibility.

## 4. Functional requirements

### 4.1 Module and administration

| ID | Requirement / acceptance intent |
| --- | --- |
| FR-001 | AI Studio is an optional business-scoped capability, default OFF; disabled tenants cannot call any feature API. |
| FR-002 | Permission-scoped menu/submenu, routes and API endpoints; denial uses 403 or non-disclosing 404 per established policy. |
| FR-003 | Admin configures allowed providers/models, per-role permissions, per-business monthly/daily budgets, per-job limit, concurrency and retention. |
| FR-004 | Provider credentials are stored in backend secret management; never returned to the browser, logged, or included in exports. |
| FR-005 | All business-facing artefacts and logs preserve Business ID, creator, timestamps and state. |

### 4.2 Design brief and reference library

| ID | Requirement |
| --- | --- |
| FR-010 | Create/version a design brief: garment type, collection, target customer, occasion, fabric class, silhouette, sleeves, neckline, embroidery placement, palette, price/cost target, free-form brief. |
| FR-011 | Upload 1..N reference photos, sketches and optional brand inspiration with type/size validation, malware scan and metadata; configurable upper bounds. |
| FR-012 | Store provenance, usage rights declaration, permissions and asset hashes; private storage and expiring signed read URLs. |
| FR-013 | Reuse existing approved design assets and brand guidelines within same Business only. |
| FR-014 | Drafts support autosave, human-readable version history, duplication and archive/restore. |

### 4.3 AI generation and editing

| ID | Requirement |
| --- | --- |
| FR-020 | Select eligible provider/model and mode: text-to-image, reference-to-new-concept, controlled variation, image edit where supported. |
| FR-021 | Backend validates capability, quota, rights flags, prompt/image limits, model availability and provider policy before acceptance. |
| FR-022 | POST creates a durable asynchronous job and returns job ID/status promptly; no provider call within interactive web request. |
| FR-023 | Support queued, processing, succeeded, partially succeeded, failed, cancel-requested, cancelled, expired states; status polling and optional UI updates. |
| FR-024 | Record exact model ID/version, effective prompt, configuration, seed where supported, input references, timestamps, provider request ID, usage/cost estimate and outcome. |
| FR-025 | Allow variations and iterative edits to create immutable output revisions linked to the parent result; do not overwrite earlier images. |
| FR-026 | Support side-by-side provider comparison for the same versioned brief and indicate differences in model features and cost. |
| FR-027 | On failure show meaningful retry guidance; retry creates traceable attempt under job policy; cancellation is best-effort if provider cannot cancel. |
| FR-028 | Flag generated outputs as AI concepts; preview/watermark policy configurable; do not claim exact embroidery or fabric accuracy. |

### 4.4 Evaluation, workflow and production handoff

| ID | Requirement |
| --- | --- |
| FR-030 | Gallery filter/search by collection, type, creator, model, status, colour, date and approval decision. |
| FR-031 | Users rate, annotate and comment; retain reviewer identity and revision association. |
| FR-032 | Controlled workflow: Draft → Submitted → Shortlisted → Sample Requested → Sample Reviewed → Approved/Rejected → Production Ready; changes audited. |
| FR-033 | Generate draft production pack (front/back visual concepts if supported, material/colour/embroidery notes, spec placeholders), with explicit human verification status. |
| FR-034 | Capture manual sample measurements, BOM, fabric consumption, embroidery time, labour/other cost and sample feedback; calculations state assumptions. |
| FR-035 | After production approval, authorised Product Manager may create a Product Master DRAFT link; must not mutate stock, sales prices, inventory or accounting automatically. |
| FR-036 | One approved design maps to one main product design code; colour and size are child attributes and each valid combination receives unique SKU/barcode through existing authoritative product workflow. |
| FR-037 | Maintain backlinks: AI design ↔ Product Master code ↔ variants ↔ selected actual sales analytics, read-only by default. |

### 4.5 Reporting and governance

| ID | Requirement |
| --- | --- |
| FR-040 | Usage report by business, provider, model, user, collection, period, success/fail and estimated vs invoiced cost when available. |
| FR-041 | Budgets reserve estimated cost at job acceptance, settle actual usage on completion where supported; prevent overspend from concurrent requests. |
| FR-042 | Trace every generation, asset deletion, model change, role permission change, approval and product handoff through audit v2. |
| FR-043 | Configurable retention and deletion, tenant export, records for provider data-sharing policy, and orphan-asset cleanup. |
| FR-044 | Provider availability/capability registry avoids offering unsupported editing or resolution modes. |

## 5. Non-functional requirements — POS isolation (critical)

The following are release gates, not optional engineering refinements. Baselines must be measured in the actual staging environment before setting final numeric limits.

| ID | Requirement / measurable verification |
| --- | --- |
| NFR-001 | AI API submission must enqueue persistently and return without awaiting AI vendor generation; proposed p95 ≤750 ms excluding reference upload. |
| NFR-002 | Generation workers run as independently deployable processes/containers with separate CPU, memory, process pools and autoscaling/resource ceilings from POS API. |
| NFR-003 | Use a dedicated AI work queue (or logical namespace with enforceable resource limits), separate consumers, retry policy and DLQ from financial and inventory messages. |
| NFR-004 | AI output files live in object storage, not transactional DB blobs; direct-to-private-object-storage uploads preferred using scoped, short-lived signed URLs. |
| NFR-005 | AI metadata connections and job status traffic use separately constrained DB pools and query budgets; no long transactions or worker scans on hot POS tables. |
| NFR-006 | AI worker vendor HTTP calls have timeouts, concurrency caps, rate limits, bounded retries with jitter, circuit breakers and per-provider throttles. |
| NFR-007 | Under AI queue saturation, provider outage and simultaneous large uploads, measure no material regression of POS checkout p95 or error rate versus load baseline; proposed guardrail ≤5% checkout p95 regression and no AI-caused 5xx. |
| NFR-008 | AI job backlog does not delay sale completion, stock reservation/update, payment/reconciliation, Accounting journals, Payroll or core RabbitMQ/outbox processing. |
| NFR-009 | Async submission uses a reliable persisted outbox/job record; enqueue failure cannot silently lose work or create unlimited duplicates. |
| NFR-010 | AI failures must fail closed only for AI features; disable AI capability without restart/deployment of checkout service where operationally supported. |
| NFR-011 | All endpoints enforce tenant isolation, object-level authorisation, encryption in transit/at rest, redacted logs and provider retention disclosures. |
| NFR-012 | UI remains responsive while jobs execute; status refresh uses bounded polling/backoff or server push, not high-frequency hammering. |
| NFR-013 | Provider cost, queue depth, completion latency, worker saturation and POS checkout latency have separate dashboards and alert thresholds. |
| NFR-014 | Schema changes are backward compatible; bulk AI asset cleanup and reporting use background, throttled execution. |

## 6. Workload separation and job lifecycle

Preferred modular-monolith control plane with an isolated AI execution plane:

- Control plane: Angular → secured RetailPOS AI endpoints → validate permission/budget → write design job + outbox in short DB transaction → return 202 Accepted and job ID.
- Execution plane: separate AI Dispatcher/Worker with dedicated queue, compute and connection budget → obtain short-lived references → call provider API → store outputs in private object storage → update job state/cost → emit internal notification.
- Core POS path: Sales/Inventory/Payments/Accounting/HR/Payroll → existing application services and their own priority resource limits; never call the AI generation worker or wait for design jobs.
- Isolation baseline: AI Studio owns a separate logical metadata database with separate connection pools and dedicated AI worker process; on shared database host/disk, enforce measurable resource limits and permit later physical separation.
| Failure case | Required behaviour |
| --- | --- |
| Provider takes 2 minutes | Browser may navigate away; job persists and resumes displaying progress later; checkout unaffected. |
| Provider 429 / outage | Rate-limit/backoff and circuit-breaker in AI lane only; no POS request retries. |
| AI worker crash | Lease/recovery handling and idempotency restore or fail job; no duplicate product creation. |
| AI queue has 10,000 jobs | Bound job acceptance by budget/queue limit; prioritise core enterprise workload; clear wait-time indication. |
| User presses Cancel | Stop queued work; for in-flight work attempt cancellation, otherwise mark requested and settle actual costs correctly. |
| Blob upload interrupted | Multipart/resumable upload if supported; no large image bytes through checkout API memory. |

## 7. Logical data entities and integration contracts

| Entity | Selected fields / controls |
| --- | --- |
| DesignProject / Collection | BusinessId, name, category, status, owner, timestamps |
| DesignBrief / BriefRevision | ProjectId, requirements JSON, revision, creator, rights declaration |
| ReferenceAsset | BusinessId, object key, hash, MIME, bytes, provenance, privacy status |
| GenerationJob / Attempt | BusinessId, user, provider/model, status, idempotency key, attempts, lease, timings, failure category |
| GeneratedAsset / Revision | JobId, parent revision, private object key, prompt hash, metadata, selected flag |
| Review / Approval | DesignId, revision, reviewer, rating, decision, reason, time |
| DesignProductionPack | Measurements, fabrics, embroidery, cost estimate, reviewed-by, sample status |
| ProductDesignLink | DesignId, authoritative ProductId/main code, creation status, variant mapping |
| AIUsageLedger | BusinessId, provider/model, reservation, estimated/actual cost, billing currency, event time |
| Audit event | Actor, action, target, before/after controlled metadata, correlation ID |

Expected integrations: existing Identity/Business/Location permissions, Product Master and variants, protected private file storage (ADR-03 dependency), durable delivery/outbox (ADR-04 dependency), audit v2, notifications, and selected sales analytics as read-only aggregated evidence. Reuse established contracts; do not introduce parallel business/permission truth.

## 8. UX and API requirements

- Main navigation: independent AI Fashion Design Studio main menu, visible only for authorised businesses and users; provider/storage administrative settings live under existing System Administration.
- Pages: Dashboard, Design Studio, My Designs, Collections, Compare Designs, Approvals, Sample Management, Production Packs, Generation Jobs and Usage & Costs; settings under System Administration (phased visibility).
- Provide before/after image slider, zoom, clear job cost estimate, accessible status labels, errors and estimated waiting category (not fabricated ETA).
- REST examples (non-binding): POST /api/ai-design/jobs → 202; GET /api/ai-design/jobs/{id}; POST /api/ai-design/jobs/{id}/cancel; GET /api/ai-design/assets/{id}/signed-url; POST /api/ai-design/designs/{id}/submit; POST /api/ai-design/designs/{id}/approve; POST /api/ai-design/designs/{id}/product-draft.
- All POST mutation endpoints support idempotency where retry is realistic; use pagination and scoped queries. Browser must not receive provider API secrets.

## 9. Model/provider abstraction

Providers should be configured as capability profiles rather than hard-coded vendor labels. Each profile declares text-to-image, reference input, editing/inpainting, max reference count, allowed output sizes, pricing method, region/data-retention policy, supported image formats and cancellation capability. Offer OpenAI and Google image-provider adapters in the pilot; add FLUX/Ideogram after operational validation. Claude may optionally assist with prompt/brief analysis but must not be misrepresented as a native photographic image generator. Confirm live API eligibility, model version and vendor data terms during implementation.

A future provider adapter cannot bypass business authorization, budget reservation, audit recording, private-storage policy or worker scheduling. Provider selection may be disallowed by business policy or jurisdiction.

## 10. Security, privacy, IP and policy

- Explicit uploader confirmation that the business owns or is authorised to use reference photos; reject forbidden/abusive content and comply with vendor safety policies.
- Keep reference images private by default and explain any transfer to external AI service; provider usage and retention policies must be documented.
- Do not use tenant assets to train a proprietary shared model without separate explicit consent; no cross-tenant retrieval or prompt context.
- Avoid automatically copying competitor designs; require originality review and maintain references/provenance.
- Encrypt secrets, URLs expire, limit download permissions, scan input files and validate file signatures (not just extensions).
- Implement tenant-scoped deletion and retention with lawful audit retention carve-outs; do not put sensitive images in diagnostic logs.

## 11. Acceptance scenarios and test plan

| ID | Given / when / then |
| --- | --- |
| AT-01 | Given AI Studio OFF, when a designer hits an API directly, then access is denied and no job is created. |
| AT-02 | Given Business A asset ID, when Business B requests it, then content and metadata are not exposed (including signed URL). |
| AT-03 | Given 10 simultaneous generation requests, then API promptly returns durable job IDs; all recover from UI refresh. |
| AT-04 | Given AI provider hangs or returns 429, then job respects timeout/backoff/DLQ; POS sales complete normally. |
| AT-05 | Given worker crash after provider result, then reconciliation ensures at most one adopted output revision and accurate charging. |
| AT-06 | Given budget exhaustion, then further billable jobs are rejected before provider call, except admin-authorised override. |
| AT-07 | Given generation continues for minutes, then user can navigate to checkout and back; job state and eventual result survive. |
| AT-08 | Given a generated concept, when approved, then reviewer, design revision and decision are immutable/auditable. |
| AT-09 | Given approved design with 3 colours × 4 sizes, then Product Master proposal links one main design code and 12 unique valid variant barcodes subject to existing rules. |
| AT-10 | Given load test saturation of AI workers/queue/uploads, then checkout p95 regression stays within approved guardrail and no AI-attributable sale errors occur. |
| AT-11 | Given model lacks reference editing, then UI disallows unsupported mode and backend rejects forged requests. |
| AT-12 | Given AI service outage, then operations dashboard clearly isolates AI incident; Sales, Stock and Accounting remain available. |

## 12. Phased delivery / dependencies

| Phase | Scope | Exit gate |
| --- | --- | --- |
| 0 — Discovery | Validate current Business/role model, Product APIs, Audit v2, ADR-03 private storage, ADR-04 durable delivery and actual baseline checkout SLO. | Approved dependencies and measurable performance baseline |
| 1 — Safe core | Capability flags, permissions, briefs, private upload, queue/worker isolation, OpenAI adapter, gallery, usage metering. | AT-01..07, AT-10, AT-12 |
| 2 — Creative workflow | Google adapter, multi-provider comparison, editing/variations, ratings, reviews, approvals. | AT-08, AT-11; provider conformance suite |
| 3 — Manufacturing | Production pack, sample review, cost worksheet, Product Master draft handoff. | AT-09 and human verified spec pack |
| 4 — Analytics/SaaS | Read-only sales-performance feedback, tenant billing/quota, provider expansion and operational automation. | Tenant isolation and production load sign-off |

## 13. Open decisions requiring confirmation

- Confirm phase-one garment families and multilingual design briefs (Bangla/English); default proposed families are abaya, jilbab, and embroidered/modest outfits.
- Verify ADR-03 private-storage and ADR-04 durable-delivery acceptance status in repository; AI launch is blocked until secure Local/S3 storage and recoverable outbox/queue delivery are verified.
- Which provider accounts/regions are allowed for sending customer/model photos and whether consent is required?
- How many concurrent generation jobs and what monthly spend cap are acceptable per Business?
- Does Product Master already support design-to-main-code draft creation via an approved command?
- Who may approve a production sample and who may convert an approved design into sellable SKUs?
- What are current checkout p95/p99 and peak-load baselines? The 5% guardrail is provisional until measured.

## 14. Definition of done

A phase is complete only when documented requirement-to-test traceability exists; role/business isolation and audit checks pass; provider failures are exercised; workers are isolated operationally; AI queue saturation tests show no unacceptable core POS degradation; backup/recovery/retention are validated; release builds, EF migration/model checks and existing RetailPOS regression suites pass; and product creation remains permission-gated and human-controlled.

## 15. Consolidated architecture baseline (v1.1)

This section supersedes conflicting implementation options or navigation paths elsewhere in v1.0. Requirements from earlier sections remain in force unless expressly refined here. Same Git monorepo does not imply one deployment unit, one process, or one database.

| Decision | v1.1 baseline / constraint |
| --- | --- |
| Repository | Existing RetailPOS monorepo; reuse existing project conventions; no duplicate parallel solution. |
| Frontend | Existing Angular app with lazy-loaded AI Studio feature and permission-based navigation. |
| Control plane | Existing RetailPOS .NET API hosts secured AI design endpoints and CQRS application orchestration initially. |
| Execution plane | Separately deployable .NET AI Studio Worker (Windows Service/container), independently restartable and resource-limited. |
| Database | Separate logical AI Studio database, preferably on the established production DB engine; separate DB credentials/connection pool and migration history. |
| Broker | Reuse RabbitMQ infrastructure if durable delivery gate is verified; use dedicated AI queues and quotas; core queues retain capacity priority. |
| Files | Configurable private Local File System or AWS S3 through shared storage abstraction. |
| AI vendors | Provider-neutral adapters; initially OpenAI and Google image-capable models, optional future providers by capability check. |
| Extraction path | AI API may become independently deployed only when justified by measured load and operational complexity. |

### 15.1 Workload containment and failure domains

- FR-ARCH-001: API accepts a validated job in a short transaction, writes a recoverable outbox record, returns HTTP 202 with JobId; never waits for remote image generation.
- FR-ARCH-002: Separate worker process with independent CPU/memory limits, maximum parallel jobs, RabbitMQ prefetch, provider rate limits, worker DB pool and storage I/O budget.
- FR-ARCH-003: Core Sales, Payments, Inventory, Accounting and HR/Payroll must not call AI providers, AI worker or AI Studio DB synchronously.
- FR-ARCH-004: Consumer deduplication/idempotency and delivery acknowledgements only after durable processing; exponential retry with jitter, DLQ, poison job handling and recoverable job state.
- FR-ARCH-005: Queue depth, worker health and storage pressure can pause new AI job admissions; existing design browsing remains available where safe, POS remains fully functional.
- NFR-ARCH-001: Core checkout p95 latency under stressed AI load must not exceed agreed measured baseline by >5% as provisional pilot gate; verify also p99, error rate, stock posting and DB contention; baseline to be approved before release.
- NFR-ARCH-002: Shared physical host/volume is not guaranteed isolation: set hard worker CPU/RAM/I/O quotas where supported, dedicated worker identity, separate DB pool, disk free-space thresholds, and capacity alerts; plan separate host/volume if guardrails fail.
- NFR-ARCH-003: Avoid synchronous image transformations in web API; process thumbnails and large outputs in AI workers, serve images via protected streaming or short-lived signed URLs.

### 15.2 AI database ownership and cross-context contracts

AIStudio is system of record for DesignBrief, Design, DesignVersion, ReferenceAsset, GeneratedAsset, Collection, GenerationJob/Attempt, ProviderProfile, Approval, Sample, ProductionPack, CostLedger and AIOutbox/Inbox. Existing RetailPOS remains system of record for Business, Identity, Permissions, Product Master, inventory, sales and accounting. Never create SQL foreign keys between databases; store externally validated IDs and enforce scope through APIs/application contracts.

Product handoff: approved design -> authorised CreateProductDraft command in existing Product application -> existing validations for Main Product Code, color, size and unique barcode combinations. No direct AI database writes to operational POS tables. Use idempotent handoff key and reconciliation for partial failures.

| Logical entity | Essential attributes |
| --- | --- |
| Design/Version | Id, BusinessId, Title, Type, BriefId, ParentVersionId, Status, CreatedBy, CreatedAt, ApprovedBy, ApprovedAt |
| GenerationJob | Id, BusinessId, DesignId, Provider, ModelIdentifier, IdempotencyKey, Status, Attempt, QueuedAt, StartedAt, CompletedAt, ErrorCode |
| GenerationAsset | Id, BusinessId, DesignVersionId, StorageProvider, StorageKey, MIME, Bytes, SHA256, AccessClass, CreatedAt |
| GenerationCost | BusinessId, JobId, Provider/Model, EstimatedCost, ActualCost, Currency, BillableState, Timestamp |
| ProductHandoff | DesignId, ApprovedVersionId, ProductDraftId, HandoffState, CorrelationId, Timestamp |

## 16. Configurable private file storage

Storage applies to original design references, generated images, variations, thumbnails, production packs and future shared private documents. Local means a server-configured path outside the public web root, not a browser device and not implicitly a POS installation folder.

| Requirement | Specification |
| --- | --- |
| FR-STOR-001 | IPrivateFileStorage (or existing equivalent) exposes Store, OpenRead/Stream, Exists, Delete, metadata and limited signed-access capability where applicable. |
| FR-STOR-002 | Active new-write provider is selected by environment configuration: LocalFileSystem or AwsS3. Startup checks validate configuration, paths, permissions and keys; secrets never stored in Angular. |
| FR-STOR-003 | Record immutable provider ID, bucket/root reference, opaque object key, checksum, owner BusinessId, content type, size and retention class for each object. |
| FR-STOR-004 | Switching active provider changes only future writes; old files continue to resolve through their original provider when available. No implicit fallback or silent relocation. |
| FR-STOR-005 | Private-by-default authorization: business + permission check on reads; local files outside web root; S3 bucket Block Public Access, controlled IAM access. |
| FR-STOR-006 | Validate file signature, supported MIME, quotas, image dimensions, malware policy; strip risky metadata where appropriate, and stream large objects. |
| FR-STOR-007 | Tenant-aware retention, soft-delete workflow, cleanup for abandoned generations and reliable backup/recovery procedure. |
| FR-STOR-008 | Provider-to-provider migration (future phase): resumable, checksum-verified, audited; preserve source until committed and allow recovery. |
| NFR-STOR-001 | AI file operations and thumbnails shall not starve checkout disk I/O, server RAM or DB capacity; reserve disk free-space margin and alert before quota breach. |
| NFR-STOR-002 | If worker is remote, Local provider requires secure shared volume or designated storage gateway; a process-local path is not assumed mutually visible. |

Example non-secret configuration: FileStorage:Provider=Local; FileStorage:Local:RootPath=<private server path>; FileStorage:S3:BucketName=<private bucket>; FileStorage:S3:Region=<region>; FileStorage:S3:KeyPrefix=<prefix>. AWS credentials via instance role/workload identity or secure secret store, not source control.

## 17. Main navigation, routes and authorisation

AI Fashion Design Studio shall be an independent main sidebar menu (not under Product Management). Enable only when business capability is ON and user has at least one visible subpage permission. Initial launch pages: Design Studio, My Designs, Collections, Compare Designs, Approvals, Generation Jobs and Usage & Costs; additional pages appear only after delivery and permission configuration.

| Group | Sidebar item | Proposed Angular route | Required permission |
| --- | --- | --- | --- |
| Design Workspace | Dashboard | /ai-design/dashboard | AIStudio.Dashboard.View |
| Design Workspace | Design Studio | /ai-design/studio | AIStudio.Design.Create |
| Design Workspace | My Designs | /ai-design/designs | AIStudio.Design.View |
| Design Workspace | Collections | /ai-design/collections | AIStudio.Collection.Manage |
| Design Workspace | Compare Designs | /ai-design/compare | AIStudio.Design.Compare |
| Production & Approval | Design Approvals | /ai-design/approvals | AIStudio.Design.Approve |
| Production & Approval | Sample Management | /ai-design/samples | AIStudio.Sample.Manage |
| Production & Approval | Production Packs | /ai-design/production-packs | AIStudio.ProductionPack.View |
| Operations | Generation Jobs | /ai-design/jobs | AIStudio.Job.View |
| Operations | AI Usage & Costs | /ai-design/usage | AIStudio.Usage.View |
| System Admin | AI Provider Settings | /settings/ai-providers | AIStudio.Settings.Manage |
| System Admin | Storage Settings | /settings/storage | System.Storage.Manage |
| Existing Access Management | Roles & Permissions | existing route | Existing role management permissions |

Names and routes are proposed; retain project conventions at implementation. Back-end authorization is authoritative; a hidden menu is never treated as a security boundary. User may view only own designs by default unless granted business-level view. Super Admin cross-tenant access requires explicit controlled authorisation and audit evidence.

- FR-NAV-001: Main menu and each submenu are gated by Business capability flag and effective permission; missing capability blocks API even via direct URL.
- FR-NAV-002: User can navigate away or close browser after submitting; server job continues and is recoverable after restarts. Job list displays Queued, Processing, Completed, Failed, Cancelled, and retry detail where permitted.
- FR-NAV-003: Unavailable AI provider/worker disables creation or warns of deferred processing without hiding saved designs; core POS navigation remains normal.
- FR-NAV-004: Actions Generate, Edit, Compare, Approve, Sample, Production Pack, Product Draft and Settings are permission-separated, audited and server-validated.
- FR-NAV-005: Admin/provider/storage configuration is under existing System Administration instead of cluttering regular design sidebar.

## 18. Provider selection, usage governance and traceability

Providers must be listed from server-side capabilities/configuration, not hard-coded model names that can become obsolete. An administrator enables allowed providers/models, regions, output limits and API credentials per deployment; optionally restricts access by Business. Claude or other language-centric assistants may support brief/analysis, but must not be presented as native image generation unless an explicitly configured image-capable service is present.

- FR-PROV-001: Image generation/edit tasks declare required capability (text-to-image, image-reference, edit, mask, variation), supported size, cost/usage estimate and vendor-specific limitations.
- FR-PROV-002: A design request records selected provider/model, prompt revision, reference IDs, parameters, seed if supported, timestamps, provider request ID, attempts and assets.
- FR-PROV-003: Per-Business monthly budget, per-user cap, generation concurrency and admission guardrail; reserve estimated spend before queueing and reconcile actual cost.
- FR-PROV-004: Compare mode links one logical prompt to multiple independent generation jobs so provider outputs are attributable and separately billable.
- FR-PROV-005: External provider photo transfer is disclosed and limited to authorised content; metadata and personal images follow retention and privacy policy.

## 19. Additional acceptance criteria and release gates

| ID | Acceptance scenario |
| --- | --- |
| AT-01 | Simulate 20–50 queued image jobs during representative sales load; checkout p95/p99, sales commit, inventory update and API error rates remain within approved core targets. |
| AT-02 | Kill/restart AI worker mid-provider call; job transitions recover via idempotent processing or controlled reconciliation without lost/duplicated approval/product handoff. |
| AT-03 | Disable provider/S3 or fill AI local disk quota; AI jobs fail/retry deterministically while checkout and existing image metadata remain accessible as policy allows. |
| AT-04 | Switch write provider Local→S3: new assets use S3; pre-existing local assets remain retrievable under permission control; switching back does not rewrite history. |
| AT-05 | Designer without Approve/Settings permissions cannot see those pages or call protected APIs; owner from Business A cannot enumerate Business B design IDs/files. |
| AT-06 | User submits image job then logs out or switches screens; job persists and result is visible after a new session. |
| AT-07 | Product draft handoff invokes existing product validation and creates no live SKU/barcode until existing workflow authorises it. |
| AT-08 | Admin selects unsupported model task combination; validation rejects request before billing/provider call. |
| AT-09 | All generation attempts, storage reads where security relevant, approval changes and provider-setting changes are audit-linked to actor, Business and correlation ID. |
| AT-10 | Independent AI database inaccessible or queue saturated: new AI operations degrade gracefully; POS transactional database and checkout continue. |

## 20. Delivery sequence and unresolved confirmations

| Phase | Scope and exit condition |
| --- | --- |
| 0 — Foundation gates | Verify ADR-03/ADR-04 status, Business capability, DB strategy, existing shared storage abstractions and performance baseline; deployment-resource plan approved. |
| 1 — Minimum usable studio | Independent menu, briefs/references, private Local/S3 abstraction, separate AI DB, OpenAI provider, durable AI queue/worker, job history, design gallery, unit/integration/load gates. |
| 2 — Multi-model | Google image provider, capabilities/configuration, edit/variations, compare, spend accounting and usage dashboard. |
| 3 — Design governance | Collections, versioning, approval, audited samples and production-pack drafts with expert review. |
| 4 — Product & insights | Authorised Product Draft integration and sales-feedback insights, respecting data scope. |
| 5 — Scale enhancements | Resumable storage migration, advanced queue scaling, optional independent AI API and automated quality checks. |

Open confirmations: production database engine and host resources, choice of Local storage shared access strategy, S3 region, provider availability/commercial terms, baseline transaction SLOs, business budget cap, original image consent policy and project-specific permission naming. None of these should be silently assumed from this SRS.

End of SRS v1.1  •  Detailed infrastructure deployment, exact DTOs, indexes, job state machine and queue topology belong in LLD.
