<?php

namespace App\Services;

class SyncService
{
    public static function nextVersion(?int $current): int
    {
        return max(1, (int) $current + 1);
    }
}
