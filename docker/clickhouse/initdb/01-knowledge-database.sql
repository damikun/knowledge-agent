-- Runs once, when the ClickHouse data volume is initialised (docker-entrypoint-initdb.d).
-- One server is shared by:
--   knowledge  - the Knowledge Agent vector store (table knowledge.chunks, created by the API on startup)
--   default    - Langfuse (tables created by the langfuse-web migrations)
CREATE DATABASE IF NOT EXISTS knowledge;
