START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020144055_AddWebsiteAdminLogin') THEN
    ALTER TABLE telegram_guard_challenges ADD "LoginChallengeId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020144055_AddWebsiteAdminLogin') THEN
    CREATE INDEX "IX_telegram_guard_challenges_LoginChallengeId" ON telegram_guard_challenges ("LoginChallengeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020144055_AddWebsiteAdminLogin') THEN
    ALTER TABLE telegram_guard_challenges ADD CONSTRAINT "FK_telegram_guard_challenges_authentication_challenges_LoginCh~" FOREIGN KEY ("LoginChallengeId") REFERENCES authentication_challenges ("Id") ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020144055_AddWebsiteAdminLogin') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020144055_AddWebsiteAdminLogin', '10.0.11');
    END IF;
END $EF$;
COMMIT;
