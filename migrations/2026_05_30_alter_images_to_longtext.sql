-- Database Migration: Convert categories.image and menu_items.image to LONGTEXT to support Base64 data URLs
-- Created at: 2026-05-30

ALTER TABLE categories MODIFY COLUMN image LONGTEXT NULL DEFAULT NULL;
ALTER TABLE menu_items MODIFY COLUMN image LONGTEXT NULL DEFAULT NULL;
