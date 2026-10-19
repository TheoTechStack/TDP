# Transactions Dispute Portal

Customers view their transactions, dispute the ones that look wrong, and follow each dispute through
to a decision. A disputes agent reviews and resolves them. Every status change is kept in an
append-only history, so the full trail of every dispute (open, decided or withdrawn) is always visible.

**Stack:** .NET 10 Web API (controllers) · EF Core + PostgreSQL 17 · Vue 3 + TypeScript (Vite) · Docker · Terraform on AWS (ECS Fargate, RDS)

**Live demo:** http://dispute-portal-1381412907.eu-west-1.elb.amazonaws.com — sign in with any demo account below (password `Passw0rd!`). Hosted on AWS ECS Fargate + RDS PostgreSQL in eu-west-1, deployed with `scripts/deploy.sh`.

**Architecture diagram:** [`docs/architecture.drawio`](docs/architecture.drawio), drawn from the live deployment's real resource IDs. Page 1 shows how a request flows, page 2 how a change ships. Open it in [diagrams.net](https://app.diagrams.net) or the VS Code draw.io extension.

---

## Run it (one command)

Requires Docker.

```bash
docker compose up --build
```

Open **http://localhost:8080** and sign in with a demo account (password `Passw0rd!`):

| Account | Role | What to try |
|---|---|---|
| `alice@demo.test` | Customer | Has an upheld dispute and one under review: see the history timeline |
| `bongani@demo.test` | Customer | Clean slate: raise a dispute, withdraw it, raise it again |
| `agent@demo.test` | Disputes agent | Open the queue, start a review, record a decision |

The login page has one-click buttons for each account. The database is migrated and seeded on startup.

## Run the tests

```bash
# API: unit tests + integration tests against a real PostgreSQL (Testcontainers; needs Docker)
dotnet test

# Web: component and unit tests, then type-check and build
cd web && npm ci && npm test && npm run build
```

## Develop locally

```bash
docker compose up -d db                       # PostgreSQL on localhost:5433
dotnet run --project src/DisputePortal.Api    # API on :8080 (Development: seeds demo data)
cd web && npm ci && npm run dev               # Vite on :5173, proxies /api to :8080
```

OpenAPI document (Development only): http://localhost:8080/openapi/v1.json

---

## How it works

### Dispute lifecycle

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

### API

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

### Design decisions

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

### Authentication

The API issues its own short-lived JWTs for the seeded demo users so the project runs anywhere with no
external dependencies. In production I would put customers on a managed identity provider (Cognito, or
the bank's existing IdP) and keep only the JWT *validation* in this service; the endpoints and
authorisation policies would not change.

---

## Deploy to AWS

`infra/terraform` creates a VPC across two AZs, an ALB, an ECS Fargate service (2 tasks, CPU
autoscaling 2–6), RDS PostgreSQL 17 (encrypted, private subnets, password managed by RDS in Secrets
Manager), ECR, a generated JWT signing key in Secrets Manager, CloudWatch logs and alarms.

Prerequisites: AWS credentials, Terraform ≥ 1.8, Docker. `af-south-1` must be
[enabled on the account](https://docs.aws.amazon.com/accounts/latest/reference/manage-acct-regions.html);
otherwise set `AWS_REGION`.

```bash
git commit -am "..."          # the image is tagged with the commit SHA
./scripts/deploy.sh           # build → push to ECR → terraform apply → wait for a stable service
```

The script prints the URL. The first deploy takes ~15 minutes (mostly RDS). To serve HTTPS, pass an
ACM certificate: `-var certificate_arn=...`. Tear down with
`terraform -chdir=infra/terraform destroy -var image_tag=x`.

On a new AWS free-plan account, RDS only allows 1 day of backups: run
`TF_VAR_db_backup_retention_days=1 ./scripts/deploy.sh`.

Cost-conscious defaults for a demo: tasks run in public subnets with no NAT gateway (inbound only from
the ALB), single-AZ database. For production set `db_multi_az=true` and `deletion_protection=true`,
and move tasks to private subnets behind NAT or VPC endpoints.

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
infra/terraform/             AWS infrastructure
docs/architecture.drawio     Architecture diagram
Dockerfile                   Multi-stage: Node build → .NET publish → chiseled runtime
```

## What I'd do next

- Evidence uploads (pre-signed S3 URLs) and email notifications on status changes (transactional outbox
  from `dispute_events` → SQS → SES). Both are on the diagram's roadmap.
- Managed identity (Cognito / IdP) and MFA for agents.
- Agent assignment and SLA timers on the queue; idempotency keys on `POST` for flaky mobile networks.
- CD from GitHub Actions using OIDC into an AWS deploy role, with remote Terraform state.
- Playwright end-to-end smoke test run against each deployment.
