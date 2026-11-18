# pg_dump must match the server major (postgres:18 in ../docker-compose.yml); rclone uploads to R2, curl pings healthchecks.io.
# Debian's rclone 1.60 HEADs each upload with a versionId, which R2 answers 501; the static upstream build does not.
FROM rclone/rclone:1.71.2 AS rclone
FROM postgres:18
COPY --from=rclone /usr/local/bin/rclone /usr/local/bin/rclone
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY db-backup.sh /usr/local/bin/db-backup
# The drill runs a throwaway server, and initdb refuses root.
USER postgres
ENTRYPOINT ["db-backup"]
CMD ["loop"]
