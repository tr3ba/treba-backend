START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE product_import_drafts (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SourceProductId" character varying(30) NOT NULL,
        "SourceUrl" character varying(1500) NOT NULL,
        "SourceCategoryKey" character varying(20) NOT NULL,
        "SourceCategoryName" character varying(250) NOT NULL,
        "CategoryId" uuid NOT NULL,
        "Name" character varying(250) NOT NULL,
        "Description" character varying(10000) NOT NULL,
        "BrandName" character varying(200) NOT NULL,
        "ImagesJson" jsonb NOT NULL,
        "CharacteristicsJson" jsonb NOT NULL,
        "SourcePrice" numeric(12,2),
        "ProductId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "SubmittedAt" timestamp with time zone,
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_product_import_drafts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_product_import_drafts_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_product_import_drafts_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE rozetka_category_mappings (
        "Id" uuid NOT NULL,
        "ExternalKey" character varying(20) NOT NULL,
        "CategoryId" uuid NOT NULL,
        CONSTRAINT "PK_rozetka_category_mappings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_rozetka_category_mappings_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE seller_category_grants (
        "Id" uuid NOT NULL,
        "SellerId" uuid NOT NULL,
        "CategoryId" uuid NOT NULL,
        "IncludeDescendants" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_seller_category_grants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_seller_category_grants_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_seller_category_grants_sellers_SellerId" FOREIGN KEY ("SellerId") REFERENCES sellers ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE telegram_bindings (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TelegramUserId" bigint NOT NULL,
        "ChatId" bigint NOT NULL,
        "Username" character varying(100),
        "LinkedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_telegram_bindings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_telegram_bindings_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE telegram_bot_cursors (
        "BotId" bigint NOT NULL,
        "LastUpdateId" bigint NOT NULL,
        CONSTRAINT "PK_telegram_bot_cursors" PRIMARY KEY ("BotId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE telegram_guard_challenges (
        "Id" uuid NOT NULL,
        "BrowserSecretHash" character varying(64) NOT NULL,
        "ApprovalSecretHash" character varying(64) NOT NULL,
        "DisplayCode" integer NOT NULL,
        "DeviceName" character varying(200) NOT NULL,
        "ClientIp" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "ApprovedUserId" uuid,
        "ApprovedTelegramUserId" bigint,
        "ApprovedAt" timestamp with time zone,
        "ConsumedAt" timestamp with time zone,
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_telegram_guard_challenges" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_telegram_guard_challenges_users_ApprovedUserId" FOREIGN KEY ("ApprovedUserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE TABLE telegram_link_challenges (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "TelegramUserId" bigint,
        "ChatId" bigint,
        "Username" character varying(100),
        "TelegramName" character varying(200),
        "ClaimedAt" timestamp with time zone,
        "CompletedAt" timestamp with time zone,
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_telegram_link_challenges" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_telegram_link_challenges_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_product_import_drafts_CategoryId" ON product_import_drafts ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_product_import_drafts_ExpiresAt" ON product_import_drafts ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_product_import_drafts_UserId_SourceProductId" ON product_import_drafts ("UserId", "SourceProductId") WHERE "SubmittedAt" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_rozetka_category_mappings_CategoryId" ON rozetka_category_mappings ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_rozetka_category_mappings_ExternalKey" ON rozetka_category_mappings ("ExternalKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_seller_category_grants_CategoryId" ON seller_category_grants ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_seller_category_grants_SellerId_CategoryId" ON seller_category_grants ("SellerId", "CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_telegram_bindings_TelegramUserId" ON telegram_bindings ("TelegramUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_telegram_bindings_UserId" ON telegram_bindings ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_telegram_guard_challenges_ApprovedUserId" ON telegram_guard_challenges ("ApprovedUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_telegram_guard_challenges_ExpiresAt" ON telegram_guard_challenges ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_telegram_link_challenges_ExpiresAt" ON telegram_link_challenges ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE UNIQUE INDEX "IX_telegram_link_challenges_TokenHash" ON telegram_link_challenges ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    CREATE INDEX "IX_telegram_link_challenges_UserId" ON telegram_link_challenges ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    INSERT INTO seller_category_grants ("Id", "SellerId", "CategoryId", "IncludeDescendants", "CreatedAt")
    SELECT DISTINCT md5(s."Id"::text || p."CategoryId"::text)::uuid, s."Id", p."CategoryId", FALSE, CURRENT_TIMESTAMP
    FROM sellers s
    JOIN stores st ON st."SellerId" = s."Id"
    JOIN products p ON p."StoreId" = st."Id"
    JOIN categories c ON c."Id" = p."CategoryId" AND c."IsActive"
    WHERE s."Status" = 'ACTIVE'
    ON CONFLICT ("SellerId", "CategoryId") DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261018112728_AddTelegramWorkspace') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261018112728_AddTelegramWorkspace', '10.0.11');
    END IF;
END $EF$;
COMMIT;
