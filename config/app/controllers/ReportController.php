<?php

namespace App\Controllers;

use App\Core\Database;
use App\Models\Client;
use App\Models\Order;

class ReportController
{
    public function summary(): void
    {
        $db = Database::connection();
        Order::ensureSchema();
        $timezone = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
        $databaseTimezone = $timezone;
        $date = (string) ($_GET['date'] ?? (new \DateTimeImmutable('now', $timezone))->format('Y-m-d'));
        $range = strtolower((string) ($_GET['range'] ?? 'day'));
        $dateStartLocal = new \DateTimeImmutable($date . ' 00:00:00', $timezone);

        if ($range === 'week') {
            $dateStartLocal = $dateStartLocal->modify('monday this week');
            $dateEndLocal = $dateStartLocal->modify('+6 days')->setTime(23, 59, 59);
        } elseif ($range === 'month') {
            $dateStartLocal = $dateStartLocal->modify('first day of this month');
            $dateEndLocal = $dateStartLocal->modify('last day of this month')->setTime(23, 59, 59);
        } else {
            $range = 'day';
            $dateEndLocal = $dateStartLocal->setTime(23, 59, 59);
        }
        $dateStart = $dateStartLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $dateEnd = $dateEndLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $clientId = Client::currentId();
        $billPrefix = $this->billPrefixForClient(Client::current());
        $billFilter = "(o.report_visible = 1 AND o.is_kot_only = 0 AND o.billed_at IS NOT NULL)";
        $ordersStmt = $db->prepare(
            "SELECT COUNT(*) AS count, COALESCE(SUM(o.total_amount), 0) AS revenue
             FROM orders o
             WHERE o.client_id = ?
               AND {$billFilter}"
        );
        $ordersStmt->execute([$clientId]);
        $orders = $ordersStmt->fetch();
        $todayStmt = $db->prepare(
            'SELECT COUNT(*) AS count, COALESCE(SUM(total_amount), 0) AS revenue
             FROM orders o
             WHERE o.client_id = ?
               AND billed_at >= ? AND billed_at <= ?
               AND ' . $billFilter
        );
        $todayStmt->execute([$clientId, $dateStart, $dateEnd]);
        $today = $todayStmt->fetch();
        $topItemsStmt = $db->prepare(
            'SELECT oi.item_name,
                    COALESCE(c.name, "") AS category,
                    COALESCE(sc.name, "") AS sub_category,
                    SUM(oi.quantity) AS quantity,
                    SUM(oi.total) AS revenue
             FROM order_items oi
             JOIN orders o ON o.id = oi.order_id
             LEFT JOIN menu_items mi ON mi.id = oi.item_id
             LEFT JOIN categories c ON c.id = mi.category_id
             LEFT JOIN categories sc ON sc.id = mi.sub_category_id
             WHERE o.client_id = ?
               AND o.billed_at >= ? AND o.billed_at <= ?
               AND ' . $billFilter . '
             GROUP BY oi.item_name, c.name, sc.name
             ORDER BY quantity DESC
             LIMIT 10'
        );
        $topItemsStmt->execute([$clientId, $dateStart, $dateEnd]);
        $topItems = $topItemsStmt->fetchAll();
        $recentBillsStmt = $db->prepare(
            "SELECT o.*, rt.table_number,
                    COALESCE(c.name, o.customer_name) AS customer_name,
                    COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
             FROM orders o
             LEFT JOIN customers c ON c.id = o.customer_id
             LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
             WHERE o.client_id = ?
               AND {$billFilter}
             ORDER BY o.billed_at DESC, o.id DESC
             LIMIT 50"
        );
        $recentBillsStmt->execute([$clientId]);
        $recentBills = $recentBillsStmt->fetchAll();
        $salesTimelineStmt = $db->prepare(
             "SELECT HOUR(billed_at) AS hour, COUNT(*) AS orders, COALESCE(SUM(total_amount), 0) AS revenue
             FROM orders o
             WHERE o.client_id = ?
               AND billed_at >= ? AND billed_at <= ?
               AND {$billFilter}
             GROUP BY HOUR(billed_at)
             ORDER BY hour"
        );
        $salesTimelineStmt->execute([$clientId, $dateStart, $dateEnd]);
        $salesTimeline = $salesTimelineStmt->fetchAll();
        $allOrdersStmt = $db->prepare(
            "SELECT o.*,
                    COALESCE(c.name, o.customer_name) AS customer_name,
                    COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
             FROM orders o
             LEFT JOIN customers c ON c.id = o.customer_id
             WHERE o.client_id = ?
               AND {$billFilter}
             ORDER BY o.billed_at DESC, o.id DESC"
        );
        $allOrdersStmt->execute([$clientId]);
        $allOrders = $allOrdersStmt->fetchAll();
        $allOrderItemsStmt = $db->prepare(
            'SELECT oi.*,
                    COALESCE(c.name, "") AS category,
                    COALESCE(sc.name, "") AS sub_category
             FROM order_items oi
             JOIN orders o ON o.id = oi.order_id
             LEFT JOIN menu_items mi ON mi.id = oi.item_id
             LEFT JOIN categories c ON c.id = mi.category_id
             LEFT JOIN categories sc ON sc.id = mi.sub_category_id
             WHERE o.client_id = ?
               AND ' . $billFilter . '
             ORDER BY oi.order_id, oi.id'
        );
        $allOrderItemsStmt->execute([$clientId]);
        $allOrderItems = $allOrderItemsStmt->fetchAll();
        $gstRates = $db->query('SELECT * FROM gst_rates ORDER BY is_active DESC, id')->fetchAll();
        $itemsByOrder = [];

        foreach ($allOrderItems as $item) {
            $itemsByOrder[(int) $item['order_id']][] = $item;
        }

        foreach ($recentBills as &$bill) {
            $orderItems = $itemsByOrder[(int) $bill['id']] ?? [];
            $bill['created_at'] = $this->toLocalTimestamp($bill['created_at'] ?? null, $timezone, $databaseTimezone);
            $bill['updated_at'] = $this->toLocalTimestamp($bill['updated_at'] ?? null, $timezone, $databaseTimezone);
            $bill['billed_at'] = $this->toLocalTimestamp($bill['billed_at'] ?? null, $timezone, $databaseTimezone);
            $bill['bill_prefix'] = $billPrefix;
            $bill['formatted_bill_number'] = $this->formatBillNumber((int) ($bill['bill_number'] ?? 0), $billPrefix);
            $bill['items'] = $orderItems;
            $bill['itemList'] = $this->receiptItems($orderItems);
        }
        unset($bill);

        foreach ($allOrders as &$order) {
            $orderItems = $itemsByOrder[(int) $order['id']] ?? [];
            $order['created_at'] = $this->toLocalTimestamp($order['created_at'] ?? null, $timezone, $databaseTimezone);
            $order['updated_at'] = $this->toLocalTimestamp($order['updated_at'] ?? null, $timezone, $databaseTimezone);
            $order['billed_at'] = $this->toLocalTimestamp($order['billed_at'] ?? null, $timezone, $databaseTimezone);
            $order['bill_prefix'] = $billPrefix;
            $order['formatted_bill_number'] = $this->formatBillNumber((int) ($order['bill_number'] ?? 0), $billPrefix);
            $order['items'] = $orderItems;
            $order['itemList'] = $this->receiptItems($orderItems);
        }
        unset($order);

        success_response([
            'orders' => $orders,
            'today' => $today,
            'top_items' => $topItems,
            'recent_bills' => $recentBills,
            'sales_timeline' => $salesTimeline,
            'all_orders' => $allOrders,
            'order_items' => $allOrderItems,
            'gst_rates' => $gstRates,
            'festival_dates' => $this->googleCalendarFestivalDates($timezone),
        ]);
    }

