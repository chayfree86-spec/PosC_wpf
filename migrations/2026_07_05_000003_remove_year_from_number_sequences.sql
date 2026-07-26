SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `number_sequences` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `prefix` varchar(20) NOT NULL,
  `last_number` int(11) NOT NULL DEFAULT 0,
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_number_sequences_prefix` (`prefix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TEMPORARY TABLE `tmp_number_sequences_compacted` AS
SELECT
  MIN(`id`) AS `keep_id`,
  `prefix`,
  MAX(`last_number`) AS `last_number`,
  MAX(`updated_at`) AS `updated_at`
FROM `number_sequences`
GROUP BY `prefix`;

UPDATE `number_sequences` ns
JOIN `tmp_number_sequences_compacted` c ON c.`keep_id` = ns.`id`
SET
  ns.`last_number` = c.`last_number`,
  ns.`updated_at` = c.`updated_at`;

DELETE ns
FROM `number_sequences` ns
LEFT JOIN `tmp_number_sequences_compacted` c ON c.`keep_id` = ns.`id`
WHERE c.`keep_id` IS NULL;

DROP TEMPORARY TABLE `tmp_number_sequences_compacted`;

ALTER TABLE `number_sequences` DROP COLUMN IF EXISTS `year`;
ALTER TABLE `number_sequences` ADD UNIQUE KEY IF NOT EXISTS `uq_number_sequences_prefix` (`prefix`);
