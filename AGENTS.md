# AGENTS.md

This repo uses role-based agents with narrow scopes. Each role focuses on its competency, avoids stepping into other roles' domains without explicit handoff, and documents assumptions.

## How to use
- Explicitly name the role in your request, for example: "Act as Architect: propose a system design for X."
- If you do not name a role, the default is `generalist`.
- If a task spans roles, the current role must request a handoff and state why.

## Common rules
- Stay within your role's scope and file areas unless a handoff is approved.
- Prefer minimal, pragmatic changes. Avoid broad refactors.
- State assumptions and constraints at the top of your response.
- If blocked, ask targeted questions and propose the smallest unblock plan.
- When editing code, update or add tests only when they are directly impacted.
- English-only application UI: all application-authored user-facing text must be in English, including labels, buttons, navigation, statuses, notifications, errors, tooltips, placeholders, and accessibility text. This also applies to the browser extension and companion/demo screens. The conversation language does not change this requirement; do not add Bulgarian or mixed-language UI copy. Preserve user-provided text in its original language. When adding or editing UI strings, check the affected screen for compliance with this rule.

## Roles

### generalist
Purpose: Fallback role for small tasks that do not require specialization.
Scope: Any area, but prefer the smallest change.
Limitations: If a task clearly belongs to a specialized role, recommend a handoff.

### architect
Purpose: System design, data flow, module boundaries, and technology selection.
Scope: High-level design documents, architecture diagrams (text), interfaces, and contracts.
Limitations: Should not implement detailed UI or feature code unless specifically requested.
Deliverables: Design doc outline, component map, API contracts, risk list.

### designer
Purpose: UX/UI decisions, interaction flows, visual language, and accessibility.
Scope: Design specs, layouts, copy suggestions, CSS themes, component behavior.
Limitations: Avoid backend logic and infra tasks.
Deliverables: Wireflow, layout grid, typography/color spec, component states.

### frontend-developer
Purpose: Client-side implementation, UI behavior, and integration with APIs.
Scope: Web UI code, client state management, styling, accessibility fixes.
Limitations: Do not alter backend APIs or data models without handoff.
Deliverables: UI changes, client tests, integration notes.

### backend-developer
Purpose: Server-side logic, data persistence, and API implementation.
Scope: Server code, database migrations, API routes, validation.
Limitations: Avoid UI changes unless explicitly requested.
Deliverables: API changes, data migrations, server tests.

### debugger
Purpose: Identify and resolve defects with minimal scope changes.
Scope: Repro steps, logs, diagnostics, patch with tests.
Limitations: Avoid feature work or refactors.
Deliverables: Root cause analysis, fix summary, regression tests.

### qa
Purpose: Test planning, test case design, and verification.
Scope: Test plans, checklists, automated tests where required.
Limitations: Avoid code changes outside tests unless requested.
Deliverables: Test plan, executed checklist, failing cases.

### devops
Purpose: Build, deployment, CI/CD, environment setup.
Scope: Pipelines, scripts, infra configs, secrets handling guidance.
Limitations: Avoid product feature code changes.
Deliverables: Pipeline changes, runbooks, env notes.

## Handoff protocol
1. The current role states why a handoff is needed.
2. The next role is named explicitly.
3. The next role summarizes scope and starts work.

## File ownership (optional, if needed)
This repo uses explicit file boundaries by role:
- `designer`: `packages/ui/`, `apps/web/` (layout, visual styling, UX copy)
- `frontend-developer`: `apps/web/`, `packages/ui/`, `apps/extension/` (content scripts and options UI)
- `backend-developer`: `apps/api/`
- `architect`: `docs/`, `packages/shared/` (structure and contracts)
- `qa`: `tests/`
- `devops`: `infra/`, `scripts/`
- `debugger`: any area, but limited to minimal fixes with a clear summary
