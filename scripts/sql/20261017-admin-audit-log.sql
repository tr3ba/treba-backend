-- Run as the database owner in treba_dev. Additive migration: no existing data is changed.
BEGIN;
CREATE TABLE public.admin_audit_logs (
    "Id" uuid NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "ActorId" uuid NOT NULL,
    "ActorName" character varying(320) NOT NULL,
    "ActorRole" character varying(50) NOT NULL,
    "Action" character varying(20) NOT NULL,
    "EntityType" character varying(64) NOT NULL,
    "EntityId" uuid NOT NULL,
    "EntityName" character varying(500) NOT NULL,
    "ChangesJson" jsonb NOT NULL,
    CONSTRAINT "PK_admin_audit_logs" PRIMARY KEY ("Id")
);
CREATE INDEX "IX_admin_audit_logs_OccurredAt" ON public.admin_audit_logs ("OccurredAt" DESC);
CREATE INDEX "IX_admin_audit_logs_ActorId" ON public.admin_audit_logs ("ActorId");
CREATE INDEX "IX_admin_audit_logs_EntityType_EntityId" ON public.admin_audit_logs ("EntityType", "EntityId");
-- The developer account used by the local API needs append/read access only.
GRANT SELECT, INSERT ON public.admin_audit_logs TO treba_sasha;
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261017110000_AddAdminAuditLog', '10.0.11');
COMMIT;
