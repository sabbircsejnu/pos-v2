# PostgreSQL Docker Setup

## Quick Start

Start the PostgreSQL container:
```bash
docker-compose up -d
```

Check container status:
```bash
docker-compose ps
```

View logs:
```bash
docker-compose logs -f postgres
```

Stop the container:
```bash
docker-compose down
```

Stop and remove data:
```bash
docker-compose down -v
```

## Connection Details

- **Host:** localhost
- **Port:** 5432
- **Database:** retailpos_db
- **Username:** postgres
- **Password:** postgres

## Connect via psql

```bash
docker exec -it retailpos-postgres psql -U postgres -d retailpos_db
```

## Backup Database

```bash
docker exec retailpos-postgres pg_dump -U postgres retailpos_db > backup.sql
```

## Restore Database

```bash
docker exec -i retailpos-postgres psql -U postgres retailpos_db < backup.sql
```
