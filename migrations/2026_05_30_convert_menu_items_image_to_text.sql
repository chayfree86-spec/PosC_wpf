-- Database Migration: Convert menu_items image column to TEXT to support multiple images JSON
-- Created at: 2026-05-30

ALTER TABLE menu_items MODIFY COLUMN image TEXT NULL DEFAULT NULL;
