<?php

namespace App\Controllers;

use App\Models\Table;
use App\Models\Order;
use App\Core\Database;

class TableController
{
    public function index(): void
    {
        success_response(Table::all());
    }

    public function store(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['table_number']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => Table::create($data)], 'Table created.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['table_number']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        Table::update((int) $id, $data);
        success_response(null, 'Table updated.');
    }

    public function destroy(string $id): void
    {
        Table::delete((int) $id);
        success_response(null, 'Table deleted.');
    }

    public function status(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['status']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $tableId = (int) $id;
        $currentTable = Table::find($tableId);
        $amount = isset($data['amount']) ? (float) $data['amount'] : (float) ($currentTable['amount'] ?? 0);

        Table::updateStatus(
            $tableId,
            $data['status'],
            $amount,
            isset($data['order_timestamp']) ? (int) $data['order_timestamp'] : null
        );

        success_response(Table::find($tableId), 'Table status updated.');
    }

    public function validateQr(): void
    {
        $token = $_GET['token'] ?? '';
        if ($token === '') {
            error_response('QR token is required.', 422);
        }

        $table = Table::findByToken($token);

        if ($table) {
            success_response([
                'valid' => true,
                'table' => [
                    'id' => $table['id'],
                    'table_number' => $table['table_number'],
                    'qr_token' => $table['qr_token']
                ]
            ], 'QR code validated successfully.');
        } else {
            error_response('Invalid QR code.', 404);
        }
    }

    public function transfer(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['source_table_id', 'target_table_id']);
        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $sourceId = (int) $data['source_table_id'];
        $targetId = (int) $data['target_table_id'];
        $clientId = \App\Models\Client::currentId();

        $db = Database::connection();
        $db->beginTransaction();

        try {
            // Find active order for source table
            $stmt = $db->prepare("
                SELECT id, total_amount, order_timestamp FROM orders
                WHERE table_id = ? AND client_id = ? AND order_status != 'cancelled' AND (report_visible = 0 OR report_visible IS NULL)
                ORDER BY id DESC LIMIT 1
            ");
            $stmt->execute([$sourceId, $clientId]);
            $order = $stmt->fetch();

            if (!$order) {
                throw new \Exception("No active order found on source table.");
            }

            $orderId = (int)$order['id'];
            $amount = (float)$order['total_amount'];
            $timestamp = $order['order_timestamp'] ? (int)$order['order_timestamp'] : (int)round(microtime(true) * 1000);

            // Update order's table_id to target table
            $updateOrder = $db->prepare("UPDATE orders SET table_id = ?, sync_version = sync_version + 1 WHERE id = ?");
            $updateOrder->execute([$targetId, $orderId]);

            // Update source table status to available
            Table::updateState($sourceId, 'available', 0, null);

            // Update target table status to ordered
            Table::updateState($targetId, 'ordered', $amount, $timestamp);

            $db->commit();
            success_response(null, 'Table transferred successfully.');
        } catch (\Throwable $e) {
            $db->rollBack();
            error_response($e->getMessage(), 500);
        }
    }

    public function merge(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['source_table_id', 'target_table_id']);
        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $sourceId = (int) $data['source_table_id'];
        $targetId = (int) $data['target_table_id'];
        $clientId = \App\Models\Client::currentId();

        $db = Database::connection();
        $db->beginTransaction();

        try {
            // Find active order for source table
            $stmt = $db->prepare("
                SELECT id, total_amount, discount_amount, customer_name, customer_mobile, bill_note FROM orders
                WHERE table_id = ? AND client_id = ? AND order_status != 'cancelled' AND (report_visible = 0 OR report_visible IS NULL)
                ORDER BY id DESC LIMIT 1
            ");
            $stmt->execute([$sourceId, $clientId]);
            $sourceOrder = $stmt->fetch();

            if (!$sourceOrder) {
                throw new \Exception("No active order found on source table.");
            }

            // Find active order for target table
            $stmt = $db->prepare("
                SELECT id, total_amount, discount_amount, customer_name, customer_mobile, bill_note FROM orders
                WHERE table_id = ? AND client_id = ? AND order_status != 'cancelled' AND (report_visible = 0 OR report_visible IS NULL)
                ORDER BY id DESC LIMIT 1
            ");
            $stmt->execute([$targetId, $clientId]);
            $targetOrder = $stmt->fetch();

            if (!$targetOrder) {
                // Target has no order, simply transfer!
                $orderId = (int)$sourceOrder['id'];
                $amount = (float)$sourceOrder['total_amount'];
                $timestamp = (int)round(microtime(true) * 1000);

                $updateOrder = $db->prepare("UPDATE orders SET table_id = ?, sync_version = sync_version + 1 WHERE id = ?");
                $updateOrder->execute([$targetId, $orderId]);

                Table::updateState($sourceId, 'available', 0, null);
                Table::updateState($targetId, 'ordered', $amount, $timestamp);
            } else {
                // Both orders exist! Merge items
                $sourceOrderId = (int)$sourceOrder['id'];
                $targetOrderId = (int)$targetOrder['id'];

                // Get source items
                $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ?");
                $stmt->execute([$sourceOrderId]);
                $sourceItems = $stmt->fetchAll();

                // Get target items
                $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ?");
                $stmt->execute([$targetOrderId]);
                $targetItems = $stmt->fetchAll();

                // Merge items
                $mergedItems = [];
                // Standardize target items first
                foreach ($targetItems as $item) {
                    $key = ($item['item_id'] !== null ? $item['item_id'] : $item['client_item_id']) . '|' . $item['is_parcel'];
                    $mergedItems[$key] = $item;
                }

                // Merge source items in
                foreach ($sourceItems as $item) {
                    $key = ($item['item_id'] !== null ? $item['item_id'] : $item['client_item_id']) . '|' . $item['is_parcel'];
                    if (isset($mergedItems[$key])) {
                        $mergedItems[$key]['quantity'] += $item['quantity'];
                        $mergedItems[$key]['total'] = $mergedItems[$key]['quantity'] * $mergedItems[$key]['price'];
                    } else {
                        $mergedItems[$key] = $item;
                        $mergedItems[$key]['order_id'] = $targetOrderId;
                    }
                }

                // Delete old items of target and insert merged
                $del = $db->prepare("DELETE FROM order_items WHERE order_id = ?");
                $del->execute([$targetOrderId]);

                $ins = $db->prepare("
                    INSERT INTO order_items (order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total, discount_amount, discount_type, discount_value, discount_label)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ");
                $totalAmount = 0.0;
                foreach ($mergedItems as $item) {
                    $ins->execute([
                        $targetOrderId,
                        $item['item_id'],
                        $item['client_item_id'],
                        $item['item_name'],
                        $item['price'],
                        $item['quantity'],
                        $item['is_parcel'],
                        $item['total'],
                        $item['discount_amount'] ?? 0,
                        $item['discount_type'] ?? null,
                        $item['discount_value'] ?? 0,
                        $item['discount_label'] ?? null
                    ]);
                    $totalAmount += (float)$item['total'];
                }

                // Delete source order and items
                $delSourceItems = $db->prepare("DELETE FROM order_items WHERE order_id = ?");
                $delSourceItems->execute([$sourceOrderId]);

                $delSourceLogs = $db->prepare("DELETE FROM order_status_logs WHERE order_id = ?");
                $delSourceLogs->execute([$sourceOrderId]);

                $delSourceOrder = $db->prepare("DELETE FROM orders WHERE id = ?");
                $delSourceOrder->execute([$sourceOrderId]);

                // Update target order total
                $discountAmount = (float)$targetOrder['discount_amount'] + (float)$sourceOrder['discount_amount'];
                $netAmount = max(0, $totalAmount - $discountAmount);
                $updateTargetOrder = $db->prepare("UPDATE orders SET total_amount = ?, discount_amount = ?, sync_version = sync_version + 1 WHERE id = ?");
                $updateTargetOrder->execute([$netAmount, $discountAmount, $targetOrderId]);

                // Update tables
                Table::updateState($sourceId, 'available', 0, null);
                Table::updateState($targetId, 'ordered', $netAmount, (int)round(microtime(true) * 1000));
            }

            $db->commit();
            success_response(null, 'Tables merged successfully.');
        } catch (\Throwable $e) {
            $db->rollBack();
            error_response($e->getMessage(), 500);
        }
    }

    public function split(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['source_table_id', 'target_table_id', 'items']);
        if ($errors || !is_array($data['items'])) {
            error_response('Validation failed.', 422, $errors ?: ['items' => 'Items must be an array.']);
        }

        $sourceId = (int) $data['source_table_id'];
        $targetId = (int) $data['target_table_id'];
        $itemsToSplit = $data['items'];
        $clientId = \App\Models\Client::currentId();

        $db = Database::connection();
        $db->beginTransaction();

        try {
            // Find active order for source table
            $stmt = $db->prepare("
                SELECT id, total_amount, discount_amount, customer_name, customer_mobile, bill_note, created_by FROM orders
                WHERE table_id = ? AND client_id = ? AND order_status != 'cancelled' AND (report_visible = 0 OR report_visible IS NULL)
                ORDER BY id DESC LIMIT 1
            ");
            $stmt->execute([$sourceId, $clientId]);
            $sourceOrder = $stmt->fetch();

            if (!$sourceOrder) {
                throw new \Exception("No active order found on source table.");
            }
            $sourceOrderId = (int)$sourceOrder['id'];

            // Find or create active order for target table
            $stmt = $db->prepare("
                SELECT id, total_amount, discount_amount, customer_name, customer_mobile, bill_note FROM orders
                WHERE table_id = ? AND client_id = ? AND order_status != 'cancelled' AND (report_visible = 0 OR report_visible IS NULL)
                ORDER BY id DESC LIMIT 1
            ");
            $stmt->execute([$targetId, $clientId]);
            $targetOrder = $stmt->fetch();

            $targetOrderId = null;
            if ($targetOrder) {
                $targetOrderId = (int)$targetOrder['id'];
            } else {
                // Create new order for target table
                $insert = $db->prepare('
                    INSERT INTO orders (uuid, client_id, table_id, created_by, order_status, total_amount, discount_amount, customer_name, customer_mobile, is_kot_only, report_visible)
                    VALUES (UUID(), ?, ?, ?, ?, 0, 0, ?, ?, 1, 0)
                ');
                $insert->execute([
                    $clientId,
                    $targetId,
                    $sourceOrder['created_by'] ?? null,
                    'pending',
                    $sourceOrder['customer_name'] ?? '',
                    $sourceOrder['customer_mobile'] ?? ''
                ]);
                $targetOrderId = (int)$db->lastInsertId();
            }

            // Process each split item
            foreach ($itemsToSplit as $splitItem) {
                $itemId = $splitItem['item_id'] !== null ? (int)$splitItem['item_id'] : null;
                $clientItemId = $splitItem['client_item_id'] ?? '';
                $qtyToMove = (int)$splitItem['quantity'];
                $isParcel = (int)$splitItem['is_parcel'];

                // Get matching item from source order
                if ($itemId !== null) {
                    $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ? AND item_id = ? AND is_parcel = ?");
                    $stmt->execute([$sourceOrderId, $itemId, $isParcel]);
                } else {
                    $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ? AND client_item_id = ? AND is_parcel = ?");
                    $stmt->execute([$sourceOrderId, $clientItemId, $isParcel]);
                }
                $sourceItem = $stmt->fetch();

                if (!$sourceItem) {
                    continue; // Item not found in source order
                }

                $sourceItemQty = (int)$sourceItem['quantity'];
                $qtyToMove = min($qtyToMove, $sourceItemQty);

                if ($qtyToMove <= 0) {
                    continue;
                }

                // Deduct from source order
                $newSourceQty = $sourceItemQty - $qtyToMove;
                if ($newSourceQty <= 0) {
                    $del = $db->prepare("DELETE FROM order_items WHERE id = ?");
                    $del->execute([(int)$sourceItem['id']]);
                } else {
                    $upd = $db->prepare("UPDATE order_items SET quantity = ?, total = ? * price WHERE id = ?");
                    $upd->execute([$newSourceQty, $newSourceQty, (int)$sourceItem['id']]);
                }

                // Add to target order
                if ($itemId !== null) {
                    $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ? AND item_id = ? AND is_parcel = ?");
                    $stmt->execute([$targetOrderId, $itemId, $isParcel]);
                } else {
                    $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id = ? AND client_item_id = ? AND is_parcel = ?");
                    $stmt->execute([$targetOrderId, $clientItemId, $isParcel]);
                }
                $targetItem = $stmt->fetch();

                if ($targetItem) {
                    $newTargetQty = (int)$targetItem['quantity'] + $qtyToMove;
                    $upd = $db->prepare("UPDATE order_items SET quantity = ?, total = ? * price WHERE id = ?");
                    $upd->execute([$newTargetQty, $newTargetQty, (int)$targetItem['id']]);
                } else {
                    $ins = $db->prepare("
                        INSERT INTO order_items (order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total, discount_amount, discount_type, discount_value, discount_label)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, null, 0, null)
                    ");
                    $ins->execute([
                        $targetOrderId,
                        $itemId,
                        $clientItemId,
                        $sourceItem['item_name'],
                        $sourceItem['price'],
                        $qtyToMove,
                        $isParcel,
                        $qtyToMove * (float)$sourceItem['price']
                    ]);
                }
            }

            // Recalculate totals for both orders
            // Recalculate source total
            $stmt = $db->prepare("SELECT SUM(total) FROM order_items WHERE order_id = ?");
            $stmt->execute([$sourceOrderId]);
            $sourceItemsTotal = (float)$stmt->fetchColumn();

            // Recalculate target total
            $stmt = $db->prepare("SELECT SUM(total) FROM order_items WHERE order_id = ?");
            $stmt->execute([$targetOrderId]);
            $targetItemsTotal = (float)$stmt->fetchColumn();

            // Update source order
            if ($sourceItemsTotal <= 0) {
                // No items left in source order! Delete it
                $delSourceItems = $db->prepare("DELETE FROM order_items WHERE order_id = ?");
                $delSourceItems->execute([$sourceOrderId]);

                $delSourceLogs = $db->prepare("DELETE FROM order_status_logs WHERE order_id = ?");
                $delSourceLogs->execute([$sourceOrderId]);

                $delSourceOrder = $db->prepare("DELETE FROM orders WHERE id = ?");
                $delSourceOrder->execute([$sourceOrderId]);

                Table::updateState($sourceId, 'available', 0, null);
            } else {
                $discountAmount = min($sourceItemsTotal, (float)$sourceOrder['discount_amount']);
                $netAmount = max(0, $sourceItemsTotal - $discountAmount);
                $updSourceOrder = $db->prepare("UPDATE orders SET total_amount = ?, discount_amount = ?, sync_version = sync_version + 1 WHERE id = ?");
                $updSourceOrder->execute([$netAmount, $discountAmount, $sourceOrderId]);

                Table::updateState($sourceId, 'ordered', $netAmount, (int)round(microtime(true) * 1000));
            }

            // Update target order
            $discountAmountTarget = min($targetItemsTotal, (float)($targetOrder['discount_amount'] ?? 0));
            $netAmountTarget = max(0, $targetItemsTotal - $discountAmountTarget);
            $updTargetOrder = $db->prepare("UPDATE orders SET total_amount = ?, discount_amount = ?, sync_version = sync_version + 1 WHERE id = ?");
            $updTargetOrder->execute([$netAmountTarget, $discountAmountTarget, $targetOrderId]);

            Table::updateState($targetId, 'ordered', $netAmountTarget, (int)round(microtime(true) * 1000));

            $db->commit();
            success_response(null, 'Table split successfully.');
        } catch (\Throwable $e) {
            $db->rollBack();
            error_response($e->getMessage(), 500);
        }
    }
}
