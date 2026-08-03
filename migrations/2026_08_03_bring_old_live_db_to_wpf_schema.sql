-- ============================================================================
-- Brings the live database behind posapi-v2 (only migration 2026_07_05_000001
-- applied) up to the schema the WPF POS + current API expect.
--
-- Server is MariaDB 11.8 (per the dump), so ADD COLUMN IF NOT EXISTS is
-- supported — every statement below is idempotent and SAFE TO RE-RUN:
--   * MODIFY to longtext is a no-op if already longtext (and a lossless widen
--     from varchar/text otherwise);
--   * ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS / INSERT IGNORE
--     skip cleanly when already present.
--
-- Only structure changes — no row is touched or deleted. Take a backup first
-- anyway (phpMyAdmin → Export) before running on the live DB.
-- ============================================================================

-- 1. categories.image : varchar(255) -> longtext (long / base64 image data)
ALTER TABLE `categories`
  MODIFY `image` LONGTEXT NULL;

-- 2. menu_items.image : text -> longtext
ALTER TABLE `menu_items`
  MODIFY `image` LONGTEXT NULL;

-- 3. customer_ledger_entries : new payment_mode column (Cash / UPI / …)
ALTER TABLE `customer_ledger_entries`
  ADD COLUMN IF NOT EXISTS `payment_mode` VARCHAR(20) NULL AFTER `note`;

-- 4. number_sequences : new table backing per-prefix bill numbering
CREATE TABLE IF NOT EXISTS `number_sequences` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `prefix` varchar(20) NOT NULL,
  `last_number` int(11) NOT NULL DEFAULT 0,
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_number_sequences_prefix` (`prefix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- Baseline: adopt this existing database into the auto-migration runner.
--
-- Records EVERY migration the project has shipped so far as already-applied, so
-- the runner never re-runs them here — critically the create-all-tables and the
-- seed migration, which would otherwise recreate/overwrite this live data. The
-- __baseline__ marker arms the auto-runner; from here on only migrations added
-- AFTER today apply automatically on deploy. This writes rows only — it runs no
-- data SQL and cannot lose anything.
-- ----------------------------------------------------------------------------
INSERT IGNORE INTO `schema_migrations` (`migration`, `applied_at`) VALUES
  ('2026_04_29_add_customer_fields_to_orders.sql', NOW()),
  ('2026_04_29_add_discount_fields_to_orders.sql', NOW()),
  ('2026_04_29_create_customer_ledger_entries.sql', NOW()),
  ('2026_04_29_create_customers_and_link_orders.sql', NOW()),
  ('2026_05_03_add_client_scoping.sql', NOW()),
  ('2026_05_30_add_image_to_categories.sql', NOW()),
  ('2026_05_30_alter_images_to_longtext.sql', NOW()),
  ('2026_05_30_convert_menu_items_image_to_text.sql', NOW()),
  ('2026_07_05_000001_create_all_tables.sql', NOW()),
  ('2026_07_05_000002_seed_master_data.sql', NOW()),
  ('2026_07_05_000003_remove_year_from_number_sequences.sql', NOW()),
  ('2026_08_03_bring_old_live_db_to_wpf_schema.sql', NOW()),
  ('__baseline__', NOW());
