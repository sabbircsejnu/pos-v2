# IraniMart AI Business Advisor — Software Requirements Specification (SRS)

**Version:** 1.0 (business baseline freeze)  
**Date:** 2026-10-27  
**Status:** Approved Decisions 01–05 frozen; implementation acceptance gates open  
**Initial customer:** IraniMart (7 showrooms)  
**Product direction:** Multi-tenant SaaS-ready  
**Documentation path:** `Docs/AI_Business_Advisor/AI_BUSINESS_ADVISOR_SRS.md`

## 1. Purpose and scope

Build an in-house, extensible AI Fashion Market Intelligence and Business Advisor that combines authorised Bangladesh market/social/search signals with tenant-owned ERP/POS operational data to produce evidence-backed weekly and on-demand analyses, design opportunities, procurement *proposals*, and trackable business recommendations. The first deployment serves IraniMart's seven showrooms, focusing on **Burqa/Abaya, Hijab, Panjabi**. Architecture must allow additional categories, outlets and SaaS tenants without redesigning identity, storage or permissions.

**Explicit exclusions / constraints:** The system cannot inspect other users' private ChatGPT/Claude/Gemini prompts, circumvent social network API restrictions, infer national demand from unrepresentative samples, execute financial/stock mutations without authorised human approval, or treat generated outputs as verified market facts. A provider named in the target research scope does **not** mean that provider guarantees API access. Forecasts are estimates, not assured outcomes.

## 2. Approved decisions — immutable baseline

| ID | Approved decision | Normative implications |
|---|---|---|
| D01 | Hybrid Connector Architecture | Dedicated code adapters for common providers **and** policy-constrained configurable REST/JSON connector; capabilities and permissions are explicitly declared; no arbitrary scripts or network access. |
| D02 | Hybrid Credential Model | Platform-managed and tenant-owned BYOK credentials; tenant entitlement, costs, credential source and provider terms checked per request; never silently charge platform fallback. |
| D03 | Hybrid Source Management with Automatic Discovery and Human Governance | Manually curated sources plus assisted source discovery; policy-qualified low-risk auto-activation only within budgets; paid/restricted/high-risk sources require approval. |
| D04 | Tiered Research Data Retention with Historical Analytics | PostgreSQL workflows/system-of-record; Elasticsearch analytics and searchable evidence; Local/S3 artifacts; provider-aware retention, provenance, deletion and historical aggregates. |
| D05 | Controlled Autonomous Agent with Human-in-the-Loop Business Execution | Autonomous permitted research, interpretation, reporting and draft generation; product activation, pricing, confirmed purchasing, inventory and financial mutations require authorised human approval through existing application boundaries. |

Approved decisions take precedence over unapproved implementation suggestions. All future changes require versioned change control.

## 3. Stakeholders and roles

- **Platform Super Admin:** connector definitions, platform credentials, global source catalogue, SaaS service policies, monitoring; no default authorisation to approve a tenant's commercial actions.
- **Business Owner:** full tenant reporting and business governance, design and commercial approvals per policy.
- **Business Admin:** manages tenant sources, credentials, teams, budgets, schedules, and delegated approvals.
- **Research Analyst/Merchandising Manager:** research tasks, reviews, comparative insights, recommendations and design briefs subject to scope.
- **Design Manager:** design briefs, visual concepts and design approval.
- **Purchase Manager:** sourcing proposals and separate commercial approval.
- **Outlet Manager:** assigned-outlet insights and feedback; never implicit access to another outlet's sensitive financial data.
- **Viewer:** policy-scoped reports without mutation powers.
- **System worker:** service identity with narrow scoped rights; no unrestricted owner privileges.

Access must derive from the existing identity/permission system and applicable outlet assignment semantics, **not** unrelated `all_locations` permissions. Use tenant scope at all stages and per-outlet authorisation at query and command boundaries.

## 4. Business workflows

