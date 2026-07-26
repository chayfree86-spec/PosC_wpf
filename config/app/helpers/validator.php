<?php

function validate_required(array $data, array $fields): array
{
    $errors = [];

    foreach ($fields as $field) {
        if (!array_key_exists($field, $data)) {
            $errors[$field] = ucfirst(str_replace('_', ' ', $field)) . ' is required.';
            continue;
        }

        $value = $data[$field];
        $isMissing = is_array($value) ? $value === [] : trim((string) $value) === '';

        if ($isMissing) {
            $errors[$field] = ucfirst(str_replace('_', ' ', $field)) . ' is required.';
        }
    }

    return $errors;
}
