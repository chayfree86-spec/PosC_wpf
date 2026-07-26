<?php

declare(strict_types=1);

use App\Controllers\QzController;

require __DIR__ . '/_bootstrap.php';

(new QzController())->sign();