### WF-01 Weekly automated intelligence
1. Scheduler creates a tenant-scoped idempotent job for configured week/category/coverage.
2. Entitlement, budgets, source policies and connector capabilities are resolved.
3. Permitted connectors retrieve evidence with source references and execution diagnostics; failures are isolated and surfaced.
4. Normalisation, deduplication, Bangla/Banglish/English classification, design extraction and provenance capture occur.
5. Business-domain read models supply authorised sales/stock/margin aggregates without blocking POS writes.
6. Deterministic metrics and estimates are calculated, then AI synthesises evidenced, uncertainty-aware recommendations.
7. Versioned report and recommendations are persisted; notifications sent asynchronously.
8. The reviewer sees evidence, provenance, coverage limits, report history and recommended next actions.

### WF-02 On-demand research
Authorised user specifies question, date range, categories, markets, outlet scope and external/internal research scope. A job is created; progress, cancellation and partial output are supported; historical result reuse requires rights validation. Duplicate requests may be reused subject to scope and freshness policy. On-demand reports must never claim access to disconnected data.

### WF-03 Discovery to monitoring
Manual or AI-discovered source → canonicalisation/deduplication → platform capability and policy check → credibility/relevance assessment → approved/auto-activated under strict policy or pending manual review → monitored and periodically revalidated → paused/removed on policy or health failure.

### WF-04 Recommendation lifecycle
Proposed → Under Review → Approved / Rejected / Revision Requested → In Progress → Implemented → Outcome Evaluated → Closed. Revisions and supersession preserve history. A recommendation can relate to zero or many downstream drafts, each with its own approval and status. Track original hypothesis, actual sales outcomes, margin, stockouts, promotion effects and forecast variance; do not claim causation from correlation alone.

### WF-05 Controlled design/purchasing integration
AI may create **draft** design brief/concepts and **draft** product/purchase proposals after policy evaluation. Design approval is independent of commercial approval. Never allow a model or retrieved webpage to invoke saleable product publication, price changes, PO confirmation, stock movements or financial posting directly. Downstream execution must pass typed command handlers and approval checks.

## 5. Functional requirements

### 5.1 Tenant / outlet / RBAC / governance
- **FR-001** All records, jobs and derived read models must have an explicit tenant scope; outlet-specific metrics must honour existing location assignments and privilege rules.
- **FR-002** Platform-wide reusable research must be independently classified/licensed; private tenant inputs/reports may never be reused across tenants by default.
- **FR-003** Support plan entitlements, research budgets, per-user and per-business quotas, execution audit and subscription readiness without requiring first-release billing.
- **FR-004** Existing approval kernel and user permission services are the policy authority; all writes are server-authorised.

### 5.2 Connector and credential management
- **FR-010** Provide code-based connectors and an administrator-configured generic REST/JSON connector with an allowlisted host/endpoint, approved method, schema, mapping and authenticated secret reference.
- **FR-011** Connector registry exposes discovery/search/posts/comments/videos/trends/prices/reviews capabilities as `Supported`, `Unsupported`, or `ApprovalRequired`, and reports data availability and coverage.
- **FR-012** Credential resolver supports platform-managed and BYOK configurations, OAuth refresh where authorised, rotation/revocation, expiry and tenant ownership.
- **FR-013** Store secrets in secure server-side secret storage; never return secret values or credentials in frontend payloads, telemetry, logs or AI prompts.
- **FR-014** Per provider: schedule, timeout, retry/rate/backoff, circuit breaker, request quota, cost estimate, policy/consent metadata and disabled-state controls.
- **FR-015** No automatic billable credential fallback without configured explicit permission.
- **FR-016** Connector results must retain provider record IDs, retrieval timestamps, source URL, permitted rights, licensing and API response coverage.

### 5.3 Source management and discovery
- **FR-020** CRUD manually monitored pages, creator profiles, channels, hashtags, keywords, competitor sites, product listings, suppliers and publications.
- **FR-021** Discover potential sources from authorised public signals and existing curated seeds; do not imply access to non-public followers, posts or user conversations.
- **FR-022** Classify Bangladesh relevance, category, fashion relevance, authenticity, freshness, reliability, ownership, rights and expected research value.
- **FR-023** Approval workflows for high-risk/restricted/paid sources; eligible low-risk public sources can auto-activate under tenant policy and cost caps.
- **FR-024** Source lifecycle: Candidate, PendingReview, Active, Paused, Rejected, Retired; reason/status timestamps audited.
- **FR-025** Unique canonical identity, alias and content-level deduplication; user-editable source groups and watchlists.

