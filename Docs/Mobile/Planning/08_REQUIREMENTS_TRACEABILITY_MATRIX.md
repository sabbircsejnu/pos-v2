# Requirements Traceability Matrix

**Source of truth:** SRS v1.4; LLD v1.4 supplies design proposals, not evidence of current implementation. The rows below cover each functional, catalogue, architecture, nonfunctional and notification requirement ID in the SRS. Test names are proposed selectors/categories; they do not claim tests already exist.

## Functional requirements

| SRS requirement IDs | Backend / Angular target | Flutter target | Test evidence planned | Phase / gate |
|---|---|---|---|---|
| AUTH-001, AUTH-002 | Approved native-client auth/session, `me`, grants and authorized context | Secure sign-in, refresh, expiry/revoke state | Auth integration, token lifecycle, role/scope negative tests | P1/P2 |
| AUTH-003 | Server authorization remains authoritative | Accessible role/grant navigation; demo role controls excluded from production | Route guard and server permission tests | P2 |
| AUTH-004 | Context and allowed-outlet APIs; revoke old scoped sessions | Clear/warn on account or tenant switch; no silent unsynced-data loss | Switch/logout/cache partition tests | P1/P2/P5 |
| AUTH-005 | Reject unauthorized/expired submission | Offline state shown; submission gated on valid authorization | Expiry during pending operation tests | P1/P2/P5 |
| AUTH-006 | Exclude unauthorized financial fields from response | Hide/redact fields consistently | DTO permission serialization tests | P1/P6 |
| DASH-001 | Authorized owner metrics and definitions | Owner dashboard/trends/outlet/design ranks | Golden report reconciliation and permission tests | P6 |
| DASH-002 | Assigned-outlet scope, targets, leaderboard and alerts | Sales manager dashboard | Scope/target/report tests | P6 |
| DASH-003 | Self-only salesperson metrics and target state | Salesperson dashboard | Self-only access and no-target tests | P6 |
| DASH-004 | Metric definition, currency, timezone/window/freshness | Consistent metric labels and stale state | Golden metric/timezone/tax/returns tests | P6 |
| DASH-005 | Authorized filters | Session-persisted filters and loading/empty/error states | Filter grant and UI state tests | P6 |
| DASH-006 | Consistent unavailable/no-target/stale DTOs | Graceful negative/empty/profitability state | Widget cases | P6 |
| STK-001, STK-002 | Authorized scope; opaque session/ref, revision/baseline/catalogue pin | Create/select prepared session | Scope and session creation integration | P4/P5 |
| STK-003 | Enforce existing ERP workflow transitions | Display server lifecycle; no arbitrary status mutation | State transition matrix | P4/P5 |
| STK-004, STK-005, STK-006 | Accept discrete operations with stable IDs | Immediate local scan feedback and camera detection throttle | Scan repeated/detection distinction tests | P5 |
| STK-007, STK-008 | Validate reason/corrections, preserve history | Search/manual quantity/reason; append correction/undo | Immutable operation and reason tests | P4/P5 |
| STK-009 | Session reopen/recovery policy | Pause/resume/process-kill recovery | Restart and lease recovery tests | P4/P5 |
| STK-010, STK-011 | Blind payload and explicit unknown/ambiguous status | Do not show baseline; quarantine unknown barcode | Response redaction and ambiguity tests | P3/P4/P5 |
| STK-012 | Scope/completeness state; missing SKU not auto-zero | Explicit completeness declaration | Full/partial scope reconciliation tests | P4/P5 |
| STK-013, STK-017 | Expected revision, idempotent submission and movement/baseline policy | Surface conflict; no silent merge | Replay, concurrent movement/device integration tests | P4/P5 |
| STK-014 | Enforce read-only submitted state; approved reopen workflow | Read-only after successful submit | State/authorization tests | P4/P5 |
| STK-015 | Existing ERP review/exception/approval/rejection | Submission status and notification/deep link where enabled | ERP workflow regression and mobile submission test | P4/P5 |
| STK-016 | Existing adjustment/ledger path, at-most-once posting | No stock posting in app | Double-post and ledger reconciliation test | P4/P5 |
| STK-018 | Permission-protected variance/shortage/overage/status DTO | Approved comparison view | Field grant and arithmetic reconciliation test | P4/P5 |
| STK-019 | Official Excel and print-ready PDF from ERP state | Export/view/share approved file | Output schema/total/reconciliation tests | P5 |
| STK-020 | Audit actor/device/session/event/scope/correlation/reason/lineage | Capture operation provenance | Audit completeness and redaction tests | P4/P5 |
| STK-021 | Lease or conflict-safe event merge | Multi-device user-facing conflict state | PostgreSQL concurrent writers and lease expiry tests | P0/P4/P5 |
| SYNC-001, SYNC-002 | Catalogue data contract; no inventory side effect | Local indexed catalogue; commit before success | DB transaction and offline lookup tests | P3/P5 |
| SYNC-003, SYNC-004 | Idempotent ordered batches and per-op receipts | Stable operation IDs, partial ack/resume | Duplicate/out-of-order/partial ack tests | P4/P5 |
| SYNC-005, SYNC-006 | Recoverable server receipts/status | Durable pending/last sync/offline/conflict status | Process kill/reboot/network fault tests | P4/P5 |
| SYNC-007 | Revalidate principal/scope; safe unsynced-data resolution | Warn/block and managed resolve on logout/scope switch | Cross-tenant cache isolation and pending logout tests | P1/P2/P5 |
| SYNC-008 | Revision/remap/deactivation exception responses | Explicit catalogue conflict resolution | P3/P4 revision/remap tests | P3/P5 |
| INS-001 | Authorized insights and reusable report queries | Product velocity/demand/trend views | Metric source reconciliation, scope tests | P6 |
| INS-002 | Document metric/discount definitions | Label descriptive correlations accurately | Metric-copy/content review and DTO tests | P6 |
| FCST-001, FCST-002 | Forecast API with approved horizon/features | Filters and demand projections | Calendar horizon, input coverage, stock-out backtest | P8 |
| FCST-003 | Persist model/version/cutoff/coverage/quality metadata | Show provenance and generation time | DTO/provenance tests | P8 |
| FCST-004, FCST-005 | Advisory production proposal and simulation endpoints | What-if controls separate from saved decisions | Simulation does not mutate ERP; net quantity tests | P8 |
| FCST-006, FCST-007 | Grounded explanation/fallback policy | Separate fact/estimate and unavailable state | No-data and explanation provenance tests | P8 |
| FCST-008 | Hierarchical reconciliation and cold-start policy | Label coverage by hierarchy | Backtest sparse/new design and consistency tests | P8 |
| NOT-001, NOT-002, NOT-003, NOT-004, NOT-005, NOT-006 | Scoped alert policy, Angular configuration and inbox APIs | Shared inbox/badge and protected links | Policy, Angular CRUD, recipient isolation and client inbox tests | P7 |
| NOT-007, NOT-008, NOT-009 | Transactional event/outbox, idempotent worker, retry/DLQ/replay | Reconcile after reconnect; no client-generated business alerts | Transaction rollback, crash, duplicate, poison and replay tests | P7 |
| NOT-010, NOT-011, NOT-012, NOT-013, NOT-014 | Preferences, redaction, event/rule versions, shared read-state and UX | Shared read state, offline/revoked/deep-link behavior | Cross-client convergence, version/audit/security tests | P7 |
| NOT-015 | Seed the exact 28 candidate stable types in `06_NOTIFICATION_ARCHITECTURE.md` | Render only enabled types | Producer verification, registry schema and capability gate tests | P7 |
| NOT-016, NOT-017, NOT-018, NOT-019 | Typed rules, calendars, suppressions, provenance and edge-trigger semantics | Explain eligible/suppressed states only where appropriate | Threshold boundary, duplicate scheduler, cooldown and replay tests | P7 |
| NOT-020, NOT-021 | Disable unsupported types; version safe templates/deep links | Safe localized content and reauthorized drill-down | Feature-gate, sanitization and post-revocation tests | P7 |
| PRF-001 | Profile/grants/outlets endpoint; logout/revoke | Profile, scanner preferences, sync/help/logout | Profile permission and logout/pending-data tests | P2/P5 |
| OBS-001 | Request correlation; redacted server telemetry | Correlate app operation to API request without secrets/stock leakage | Structured log redaction tests | P2/P9 |

