START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    ALTER TABLE promotions ADD "Version" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE TABLE product_review_baselines (
        "ProductId" uuid NOT NULL,
        "CapturedAt" timestamp with time zone NOT NULL,
        "Snapshot" jsonb NOT NULL,
        CONSTRAINT "PK_product_review_baselines" PRIMARY KEY ("ProductId"),
        CONSTRAINT "FK_product_review_baselines_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES products ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE TABLE support_tickets (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "Kind" character varying(20) NOT NULL,
        "Subject" character varying(200) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_support_tickets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_support_tickets_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES orders ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_support_tickets_users_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE TABLE support_messages (
        "Id" uuid NOT NULL,
        "TicketId" uuid NOT NULL,
        "AuthorId" uuid NOT NULL,
        "AuthorName" character varying(320) NOT NULL,
        "AuthorRole" character varying(50) NOT NULL,
        "Message" character varying(4000) NOT NULL,
        "Internal" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_support_messages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_support_messages_support_tickets_TicketId" FOREIGN KEY ("TicketId") REFERENCES support_tickets ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE INDEX "IX_support_messages_TicketId_CreatedAt" ON support_messages ("TicketId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE INDEX "IX_support_tickets_CustomerId" ON support_tickets ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE INDEX "IX_support_tickets_OrderId" ON support_tickets ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE UNIQUE INDEX "IX_support_tickets_OrderId_Kind" ON support_tickets ("OrderId", "Kind") WHERE "Kind" = 'RETURN' AND "Status" IN ('OPEN', 'IN_PROGRESS', 'APPROVED');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    CREATE INDEX "IX_support_tickets_Status_UpdatedAt" ON support_tickets ("Status", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    DO $operations_permissions$
    DECLARE recipient record;
    BEGIN
        FOR recipient IN
            SELECT r.rolname, targets.target_table,
                string_agg(DISTINCT a.privilege_type, ', ') AS privileges
            FROM (VALUES ('orders', 'support_tickets'), ('orders', 'support_messages'),
                         ('products', 'product_review_baselines')) AS targets(source_table, target_table)
            JOIN pg_class c ON c.oid = to_regclass(targets.source_table)
            CROSS JOIN LATERAL aclexplode(COALESCE(c.relacl, acldefault('r', c.relowner))) a
            JOIN pg_roles r ON r.oid = a.grantee
            WHERE a.privilege_type IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE')
            GROUP BY r.rolname, targets.target_table
        LOOP
            EXECUTE format('GRANT %s ON TABLE %I TO %I', recipient.privileges, recipient.target_table, recipient.rolname);
        END LOOP;
    END
    $operations_permissions$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020093201_AddAdminOperations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020093201_AddAdminOperations', '10.0.11');
    END IF;
END $EF$;
COMMIT;