### 5.4 Evidence collection and analysis
- **FR-030** Target: Facebook, Instagram, TikTok, YouTube, Google/Search/Trends, LinkedIn, e-commerce catalogues, fashion blogs/news, permitted aggregated AI-search insight publications, and tenant-owned channels; actual capability is provider-dependent.
- **FR-031** Separate source existence, raw engagement, purchase-intent indicators, sentiment, product design attributes, price observations and estimated market trends.
- **FR-032** Recognise Bangla/English/Banglish spelling variants, categories and variants. Never treat `borka`, `burqa`, `abaya` as interchangeable without context.
- **FR-033** Eligible visual inputs support image/video-frame design classification for cuts, colour, sleeve/neckline and embroidery; uncertain material attributes must be labelled inferred.
- **FR-034** Detect reposts, bots/suspicious engagement where signals are available, promotional content and skewed audience reach; do not claim representative market share from social samples.
- **FR-035** Source trust scores and claim/evidence confidence are separate, versioned measures; always show source/sample/time/coverage and limitations.
- **FR-036** Search Trends interest and social mention counts must remain different units; no unsupported absolute demand inference.
- **FR-037** Preserve evidence links, model/prompt versions, extraction timestamp, classification rationale, rights and lifecycle.

### 5.5 Internal ERP/POS intelligence
- **FR-040** Consume authorised sales, purchase, inventory, product/variant, returns/exchanges and accounting events via transactional outbox → RabbitMQ → idempotent projections (or controlled initial backfill).
- **FR-041** Analyse 7 current outlets and consolidated business scope; extensible to more outlets and other tenants.
- **FR-042** KPIs: units/net revenue net of VAT, COGS, gross margin, discounts, sales velocity, sell-through, returns, stock ageing, stockout-adjusted analysis, purchase lead times, supplier MOQ.
- **FR-043** Enforce correct treatment of cancelled/void sales, returns, exchanges, transfers and late-arriving/replayed events; no double counting.
- **FR-044** Add/ingest configurable product fashion taxonomy: category, collection, silhouette, colour, fabric, sleeve, neckline, embroidery, pattern, sizes, design reference/images, season.
- **FR-045** Missing/poor product attributes must be reported as data-quality gaps, not fabricated.

### 5.6 Intelligence engine and AI providers
- **FR-050** Model-neutral AI interface initially OpenAI and extendible to other approved providers; provider selection by task/tenant, cost and capabilities.
- **FR-051** Use deterministic analytics for financial calculations; LLM is used for interpretation, extraction and narrative with evidence/citation validation.
- **FR-052** Track emerging trends, cross-platform corroboration, demand intent, complaints, competitor pricing and assortment gaps.
- **FR-053** Evaluate opportunities with configurable factor weights (sales/demand, market evidence, margin, stock fit, differentiation, feasibility); missing dimensions yield provisional/insufficient-evidence status, never zero by default.
- **FR-054** Generate risk-aware price, demand range, quantity and outlet allocation *proposals* only when sufficient reliable inputs exist; show assumptions and uncertainty.
- **FR-055** Source documents are untrusted; enforce prompt injection prevention, allowlisted tools, output schema validation and data minimisation.
- **FR-056** AI chat must respect the requester's business/outlet/report permissions and distinguish actual retrieved data from simulated examples.

### 5.7 Reports, interaction and notifications
- **FR-060** Schedule weekly reports plus daily configurable observation jobs and event/season-specific reports; enable on-demand run/cancel/retry.
- **FR-061** Versioned reports contain executive summary, scope, coverage/limitations, market trend topics, source citations, competitor comparison, internal sales/inventory, design opportunities, forecasts, risk/confidence, next actions and approvals.
- **FR-062** Dashboard displays by category, period, source, outlet and research status; allow drill-down and comparison of report versions.
- **FR-063** Business Advisor chat supports scoped analytical Q&A and on-demand research; never silently use unauthorised/unconnected sources.
- **FR-064** In-app notifications; optional email channel on explicit configuration; failures must be retried without duplicate external notifications.
- **FR-065** Export reports in configured formats (HTML/PDF where implementation supports it) and preserve underlying structured JSON and evidence references.
- **FR-066** Show pipeline health, partial coverage and `No Data`, `Unavailable`, `Not Licensed`, and `No Access` distinctly.

