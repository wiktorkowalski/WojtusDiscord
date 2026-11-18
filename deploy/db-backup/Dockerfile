# pg_dump must match the server major (postgres:18 in ../docker-compose.yml); rclone uploads to R2, curl pings healthchecks.io.
FROM postgres:18
RUN apt-get update \
 && apt-get install -y --no-install-recommends rclone curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY db-backup.sh /usr/local/bin/db-backup
# The drill runs a throwaway server, and initdb refuses root.
USER postgres
ENTRYPOINT ["db-backup"]
CMD ["loop"]
