-- ============================================================================
-- Brings the OLD live database (u748421121_pos_chaupal — only migration
-- 2026_07_05_000001 applied) up to the schema the WPF POS + current API expect.
--
-- Compared column-by-column against the current schema; these are the ONLY four
-- structural differences. Catalog data (menu_items, categories, gallery_images)
-- already matches, so nothing here touches data — it only adds the missing
-- column, table, and widens two image columns.
--
-- Safe to run once on the live DB via phpMyAdmin. Re-running would error on the
-- ADD COLUMN / MODIFY (already applied) — that's expected, run it a single time.
-- ============================================================================

-- 1. dining_areas.image : varchar(255) -> longtext (base64 / long image data)
ALTER TABLE `dining_areas`
  MODIFY `image` LONGTEXT NULL;

-- 2. menu_items.image : text -> longtext
ALTER TABLE `menu_items`
  MODIFY `image` LONGTEXT NULL;

-- 3. customer_ledger_entries : new payment_mode column (Cash / UPI / …)
ALTER TABLE `customer_ledger_entries`
  ADD COLUMN `payment_mode` VARCHAR(20) NULL AFTER `note`;

-- 4. number_sequences : new table backing per-prefix bill numbering
CREATE TABLE IF NOT EXISTS `number_sequences` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `prefix` varchar(20) NOT NULL,
  `last_number` int(11) NOT NULL DEFAULT 0,
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_number_sequences_prefix` (`prefix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Record that this DB is now in step with these migrations, so any future
-- migration runner doesn't try to re-apply them.
INSERT IGNORE INTO `schema_migrations` (`migration`, `applied_at`) VALUES
  ('2026_07_05_000002_seed_master_data.sql', NOW()),
  ('2026_07_05_000003_remove_year_from_number_sequences.sql', NOW()),
  ('2026_08_03_bring_old_live_db_to_wpf_schema.sql', NOW());
