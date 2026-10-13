CREATE TYPE public.browser_state AS ENUM ('new', 'done');
CREATE TABLE public.browser_rows (
 tenant_id integer NOT NULL,
 id integer NOT NULL,
 amount numeric(60,20),
 note text,
 payload bytea,
 document jsonb,
 numbers integer[],
 state public.browser_state,
 occurred_at timestamp with time zone,
 PRIMARY KEY (tenant_id,id)
);
INSERT INTO public.browser_rows
SELECT i%3, i, 1234567890123456789012345678901234567890.12345678901234567890::numeric(60,20),
 CASE WHEN i=1 THEN NULL WHEN i=2 THEN '' ELSE 'Тестовая строка '||i END,
 decode('00ff','hex'), '{"value":true}'::jsonb, ARRAY[1,2,3], 'new'::public.browser_state,
 '2026-10-12 10:15:00+04'::timestamptz FROM generate_series(1,205) AS i;
CREATE TABLE public.browser_no_key (note text, amount numeric);
INSERT INTO public.browser_no_key VALUES ('same',1),('same',1),(NULL,NULL);
CREATE VIEW public.browser_readonly_probe AS SELECT current_setting('transaction_read_only') AS read_only, current_setting('TimeZone') AS timezone;
CREATE SCHEMA "Кавычки "" схема";
CREATE TABLE "Кавычки "" схема"."таблица "" ; DROP TABLE x; --" ("ключ "" ." integer PRIMARY KEY, "значение" text);
INSERT INTO "Кавычки "" схема"."таблица "" ; DROP TABLE x; --" VALUES (1,'Проверка Unicode 🌍');
CREATE TABLE public.browser_hidden (secret text);
GRANT CONNECT ON DATABASE smartsql_browser_test TO smartsql_browser_reader;
GRANT USAGE ON SCHEMA public,"Кавычки "" схема" TO smartsql_browser_reader;
GRANT SELECT ON public.browser_rows,public.browser_no_key,public.browser_readonly_probe TO smartsql_browser_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA "Кавычки "" схема" TO smartsql_browser_reader;

CREATE TABLE public.browser_large_value (note text);
INSERT INTO public.browser_large_value VALUES (repeat('x',1048577));
GRANT SELECT ON public.browser_large_value TO smartsql_browser_reader;