### 5.8 Recommendations and downstream actions
- **FR-070** Persist recommendation, rationale, sources, benefits, risks, confidence, assumptions, owner, stage and due dates.
- **FR-071** Permit automatically generated draft design briefs/concepts and draft product/PO proposals within task/tenant quotas.
- **FR-072** Require human approval for publishing products, final pricing, confirmed PO, stock adjustments/transfers and accounting impact.
- **FR-073** Design approval and commercial approval are separate; cross-module commands typed, idempotent, optimistic-concurrency controlled and audited.
- **FR-074** Outcome evaluation links recommendations to actual launches, windows, promotions, gross margin, returns and inventory effects, with provenance and confounder caveats.

## 6. Required report schema (logical)

`Report`: id, tenantId, periodStart/End, categories, outletScope, initiatedBy, sourceCoverage, internalCoverage, executionStatus, version, previousReportId, generatedAt, aiProvider/model, promptVersion, financialMetricVersion, executiveSummary, findings[], recommendations[], limitations[], evidenceRefs[], approvalStatus, retentionPolicyId.

`Finding`: claim, signalType, dimensions, observedWindow, metrics/units, sourceIds, evidenceIds, sampleCount, confidence, limitations.

`Recommendation`: actionType, category/designAttributes, targetOutlets, description, businessCase, inputs/assumptions, risk, evidenceRefs, confidence, reviewStatus, proposedDraftRefs, dueDate, outcomeEvaluation.

## 7. Example weekly report outline
1. Executive summary and recommended business priorities.
2. Run scope, licensed providers, accessible coverage and missing-data notices.
3. Bangladesh cross-platform discussions and rising search questions.
4. Burqa/Abaya, Hijab and Panjabi design attributes/colour/material trend evidence.
5. Competitor assortment, offered prices and available reviews.
6. Seven-showroom sales, returns, gross margin, stockouts and excess inventory.
7. Market-vs-internal product/design opportunities.
8. Ranked **provisional** concepts with evidence, uncertainty, supplier feasibility and sample plans.
9. Outlet-scoped trial launch and procurement proposal (if evidence suffices).
10. Review decisions, outcome monitoring and next weekly follow-up.

## 8. Non-functional requirements

- **NFR-01 Security:** TLS, secret vault/KMS where available, encryption at rest, short-lived access, least privilege, strict server-side authorisation and audit.
- **NFR-02 Isolation:** Automated tests ensure no cross-tenant read/write/cache/index leakage; global evidence scope is rights-gated.
- **NFR-03 Availability:** POS transactional checkout must not depend on research system uptime; research queue outages cause no POS failures.
- **NFR-04 Resilience:** bounded retries with jitter, provider-specific circuit breakers, dead-letter handling, job checkpoints, cancellation, and safe partial-report generation.
- **NFR-05 Performance:** reports use analytical read models, not live full-table transaction scans; service objectives to be measured and baselined after representative load tests.
- **NFR-06 Observability:** trace IDs for job/evidence/model/report/approval, operational metrics, source freshness, ingestion lag, cost meters, alerts without secrets/PII.
- **NFR-07 Accessibility:** responsive Angular UI, keyboard navigation, clear data/status labelling, Bangla/English support planned.
- **NFR-08 Cost:** caps on API requests, AI tokens, storage and expensive image-generation tasks; hard stop or documented approval overrides.
- **NFR-09 Privacy:** collect minimum needed personal information; source rights, deletion/takedown and retention enforced across index, cache, object storage and exports.
- **NFR-10 Reproducibility:** version model, extraction schema, prompt, assumptions, sources and report artefacts when rights permit.
- **NFR-11 Data quality:** missing dimensions are labelled; never silently substitute synthetic facts.

## 9. Initial retention policy defaults (must obey stricter restrictions)

| Material | Suggested default | Control |
|---|---|---|
| Raw permitted social evidence | 30–90 days max | Provider rights may force less/no storage |
| Permitted cached media | Up to 30 days | Provider/media-specific restrictions |
| Search observation records | Up to 12 months | Subject to license |
| Normalised trend signals | Up to 24 months | Deletion/provenance-aware |
| Aggregated non-identifying metrics | Up to 5 years | Only where lawful, non-reidentifying |
| Competitor price observations | Up to 24 months | Terms-dependent |
| Reports/recommendations/outcomes | Default 5 years | Tenant policy, rights, retention schedule |
| Execution diagnostics | 90 days | Sanitised logs |
| Security audit | Proposed 12 months | Legal/tenant-specific controls |

