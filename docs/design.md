# Design

How the portal works and why it is built this way. The [README](../README.md) has the summary.

## Dispute lifecycle

```
Submitted ──► UnderReview ──► Upheld
    │              │      └─► Rejected
    └──────────────┴────────► Withdrawn (by the customer)
```

- **One open dispute per transaction.** Checked in the domain *and* enforced by a partial unique index
  (`WHERE status IN ('Submitted','UnderReview')`), so two simultaneous requests cannot both succeed.
- **Decided disputes are final.** After a withdrawal the customer may dispute again.
- **120-day dispute window** from the transaction date.
- **Reason "Other" needs a description; a decision needs a note** for the customer.
- **History is append-only.** Each transition writes a `dispute_events` row (who, when, from → to, note).
  A database trigger rejects `UPDATE`/`DELETE` on that table, so the audit trail cannot be rewritten,
  even by a bug.

## API

All endpoints return [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) on error, with a
stable `code` (e.g. `already_disputed`, `dispute_window_closed`, `invalid_status_transition`) that the UI
uses instead of parsing messages.

| Method | Path | Who | Notes |
|---|---|---|---|
| `POST` | `/api/auth/login` | anyone | Rate-limited per IP. Returns a 60-minute JWT. |
| `GET` | `/api/auth/me` | signed in | |
| `GET` | `/api/transactions?page&pageSize&search` | customer | Own transactions only, each with latest dispute + `canDispute`. |
| `GET` | `/api/transactions/{id}` | customer | 404 for other customers' ids (doesn't leak existence). |
| `POST` | `/api/transactions/{id}/disputes` | customer | `{ reason, description }` → 201. 409 if already disputed, 422 if outside window. |
| `GET` | `/api/disputes?page&pageSize&status` | customer, agent | Customers see their own; agents see all. |
| `GET` | `/api/disputes/{id}` | customer, agent | Detail, full history and `allowedActions` for the caller. |
| `POST` | `/api/disputes/{id}/withdraw` | customer | |
| `POST` | `/api/disputes/{id}/review` | agent | |
| `POST` | `/api/disputes/{id}/resolve` | agent | `{ outcome: Upheld \| Rejected, note }` |
| `GET` | `/health/live`, `/health/ready` | anyone | Liveness (ALB) and readiness (checks the database). |

## Design decisions

- **Vertical slices.** Each feature (`Auth`, `Transactions`, `Disputes`) owns its controller, service and
  contracts, so a change to disputes touches one folder. Controllers are thin: they bind the request,
  apply the authorisation policy and turn the service result into a status code. Services hold the use
  cases and call the domain. The rules themselves live in `Domain/`. I'd split this into separate
  projects (Domain / Application / Infrastructure) once there are several bounded contexts; for one
  aggregate that would only add interfaces and mapping code.
- **Repositories per aggregate, unit of work for commits.** Services depend on `IDisputeRepository`,
  `ITransactionRepository`, `IUserRepository` and `IUnitOfWork` (interfaces in `Domain/Repositories`),
  never on EF Core. Repositories are specific to each aggregate, not a generic `Repository<T>`, so each
  one exposes only the queries the use cases need. They never save on their own: the service calls
  `IUnitOfWork.SaveChangesAsync` once its work is complete, so one use case is one transaction. This
  keeps the services unit-testable with Moq (`DisputeServiceTests`) while the integration tests cover
  the real EF Core queries against PostgreSQL.
- **One container, two apps.** The Vue build is copied into the API's `wwwroot`, so UI and API share an
  origin: no CORS, one image, one deployment, one thing to scale. Splitting them later (S3 + CloudFront
  for the SPA) is a deployment change, not a code change.
- **Workflow rules live in the domain, once.** `Dispute` owns its state machine; `DisputePolicy` decides
  whether a transaction can be disputed. The API returns `allowedActions` and `canDispute`, so the UI
  renders buttons from server rules instead of duplicating them.
- **Correct under concurrency.** Optimistic concurrency on disputes via PostgreSQL `xmin` (lost updates
  become a 409, and the UI reloads), plus the partial unique index above.
- **Schema as versioned SQL.** Migrations are plain, reviewable SQL files embedded in the assembly and
  applied at startup under an advisory lock, so several tasks starting at once cannot race. EF Core is
  used for data access only.
- **Authorisation by scoping, not by checking.** Every customer query is filtered by the caller's id;
  another customer's resource is simply "not found".
- **Security basics on by default.** PBKDF2 password hashing, strict CSP and security headers, login rate
  limiting, chiseled non-root runtime image, secrets only from Secrets Manager in AWS, database in
  private subnets reachable only from the app.
- **Operable.** Structured JSON logs with a correlation id per request (`X-Correlation-Id`), health
  checks, CloudWatch alarms on 5xx and unhealthy targets, ECS deployment circuit breaker with rollback.

## Authentication

The API issues its own short-lived JWTs for the seeded demo users so the project runs anywhere with no
external dependencies. In production I would put customers on a managed identity provider (Cognito, or
the bank's existing IdP) and keep only the JWT *validation* in this service; the endpoints and
authorisation policies would not change.

## Project layout

```
src/DisputePortal.Api/
  Features/                  One folder per feature (vertical slice)
    Disputes/                DisputesController · DisputeService · DisputeContracts
    Transactions/            TransactionsController · TransactionService · TransactionContracts
    Auth/                    AuthController · AuthService · AuthContracts
  Domain/                    Dispute aggregate + state machine, DisputePolicy, entities
    Repositories/            IDisputeRepository, ITransactionRepository, IUserRepository, IUnitOfWork
  Infrastructure/
    Persistence/             DbContext (the unit of work), SQL migrations + migrator, demo seeder
      Repositories/          EF Core implementations of the repository interfaces
    Security/                JWT issuing, password hashing
    Web/                     Problem-details mapping, correlation id, security headers, health
tests/DisputePortal.Api.Tests/
  Domain/                    Fast unit tests for the workflow rules
  Features/                  Service unit tests with mocked repositories (Moq)
  Integration/               Real HTTP + real PostgreSQL (Testcontainers)
web/                         Vue 3 + TypeScript SPA (Vite, Vue Router, Pinia, Vitest)
infra/bootstrap/             One-time: Terraform state bucket + GitHub OIDC deploy role
infra/terraform/             AWS infrastructure (state in S3)
docs/architecture.drawio     Architecture diagram
docs/deployment-runbook.pdf  CI/CD + Terraform setup runbook
Dockerfile                   Multi-stage: Node build → .NET publish → chiseled runtime
```

## What I'd do next

- Evidence uploads (pre-signed S3 URLs) and email notifications on status changes (transactional outbox
  from `dispute_events` → SQS → SES). Both are on the diagram's roadmap.
- Managed identity (Cognito / IdP) and MFA for agents.
- Agent assignment and SLA timers on the queue; idempotency keys on `POST` for flaky mobile networks.
- Least-privilege policy for the deploy role instead of AdministratorAccess.
- Playwright end-to-end smoke test run against each deployment.
