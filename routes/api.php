<?php

use App\Controllers\AuthController;
use App\Controllers\DiningAreaController;
use App\Controllers\GstController;
use App\Controllers\LedgerController;
use App\Controllers\MenuController;
use App\Controllers\OrderController;
use App\Controllers\PrintController;
use App\Controllers\QzController;
use App\Controllers\ReportController;
use App\Controllers\SettingController;
use App\Controllers\SyncController;
use App\Controllers\TableController;
use App\Controllers\DiscountController;
use App\Controllers\GalleryController;
use App\Controllers\QrOrderController;

$apiStatus = fn () => success_response([
    'status' => 'ok',
    'name' => 'POS API',
]);

$router->get('/api', $apiStatus);
$router->get('/api/', $apiStatus);
$router->get('/api/health', fn () => success_response(['status' => 'ok']));

// The WPF app's auto-update manifest: the latest published build's version, its download URL and
// a short "what's new". Editing config/app-version.json (bump the version, point url at the new
// build's zip) is all it takes to roll an update out — no code deploy. A newer version here than
// a till is running lights up its "Update" badge.
$router->get('/api/app-version', function (): void {
    $file = dirname(__DIR__) . '/config/app-version.json';
    $manifest = is_file($file) ? json_decode((string) file_get_contents($file), true) : null;
    if (!is_array($manifest) || !isset($manifest['version'])) {
        $manifest = ['version' => '3.0.0', 'url' => '', 'notes' => '', 'mandatory' => false];
    }
    success_response($manifest);
});
$router->get('/api/bootstrap', [SyncController::class, 'bootstrap']);
$router->get('/api/system-printers', [PrintController::class, 'printers']);
$router->post('/api/print', [PrintController::class, 'print']);
$router->get('/api/qz/certificate', [QzController::class, 'certificate']);
$router->post('/api/qz/sign', [QzController::class, 'sign']);
$router->get('/api/qz/certificate/index.php', [QzController::class, 'certificate']);
$router->post('/api/qz/sign/index.php', [QzController::class, 'sign']);
$router->get('/api/qz-certificate.php', [QzController::class, 'certificate']);
$router->post('/api/qz-sign.php', [QzController::class, 'sign']);

$router->post('/api/auth/register', [AuthController::class, 'register']);
$router->post('/api/auth/login', [AuthController::class, 'login']);
$router->get('/api/auth/clients', [AuthController::class, 'clients']);
$router->get('/api/auth/profile', [AuthController::class, 'profile']);
$router->put('/api/auth/profile', [AuthController::class, 'updateProfile']);

$router->get('/api/categories', [MenuController::class, 'categories']);
$router->post('/api/categories', [MenuController::class, 'storeCategory']);
$router->put('/api/categories/{id}', [MenuController::class, 'updateCategory']);
$router->delete('/api/categories/{id}', [MenuController::class, 'deleteCategory']);

$router->get('/api/menu-items', [MenuController::class, 'items']);
$router->post('/api/menu-items', [MenuController::class, 'storeItem']);
$router->put('/api/menu-items/{id}', [MenuController::class, 'updateItem']);
$router->delete('/api/menu-items/{id}', [MenuController::class, 'deleteItem']);
$router->get('/api/transliterate', [MenuController::class, 'transliterate']);
$router->post('/api/upload', [MenuController::class, 'uploadImage']);

$router->get('/api/gallery', [GalleryController::class, 'index']);
$router->post('/api/gallery', [GalleryController::class, 'store']);
$router->put('/api/gallery/{id}', [GalleryController::class, 'update']);
$router->delete('/api/gallery/{id}', [GalleryController::class, 'destroy']);

$router->get('/api/tables', [TableController::class, 'index']);
$router->post('/api/tables', [TableController::class, 'store']);
$router->put('/api/tables/{id}', [TableController::class, 'update']);
$router->patch('/api/tables/{id}/status', [TableController::class, 'status']);
$router->delete('/api/tables/{id}', [TableController::class, 'destroy']);
$router->post('/api/tables/transfer', [TableController::class, 'transfer']);
$router->post('/api/tables/merge', [TableController::class, 'merge']);
$router->post('/api/tables/split', [TableController::class, 'split']);

$router->get('/api/dining-areas', [DiningAreaController::class, 'index']);
$router->post('/api/dining-areas', [DiningAreaController::class, 'store']);
$router->put('/api/dining-areas/{id}', [DiningAreaController::class, 'update']);
$router->delete('/api/dining-areas/{id}', [DiningAreaController::class, 'destroy']);

$router->get('/api/gst-rates', [GstController::class, 'index']);
$router->post('/api/gst-rates', [GstController::class, 'store']);
$router->put('/api/gst-rates/{id}', [GstController::class, 'update']);
$router->delete('/api/gst-rates/{id}', [GstController::class, 'destroy']);

$router->get('/api/orders', [OrderController::class, 'index']);
$router->get('/api/orders/next-bill-number', [OrderController::class, 'nextBillNumber']);
$router->post('/api/orders', [OrderController::class, 'store']);
$router->post('/api/table-orders', [OrderController::class, 'saveTableOrder']);
$router->patch('/api/orders/{id}/status', [OrderController::class, 'status']);
$router->delete('/api/orders/{id}', [OrderController::class, 'destroy']);
$router->post('/api/sync/orders', [OrderController::class, 'syncFromLocal']);

$router->get('/api/reports/summary', [ReportController::class, 'summary']);

$router->get('/api/ledger', [LedgerController::class, 'index']);
$router->post('/api/ledger/customers', [LedgerController::class, 'storeCustomer']);
$router->put('/api/ledger/customers/{id}', [LedgerController::class, 'updateCustomer']);
$router->delete('/api/ledger/customers/{id}', [LedgerController::class, 'deleteCustomer']);

$router->get('/api/tables/validate-qr', [TableController::class, 'validateQr']);

$router->get('/api/customers/check', [LedgerController::class, 'checkCustomerByMobile']);
$router->post('/api/customers/send-otp', [LedgerController::class, 'sendOtp']);
$router->post('/api/customers/verify-otp', [LedgerController::class, 'verifyOtp']);

// QR orders: customer mobile-menu -> POS operator bridge (separate from the
// regular `orders` flow used by table/quick orders).
$router->post('/api/qr-orders', [QrOrderController::class, 'store']);
$router->post('/api/qr-orders/settle-table', [QrOrderController::class, 'settleTable']);
$router->get('/api/qr-orders', [QrOrderController::class, 'index']);
$router->patch('/api/qr-orders/{id}/status', [QrOrderController::class, 'status']);
$router->get('/api/qr-orders/{id}', [QrOrderController::class, 'show']);

$router->post('/api/ledger/customers/{id}/entries', [LedgerController::class, 'storeEntry']);
$router->put('/api/ledger/entries/{id}', [LedgerController::class, 'updateEntry']);
$router->delete('/api/ledger/entries/{id}', [LedgerController::class, 'deleteEntry']);

$router->get('/api/settings', [SettingController::class, 'index']);
$router->put('/api/settings/{key}', [SettingController::class, 'update']);

$router->get('/api/discounts', [DiscountController::class, 'index']);
$router->post('/api/discounts', [DiscountController::class, 'store']);
$router->get('/api/discounts/{id}', [DiscountController::class, 'show']);
$router->put('/api/discounts/{id}', [DiscountController::class, 'update']);
$router->delete('/api/discounts/{id}', [DiscountController::class, 'destroy']);
