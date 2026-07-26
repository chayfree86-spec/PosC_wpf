<?php

namespace App\Core;

class Router
{
    private array $routes = [];

    public function get(string $path, array|callable $handler): void
    {
        $this->add('GET', $path, $handler);
    }

    public function post(string $path, array|callable $handler): void
    {
        $this->add('POST', $path, $handler);
    }

    public function put(string $path, array|callable $handler): void
    {
        $this->add('PUT', $path, $handler);
    }

    public function patch(string $path, array|callable $handler): void
    {
        $this->add('PATCH', $path, $handler);
    }

    public function delete(string $path, array|callable $handler): void
    {
        $this->add('DELETE', $path, $handler);
    }

    private function add(string $method, string $path, array|callable $handler): void
    {
        $pattern = preg_replace('#\{([a-zA-Z_][a-zA-Z0-9_]*)\}#', '(?P<$1>[^/]+)', $path);
        $this->routes[] = [
            'method' => $method,
            'path' => $path,
            'pattern' => '#^' . $pattern . '$#',
            'handler' => $handler,
        ];
    }

    public function dispatch(string $method, string $uri): void
    {
        $path = parse_url($uri, PHP_URL_PATH) ?: '/';
        $scriptDir = rtrim(str_replace('\\', '/', dirname($_SERVER['SCRIPT_NAME'] ?? '')), '/');

        if ($scriptDir && str_starts_with($path, $scriptDir)) {
            $path = substr($path, strlen($scriptDir)) ?: '/';
        }

        if (str_starts_with($path, '/index.php/')) {
            $path = substr($path, strlen('/index.php')) ?: '/';
        } elseif ($path === '/index.php') {
            $path = '/';
        }

        foreach ($this->routes as $route) {
            if ($route['method'] !== $method) {
                continue;
            }

            if (!preg_match($route['pattern'], $path, $matches)) {
                continue;
            }

            $params = array_filter($matches, 'is_string', ARRAY_FILTER_USE_KEY);
            $handler = $route['handler'];

            if (is_array($handler)) {
                [$class, $action] = $handler;
                $handler = [new $class(), $action];
            }

            call_user_func_array($handler, $params);
            return;
        }

        error_response('Route not found.', 404);
    }
}
