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

    private function safeActiveTableOrders(): array
    {
        try {
            return Order::activeTableOrders();
        } catch (\Throwable) {
            return [];
        }
    }
}
