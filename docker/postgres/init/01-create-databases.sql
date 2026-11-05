-- Runs once, when the Postgres data volume is initialised (docker-entrypoint-initdb.d).
-- One server hosts both databases to keep the local stack small:
--   knowledge  - the Knowledge Agent API (EF Core migrations run on API startup)
--   langfuse   - Langfuse (Prisma migrations run on langfuse-web startup)
-- Idempotent (psql \gexec), so it can also be re-run by hand:
--   docker compose exec postgres psql -U postgres -f /docker-entrypoint-initdb.d/01-create-databases.sql

SELECT 'CREATE DATABASE knowledge'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'knowledge')\gexec

SELECT 'CREATE DATABASE langfuse'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'langfuse')\gexec
