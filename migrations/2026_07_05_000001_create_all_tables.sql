SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE IF NOT EXISTS `clients` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `name` varchar(150) NOT NULL,
  `slug` varchar(80) NOT NULL,
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `slug` (`slug`),
  UNIQUE KEY `uuid` (`uuid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `dining_areas` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `name` varchar(100) NOT NULL,
  `sort_order` int(11) DEFAULT 0,
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `categories` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `name` varchar(100) NOT NULL,
  `image` longtext DEFAULT NULL,
  `parent_id` int(11) DEFAULT NULL,
  `sort_order` int(11) DEFAULT 0,
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  KEY `idx_category_parent` (`parent_id`),
  CONSTRAINT `fk_categories_parent` FOREIGN KEY (`parent_id`) REFERENCES `categories` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `gst_rates` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `name` varchar(100) NOT NULL,
  `rate_percent` decimal(5,2) NOT NULL DEFAULT 0.00,
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `users` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `client_id` int(11) NOT NULL DEFAULT 1,
  `name` varchar(100) NOT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `password` varchar(255) DEFAULT NULL,
  `pin` varchar(255) DEFAULT NULL,
  `role` enum('admin','manager','waiter','cashier') DEFAULT 'waiter',
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  KEY `idx_users_client_email` (`client_id`,`email`),
  KEY `idx_users_client_phone` (`client_id`,`phone`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `customers` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `client_id` int(11) NOT NULL DEFAULT 1,
  `name` varchar(150) DEFAULT NULL,
  `mobile` varchar(20) DEFAULT NULL,
  `normalized_mobile` varchar(20) DEFAULT NULL,
  `email` varchar(150) DEFAULT NULL,
  `address` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  UNIQUE KEY `uq_customers_client_normalized_mobile` (`client_id`,`normalized_mobile`),
  KEY `idx_customers_client_mobile` (`client_id`,`normalized_mobile`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `restaurant_tables` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `table_number` varchar(20) NOT NULL,
  `area_id` int(11) DEFAULT NULL,
  `qr_code` text DEFAULT NULL,
  `qr_token` varchar(100) DEFAULT NULL,
  `table_status` enum('available','occupied','ordered','complete','inactive') DEFAULT 'available',
  `current_amount` decimal(10,2) DEFAULT 0.00,
  `order_timestamp` bigint(20) DEFAULT NULL,
  `is_active` tinyint(1) DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  UNIQUE KEY `qr_token` (`qr_token`),
  KEY `idx_restaurant_tables_area` (`area_id`),
  CONSTRAINT `fk_restaurant_tables_area` FOREIGN KEY (`area_id`) REFERENCES `dining_areas` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `menu_items` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `name` varchar(150) NOT NULL,
  `category_id` int(11) NOT NULL,
  `sub_category_id` int(11) DEFAULT NULL,
  `price` decimal(10,2) NOT NULL,
  `image` longtext DEFAULT NULL,
  `is_veg` tinyint(1) DEFAULT 1,
  `is_available` tinyint(1) DEFAULT 1,
  `description` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  KEY `idx_menu_category` (`category_id`),
  KEY `idx_menu_sub_category` (`sub_category_id`),
  CONSTRAINT `fk_menu_items_category` FOREIGN KEY (`category_id`) REFERENCES `categories` (`id`),
  CONSTRAINT `fk_menu_items_sub_category` FOREIGN KEY (`sub_category_id`) REFERENCES `categories` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `orders` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `client_id` int(11) NOT NULL DEFAULT 1,
  `table_id` int(11) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `order_status` enum('ordered','completed','settled') DEFAULT 'ordered',
  `total_amount` decimal(10,2) DEFAULT 0.00,
  `discount_amount` decimal(10,2) DEFAULT 0.00,
  `discount_type` varchar(20) DEFAULT NULL,
  `discount_value` decimal(10,2) DEFAULT 0.00,
  `discount_label` varchar(150) DEFAULT NULL,
  `discount_date` date DEFAULT NULL,
  `discount_start_time` varchar(10) DEFAULT NULL,
  `discount_end_time` varchar(10) DEFAULT NULL,
  `discount_is_paused` tinyint(1) DEFAULT 0,
  `bill_note` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  `customer_name` varchar(150) DEFAULT NULL,
  `customer_mobile` varchar(20) DEFAULT NULL,
  `customer_id` int(11) DEFAULT NULL,
  `is_kot_only` tinyint(1) NOT NULL DEFAULT 1,
  `report_visible` tinyint(1) NOT NULL DEFAULT 0,
  `billed_at` datetime DEFAULT NULL,
  `bill_number` int(11) DEFAULT NULL,
  `sqlite_uuid` varchar(36) DEFAULT NULL,
  `is_parcel_mode` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  UNIQUE KEY `uniq_orders_client_bill_number` (`client_id`,`bill_number`),
  UNIQUE KEY `uniq_orders_sqlite_uuid` (`sqlite_uuid`),
  KEY `idx_orders_table` (`table_id`),
  KEY `idx_orders_user` (`created_by`),
  KEY `idx_orders_customer` (`customer_id`),
  KEY `idx_orders_created_at` (`created_at`),
  KEY `idx_orders_status_created_at` (`order_status`,`created_at`),
  KEY `idx_orders_client_created_at` (`client_id`,`created_at`),
  KEY `idx_orders_client_table_status` (`client_id`,`table_id`,`order_status`),
  KEY `idx_orders_client_billed_at` (`client_id`,`billed_at`),
  CONSTRAINT `fk_orders_created_by` FOREIGN KEY (`created_by`) REFERENCES `users` (`id`),
  CONSTRAINT `fk_orders_customer` FOREIGN KEY (`customer_id`) REFERENCES `customers` (`id`) ON DELETE SET NULL,
  CONSTRAINT `fk_orders_table` FOREIGN KEY (`table_id`) REFERENCES `restaurant_tables` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `order_items` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `order_id` int(11) NOT NULL,
  `item_id` int(11) DEFAULT NULL,
  `client_item_id` varchar(100) DEFAULT NULL,
  `item_name` varchar(150) DEFAULT NULL,
  `price` decimal(10,2) DEFAULT NULL,
  `quantity` int(11) NOT NULL,
  `is_parcel` tinyint(1) DEFAULT 0,
  `total` decimal(10,2) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  `discount_amount` decimal(10,2) DEFAULT 0.00,
  `discount_type` varchar(20) DEFAULT NULL,
  `discount_value` decimal(10,2) DEFAULT 0.00,
  `discount_label` varchar(150) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  KEY `idx_order_items_order` (`order_id`),
  CONSTRAINT `fk_order_items_order` FOREIGN KEY (`order_id`) REFERENCES `orders` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `order_status_logs` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `order_id` int(11) DEFAULT NULL,
  `status` varchar(50) DEFAULT NULL,
  `changed_by` int(11) DEFAULT NULL,
  `changed_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `fk_order_status_logs_order` (`order_id`),
  KEY `fk_order_status_logs_changed_by` (`changed_by`),
  CONSTRAINT `fk_order_status_logs_changed_by` FOREIGN KEY (`changed_by`) REFERENCES `users` (`id`),
  CONSTRAINT `fk_order_status_logs_order` FOREIGN KEY (`order_id`) REFERENCES `orders` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `customer_ledger_entries` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uuid` varchar(36) DEFAULT NULL,
  `client_id` int(11) NOT NULL DEFAULT 1,
  `customer_id` int(11) NOT NULL,
  `entry_type` enum('debit','credit') NOT NULL,
  `amount` decimal(10,2) NOT NULL,
  `note` varchar(255) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `occurred_at` datetime NOT NULL DEFAULT current_timestamp(),
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `sync_version` int(11) DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uuid` (`uuid`),
  KEY `fk_customer_ledger_created_by` (`created_by`),
  KEY `idx_customer_ledger_customer` (`customer_id`),
  KEY `idx_customer_ledger_client_customer` (`client_id`,`customer_id`),
  CONSTRAINT `fk_customer_ledger_created_by` FOREIGN KEY (`created_by`) REFERENCES `users` (`id`) ON DELETE SET NULL,
  CONSTRAINT `fk_customer_ledger_customer` FOREIGN KEY (`customer_id`) REFERENCES `customers` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `table_client_states` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `client_id` int(11) NOT NULL,
  `table_id` int(11) NOT NULL,
  `table_status` varchar(30) DEFAULT 'available',
  `current_amount` decimal(10,2) DEFAULT 0.00,
  `order_timestamp` bigint(20) DEFAULT NULL,
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_table_client_state` (`client_id`,`table_id`),
  KEY `idx_table_client_states_table` (`table_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `app_settings` (
  `client_id` int(11) NOT NULL DEFAULT 1,
  `key` varchar(100) NOT NULL,
  `value` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`value`)),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`client_id`,`key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `gallery_images` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `client_id` int(11) NOT NULL,
  `url` text NOT NULL,
  `filename` varchar(255) DEFAULT NULL,
  `is_visible` tinyint(1) DEFAULT 1,
  `created_at` datetime DEFAULT current_timestamp(),
  `category_id` int(11) DEFAULT NULL,
  `sub_category_id` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_gallery_images_client` (`client_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `menu_item_client_preferences` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `client_id` int(11) NOT NULL,
  `menu_item_id` int(11) NOT NULL,
  `is_favorite` tinyint(1) NOT NULL DEFAULT 0,
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_menu_item_client_preference` (`client_id`,`menu_item_id`),
  KEY `idx_menu_item_client_preference_item` (`menu_item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `qr_orders` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `client_id` int(11) NOT NULL DEFAULT 1,
  `table_id` int(11) DEFAULT NULL,
  `table_token` varchar(64) DEFAULT NULL,
  `table_number` varchar(50) DEFAULT NULL,
  `items` longtext NOT NULL,
  `total_amount` decimal(10,2) NOT NULL DEFAULT 0.00,
  `customer_name` varchar(150) DEFAULT NULL,
  `customer_mobile` varchar(20) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'pending',
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_qr_client_status` (`client_id`,`status`),
  KEY `idx_qr_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `discounts` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `client_id` int(11) NOT NULL,
  `name` varchar(150) NOT NULL,
  `discount_type` varchar(20) NOT NULL,
  `discount_value` decimal(10,2) NOT NULL DEFAULT 0.00,
  `min_order_amount` decimal(10,2) DEFAULT 0.00,
  `max_discount` decimal(10,2) DEFAULT NULL,
  `is_paused` tinyint(1) DEFAULT 0,
  `start_time` varchar(10) DEFAULT NULL,
  `end_time` varchar(10) DEFAULT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_discounts_client` (`client_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `customer_otps` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `mobile` varchar(20) NOT NULL,
  `otp` varchar(10) NOT NULL,
  `expires_at` datetime NOT NULL,
  `is_verified` tinyint(1) DEFAULT 0,
  `created_at` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_customer_otps_mobile` (`mobile`,`otp`,`expires_at`,`is_verified`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `categories` ADD COLUMN IF NOT EXISTS `image` longtext DEFAULT NULL AFTER `name`;
ALTER TABLE `categories` MODIFY COLUMN `image` longtext DEFAULT NULL;

ALTER TABLE `menu_items` ADD COLUMN IF NOT EXISTS `image` longtext DEFAULT NULL AFTER `price`;
ALTER TABLE `menu_items` MODIFY COLUMN `image` longtext DEFAULT NULL;

ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `client_id` int(11) NOT NULL DEFAULT 1 AFTER `uuid`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_amount` decimal(10,2) DEFAULT 0.00 AFTER `total_amount`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_type` varchar(20) DEFAULT NULL AFTER `discount_amount`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_value` decimal(10,2) DEFAULT 0.00 AFTER `discount_type`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_label` varchar(150) DEFAULT NULL AFTER `discount_value`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_date` date DEFAULT NULL AFTER `discount_label`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_start_time` varchar(10) DEFAULT NULL AFTER `discount_date`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_end_time` varchar(10) DEFAULT NULL AFTER `discount_start_time`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `discount_is_paused` tinyint(1) DEFAULT 0 AFTER `discount_end_time`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `customer_name` varchar(150) DEFAULT NULL AFTER `sync_version`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `customer_mobile` varchar(20) DEFAULT NULL AFTER `customer_name`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `customer_id` int(11) DEFAULT NULL AFTER `customer_mobile`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `is_kot_only` tinyint(1) NOT NULL DEFAULT 1 AFTER `customer_id`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `report_visible` tinyint(1) NOT NULL DEFAULT 0 AFTER `is_kot_only`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `billed_at` datetime DEFAULT NULL AFTER `report_visible`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `bill_number` int(11) DEFAULT NULL AFTER `billed_at`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `sqlite_uuid` varchar(36) DEFAULT NULL AFTER `bill_number`;
ALTER TABLE `orders` ADD COLUMN IF NOT EXISTS `is_parcel_mode` tinyint(1) NOT NULL DEFAULT 0 AFTER `sqlite_uuid`;

ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `client_item_id` varchar(100) DEFAULT NULL AFTER `item_id`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `item_name` varchar(150) DEFAULT NULL AFTER `client_item_id`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `is_parcel` tinyint(1) DEFAULT 0 AFTER `quantity`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `discount_amount` decimal(10,2) DEFAULT 0.00 AFTER `sync_version`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `discount_type` varchar(20) DEFAULT NULL AFTER `discount_amount`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `discount_value` decimal(10,2) DEFAULT 0.00 AFTER `discount_type`;
ALTER TABLE `order_items` ADD COLUMN IF NOT EXISTS `discount_label` varchar(150) DEFAULT NULL AFTER `discount_value`;

ALTER TABLE `customers` ADD COLUMN IF NOT EXISTS `client_id` int(11) NOT NULL DEFAULT 1 AFTER `uuid`;
ALTER TABLE `customer_ledger_entries` ADD COLUMN IF NOT EXISTS `client_id` int(11) NOT NULL DEFAULT 1 AFTER `uuid`;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `client_id` int(11) NOT NULL DEFAULT 1 AFTER `uuid`;

SET FOREIGN_KEY_CHECKS = 1;
