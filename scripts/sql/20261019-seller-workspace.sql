SET search_path TO public;
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE TABLE product_identities (
        "ProductId" uuid NOT NULL,
        "ManufacturerPartNumber" character varying(100) NOT NULL,
        "NormalizedPartNumber" character varying(100) NOT NULL,
        CONSTRAINT "PK_product_identities" PRIMARY KEY ("ProductId"),
        CONSTRAINT "FK_product_identities_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES products ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE TABLE seller_import_jobs (
        "Id" uuid NOT NULL,
        "BatchId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SourceUrl" character varying(1500) NOT NULL,
        "CategoryId" uuid,
        "Status" character varying(20) NOT NULL,
        "Error" character varying(2000),
        "PreviewId" uuid,
        "Attempts" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "LeaseUntil" timestamp with time zone,
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_seller_import_jobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_seller_import_jobs_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_seller_import_jobs_product_import_drafts_PreviewId" FOREIGN KEY ("PreviewId") REFERENCES product_import_drafts ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_seller_import_jobs_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE TABLE telegram_notifications (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ExpectedRole" character varying(40) NOT NULL,
        "ExpectedStatus" character varying(40) NOT NULL,
        "Text" character varying(3500) NOT NULL,
        "View" character varying(20) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "AvailableAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "LeaseUntil" timestamp with time zone,
        "Attempts" integer NOT NULL,
        "Result" character varying(100),
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_telegram_notifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_telegram_notifications_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES products ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_telegram_notifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_product_identities_NormalizedPartNumber" ON product_identities ("NormalizedPartNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_seller_import_jobs_CategoryId" ON seller_import_jobs ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_seller_import_jobs_PreviewId" ON seller_import_jobs ("PreviewId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_seller_import_jobs_Status_LeaseUntil" ON seller_import_jobs ("Status", "LeaseUntil");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_seller_import_jobs_UserId_CreatedAt" ON seller_import_jobs ("UserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_telegram_notifications_CompletedAt_AvailableAt" ON telegram_notifications ("CompletedAt", "AvailableAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_telegram_notifications_ProductId" ON telegram_notifications ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    CREATE INDEX "IX_telegram_notifications_UserId" ON telegram_notifications ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019165544_AddSellerWorkspaceTools') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261019165544_AddSellerWorkspaceTools', '10.0.11');
    END IF;
END $EF$;
COMMIT;


-- Give the same application roles the same data privileges as the existing import table.
-- Only the three tables introduced by this migration are affected; PUBLIC is excluded.
BEGIN;
DO $workspace_permissions$
DECLARE
    recipient record;
    workspace_table text;
BEGIN
    FOR recipient IN
        SELECT r.rolname, string_agg(DISTINCT a.privilege_type, ', ') AS privileges
        FROM pg_class c
        CROSS JOIN LATERAL aclexplode(COALESCE(c.relacl, acldefault('r', c.relowner))) a
        JOIN pg_roles r ON r.oid = a.grantee
        WHERE c.oid = 'public.product_import_drafts'::regclass
          AND a.privilege_type IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE')
        GROUP BY r.rolname
    LOOP
        FOREACH workspace_table IN ARRAY ARRAY['product_identities','seller_import_jobs','telegram_notifications']
        LOOP
            EXECUTE format('GRANT %s ON TABLE public.%I TO %I', recipient.privileges, workspace_table, recipient.rolname);
        END LOOP;
    END LOOP;
END
$workspace_permissions$;
COMMIT;
