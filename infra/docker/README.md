# Local Infrastructure (Docker Compose)

Phase 1 provides only a PostgreSQL container, provisioned with the two-role
security model mandated by the architecture charter.

## Usage

```bash
cd infra/docker
cp .env.example .env   # then edit the passwords
docker compose up -d
```

This will:

1. Start PostgreSQL 17.
2. On first initialization only, run `postgres/init/01-create-roles.sh`, which
   creates two roles:
   - `migrator_role` — schema owner; used by `dotnet ef database update` and
     future migration/DDL tooling. Never used by the running application.
   - `app_role` — used by the running ASP.NET Core application. No DDL rights,
     no `BYPASSRLS`, not the database owner.

## Why no `app` or `web` service yet

Phase 1 explicitly excludes application code (auth, tenants, projects, tasks,
billing, etc. — see `docs/architecture/architecture-charter.md`). Containerizing
the backend/frontend before there is real application behavior to run would add
Docker-build maintenance for no benefit. `dotnet run` / `npm run dev` against this
containerized database is the intended Phase 1 workflow. Application containers
are expected to be introduced in a later phase; see
`docs/architecture/decisions/0001-modular-monolith-over-microservices.md` for how
this project decides when to add infrastructure.

## Security notes

- Never point the running application at the `postgres` superuser or
  `migrator_role` credentials — only `app_role`.
- `.env` is git-ignored; only commit `.env.example` with placeholder values.
