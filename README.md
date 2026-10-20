# Transactions Dispute Portal

Customers view their transactions and dispute the ones that look wrong; a disputes agent reviews and
resolves them. Every dispute keeps a full, append-only history, so past disputes stay visible.

**Live demo:** http://dispute-portal-1381412907.eu-west-1.elb.amazonaws.com. Click a demo account on the
sign-in page, then **Sign in**.

| Account | Role | Try this |
|---|---|---|
| Alice Mokoena | Customer | Open **My disputes** to see an upheld dispute and one under review, with their history |
| Bongani Dlamini | Customer | Raise a dispute on a transaction, withdraw it, raise it again |
| Thandi Nkosi | Disputes agent | Open the queue, start a review, record a decision |

## TL;DR

- **Stack:** .NET 10 Web API · EF Core + PostgreSQL 17 · Vue 3 + TypeScript · Docker · Terraform on AWS (ECS Fargate, RDS).
- **Domain rules live in one place:** a dispute state machine, one open dispute per transaction, a 120-day window, decided disputes are final.
- **Audit trail can't be rewritten:** every status change is a row in `dispute_events`; a database trigger blocks updates and deletes.
- **Safe under concurrency:** optimistic locking (`xmin`) plus a partial unique index; conflicts return 409 and the UI reloads.
- **Secure by default:** customers only ever see their own data (404 otherwise), PBKDF2 passwords, short-lived JWTs, login rate limiting, strict CSP, secrets only in AWS Secrets Manager.
- **Shipped by CI/CD:** every push to `main` runs the tests and a secret scan, builds one image, and deploys to AWS via GitHub OIDC (no AWS keys stored in GitHub).

**Architecture:** [`docs/architecture.drawio`](docs/architecture.drawio). Open it in [diagrams.net](https://app.diagrams.net); page 1 is the request flow, page 2 the deploy flow.

## Run it

Requires Docker.

```bash
cp .env.example .env        # set DEMO_PASSWORD (12+ characters); .env is git-ignored
docker compose up --build
```

Open http://localhost:8080 and click a demo account. The database is migrated and seeded on startup.

## Test it

```bash
dotnet test                                  # unit + integration tests (real PostgreSQL via Testcontainers)
cd web && npm ci && npm test && npm run build
```

## Known limitations

- HTTP only: HTTPS needs a domain and an ACM certificate (the Terraform already accepts `certificate_arn`).
- Single-AZ database with 1-day backups (free-plan account); production would use Multi-AZ and longer retention.
- The deploy role has `AdministratorAccess`; its trust policy is narrow, and least privilege is the next step.
- Demo accounts and one-click sign-in are demo-only settings (`Seed:Enabled`, `Seed:RevealDemoPassword`), off by default.

## More detail

| Doc | What's in it |
|---|---|
| [docs/design.md](docs/design.md) | Dispute lifecycle, API endpoints, design decisions, project layout |
| [docs/deployment.md](docs/deployment.md) | AWS setup, CI/CD, secrets and configuration, local development |
| [docs/deployment-runbook.pdf](docs/deployment-runbook.pdf) | Step-by-step pipeline and Terraform setup, including every issue hit and its fix |
