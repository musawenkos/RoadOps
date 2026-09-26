# RoadOps PostgreSQL image
# Build/run via docker-compose.yml at the repository root.
FROM postgres:16-alpine

ENV POSTGRES_DB=roadops \
    POSTGRES_USER=roadops_user \
    POSTGRES_PASSWORD=roadops_dev_password \
    TZ=UTC \
    PGTZ=UTC

# Scripts in this folder run once, the first time the data volume is initialised.
COPY init/ /docker-entrypoint-initdb.d/

EXPOSE 5432

HEALTHCHECK --interval=5s --timeout=5s --retries=10 \
  CMD pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" || exit 1
