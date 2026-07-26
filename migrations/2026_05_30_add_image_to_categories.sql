-- Database Migration: Add image column to categories table
-- Created at: 2026-05-30

ALTER TABLE categories ADD COLUMN image VARCHAR(255) NULL DEFAULT NULL AFTER name;