Effective retention = the shortest applicable limit. Deletion jobs cover Elasticsearch, PostgreSQL references, objects, backups under restoration policy, caches and shared projections.

## 10. Acceptance gates (implementation-critical; not new business decisions)

### Gate G1 — Data provider capability, licensing and commercial use
Before activating a provider in production, verify the permitted endpoints, geography, authentication, fields, quota, commercial use, data retention and deletion rules; run compatibility/compliance tests. Unsupported or disallowed channels remain disabled with visible coverage gaps. No assumed private AI prompt access.

### Gate G2 — Tenant data governance
Before SaaS/multi-tenant release, pass automated tenant isolation, outlet permission, secret resolution, allowed cross-tenant global evidence reuse, deletion and audit tests. Platform admin does not have implicit tenant commercial approval rights.

### Gate G3 — Human approval and downstream integration
Before enabling downstream workflows, verify typed contracts to Design Studio/Product/Purchase, Approval Kernel and command-time permission checks; prove an AI-generated action cannot publish products, set selling prices, confirm orders, mutate stock or post finance entries without approved authorised business commands.

## 11. Release planning

**R1 — Research/reporting MVP:** provider registry, secure credentials, curated sources/discovery review, authorised connectors, analytical projections, reports and Q&A, weekly/on-demand jobs, evidence and coverage visibility. Multi-tenant schema/security included.

**R2 — Design/action intelligence:** visual design extraction, collection opportunities, Design Studio draft briefs, product/PO drafts, approval workflows, outcome evaluation.

**R3 — SaaS commercialisation:** tenant onboarding UI, subscription entitlements and billing integration, platform research catalogue, operational scale, tenant self-service and advanced governance. No migration away from tenant-aware model needed.

## 12. Test and acceptance catalogue

- Connector: capabilities, OAuth/BYOK, invalid credentials, consent expiration, terms-enforced `Unsupported`, API error/backoff, safe generic connector allowlisting/SSRF protection.
- Source: add, dedupe, discover, review, pause/revoke, auto-activation caps, tenant-specific watchlist.
- Evidence: provenance, signed observation time, content hash, deduplication, differing units, uncertain fabric classification, no AI prompt leakage.
- Analytics: seven outlets, assigned-outlet restriction, returns/exchanges/void logic, taxes excluded from net sales, late-arrival replay, stockouts, non-null missing data.
- Reporting: weekly scheduling and idempotent rerun, on-demand cancel, partial status, source citations, report versions, AI provider failure and transparent disconnected sources.
- Workflow: duplicate approvals, unauthorised approver, concurrency conflicts, product activation block, distinct design/commercial approvals, command deduplication.
- Governance: tenant isolation across REST, Elasticsearch alias/filter, caches, artifacts, jobs and recommendations; deletion/retention at all storage tiers.
- NFR: load/performance budgets, instrumentation, disaster recovery, accessibility and representative Bangla/Banglish extraction evaluations.

## 13. Success measures (baselines must be measured; no fabricated targets)

Evidence-backed recommendation adoption; time to produce weekly reports; accessible source coverage; report factual/citation accuracy; internal KPI parity with ERP; forecast calibration; design-trial sell-through; rate of recommendations with evaluated outcomes; API cost per completed report; tenant isolation and approval policy violations (target zero).

## 14. Open external dependencies — tracked, not silently assumed

Provider approval/commercial licensing, contractual social listening data access, current ERP field/taxonomy completeness, full downstream module APIs and supplier attributes, exact SaaS subscription/plan pricing. These are implementation details/acceptance gates, not unresolved business-model blockers. Changes follow controlled specification revisions.

## 15. Traceability

D01→FR-010–016/G1; D02→FR-012–015/NFR-01; D03→FR-020–025; D04→FR-030–037/retention/G2; D05→FR-070–074/G3. Report/analysis requirements FR-040–066 span all decisions.
