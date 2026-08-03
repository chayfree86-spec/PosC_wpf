<?php

namespace App\Controllers;

use App\Core\Database;
use App\Models\Client;
use App\Models\Setting;

class SettingController
{
    public function index(): void
    {
        success_response(Setting::all());
    }

    public function update(string $key): void
    {
        $data = request_json();
        $value = $data['value'] ?? null;
        $saved = Setting::put($key, $value);

        if ($key === 'restaurant_profile') {
            self::syncClientName($value);
            self::syncLoginPhone($value);
            self::syncLoginEmail($value);
        }

        success_response($saved, 'Setting saved.');
    }

    /**
     * The contact number on the profile IS the login number, so saving the profile has to
     * move it onto the user who signs in with it — otherwise the operator changes the number
     * on screen and then can't get in with it.
     *
     * Only the client's primary user is touched. Once a shop has several staff logins each
     * needs its own number, and overwriting them all with the shop's number would lock
     * everyone but one person out.
     */
    private static function syncLoginPhone(mixed $value): void
    {
        $phone = is_array($value) ? trim((string) ($value['contactNumber'] ?? '')) : '';
        if ($phone === '' || strlen(preg_replace('/\D+/', '', $phone)) < 10) {
            return;
        }

        $db = Database::connection();
        $primary = $db->prepare(
            "SELECT id FROM users
             WHERE client_id = ?
             ORDER BY (role = 'manager') DESC, id
             LIMIT 1"
        );
        $primary->execute([Client::currentId()]);
        $userId = $primary->fetchColumn();

        if ($userId) {
            $db->prepare('UPDATE users SET phone = ? WHERE id = ?')->execute([$phone, (int) $userId]);
        }
    }

    /**
     * The business email on the profile is mirrored onto the client's primary user, so the
     * account's email stays in step with what the manager typed in Settings — the same idea as
     * syncLoginPhone, for the email field.
     *
     * Only a well-formed address is written, and only the client's primary (manager) user is
     * touched; a blank or invalid value is ignored rather than wiping an email that is already
     * there. Extra staff logins keep their own addresses.
     */
    private static function syncLoginEmail(mixed $value): void
    {
        $email = is_array($value) ? trim((string) ($value['email'] ?? '')) : '';
        if ($email === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) {
            return;
        }

        $db = Database::connection();
        $primary = $db->prepare(
            "SELECT id FROM users
             WHERE client_id = ?
             ORDER BY (role = 'manager') DESC, id
             LIMIT 1"
        );
        $primary->execute([Client::currentId()]);
        $userId = $primary->fetchColumn();

        if ($userId) {
            $db->prepare('UPDATE users SET email = ? WHERE id = ?')->execute([$email, (int) $userId]);
        }
    }

    /**
     * The shop's name lives in two places: the profile setting that gets printed on bills,
     * and the clients row that names the business everywhere else. Renaming the shop used to
     * change only the first, leaving the client list showing the old name for ever.
     *
     * The slug is deliberately left alone — it identifies the client in URLs and headers, so
     * changing it would cut existing tills off from their own data.
     */
    private static function syncClientName(mixed $value): void
    {
        $name = is_array($value) ? trim((string) ($value['name'] ?? '')) : '';
        if ($name === '') {
            return;
        }

        $stmt = Database::connection()->prepare('UPDATE clients SET name = ? WHERE id = ?');
        $stmt->execute([$name, Client::currentId()]);
    }
}
