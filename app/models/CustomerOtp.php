<?php

namespace App\Models;

use App\Core\Database;

class CustomerOtp
{
    private static bool $schemaChecked = false;

    public static function ensureTable(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        $db = Database::connection();
        $db->exec(
            "CREATE TABLE IF NOT EXISTS customer_otps (
                id INT AUTO_INCREMENT PRIMARY KEY,
                mobile VARCHAR(20) NOT NULL,
                otp VARCHAR(10) NOT NULL,
                expires_at DATETIME NOT NULL,
                is_verified TINYINT(1) DEFAULT 0,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )"
        );

        self::$schemaChecked = true;
    }

    public static function createOtp(string $mobile, string $otp): void
    {
        self::ensureTable();
        $db = Database::connection();
        
        // Mark previous OTPs for this mobile as expired/verified to invalidate them
        $stmt = $db->prepare('UPDATE customer_otps SET expires_at = NOW() WHERE mobile = ? AND is_verified = 0');
        $stmt->execute([$mobile]);

        // Insert new OTP expiring in 10 minutes (using NOW() which is affected by SET time_zone)
        $stmt = $db->prepare('INSERT INTO customer_otps (mobile, otp, expires_at) VALUES (?, ?, DATE_ADD(NOW(), INTERVAL 10 MINUTE))');
        $stmt->execute([$mobile, $otp]);
    }

    public static function verifyOtp(string $mobile, string $otp): bool
    {
        self::ensureTable();
        $db = Database::connection();

        $stmt = $db->prepare('SELECT id FROM customer_otps WHERE mobile = ? AND otp = ? AND expires_at > NOW() AND is_verified = 0 ORDER BY id DESC LIMIT 1');
        $stmt->execute([$mobile, $otp]);
        $row = $stmt->fetch();

        if ($row) {
            $update = $db->prepare('UPDATE customer_otps SET is_verified = 1 WHERE id = ?');
            $update->execute([$row['id']]);
            return true;
        }

        return false;
    }
}