    private function billPrefixForClient(array $client): string
    {
        $slug = strtolower((string) ($client['slug'] ?? ''));
        $name = strtolower((string) ($client['name'] ?? ''));

        if (str_contains($slug, 'chay') || str_contains($name, 'chay')) {
            return 'CC';
        }

        if (str_contains($slug, 'daal') || str_contains($slug, 'dal') || str_contains($name, 'daal') || str_contains($name, 'dal')) {
            return 'DR';
        }

        return 'BILL';
    }

    private function receiptItems(array $items): array
    {
        return array_values(array_map(static function (array $item): array {
            $quantity = (float) ($item['quantity'] ?? $item['qty'] ?? 1);
            $price = (float) ($item['price'] ?? 0);
            $total = (float) ($item['total'] ?? ($price * $quantity));

            return [
                'name' => (string) ($item['item_name'] ?? $item['name'] ?? ''),
                'qty' => $quantity,
                'price' => $price,
                'total' => $total,
            ];
        }, $items));
    }

    private function formatBillNumber(int $billNumber, string $prefix): ?string
    {
        if ($billNumber <= 0) {
            return null;
        }

        return '#' . $prefix . '-' . str_pad((string) $billNumber, 4, '0', STR_PAD_LEFT);
    }

    private function googleCalendarFestivalDates(\DateTimeZone $timezone): array
    {
        $year = (int) (new \DateTimeImmutable('now', $timezone))->format('Y');
        $startYear = $year - 3;
        $endYear = $year;
        $url = (string) env(
            'GOOGLE_HOLIDAY_CALENDAR_ICS',
            'https://calendar.google.com/calendar/ical/en.indian%23holiday%40group.v.calendar.google.com/public/basic.ics'
        );

        try {
            $ics = $this->fetchCalendarIcs($url);
            if ($ics === '') {
                return [];
            }

            return $this->parseFestivalIcs($ics, $startYear, $endYear);
        } catch (\Throwable) {
            return [];
        }
    }

