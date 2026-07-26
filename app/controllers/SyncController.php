<?php

namespace App\Controllers;

use App\Core\Database;
use App\Models\Setting;
use App\Models\Order;
use App\Models\GstRate;
use App\Models\Table;
use App\Models\MenuItem;
use App\Models\Client;

class SyncController
{
    public function bootstrap(): void
    {
        $db = Database::connection();
        $categories = $db->query('SELECT * FROM categories ORDER BY parent_id IS NOT NULL, sort_order, name')->fetchAll();
        $menuItems = MenuItem::all();
        $tables = Table::all();
        $areas = $db->query('SELECT * FROM dining_areas ORDER BY sort_order, name')->fetchAll();
        $tableOrders = $this->safeActiveTableOrders();

        success_response([
            'categories' => $categories,
            'menu_items' => $menuItems,
            'tables' => $tables,
            'dining_areas' => $areas,
            'table_orders' => $tableOrders,
            'gst_rates' => GstRate::all(),
            'settings' => Setting::all(),
            'users' => $this->clientUsers(),
            'counts' => [
                'menu_count' => count($menuItems),
                'category_count' => count($categories),
                'table_count' => count($tables),
            ],
            'client' => [
                'id' => Client::currentId(),
                'slug' => Client::current()['slug'] ?? null,
                'name' => Client::current()['name'] ?? null,
            ],
        ]);
    }

    /**
     * The operators of this client, so the till can sign them in and put their name on a bill.
     *
     * The PIN travels as its bcrypt hash — never the PIN itself — because the counter has to
     * be able to log someone in with the line down, and that means checking the PIN locally.
     * A hash at cost 12 is not reversible in any practical sense, so a stolen laptop yields
     * nothing usable. The password hash stays behind entirely: e-mail login is an online-only
     * path and the till never needs it.
     */
    private function clientUsers(): array
    {
        $stmt = Database::connection()->prepare(
            'SELECT id, uuid, client_id, name, phone, email, role, is_active, pin, updated_at
             FROM users
             WHERE client_id = ?
             ORDER BY id'
        );
        $stmt->execute([Client::currentId()]);

        return $stmt->fetchAll() ?: [];
    }

    private function safeActiveTableOrders(): array
    {
        try {
            return Order::activeTableOrders();
        } catch (\Throwable) {
            return [];
        }
    }
}