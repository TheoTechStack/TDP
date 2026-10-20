# Transactions Dispute Portal

Customers view their transactions and dispute the ones that look wrong; a disputes agent reviews and
resolves them. Every dispute keeps a full, append-only history, so past disputes stay visible.

## **Live demo:** http://dispute-portal-1381412907.eu-west-1.elb.amazonaws.com. 

Click a demo account on the
sign-in page, then **Sign in** which will populate creds stored safely.

| Account | Role | Try this |
|---|---|---|
| Alice Mokoena | Customer | Open **My disputes** to see an upheld dispute and one under review, with their history |
| Bongani Dlamini | Customer | Raise a dispute on a transaction, withdraw it, raise it again |
| Thandi Nkosi | Disputes agent | Open the queue, start a review, record a decision |

## TL;DR

- **Stack:** .NET 10 Web API · EF Core + PostgreSQL 17 · Vue 3 + TypeScript · Docker · Terraform on AWS (ECS Fargate, RDS).
- **Domain rules live in one place:** one open dispute per transaction, a 120-day window, decided disputes are final.
- **Audit trail can't be rewritten:** every status change is a row in `dispute_events`;
- **Safe under concurrency:** optimistic locking using unique index; conflicts.

##

**Architecture:** [`docs/architecture.drawio`](docs/architecture.drawio). 
<img width="3060" height="2080" alt="architecture-1 · Request flow drawio" src="https://github.com/user-attachments/assets/83b08016-1406-4b09-a1c0-62e578039343" />


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
- **Shipped by CI/CD:** every push to `main` runs the tests and a secret scan, builds one image, and deploys to AWS via GitHub OIDC (no AWS keys stored in GitHub).

<img width="1135" height="293" alt="image" src="https://github.com/user-attachments/assets/c70ef0e9-27ba-4ba3-882b-9ee43cfb78ad" />


## More detail

| Doc | What's in it |
|---|---|
| [docs/design.md](docs/design.md) | Dispute lifecycle, API endpoints, design decisions, project layout |
| [docs/deployment.md](docs/deployment.md) | AWS setup, CI/CD, secrets and configuration, local development |
| [docs/deployment-runbook.pdf](docs/deployment-runbook.pdf) | Step-by-step pipeline and Terraform setup, including every issue hit and its fix |
