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
        $section = strtolower((string) ($_GET['section'] ?? 'all'));

        $startDateGet = $_GET['start_date'] ?? null;
        $endDateGet = $_GET['end_date'] ?? null;

        if ($startDateGet && $endDateGet) {
            $dateStartLocal = new \DateTimeImmutable($startDateGet . ' 00:00:00', $timezone);
            $dateEndLocal = new \DateTimeImmutable($endDateGet . ' 23:59:59', $timezone);
            $range = 'custom';
            $date = $startDateGet;
            $originalSelectedDate = $date;
        } else {
            $date = (string) ($_GET['date'] ?? (new \DateTimeImmutable('now', $timezone))->format('Y-m-d'));
            $originalSelectedDate = $date;
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
        }
        $dateStart = $dateStartLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $dateEnd = $dateEndLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $selectedDayStartLocal = new \DateTimeImmutable($date . ' 00:00:00', $timezone);
        $dashboardWindowStarts = [
            $selectedDayStartLocal->modify('-1 day'),
            $selectedDayStartLocal->modify('monday this week')->modify('-1 week'),
            $selectedDayStartLocal->modify('first day of this month')->modify('-1 month'),
            $selectedDayStartLocal->modify('first day of January this year'),
        ];
        $dashboardWindowEnds = [
            $dateEndLocal,
            $selectedDayStartLocal->modify('monday this week')->modify('+1 week'),
            $selectedDayStartLocal->modify('first day of next month'),
            $selectedDayStartLocal->modify('first day of January next year'),
        ];
        $dashboardStartLocal = array_reduce(
            $dashboardWindowStarts,
            static fn(\DateTimeImmutable $carry, \DateTimeImmutable $candidate): \DateTimeImmutable =>
                $candidate->getTimestamp() < $carry->getTimestamp() ? $candidate : $carry,
            $dashboardWindowStarts[0]
        );
        $dashboardEndLocal = array_reduce(
            $dashboardWindowEnds,
            static fn(\DateTimeImmutable $carry, \DateTimeImmutable $candidate): \DateTimeImmutable =>
                $candidate->getTimestamp() > $carry->getTimestamp() ? $candidate : $carry,
            $dashboardWindowEnds[0]
        );
        $dashboardStart = $dashboardStartLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $dashboardEnd = $dashboardEndLocal->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
        $requestedSlug = \App\Models\Client::normalizeSlug((string) ($_GET['report_client'] ?? $_GET['client'] ?? ''));
        $reportClient = $requestedSlug !== '' ? \App\Models\Client::findBySlug($requestedSlug) : null;
        if (!$reportClient) {
            $reportClient = \App\Models\Client::current();
        }
        $clientId = (int) $reportClient['id'];

        $clientLastOrderId = (int) ($_GET['last_order_id'] ?? 0);
        $maxOrderIdStmt = $db->prepare("SELECT COALESCE(MAX(id), 0) FROM orders WHERE client_id = ?");
        $maxOrderIdStmt->execute([$clientId]);
        $maxOrderId = (int) $maxOrderIdStmt->fetchColumn();

        if ($clientLastOrderId > 0 && $maxOrderId > 0 && $clientLastOrderId >= $maxOrderId) {
            success_response([
                'has_changes' => false,
                'last_order_id' => $maxOrderId
            ]);
            return;
        }

        $billPrefix = $this->billPrefixForClient($reportClient);
        $billFilter = "(o.report_visible = 1 AND o.is_kot_only = 0 AND o.billed_at IS NOT NULL AND o.order_status IN ('settled', 'completed'))";
        $orders = null;
        $today = null;
        $topItems = [];
        $recentBills = [];
        $salesTimeline = [];
        $allOrders = [];
        $allOrderItems = [];
        $gstRates = [];

        if ($section === 'all') {
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
                "SELECT o.*, rt.table_number,
                        COALESCE(c.name, o.customer_name) AS customer_name,
                        COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
                 FROM orders o
                 LEFT JOIN customers c ON c.id = o.customer_id
                 LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
                 WHERE o.client_id = ?
                   AND o.billed_at >= ? AND o.billed_at <= ?
                   AND {$billFilter}
                 ORDER BY o.billed_at DESC, o.id DESC
                 LIMIT 500"
            );
            $allOrdersStmt->execute([$clientId, $dashboardStart, $dashboardEnd]);
            $allOrders = $allOrdersStmt->fetchAll();
            $itemOrderIds = array_values(array_unique(array_map(
                static fn(array $order): int => (int) $order['id'],
                $recentBills
            )));
            $allOrderItems = [];

            if ($itemOrderIds) {
                $itemPlaceholders = implode(',', array_fill(0, count($itemOrderIds), '?'));
                $allOrderItemsStmt = $db->prepare(
                    "SELECT oi.*,
                            o.billed_at AS order_billed_at,
                            COALESCE(c.name, '') AS category,
                            COALESCE(sc.name, '') AS sub_category
                     FROM order_items oi
                     JOIN orders o ON o.id = oi.order_id
                     LEFT JOIN menu_items mi ON mi.id = oi.item_id
                     LEFT JOIN categories c ON c.id = mi.category_id
                     LEFT JOIN categories sc ON sc.id = mi.sub_category_id
                     WHERE o.client_id = ?
                       AND oi.order_id IN ($itemPlaceholders)
                       AND {$billFilter}
                     ORDER BY oi.order_id, oi.id"
                );
                $allOrderItemsStmt->execute(array_merge([$clientId], $itemOrderIds));
                $allOrderItems = $allOrderItemsStmt->fetchAll();
            }
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

            foreach ($allOrderItems as &$item) {
                $item['order_billed_at'] = $this->toLocalTimestamp($item['order_billed_at'] ?? null, $timezone, $databaseTimezone);
            }
            unset($item);
        }

        // Fetch revenue by date for all time for festival comparison
        $festivalSalesList = [];
        $festivals = [];
        if ($section === 'all' || $section === 'festivals') {
            $revenueByDateStmt = $db->prepare(
                "SELECT DATE(o.billed_at) AS local_date, COALESCE(SUM(o.total_amount), 0) AS revenue
                 FROM orders o
                 WHERE o.client_id = ?
                   AND {$billFilter}
                 GROUP BY DATE(o.billed_at)"
            );
            $revenueByDateStmt->execute([$clientId]);
            $dateRevenueMap = [];
            foreach ($revenueByDateStmt->fetchAll() as $row) {
                $dateRevenueMap[$row['local_date']] = (float) $row['revenue'];
            }

            $festivals = $this->googleCalendarFestivalDates($timezone);
            $festivalSales = [];
            foreach ($festivals as $fest) {
                $name = $fest['name'];
                $date = $fest['date'];
                $year = (int) $fest['year'];
                
                if (!isset($festivalSales[$name])) {
                    $festivalSales[$name] = [
                        'name' => $name,
                        'sales' => [
                            2023 => 0.0,
                            2024 => 0.0,
                            2025 => 0.0,
                            2026 => 0.0
                        ]
                    ];
                }
                if (isset($dateRevenueMap[$date])) {
                    $festivalSales[$name]['sales'][$year] = $dateRevenueMap[$date];
                }
            }
            $festivalSalesList = array_values($festivalSales);
        }

        // --- START NEW EXACT CALCULATIONS ---
        $exactKpis = [];
        $monthlySalesList = [];
        $highLow = [];
        if ($section === 'all' || $section === 'core') {
            $selectedDateObj = new \DateTimeImmutable($originalSelectedDate, $timezone);
            
            // 1. Today Sale (for the selected date)
            $startToday = $selectedDateObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endToday = $selectedDateObj->setTime(23, 59, 59)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            
            // 2. Prev. Day Sale
            $prevDayObj = $selectedDateObj->modify('-1 day');
            $startPrevDay = $prevDayObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endPrevDay = $prevDayObj->setTime(23, 59, 59)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            
            // 3. Current Week Sale
            $thisWeekStartObj = $selectedDateObj->modify('monday this week');
            $thisWeekEndObj = $thisWeekStartObj->modify('+6 days')->setTime(23, 59, 59);
            $startThisWeek = $thisWeekStartObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endThisWeek = $thisWeekEndObj->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            
            // 4. Prev. Week Sale
            $prevWeekStartObj = $thisWeekStartObj->modify('-1 week');
            $prevWeekEndObj = $prevWeekStartObj->modify('+6 days')->setTime(23, 59, 59);
            $startPrevWeek = $prevWeekStartObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endPrevWeek = $prevWeekEndObj->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            
            // 5. Current Month Sale
            $thisMonthStartObj = $selectedDateObj->modify('first day of this month');
            $thisMonthEndObj = $thisMonthStartObj->modify('last day of this month')->setTime(23, 59, 59);
            $startThisMonth = $thisMonthStartObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endThisMonth = $thisMonthEndObj->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            
            // 6. Prev. Month Sale
            $prevMonthStartObj = $thisMonthStartObj->modify('-1 month');
            $prevMonthEndObj = $prevMonthStartObj->modify('last day of this month')->setTime(23, 59, 59);
            $startPrevMonth = $prevMonthStartObj->setTime(0, 0, 0)->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');
            $endPrevMonth = $prevMonthEndObj->setTimezone($databaseTimezone)->format('Y-m-d H:i:s');

            // PERFORMANCE: the six KPI windows, the 12-month breakdown and all
            // six high/low periods used to be 13 separate queries (two of them
            // scanning a whole year each). We now run just TWO daily-grouped
            // index-range queries and derive every window in PHP -- identical
            // numbers, a fraction of the round-trips.
            $fetchDaily = function ($start, $end) use ($db, $clientId, $billFilter) {
                $stmt = $db->prepare(
                    "SELECT DATE(billed_at) AS d,
                            COUNT(*) AS c,
                            COALESCE(SUM(total_amount), 0) AS rev,
                            COALESCE(SUM(discount_amount), 0) AS disc
                     FROM orders o
                     WHERE o.client_id = ?
                       AND billed_at >= ? AND billed_at <= ?
                       AND {$billFilter}
                     GROUP BY DATE(billed_at)"
                );
                $stmt->execute([$clientId, $start, $end]);
                $map = [];
                foreach ($stmt->fetchAll() as $row) {
                    $map[$row['d']] = [
                        'c' => (int) $row['c'],
                        'rev' => (float) $row['rev'],
                        'disc' => (float) $row['disc'],
                    ];
                }
                return $map;
            };

            // Query 1: recent window covering all six KPI ranges + week/month high-low.
            $recentStartsEnds = [
                [$startToday, $endToday], [$startPrevDay, $endPrevDay],
                [$startThisWeek, $endThisWeek], [$startPrevWeek, $endPrevWeek],
                [$startThisMonth, $endThisMonth], [$startPrevMonth, $endPrevMonth],
            ];
            $recentStart = min(array_column($recentStartsEnds, 0));
            $recentEnd = max(array_column($recentStartsEnds, 1));
            $recentDaily = $fetchDaily($recentStart, $recentEnd);

            $sumWindow = function ($start, $end) use ($recentDaily) {
                $startDay = substr($start, 0, 10);
                $endDay = substr($end, 0, 10);
                $out = ['count' => 0, 'revenue' => 0.0, 'discount' => 0.0];
                foreach ($recentDaily as $d => $row) {
                    if ($d >= $startDay && $d <= $endDay) {
                        $out['count'] += $row['c'];
                        $out['revenue'] += $row['rev'];
                        $out['discount'] += $row['disc'];
                    }
                }
                return $out;
            };

            $todayStats = $sumWindow($startToday, $endToday);
            $prevDayStats = $sumWindow($startPrevDay, $endPrevDay);
            $thisWeekStats = $sumWindow($startThisWeek, $endThisWeek);
            $prevWeekStats = $sumWindow($startPrevWeek, $endPrevWeek);
            $thisMonthStats = $sumWindow($startThisMonth, $endThisMonth);
            $prevMonthStats = $sumWindow($startPrevMonth, $endPrevMonth);

            $activeStats = $todayStats;
            if ($range === 'week') {
                $activeStats = $thisWeekStats;
            } elseif ($range === 'month') {
                $activeStats = $thisMonthStats;
            }
            $activeRevenue = (float)$activeStats['revenue'];
            $activeCount = (int)$activeStats['count'];
            $avgSpend = $activeCount > 0 ? $activeRevenue / $activeCount : 0;
            $discount = (float)$todayStats['discount'];

            // Exact KPIs Object
            $exactKpis = [
                'today_sale' => (float)$todayStats['revenue'],
                'prev_day_sale' => (float)$prevDayStats['revenue'],
                'this_week_sale' => (float)$thisWeekStats['revenue'],
                'prev_week_sale' => (float)$prevWeekStats['revenue'],
                'this_month_sale' => (float)$thisMonthStats['revenue'],
                'prev_month_sale' => (float)$prevMonthStats['revenue'],
                'avg_spend' => (float)$avgSpend,
                'discount' => (float)$discount,
                'this_week_range' => 'Week ' . $thisWeekStartObj->format('d-m-Y') . ' to ' . $thisWeekEndObj->format('d-m-Y'),
                'prev_week_range' => 'Week ' . $prevWeekStartObj->format('d-m-Y') . ' to ' . $prevWeekEndObj->format('d-m-Y'),
            ];

            // 7 + 8. Monthly sales of the selected year AND year-level high/low
            // both come from ONE two-year daily query (selected + previous
            // year); week/month high/low reuse the recent window daily map.
            $selectedYear = (int)$selectedDateObj->format('Y');
            $twoYearDaily = $fetchDaily(
                ($selectedYear - 1) . '-01-01 00:00:00',
                $selectedYear . '-12-31 23:59:59'
            );

            $monthlySalesDb = array_fill(1, 12, 0.0);
            foreach ($twoYearDaily as $d => $row) {
                if ((int)substr($d, 0, 4) === $selectedYear) {
                    $monthlySalesDb[(int)substr($d, 5, 2)] += $row['rev'];
                }
            }
            $monthlySalesList = array_values($monthlySalesDb);

            // High & Low over a period from an in-memory daily map. Tuesdays
            // (weekly closed day) are excluded, matching the old query logic.
            $periodStatsFromMap = function (array $dailyMap, string $startStr, string $endStr) {
                $startDay = substr($startStr, 0, 10);
                $endDay = substr($endStr, 0, 10);
                $maxVal = -1.0;
                $maxDate = '';
                $minVal = INF;
                $minDate = '';
                $hasData = false;

                foreach ($dailyMap as $d => $row) {
                    if ($d < $startDay || $d > $endDay) {
                        continue;
                    }
                    // Exclude Tuesdays (day 2 of the ISO week)
                    if ((int)(new \DateTimeImmutable($d))->format('N') === 2) {
                        continue;
                    }
                    $val = $row['rev'];
                    $hasData = true;
                    if ($val > $maxVal) {
                        $maxVal = $val;
                        $maxDate = $d;
                    }
                    if ($val < $minVal) {
                        $minVal = $val;
                        $minDate = $d;
                    }
                }

                return [
                    'max' => $hasData ? $maxVal : 0.0,
                    'maxDate' => $hasData ? $maxDate : '',
                    'min' => $hasData && $minVal !== INF ? $minVal : 0.0,
                    'minDate' => $hasData && $minVal !== INF ? $minDate : ''
                ];
            };

            $highLow = [
                'week' => [
                    'current' => $periodStatsFromMap($recentDaily, $startThisWeek, $endThisWeek),
                    'prev' => $periodStatsFromMap($recentDaily, $startPrevWeek, $endPrevWeek),
                ],
                'month' => [
                    'current' => $periodStatsFromMap($recentDaily, $startThisMonth, $endThisMonth),
                    'prev' => $periodStatsFromMap($recentDaily, $startPrevMonth, $endPrevMonth),
                ],
                'year' => [
                    'current' => $periodStatsFromMap($twoYearDaily, $selectedYear . '-01-01', $selectedYear . '-12-31'),
                    'prev' => $periodStatsFromMap($twoYearDaily, ($selectedYear - 1) . '-01-01', ($selectedYear - 1) . '-12-31'),
                ]
            ];
        }

        $rangeOrders = [];
        if ($section === 'all' || $section === 'bills') {
            // 9. Exact transactions (range_orders) strictly in selected range
            $rangeOrdersStmt = $db->prepare(
                "SELECT o.*, rt.table_number,
                        COALESCE(c.name, o.customer_name) AS customer_name,
                        COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
                 FROM orders o
                 LEFT JOIN customers c ON c.id = o.customer_id
                 LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
                 WHERE o.client_id = ?
                   AND o.billed_at >= ? AND o.billed_at <= ?
                   AND {$billFilter}
                 ORDER BY o.billed_at DESC, o.id DESC
                 LIMIT 500"
            );
            $rangeOrdersStmt->execute([$clientId, $dateStart, $dateEnd]);
            $rangeOrders = $rangeOrdersStmt->fetchAll();
            
            foreach ($rangeOrders as &$order) {
                $order['created_at'] = $this->toLocalTimestamp($order['created_at'] ?? null, $timezone, $databaseTimezone);
                $order['updated_at'] = $this->toLocalTimestamp($order['updated_at'] ?? null, $timezone, $databaseTimezone);
                $order['billed_at'] = $this->toLocalTimestamp($order['billed_at'] ?? null, $timezone, $databaseTimezone);
                $order['bill_prefix'] = $billPrefix;
                $order['formatted_bill_number'] = $this->formatBillNumber((int) ($order['bill_number'] ?? 0), $billPrefix);
            }
            unset($order);
        }

        $timelineData = [];
        if ($section === 'all' || $section === 'timeline') {
            if (empty($salesTimeline)) {
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
            }

            // 10. Exact timeline data
            $dailyTimelineStmt = $db->prepare(
                 "SELECT DATE(billed_at) AS local_date, COUNT(*) AS orders, COALESCE(SUM(total_amount), 0) AS revenue
                 FROM orders o
                 WHERE o.client_id = ?
                   AND billed_at >= ? AND billed_at <= ?
                   AND {$billFilter}
                 GROUP BY DATE(billed_at)
                 ORDER BY local_date"
            );
            $dailyTimelineStmt->execute([$clientId, $dateStart, $dateEnd]);
            $dailyTimeline = $dailyTimelineStmt->fetchAll();

            $formattedHourlyTimeline = array_map(function($t) {
                return [
                    'hour' => (int)$t['hour'],
                    'orders' => (int)$t['orders'],
                    'revenue' => (float)$t['revenue']
                ];
            }, $salesTimeline);

            $formattedDailyTimeline = array_map(function($t) {
                return [
                    'local_date' => $t['local_date'],
                    'orders' => (int)$t['orders'],
                    'revenue' => (float)$t['revenue']
                ];
            }, $dailyTimeline);

            $timelineData = [
                'range' => $range,
                'hourly' => $formattedHourlyTimeline,
                'daily' => $formattedDailyTimeline,
            ];
        }

        $formattedSoldItems = [];
        if ($section === 'all' || $section === 'items') {
            // 11. Exact sold items grouped BY MENU ITEM (oi.item_id), not by the
            // name printed on the bill. Bills keep the item name as it was AT
            // SALE TIME, so a renamed item (e.g. "[F]" -> "[फुल]") appeared as
            // two separate rows in month view. Grouping by the menu item id
            // merges those, displaying the CURRENT menu name; rows still split
            // by sold price (oi.price) so the Rate column stays truthful across
            // a mid-period price change. Extra/custom items (item_id NULL)
            // still group by their sold name.
            $allSoldItemsStmt = $db->prepare(
                'SELECT COALESCE(mi.name, oi.item_name) AS item_name,
                        "" AS item_code,
                        COALESCE(c.name, "No Category") AS category,
                        COALESCE(sc.name, "") AS sub_category,
                        oi.price AS price,
                        SUM(oi.quantity) AS qty,
                        SUM(oi.total) AS amount
                 FROM order_items oi
                 JOIN orders o ON o.id = oi.order_id
                 LEFT JOIN menu_items mi ON mi.id = oi.item_id
                 LEFT JOIN categories c ON c.id = mi.category_id
                 LEFT JOIN categories sc ON sc.id = mi.sub_category_id
                 WHERE o.client_id = ?
                   AND o.billed_at >= ? AND o.billed_at <= ?
                   AND ' . $billFilter . '
                 GROUP BY COALESCE(mi.id, 0),
                          CASE WHEN mi.id IS NULL THEN oi.item_name ELSE "" END,
                          COALESCE(mi.name, oi.item_name),
                          "",
                          COALESCE(c.name, "No Category"),
                          COALESCE(sc.name, ""),
                          oi.price
                 ORDER BY qty DESC'
            );
            $allSoldItemsStmt->execute([$clientId, $dateStart, $dateEnd]);
            $allSoldItems = $allSoldItemsStmt->fetchAll();

            $formattedSoldItems = array_map(function($row) {
                return [
                    'name' => $row['item_name'],
                    'category' => $row['category'],
                    'sub_category' => $row['sub_category'],
                    'price' => (float)$row['price'],
                    'qty' => (float)$row['qty'],
                    'amount' => (float)$row['amount']
                ];
            }, $allSoldItems);
        }
        // --- END NEW EXACT CALCULATIONS ---

        success_response([
            'orders' => $orders,
            'today' => $today,
            'top_items' => $topItems,
            'recent_bills' => $recentBills,
            'sales_timeline' => $salesTimeline,
            'all_orders' => $allOrders,
            'order_items' => $allOrderItems,
            'gst_rates' => $gstRates,
            'festival_dates' => $festivals,
            'festival_sales' => $festivalSalesList,
            'kpis' => $exactKpis,
            'monthly_sales' => $monthlySalesList,
            'high_low' => $highLow,
            'range_orders' => $rangeOrders,
            'timeline_data' => $timelineData,
            'all_sold_items' => $formattedSoldItems,
            'last_order_id' => $maxOrderId
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
        $cacheFile = sys_get_temp_dir() . '/cafepos-indian-holidays.ics';
        if (is_file($cacheFile) && (time() - (int) filemtime($cacheFile)) < 86400) {
            return (string) file_get_contents($cacheFile);
        }

        if (function_exists('curl_init')) {
            $curl = curl_init($url);
            curl_setopt_array($curl, [
                CURLOPT_RETURNTRANSFER => true,
                CURLOPT_FOLLOWLOCATION => true,
                CURLOPT_CONNECTTIMEOUT => 1,
                CURLOPT_TIMEOUT => 2,
                CURLOPT_USERAGENT => 'CafePOS/1.0',
            ]);
            $body = curl_exec($curl);
            $status = (int) curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
            curl_close($curl);

            if (is_string($body) && $status >= 200 && $status < 300) {
                @file_put_contents($cacheFile, $body);
                return $body;
            }

            return '';
        }

        $context = stream_context_create([
            'http' => [
                'timeout' => 2,
                'header' => "User-Agent: CafePOS/1.0\r\n",
            ],
        ]);
        $body = @file_get_contents($url, false, $context);

        if (is_string($body)) {
            @file_put_contents($cacheFile, $body);
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
            . ' -TimeoutSec 2; $response.Content';
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