## Catalogue, architecture and nonfunctional requirements

| SRS requirement IDs | Backend / Angular target | Flutter target | Test evidence planned | Phase / gate |
|---|---|---|---|---|
| CAT-001, CAT-002, CAT-003 | Scoped complete snapshot and generic typed-attribute projection | Ready only after verified local commit; preserve ERP identities | Revision/data projection and arbitrary attribute tests | P1/P3/P5 |
| CAT-004 | Enforce uniqueness/ambiguity in scope and map aliases | Do not guess; preserve leading zeros | Cross-table collision, duplicate/ambiguous/normalization and legacy null tests | P1/P3/P5 |
| CAT-005, CAT-015 | Stable revision/cursor/tombstones/checksum/recovery contract | Atomic page staging/activation and full resync | Delta equivalence, corruption, expiry and interruption tests | P3/P5 |
| CAT-006, CAT-007 | Validate freshness/scope; pin session identity revision | Prepared offline session uses pinned revision | Stale/outlet/session-pinning tests | P3/P4/P5 |
| CAT-008, CAT-012 | Retain unresolved observation; validate mapping at submit | Quarantine and explicit online resolution | Unknown then resolved/deactivated/remapped tests | P4/P5 |
| CAT-009, CAT-013, CAT-014 | Sync metadata/search fields; optional bounded image endpoint | Offline indexed search; optional thumbnails | Add/change visibility, search latency and cache bounds tests | P3/P5 |
| CAT-010, CAT-011 | Minimal projection, scope and server revoke | Minimize local data, partition/wipe/managed recovery | Payload minimization and tenant-switch/device loss tests | P1/P5 |
| ARCH-001, ARCH-002 | Separate API/application boundaries | Standalone `retailpos-mobile/`, feature-first Clean Architecture | Build-pipeline and architecture dependency tests | P2 |
| ARCH-003, ARCH-004 | Contract boundaries and API tests | Import rules, public feature contracts, composition-root DI | CI architecture/lint and cycle checks | P2 |
| ARCH-005 | Versioned API/error schema/backward compatibility | Typed API clients and compatibility behavior | Consumer/provider contract suites | P1/P2 |
| ARCH-006 | Approved Figma assets/tokens | Native Flutter design system | Asset/token review, contrast/accessibility checks | P1/P2 |
| ARCH-007 | API/concurrency/isolation tests | Unit/widget/integration/offline recovery | Slice-specific test gate | All |
| ARCH-008 | N/A | Pinned SDK/packages/lockfile and audit | Clean CI resolver, dependency audit | P2/P9 |
| ARCH-009 | Approved ADRs for auth/cache/sync/monitoring | Follow signed decisions | ADR references in test/slice metadata | P0/all |
| ARCH-010 | API/worker latency, freshness and redacted telemetry | Scan/sync/crash metrics without unnecessary PII | Redaction, metric presence and operations dashboards | P2/P9 |
| ARCH-011 | Keep monolith; no premature extraction | Avoid unused package layers | Architecture review and release footprint | All |
| ARCH-012 | Requirement-tagged backend slices and gates | Explicit user approval between phases | Gate approvals and traceability audit | All |
| NFR-001, NFR-002 | N/A | p95 local scan commit and 5,000 scans/2,000 SKUs target on agreed device | Device benchmark and memory/UI responsiveness test | P5/P9 |
| NFR-003, NFR-004 | Resumable status/receipts | Durable offline operation under process kill/network loss | Offline chaos/device suite | P5/P9 |
| NFR-005 | TLS, auth and server-side least privilege | Secure storage, at-rest threat model, secret-free binary/logout | Mobile security review and release scanning | P2/P9 |
| NFR-006 | Accessible API errors/labels as applicable | Font/touch/screen reader/high contrast not color-only | Accessibility audit on real Android device | P2/P9 |
| NFR-007 | UTC wire timestamps and business-zone date policy | English initial, BDT, Asia/Dhaka display | Timezone/currency/date boundary tests | P6/P9 |
| NFR-008 | Correlation, API/worker metrics and privacy settings | Structured redacted logs, crash/trace/release health | Telemetry contract and redaction tests | P2/P7/P9 |
| NFR-009 | Compatible staging/prod API and deployment configuration | Reproducible flavors, pinned dependencies, signed artifact | Clean CI and signature verification | P2/P9 |
| NFR-010 | Tenant/business/outlet isolation in every endpoint | Scope-aware client cache only; server authoritative | Negative integration/security tests | P1/P9 |
| NFR-011 | Retention and purge policy for server evidence/documents | Local session/unsynced/telemetry retention | Retention/purge/restore tests | P0/P5/P9 |
| NFR-012 | Forecast drift/accuracy/failure monitoring | Display provenance and fallback | Backtest, drift alerts, inference failure tests | P8 |
| NFR-NOT-001, NFR-NOT-002, NFR-NOT-003, NFR-NOT-004 | Notification latency/loss/index/retention/lag | Reconcile authoritative inbox | Worker crash/replay, load and monitoring tests | P7 |

## UAT and ADR traceability

| SRS identifiers | Trace target | Validation / gate |
|---|---|---|
| CAT-UAT-01, CAT-UAT-02, CAT-UAT-03, CAT-UAT-04, CAT-UAT-05, CAT-UAT-06 | Offline catalogue/stock-take UAT plan in `05_OFFLINE_CATALOGUE_AND_STOCK_TAKE_DESIGN.md` | Execute full set on supported Android device and PostgreSQL-backed ERP before P5 completion. |
| ADR-N01, ADR-N02, ADR-N03, ADR-N04, ADR-N05 | Notification decisions in `09_OPEN_DECISIONS_AND_RISKS.md` | All notification ADRs approved before P7; event-source and discount definition block sale rules. |

## Completion rule

An ID is complete only when the relevant API/client behavior, tests, security/audit evidence, acceptance criteria and gate are all satisfied. Planned mappings above do not indicate current implementation or passing tests.
