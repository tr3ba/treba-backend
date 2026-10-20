START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    DROP INDEX "IX_support_tickets_CustomerId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    ALTER TABLE support_tickets ALTER COLUMN "OrderId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    ALTER TABLE support_tickets ADD "CustomerReadAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    ALTER TABLE support_tickets ADD "ProductId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    ALTER TABLE support_tickets ADD "StaffReadAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    CREATE UNIQUE INDEX "IX_support_tickets_CustomerId" ON support_tickets ("CustomerId") WHERE "Kind" = 'CHAT' AND "Status" <> 'CLOSED';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    CREATE UNIQUE INDEX "IX_support_tickets_CustomerId_ProductId" ON support_tickets ("CustomerId", "ProductId") WHERE "Kind" = 'QUESTION' AND "ProductId" IS NOT NULL AND "Status" <> 'CLOSED';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    CREATE INDEX "IX_support_tickets_CustomerId_UpdatedAt" ON support_tickets ("CustomerId", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    CREATE INDEX "IX_support_tickets_ProductId" ON support_tickets ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    ALTER TABLE support_tickets ADD CONSTRAINT "FK_support_tickets_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES products ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020104554_AddSupportConversations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020104554_AddSupportConversations', '10.0.11');
    END IF;
END $EF$;
COMMIT;
