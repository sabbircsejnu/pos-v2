# IraniMart AI Fashion Design Studio — Clickable UI Prototype

## Run

Open `index.html` in Chrome/Edge, or from this folder run `python -m http.server 8080` then visit `http://localhost:8080`.

## Screens

Standalone ERP sidebar with AI Fashion Design Studio main menu: Dashboard, Design Studio, My Designs, Collections, Compare Designs, Design Approvals, Sample Management, Production Packs, Generation Jobs and AI Usage & Costs. Administration has AI Provider Settings, Storage Settings and Roles & Permissions.

## Interactive journeys

- Design Studio → Generate Design (Demo) → Generation Jobs (Queued/Processing/Completed) → My Designs.
- My Designs → Details → Submit/Approve (local UI state only).
- Collections, Compare Designs, Usage & Costs, provider/storage settings and permissions preview.
- Responsive sidebar on narrow screens.

## Important limitations

This is a **front-end prototype**, not a deployable ERP module. All provider calls, job statuses, costs, role-based permissions, persistence, and approval actions are simulated. Reference uploads are not sent to any server. The four demo images are concept samples from the earlier design discussion. No backend, payment, POS or database connections are made.

In production, image generation must run through durable RabbitMQ messaging and a separate AI worker with independent database/resource limits; an API returns `202 Accepted` without waiting. Local/S3 storage must be private and business-scoped.

Reference: `../IraniMart_AI_Fashion_Design_Studio_SRS_v1.1.md`.
