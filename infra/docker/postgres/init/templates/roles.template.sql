-- =============================================================================
-- PostgreSQL role provisioning for strict tenant isolation (Phase 1 foundation).
--
-- Rules enforced here (see docs/architecture/architecture-charter.md and
-- .cursor/rules/30-database-security-roles.mdc):
--   * migrator_role performs migrations, DDL, schema changes, and RLS policy
--     creation. It is the schema owner.
--   * app_role is used by the running application. It must NOT own the database,
--     must NOT bypass Row-Level Security, and must NOT hold DDL permissions.
--   * The application must never run as the migration account or as a superuser.
--
-- This script only provisions roles and baseline privileges. No tables exist yet
-- in Phase 1 — RLS policies are created by migrations once tenant-owned tables
-- exist, using migrator_role.
-- =============================================================================

-- psql client-side variable substitution (:'var') does NOT occur inside
-- dollar-quoted ($$...$$) text — dollar-quoting exists precisely to keep its
-- contents literal. Interpolating the passwords directly into a `CREATE ROLE
-- ... PASSWORD :'migrator_password'` line inside the DO block below is
-- therefore a syntax error the server actually raises (confirmed by running
-- this script against a real PostgreSQL 18 instance, not just authoring it).
-- The fix: substitute the passwords into plain (non-dollar-quoted) SQL first,
-- stash them in a transaction-local setting, then read that setting from
-- inside the PL/pgSQL block via current_setting() and build the CREATE ROLE
-- statement dynamically with format(...)/EXECUTE, which also safely quotes
-- the password value (protecting against a password containing a quote).
SELECT set_config('pts.migrator_password', :'migrator_password', false);
SELECT set_config('pts.app_password', :'app_password', false);

DO
$$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'migrator_role') THEN
        EXECUTE format(
            'CREATE ROLE migrator_role WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEROLE CREATEDB',
            current_setting('pts.migrator_password'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_role') THEN
        -- NOBYPASSRLS is the default for new roles, but it is stated explicitly here
        -- because it is the single most important guarantee this script provides.
        EXECUTE format(
            'CREATE ROLE app_role WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEROLE NOCREATEDB NOBYPASSRLS',
            current_setting('pts.app_password'));
    END IF;
END
$$;

-- migrator_role owns the schema and can create/alter objects and RLS policies.
GRANT ALL PRIVILEGES ON DATABASE :"db_name" TO migrator_role;
GRANT ALL PRIVILEGES ON SCHEMA public TO migrator_role;

-- app_role may only connect and use the schema; it receives DML (not DDL) on
-- individual tables once migrations create them. Future migrations should run:
--   GRANT SELECT, INSERT, UPDATE, DELETE ON <table> TO app_role;
-- and enable + FORCE row level security on every tenant-owned table:
--   ALTER TABLE <table> ENABLE ROW LEVEL SECURITY;
--   ALTER TABLE <table> FORCE ROW LEVEL SECURITY;
GRANT CONNECT ON DATABASE :"db_name" TO app_role;
GRANT USAGE ON SCHEMA public TO app_role;

-- Ensure objects migrator_role creates in the future are NOT automatically
-- readable/writable by app_role without an explicit grant per table/migration —
-- this keeps privilege escalation opt-in and reviewable, not implicit.
ALTER DEFAULT PRIVILEGES FOR ROLE migrator_role IN SCHEMA public
    REVOKE ALL ON TABLES FROM PUBLIC;

-- Sanity checks that fail the init script loudly if the security posture regresses.
DO
$$
DECLARE
    app_role_bypasses_rls boolean;
    app_role_is_superuser boolean;
BEGIN
    SELECT rolbypassrls, rolsuper INTO app_role_bypasses_rls, app_role_is_superuser
    FROM pg_roles WHERE rolname = 'app_role';

    IF app_role_bypasses_rls THEN
        RAISE EXCEPTION 'Security invariant violated: app_role must not have BYPASSRLS';
    END IF;

    IF app_role_is_superuser THEN
        RAISE EXCEPTION 'Security invariant violated: app_role must not be SUPERUSER';
    END IF;
END
$$;
