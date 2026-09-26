-- Runs once on first container start (empty data volume).
-- Tables are created by EF Core migrations when the API starts.
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS pg_trgm;
