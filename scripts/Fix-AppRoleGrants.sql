-- Restore app_role privileges after a pg_dump restore that omitted ACLs.
-- Run as postgres or migrator_role against the pts database.

GRANT CONNECT ON DATABASE pts TO app_role;
GRANT USAGE ON SCHEMA public TO app_role;

DO $$
DECLARE
    tbl text;
BEGIN
    FOR tbl IN
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = 'public'
          AND tablename <> '__EFMigrationsHistory'
    LOOP
        EXECUTE format(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.%I TO app_role',
            tbl);
    END LOOP;
END $$;

-- task_activities: migrations grant INSERT only for app writes via policies;
-- full DML is fine for dev restores (matches other tenant tables).
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.task_activities TO app_role;

GRANT USAGE ON SCHEMA app TO app_role;

DO $$
BEGIN
    IF to_regprocedure('app.uuid_setting(text)') IS NOT NULL THEN
        EXECUTE 'GRANT EXECUTE ON FUNCTION app.uuid_setting(text) TO app_role';
    END IF;

    IF to_regprocedure('app.workspace_counts_for_user(uuid)') IS NOT NULL THEN
        EXECUTE 'GRANT EXECUTE ON FUNCTION app.workspace_counts_for_user(uuid) TO app_role';
    END IF;
END $$;

-- Ensure migrator_role owns public tables (expected by this project).
DO $$
DECLARE
    tbl text;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'migrator_role') THEN
        FOR tbl IN
            SELECT tablename FROM pg_tables WHERE schemaname = 'public'
        LOOP
            EXECUTE format('ALTER TABLE public.%I OWNER TO migrator_role', tbl);
        END LOOP;
    END IF;
END $$;
