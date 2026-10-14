-- Apply only to the preexisting dedicated loopback smartsql_browser_test fixture.
-- This adds an isolated editing schema; it does not reapply PostgresBrowser.sql.
DO $$ BEGIN
 IF current_database() <> 'smartsql_browser_test' THEN
  RAISE EXCEPTION 'This fixture is only for smartsql_browser_test';
 END IF;
 IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='smartsql_browser_editor') THEN
  CREATE ROLE smartsql_browser_editor LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
END $$;
GRANT CONNECT ON DATABASE smartsql_browser_test TO smartsql_browser_editor;
CREATE SCHEMA IF NOT EXISTS smartsql_edit_test AUTHORIZATION smartsql_browser_editor;
GRANT USAGE ON SCHEMA smartsql_edit_test TO smartsql_browser_reader;
SET ROLE smartsql_browser_editor;
ALTER DEFAULT PRIVILEGES IN SCHEMA smartsql_edit_test GRANT SELECT ON TABLES TO smartsql_browser_reader;
CREATE OR REPLACE FUNCTION smartsql_edit_test.unsafe_check(value text) RETURNS boolean
 LANGUAGE sql VOLATILE AS $$ SELECT true $$;
CREATE OR REPLACE FUNCTION smartsql_edit_test.noop_trigger() RETURNS trigger
 LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;
CREATE OR REPLACE FUNCTION smartsql_edit_test.unsafe_immutable(value text) RETURNS text
 LANGUAGE plpgsql IMMUTABLE AS $$ BEGIN RETURN value; END $$;
CREATE OR REPLACE FUNCTION smartsql_edit_test.unsafe_immutable_check(value text) RETURNS boolean
 LANGUAGE plpgsql IMMUTABLE AS $$ BEGIN RETURN true; END $$;
DO $$ BEGIN
 IF NOT EXISTS (SELECT FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace
                WHERE n.nspname='smartsql_edit_test' AND t.typname='custom_value') THEN
  CREATE TYPE smartsql_edit_test.custom_value AS (value text);
 END IF;
END $$;
CREATE OR REPLACE FUNCTION smartsql_edit_test.custom_text(smartsql_edit_test.custom_value) RETURNS text
 LANGUAGE sql VOLATILE AS $$ SELECT ($1).value $$;
DO $$ BEGIN
 IF NOT EXISTS (SELECT FROM pg_cast WHERE castsource='smartsql_edit_test.custom_value'::regtype AND casttarget='text'::regtype) THEN
  CREATE CAST (smartsql_edit_test.custom_value AS text) WITH FUNCTION smartsql_edit_test.custom_text(smartsql_edit_test.custom_value);
 END IF;
END $$;
RESET ROLE;