    private function fetchCalendarIcs(string $url): string
    {
        if (function_exists('curl_init')) {
            $curl = curl_init($url);
            curl_setopt_array($curl, [
                CURLOPT_RETURNTRANSFER => true,
                CURLOPT_FOLLOWLOCATION => true,
                CURLOPT_CONNECTTIMEOUT => 5,
                CURLOPT_TIMEOUT => 10,
                CURLOPT_USERAGENT => 'CafePOS/1.0',
            ]);
            $body = curl_exec($curl);
            $status = (int) curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
            curl_close($curl);

            return is_string($body) && $status >= 200 && $status < 300 ? $body : '';
        }

        $context = stream_context_create([
            'http' => [
                'timeout' => 10,
                'header' => "User-Agent: CafePOS/1.0\r\n",
            ],
        ]);
        $body = @file_get_contents($url, false, $context);

        if (is_string($body)) {
            return $body;
        }

        return $this->fetchCalendarIcsWithPowershell($url);
    }

    private function fetchCalendarIcsWithPowershell(string $url): string
    {
        if (!function_exists('shell_exec')) {
            return '';
        }

        $script = '$ProgressPreference = "SilentlyContinue"; '
            . '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; '
            . '$response = Invoke-WebRequest -UseBasicParsing -Uri '
            . var_export($url, true)
            . ' -TimeoutSec 10; $response.Content';
        $encodedScript = function_exists('mb_convert_encoding')
            ? mb_convert_encoding($script, 'UTF-16LE', 'UTF-8')
            : (iconv('UTF-8', 'UTF-16LE', $script) ?: '');
        $encoded = base64_encode($encodedScript);
        $body = shell_exec('powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' . $encoded);

        return is_string($body) ? $body : '';
    }

    private function parseFestivalIcs(string $ics, int $startYear, int $endYear): array
    {
        $lines = preg_split('/\r\n|\n|\r/', $ics) ?: [];
        $unfolded = [];

        foreach ($lines as $line) {
            if ($line !== '' && ($line[0] === ' ' || $line[0] === "\t") && $unfolded) {
                $unfolded[count($unfolded) - 1] .= substr($line, 1);
                continue;
            }
            $unfolded[] = $line;
        }

        $events = [];
        $event = null;

        foreach ($unfolded as $line) {
            if ($line === 'BEGIN:VEVENT') {
                $event = [];
                continue;
            }

            if ($line === 'END:VEVENT') {
                if ($event && isset($event['SUMMARY'], $event['DTSTART'])) {
                    $date = $this->icalDateToIso((string) $event['DTSTART']);
                    $eventYear = (int) substr($date, 0, 4);

                    if ($date !== '' && $eventYear >= $startYear && $eventYear <= $endYear) {
                        $events[] = [
                            'name' => $this->decodeIcsText((string) $event['SUMMARY']),
                            'date' => $date,
                            'year' => $eventYear,
                        ];
                    }
                }
                $event = null;
                continue;
            }

            if ($event === null || !str_contains($line, ':')) {
                continue;
            }

            [$key, $value] = explode(':', $line, 2);
            $key = strtoupper(strtok($key, ';') ?: $key);

            if ($key === 'SUMMARY' || $key === 'DTSTART') {
                $event[$key] = $value;
            }
        }

        usort($events, static fn(array $a, array $b): int => strcmp($b['date'], $a['date']));

        return $events;
    }

    private function icalDateToIso(string $value): string
    {
        if (!preg_match('/^(\d{4})(\d{2})(\d{2})/', $value, $matches)) {
            return '';
        }

        return "{$matches[1]}-{$matches[2]}-{$matches[3]}";
    }

    private function decodeIcsText(string $value): string
    {
        return trim(str_replace(['\\n', '\\,', '\\;'], ["\n", ',', ';'], $value));
    }

    private function toLocalTimestamp(?string $value, \DateTimeZone $timezone, \DateTimeZone $databaseTimezone): ?string
    {
        if (!$value) {
            return $value;
        }

        try {
            return (new \DateTimeImmutable($value, $databaseTimezone))
                ->setTimezone($timezone)
                ->format('Y-m-d H:i:s');
        } catch (\Throwable) {
            return $value;
        }
    }
}
