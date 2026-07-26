<?php

declare(strict_types=1);

use App\Controllers\QzController;

require dirname(__DIR__, 2) . '/_bootstrap.php';

(new QzController())->sign();
