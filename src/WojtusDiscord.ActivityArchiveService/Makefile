.PHONY: db-setup-postgres db-clean

db-reset: db-clean db-setup ef-database-update
db-reset-migration: db-clean db-setup ef-migrations-remove
db-restart: db-stop db-setup

db-setup:
	docker run --name postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=activity_archive -d -p 5432:5432 postgres:15.0-bullseye

db-clean:
	docker rm -f postgres

db-stop:
	docker stop postgres

db-start:
	docker start postgres

ef-migration-add:
ef-migrations-add:
	dotnet-ef migrations add $(name)

ef-migration-remove:
ef-migrations-remove:
	dotnet-ef migrations remove

ef-database-update:
	dotnet-ef database update
