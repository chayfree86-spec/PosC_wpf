<?php

declare(strict_types=1);

use App\Controllers\PrintController;

require dirname(__DIR__) . '/_bootstrap.php';

(new PrintController())->print();
