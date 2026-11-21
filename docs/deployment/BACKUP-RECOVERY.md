# Backup & Disaster Recovery Guide

## 1. Backup Strategy (PostgreSQL 17)

- **Continuous WAL Archiving (PITR):** Enable `archive_mode = on` with WAL shipping to encrypted S3-compatible object storage (e.g., via `pgBackRest` or managed cloud PITR).
- **Daily Logical Snapshots:** Custom-format compressed dumps (`pg_dump -Fc`) stored with server-side encryption and immutable retention locks:
  ```bash
  pg_dump -h "$PGHOST" -U nexus_backup -d nexus_prod -Fc -Z 6 \
    -f "nexus_prod_$(date -u +%Y%m%dT%H%M%SZ).dump"
  ```
- **Object Storage (`FileStorage`):** Enable S3 bucket versioning and cross-region replication.

---

## 2. Recovery Verification Drill (Mandatory Monthly Test)

A backup is only valid after a tested restore! Execute on an isolated staging host:

```bash
# 1. Create clean verification database with least-privilege roles
createdb -O nexus_migrator nexus_restore_verify

# 2. Restore snapshot
pg_restore -d nexus_restore_verify --no-owner --role=nexus_migrator nexus_prod_YYYYMMDD.dump

# 3. Re-verify RLS policies and audit immutability trigger
psql -d nexus_restore_verify -c "
  SELECT schemaname, tablename, rowsecurity
  FROM pg_tables
  WHERE schemaname IN ('organization','workflow','subscription','file_storage')
  ORDER BY 1,2;"

# 4. Verify row counts and run API smoke health check against restored DB
```

---

## 3. Target Objectives

- **RPO (Recovery Point Objective):** ≤ 5 minutes (with continuous WAL archiving).
- **RTO (Recovery Time Objective):** ≤ 30 minutes (automated Terraform/Helm redeploy + PITR restore).
- **Audit Retention:** `audit.audit_events` must be retained according to statutory compliance windows (minimum 7 years for financial ecosystems) via time-partitioned cold archival, never ad-hoc `DELETE`.
