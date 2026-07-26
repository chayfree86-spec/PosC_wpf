<?php

namespace App\Controllers;

use App\Models\Client;
use App\Models\QrOrder;
use App\Models\Table;

/**
 * Bridges the customer mobile-menu PWA and the POS operator for QR orders.
 *
 *   POST   /api/qr-orders            customer places a pending order
 *   GET    /api/qr-orders            POS board lists this client's pending orders
 *   PATCH  /api/qr-orders/{id}/status  POS accepts / rejects an order
 *   GET    /api/qr-orders/{id}       customer polls their order's status
 *
 * It only manages the pending QR-order queue. Placing an accepted order onto a
 * table is done by the POS through the existing table-order flow -- this
 * controller never touches the `orders` table, so table/quick orders are safe.
 */
class QrOrderController
{
    // Customer (mobile menu) -> create a pending QR order.
    public function store(): void
    {
        $data = request_json();

        $items = $data['items'] ?? [];
        if (!is_array($items) || count($items) === 0) {
            error_response('Order has no items.', 422);
        }

        // Resolve the table from its QR token so the order is bound to the real
        // table and its owning client (a token can't be spoofed across clients).
        // Falls back to the X-POS-Client header / ?client= when no token given.
        $token = (string) ($data['table_token'] ?? $data['table'] ?? '');
        $table = $token !== '' ? Table::findByToken($token) : null;

        $clientId = isset($table['client_id']) ? (int) $table['client_id'] : Client::currentId();

        $id = QrOrder::create([
            'client_id'       => $clientId,
            'table_id'        => $table['id'] ?? ($data['table_id'] ?? null),
            'table_token'     => $token !== '' ? $token : null,
            'table_number'    => $table['table_number'] ?? ($data['table_number'] ?? null),
            'items'           => $items,
            'total_amount'    => (float) ($data['total'] ?? $data['total_amount'] ?? 0),
            'customer_name'   => $data['customer_name'] ?? null,
            'customer_mobile' => $data['customer_mobile'] ?? null,
        ]);

        success_response(['id' => $id, 'status' => 'pending'], 'QR order placed.', 201);
    }

    // POS board -> this client's pending & accepted QR orders (still open).
    public function index(): void
    {
        success_response(QrOrder::forBoard(Client::currentId()));
    }

    // POS -> accept / reject a pending QR order.
    public function status(string $id): void
    {
        $data = request_json();
        $status = strtolower((string) ($data['status'] ?? ''));
        if (!in_array($status, ['accepted', 'rejected', 'pending', 'settled'], true)) {
            error_response('Invalid status. Use accepted, rejected, pending or settled.', 422);
        }

        $ok = QrOrder::updateStatus((int) $id, Client::currentId(), $status);
        if (!$ok) {
            error_response('QR order not found.', 404);
        }

        success_response(['id' => (int) $id, 'status' => $status], 'QR order ' . $status . '.');
    }

    // POS -> mark all accepted QR orders on a table as settled (bill settled).
    public function settleTable(): void
    {
        $data = request_json();
        $tableId = (int) ($data['table_id'] ?? 0);
        if ($tableId <= 0) {
            error_response('table_id is required.', 422);
        }
        $count = QrOrder::settleForTable($tableId, Client::currentId());
        success_response(['table_id' => $tableId, 'settled' => $count], 'QR orders settled.');
    }

    // Customer -> poll the status of their own order.
    public function show(string $id): void
    {
        $order = QrOrder::find((int) $id, Client::currentId());
        if (!$order) {
            error_response('QR order not found.', 404);
        }

        success_response($order);
    }
}
