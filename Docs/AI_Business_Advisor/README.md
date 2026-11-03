# IraniMart AI Business Advisor — Deliverables

- `AI_BUSINESS_ADVISOR_SRS.md`: Versioned business and software requirements, Decisions 01–05 preserved, release acceptance gates.
- `AI_BUSINESS_ADVISOR_LLD.md`: .NET/CQRS/MediatR, PostgreSQL, Elasticsearch, RabbitMQ, connector interfaces, API routes, security, workflow and testing design.
- `AI_BUSINESS_ADVISOR_PROTOTYPE.html`: Standalone interactive browser prototype. Open locally with Chrome/Edge/Firefox. No install, server or internet connection required.

## Prototype features

Nine screens: Overview, Fashion Trends, On-demand Research, Weekly Reports, Recommendations, AI Business Advisor, Research Sources, API Providers, Settings & Governance.

Interactions: navigation, category/source/timeframe filters, simulated research run, report generation in memory, recommendation approve/reject/revise (in-memory), adding and approving demo research sources, simulated discovery, sample chatbot, settings preferences.

**This is a UX prototype only.** All numerical examples are synthetic. No internal IraniMart operational database or third-party social/search/model API is connected. Admin configuration and review changes last only for the open page session. Do not use examples as factual market or business advice.

## Suggested integration

Copy both Markdown files to `Docs/AI_Business_Advisor/` in the POS monorepo. The HTML prototype is technology-independent; implement its layouts with existing Angular/Able Pro design components, calling the API routes specified in the LLD. Complete acceptance gates G1 (approved provider access), G2 (isolation) and G3 (approval) before corresponding production activation.
