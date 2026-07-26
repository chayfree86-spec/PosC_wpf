<?php

namespace App\Controllers;

class PrintController
{
    public function printers(): void
    {
        if (PHP_OS_FAMILY !== 'Windows') {
            json_response([
                'ok' => false,
                'printers' => [],
                'error' => 'Direct system printer listing is supported on Windows only.',
            ]);
        }

        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'
Get-CimInstance Win32_Printer |
  Sort-Object Name |
  ForEach-Object {
    [PSCustomObject]@{
      name = $_.Name
      default = [bool]$_.Default
    }
  } |
  ConvertTo-Json -Compress
PS1;

        $result = $this->runPowerShell($script);
        if (!$result['ok']) {
            json_response([
                'ok' => false,
                'printers' => [],
                'error' => $result['error'],
            ]);
        }

        $decoded = json_decode($result['output'], true);
        if (!is_array($decoded)) {
            $decoded = [];
        }

        $rows = $this->isList($decoded) ? $decoded : [$decoded];
        $printers = array_values(array_filter(array_map(
            static fn ($row) => is_array($row) ? (string) ($row['name'] ?? '') : '',
            $rows
        )));

        json_response([
            'ok' => true,
            'printers' => $printers,
            'details' => $rows,
        ]);
    }

    public function print(): void
    {
        if (PHP_OS_FAMILY !== 'Windows') {
            error_response('Direct thermal printing is supported on Windows/XAMPP only.', 500);
        }

        $data = request_json();
        $printerName = trim((string) ($data['printerName'] ?? ''));
        $content = (string) ($data['content'] ?? '');
        $rawData = (string) ($data['rawData'] ?? '');
        $imageDataUrl = (string) ($data['imageDataUrl'] ?? '');
        $copies = max(1, min(10, (int) ($data['copies'] ?? 1)));
        $paperSize = '80mm';

        if ($printerName === '') {
            error_response('Printer name is required.', 422);
        }

        if (trim($content) === '' && trim($imageDataUrl) === '' && trim($rawData) === '') {
            error_response('Print content is required.', 422);
        }

        if (trim($imageDataUrl) !== '') {
            $imageFile = $this->writeImageDataUrl($imageDataUrl);
            if ($imageFile !== null) {
                try {
                    $result = $this->printImage($printerName, $imageFile, $copies, $paperSize);
                } finally {
                    @unlink($imageFile);
                }

                if (!$result['ok']) {
                    error_response($result['error'] ?: 'Direct image print failed.', 500);
                }

                json_response([
                    'ok' => true,
                    'printerName' => $printerName,
                ]);
            }

            if (trim($content) === '' && trim($rawData) === '') {
                error_response('Valid print image data is required.', 422);
            }
        }

        if (trim($rawData) !== '') {
            $rawFile = $this->writeRawData($rawData);
            if ($rawFile !== null) {
                if (!$this->rawDataHasPrintableOutput($rawFile)) {
                    @unlink($rawFile);
                    if (trim($content) === '' && trim($imageDataUrl) === '') {
                        error_response('Print content is required.', 422);
                    }
                } else {
                    try {
                        $result = $this->printRaw($printerName, $rawFile, $copies);
                    } finally {
                        @unlink($rawFile);
                    }

                    if ($result['ok']) {
                        json_response([
                            'ok' => true,
                            'printerName' => $printerName,
                        ]);
                    }
                }
            } elseif (trim($content) === '' && trim($imageDataUrl) === '') {
                error_response('Valid raw print data is required.', 422);
            }
        }

        if (trim($content) === '') {
            error_response('Print content is required.', 422);
        }

        $qrImageDataUrl = (string) ($data['qrImageDataUrl'] ?? '');
        $qrImageFile = null;
        if (trim($qrImageDataUrl) !== '') {
            $qrImageFile = $this->writeImageDataUrl($qrImageDataUrl);
        }

        $textFile = tempnam(sys_get_temp_dir(), 'pos_print_');
        if ($textFile === false) {
            if ($qrImageFile !== null) {
                @unlink($qrImageFile);
            }
            error_response('Unable to create print buffer.', 500);
        }

        file_put_contents($textFile, "\xEF\xBB\xBF" . $content);

        $printSuccess = false;
        $printError = '';

        try {
            $rasterResult = $this->printTextRasterRaw($printerName, $textFile, $copies, $paperSize, $qrImageFile);
            if ($rasterResult['ok']) {
                $printSuccess = true;
            } else {
                $printError = $rasterResult['error'] ?: 'RAW raster print failed.';
            }
        } catch (\Throwable $e) {
            $printError = $e->getMessage() ?: 'RAW raster print failed.';
        }

        if ($printSuccess) {
            @unlink($textFile);
            if ($qrImageFile !== null) {
                @unlink($qrImageFile);
            }
            json_response([
                'ok' => true,
                'printerName' => $printerName,
            ]);
        }

        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'

$printerName = $env:POS_PRINTER_NAME
$filePath = $env:POS_PRINT_FILE
$copies = [int]$env:POS_PRINT_COPIES
$paperSize = $env:POS_PRINT_PAPER

Add-Type -AssemblyName System.Drawing

$content = Get-Content -LiteralPath $filePath -Raw -Encoding UTF8
$script:lines = $content -split "`r?`n"
$script:lineIndex = 0
$installedFonts = New-Object System.Drawing.Text.InstalledFontCollection
$fontNames = @($installedFonts.Families | ForEach-Object { $_.Name })
$script:fontFamily = @('Google Sans Devanagari', 'Google Sans', 'Nirmala UI', 'Mangal', 'Arial') | Where-Object { $fontNames -contains $_ } | Select-Object -First 1
if (-not $script:fontFamily) {
    $script:fontFamily = 'Arial'
}

$qrImagePath = $env:POS_PRINT_QR_IMAGE
$script:qrImage = $null
$script:qrHeight = 0
if ($qrImagePath -and (Test-Path $qrImagePath)) {
    try {
        $script:qrImage = [System.Drawing.Image]::FromFile($qrImagePath)
        $script:qrHeight = if ($paperSize -match '58') { 140 } else { 180 }
    } catch {}
}

$doc = New-Object System.Drawing.Printing.PrintDocument
$doc.PrinterSettings.PrinterName = $printerName
$doc.PrinterSettings.Copies = [int16]$copies

if (-not $doc.PrinterSettings.IsValid) {
    throw "Printer not found or not available: $printerName"
}

$width = if ($paperSize -match '58') { 228 } else { 315 }
$estimatedLineHeight = 24
$estimatedExtraWrapLines = 0
foreach ($line in $script:lines) {
    $trimmedLine = ([string]$line).Trim()
    if ($trimmedLine.Length -gt 24) {
        $estimatedExtraWrapLines += [Math]::Floor($trimmedLine.Length / 24)
    }
}
$estimatedHeight = [Math]::Max(220, [Math]::Min(32760, (($script:lines.Length + $estimatedExtraWrapLines + 4) * $estimatedLineHeight) + $script:qrHeight + 20))
$receiptPaper = New-Object System.Drawing.Printing.PaperSize('Receipt', $width, $estimatedHeight)
$doc.DefaultPageSettings.PaperSize = $receiptPaper
$doc.PrinterSettings.DefaultPageSettings.PaperSize = $receiptPaper
$doc.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(4, 4, 4, 4)
$doc.PrinterSettings.DefaultPageSettings.Margins = $doc.DefaultPageSettings.Margins
$doc.PrintController = New-Object System.Drawing.Printing.StandardPrintController

$script:font = New-Object System.Drawing.Font($script:fontFamily, 10, [System.Drawing.FontStyle]::Regular)
$script:smallFont = New-Object System.Drawing.Font($script:fontFamily, 8.5, [System.Drawing.FontStyle]::Regular)
$script:boldFont = New-Object System.Drawing.Font($script:fontFamily, 10, [System.Drawing.FontStyle]::Bold)
$script:headerFont = New-Object System.Drawing.Font($script:fontFamily, 11, [System.Drawing.FontStyle]::Bold)
$script:brush = [System.Drawing.Brushes]::Black
$script:rightFormat = New-Object System.Drawing.StringFormat
$script:rightFormat.Alignment = [System.Drawing.StringAlignment]::Far
$script:rightFormat.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
$script:centerFormat = New-Object System.Drawing.StringFormat
$script:centerFormat.Alignment = [System.Drawing.StringAlignment]::Center
$script:separatorCount = 0
$script:lastKotItem = $false

$doc.add_PrintPage({
    param($sender, $event)

    $graphics = $event.Graphics
    $graphics.PageUnit = [System.Drawing.GraphicsUnit]::Display
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit

    $x = $event.MarginBounds.Left
    $y = $event.MarginBounds.Top
    $width = $event.MarginBounds.Width
    $right = $event.MarginBounds.Right
    $lineHeight = [Math]::Ceiling($script:font.GetHeight($graphics)) + 4
    $maxY = $event.MarginBounds.Bottom
    $qtyWidth = if ($paperSize -match '58') { 38 } else { 42 }
    $amountWidth = if ($paperSize -match '58') { 72 } else { 88 }
    $columnGap = if ($paperSize -match '58') { 8 } else { 12 }
    $amountRight = $right - 40
    $amountLeft = $amountRight - $amountWidth
    $qtyLeft = $amountLeft - $columnGap - $qtyWidth

    while ($script:lineIndex -lt $script:lines.Length) {
        $line = [string]$script:lines[$script:lineIndex]
        $rupeeSymbol = [string][char]0x20B9
        $mojibakeRupee = ([string][char]0x00E2) + ([string][char]0x201A) + ([string][char]0x00B9)
        $trimmed = $line.Trim().Replace($rupeeSymbol, 'Rs.').Replace($mojibakeRupee, 'Rs.')

        if ($trimmed -match '^-{6,}$') {
            $script:lastKotItem = $false
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 1)
            $pen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
            $graphics.DrawLine($pen, $x, $y + 4, $right, $y + 4)
            $pen.Dispose()
            $script:separatorCount++
            $y += 2
            $script:lineIndex++
            continue
        }

        if ($trimmed -eq '') {
            $script:lastKotItem = $false
            $y += [Math]::Round($lineHeight * 0.5)
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^(\d{2}/\d{2}/\d{4}(?:\s+\S+)?)\s+(\d{1,2}:\d{2}\s*(?:AM|PM)?)$') {
            $script:lastKotItem = $false
            $dateText = $matches[1]
            $timeText = $matches[2]
            $dateSize = $graphics.MeasureString($dateText, $script:smallFont)
            $timeSize = $graphics.MeasureString($timeText, $script:headerFont)
            $gap = 5
            $groupWidth = $dateSize.Width + $gap + $timeSize.Width
            $startX = $event.MarginBounds.Left + (($event.MarginBounds.Width - $groupWidth) / 2)
            $dateY = $y + [Math]::Max(0, ($lineHeight - $dateSize.Height) / 2)
            $graphics.DrawString($dateText, $script:smallFont, $script:brush, [float]$startX, [float]$dateY)
            $graphics.DrawString($timeText, $script:headerFont, $script:brush, [float]($startX + $dateSize.Width + $gap), [float]$y)
            $y += $lineHeight
            $script:lineIndex++
            continue
        }

        if ($trimmed -eq 'KOT' -or $trimmed -match '^Table No:') {
            $script:lastKotItem = $false
            $centerRect = [System.Drawing.RectangleF]::new([float]$event.MarginBounds.Left, [float]$y, [float]$event.MarginBounds.Width, [float]($lineHeight + 6))
            $graphics.DrawString($trimmed, $script:headerFont, $script:brush, $centerRect, $script:centerFormat)
            $y += $lineHeight - 2
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^(Bill No:\s*\S+)\s+(.+)$') {
            $script:lastKotItem = $false
            $billRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]($width * 0.56), [float]($lineHeight + 4))
            $tableRightPad = 36
            $tableRect = [System.Drawing.RectangleF]::new([float]($x + ($width * 0.56)), [float]$y, [float]($width * 0.40 - $tableRightPad), [float]($lineHeight + 4))
            $tableText = $matches[2]
            $graphics.DrawString($matches[1], $script:font, $script:brush, $billRect)
            $graphics.DrawString($tableText, $script:font, $script:brush, $tableRect, $script:rightFormat)
            $y += $lineHeight
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^GRAND TOTAL:\s*(.*)$') {
            $script:lastKotItem = $false
            $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + 6))
            $graphics.DrawString('GRAND TOTAL:', $script:boldFont, $script:brush, $x, $y)
            if ($matches[1].Trim() -ne '') {
                $graphics.DrawString($matches[1].Trim(), $script:boldFont, $script:brush, $amountRect, $script:rightFormat)
            }
            $y += $lineHeight + 3
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^Total\s+(\d+)\s+(.+)$') {
            $script:lastKotItem = $false
            $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + 4))
            $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + 4))
            $graphics.DrawString('Total', $script:boldFont, $script:brush, [float]$x, [float]$y)
            $graphics.DrawString($matches[1], $script:boldFont, $script:brush, $qtyRect, $script:rightFormat)
            $graphics.DrawString($matches[2], $script:boldFont, $script:brush, [float]$amountRight, [float]$y, $script:rightFormat)
            $y += $lineHeight + 3
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^\s*Item\s+Q\s+T\s*$') {
            $script:lastKotItem = $false
            $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + 4))
            $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + 4))
            $graphics.DrawString('Item', $script:boldFont, $script:brush, $x, $y)
            $graphics.DrawString('Q', $script:boldFont, $script:brush, $qtyRect, $script:rightFormat)
            $graphics.DrawString('T', $script:boldFont, $script:brush, [float]$amountRight, [float]$y, $script:rightFormat)
            $y += $lineHeight
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^(.+?)\s+(\d{1,4})\s+((?:Rs\.\s*)?[0-9,]+(?:\.[0-9]+)?)$' -and $script:separatorCount -ge 2) {
            $script:lastKotItem = $false
            $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + 4))
            $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + 4))
            $itemWidth = [Math]::Max(70, $qtyLeft - $x - $columnGap)
            $itemText = $matches[1].Trim()
            $itemSize = $graphics.MeasureString($itemText, $script:font, [int]$itemWidth)
            $rowHeight = [Math]::Max(lineHeight + 2, [Math]::Ceiling($itemSize.Height) + 2)
            $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$itemWidth, [float]($rowHeight + 2))
            $graphics.DrawString($matches[1].Trim(), $script:font, $script:brush, $itemRect)
            $graphics.DrawString($matches[2], $script:font, $script:brush, $qtyRect, $script:rightFormat)
            $graphics.DrawString($matches[3], $script:font, $script:brush, [float]$amountRight, [float]$y, $script:rightFormat)
            $y += $rowHeight
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^(\d+\.\s*.*?)\s+(\d{1,4})$') {
            $kotQtyWidth = if ($paperSize -match '58') { 38 } else { 50 }
            $kotQtyRight = $right - 40
            $kotQtyLeft = $kotQtyRight - $kotQtyWidth
            $kotItemWidth = [Math]::Max(80, $kotQtyLeft - $x - $columnGap)
            $kotItemText = $matches[1].Trim()
            $kotItemSize = $graphics.MeasureString($kotItemText, $script:boldFont, [int]$kotItemWidth)
            $kotRowHeight = [Math]::Max($lineHeight + 3, [Math]::Ceiling($kotItemSize.Height) + 3)
            $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + 2))
            $qtyText = $matches[2]
            $qtySize = $graphics.MeasureString($qtyText, $script:boldFont)
            $qtyX = [Math]::Max($kotQtyLeft, $kotQtyRight - $qtySize.Width)
            $graphics.DrawString($matches[1].Trim(), $script:boldFont, $script:brush, $itemRect)
            $graphics.DrawString($qtyText, $script:boldFont, $script:brush, [float]$qtyX, [float]$y)
            $y += $kotRowHeight
            $script:lastKotItem = $true
            $script:lineIndex++
            continue
        }

        if ($trimmed -match '^(.+?)\s+(\d{1,4})$' -and -not ($trimmed -match '^(Note:|Table:|Date:|Bill No:|Ref:|Total:|GRAND TOTAL:|Mob\.|Tel:|GST:|Item Name)')) {
            $kotQtyWidth = if ($paperSize -match '58') { 38 } else { 50 }
            $kotQtyRight = $right - 40
            $kotQtyLeft = $kotQtyRight - $kotQtyWidth
            $kotItemWidth = [Math]::Max(80, $kotQtyLeft - $x - $columnGap)
            $kotItemText = $matches[1].Trim()
            $qtyText = $matches[2].Trim()
            $kotItemSize = $graphics.MeasureString($kotItemText, $script:boldFont, [int]$kotItemWidth)
            $kotRowHeight = [Math]::Max($lineHeight + 3, [Math]::Ceiling($kotItemSize.Height) + 3)
            $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + 2))
            $qtySize = $graphics.MeasureString($qtyText, $script:boldFont)
            $qtyX = [Math]::Max($kotQtyLeft, $kotQtyRight - $qtySize.Width)
            $graphics.DrawString($kotItemText, $script:boldFont, $script:brush, $itemRect)
            $graphics.DrawString($qtyText, $script:boldFont, $script:brush, [float]$qtyX, [float]$y)
            $y += $kotRowHeight
            $script:lastKotItem = $true
            $script:lineIndex++
            continue
        }

        if ($script:lastKotItem) {
            $kotQtyWidth = if ($paperSize -match '58') { 38 } else { 50 }
            $kotQtyRight = $right - 40
            $kotQtyLeft = $kotQtyRight - $kotQtyWidth
            $kotItemWidth = [Math]::Max(80, $kotQtyLeft - $x - $columnGap)
            $kotItemSize = $graphics.MeasureString($trimmed, $script:boldFont, [int]$kotItemWidth)
            $kotRowHeight = [Math]::Max($lineHeight + 3, [Math]::Ceiling($kotItemSize.Height) + 3)
            $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + 2))
            $graphics.DrawString($trimmed, $script:boldFont, $script:brush, $itemRect)
            $y += $kotRowHeight
            $script:lineIndex++
            continue
        }

        $script:lastKotItem = $false
        if ($script:separatorCount -lt 2) {
            $centerRect = [System.Drawing.RectangleF]::new([float]$event.MarginBounds.Left, [float]$y, [float]$event.MarginBounds.Width, [float]($lineHeight + 6))
            if ($trimmed -match '^(Mob\.|Tel:|GST:)') {
                $graphics.DrawString($trimmed, $script:smallFont, $script:brush, $centerRect, $script:centerFormat)
            } else {
                $graphics.DrawString($trimmed, $script:headerFont, $script:brush, $centerRect, $script:centerFormat)
            }
        } else {
            $graphics.DrawString($trimmed, $script:font, $script:brush, $x, $y)
        }
        $y += $lineHeight
        $script:lineIndex++
    }

    if ($script:lineIndex -ge $script:lines.Length -and $script:qrImage) {
        $qrDrawSize = $script:qrHeight
        $qrX = [Math]::Round(($event.MarginBounds.Width - $qrDrawSize) / 2) + $event.MarginBounds.Left
        $graphics.DrawString(" ", $script:font, $script:brush, $x, $y)
        $y += 10
        $graphics.DrawImage($script:qrImage, $qrX, $y, $qrDrawSize, $qrDrawSize)
        $y += $qrDrawSize
    }

    $event.HasMorePages = $false
})

try {
    $doc.Print()
} finally {
    if ($script:qrImage) { $script:qrImage.Dispose() }
    $script:font.Dispose()
    $script:smallFont.Dispose()
    $script:boldFont.Dispose()
    $script:headerFont.Dispose()
    $script:rightFormat.Dispose()
    $script:centerFormat.Dispose()
    $doc.Dispose()
}
PS1;

        $env = [
            'POS_PRINTER_NAME' => $printerName,
            'POS_PRINT_FILE' => $textFile,
            'POS_PRINT_COPIES' => (string) $copies,
            'POS_PRINT_PAPER' => $paperSize,
        ];
        if ($qrImageFile !== null) {
            $env['POS_PRINT_QR_IMAGE'] = $qrImageFile;
        }

        try {
            $result = $this->runPowerShell($script, $env);
        } finally {
            @unlink($textFile);
            if ($qrImageFile !== null) {
                @unlink($qrImageFile);
            }
        }

        if (!$result['ok']) {
            error_response($result['error'] ?: ($printError ?: 'Direct print failed.'), 500);
        }

        json_response([
            'ok' => true,
            'printerName' => $printerName,
        ]);
    }

    private function writeRawData(string $rawData): ?string
    {
        $binary = base64_decode(preg_replace('/\s+/', '', $rawData), true);
        if ($binary === false || $binary === '') {
            return null;
        }

        $rawFile = tempnam(sys_get_temp_dir(), 'pos_print_raw_');
        if ($rawFile === false) {
            return null;
        }

        file_put_contents($rawFile, $binary);
        return $rawFile;
    }

    private function rawDataHasPrintableOutput(string $rawFile): bool
    {
        $bytes = file_get_contents($rawFile);
        if ($bytes === false || $bytes === '') {
            return false;
        }

        $length = strlen($bytes);
        for ($i = 0; $i < $length; $i++) {
            $byte = ord($bytes[$i]);

            if (
                $byte === 0x1D &&
                $i + 10 < $length &&
                ord($bytes[$i + 1]) === 0x76 &&
                ord($bytes[$i + 2]) === 0x30
            ) {
                return true;
            }

            if ($byte === 0x1D && $i + 1 < $length && ord($bytes[$i + 1]) === 0x56) {
                $i += 3;
                continue;
            }

            if ($byte === 0x1B) {
                $next = $i + 1 < $length ? ord($bytes[$i + 1]) : 0;
                $i += $next === 0x40 ? 1 : 2;
                continue;
            }

            if ($byte === 0x0A || $byte === 0x0D || $byte === 0x00) {
                continue;
            }

            if ($byte >= 0x21 && $byte <= 0x7E) {
                return true;
            }
        }

        return false;
    }

    private function printRaw(string $printerName, string $rawFile, int $copies): array
    {
        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'

$printerName = $env:POS_PRINTER_NAME
$rawPath = $env:POS_PRINT_RAW
$copies = [Math]::Max(1, [int]$env:POS_PRINT_COPIES)

$source = @"
using System;
using System.IO;
using System.Runtime.InteropServices;

public class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

    public static void SendBytes(string printerName, byte[] bytes)
    {
        IntPtr hPrinter;
        if (!OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        DOCINFOA di = new DOCINFOA();
        di.pDocName = "POS Raw Receipt";
        di.pDataType = "RAW";

        IntPtr unmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
        try {
            Marshal.Copy(bytes, 0, unmanagedBytes, bytes.Length);
            if (!StartDocPrinter(hPrinter, 1, di)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (!StartPagePrinter(hPrinter)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            int written;
            if (!WritePrinter(hPrinter, unmanagedBytes, bytes.Length, out written)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
        } finally {
            Marshal.FreeCoTaskMem(unmanagedBytes);
            ClosePrinter(hPrinter);
        }
    }
}
"@

Add-Type -TypeDefinition $source
$bytes = [System.IO.File]::ReadAllBytes($rawPath)
for ($i = 0; $i -lt $copies; $i++) {
    [RawPrinterHelper]::SendBytes($printerName, $bytes)
}
PS1;

        return $this->runPowerShell($script, [
            'POS_PRINTER_NAME' => $printerName,
            'POS_PRINT_RAW' => $rawFile,
            'POS_PRINT_COPIES' => (string) $copies,
        ]);
    }

    private function writeImageDataUrl(string $imageDataUrl): ?string
    {
        if (!preg_match('/^data:image\/(?:png|jpeg|jpg);base64,([A-Za-z0-9+\/=\r\n]+)$/', trim($imageDataUrl), $matches)) {
            return null;
        }

        $binary = base64_decode(preg_replace('/\s+/', '', $matches[1]), true);
        if ($binary === false || $binary === '') {
            return null;
        }

        $imageFile = tempnam(sys_get_temp_dir(), 'pos_print_img_');
        if ($imageFile === false) {
            return null;
        }

        $imagePath = $imageFile . '.png';
        @rename($imageFile, $imagePath);
        file_put_contents($imagePath, $binary);

        return $imagePath;
    }

    private function printImage(string $printerName, string $imageFile, int $copies, string $paperSize): array
    {
        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'

$printerName = $env:POS_PRINTER_NAME
$imagePath = $env:POS_PRINT_IMAGE
$copies = [int]$env:POS_PRINT_COPIES
$paperSize = $env:POS_PRINT_PAPER

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Image]::FromFile($imagePath)
$script:image = New-Object System.Drawing.Bitmap($sourceImage.Width, $sourceImage.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$canvas = [System.Drawing.Graphics]::FromImage($script:image)
$canvas.Clear([System.Drawing.Color]::White)
$canvas.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
$canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$canvas.DrawImage($sourceImage, 0, 0, $sourceImage.Width, $sourceImage.Height)
$canvas.Dispose()
$sourceImage.Dispose()

$doc = New-Object System.Drawing.Printing.PrintDocument
$doc.PrinterSettings.PrinterName = $printerName
$doc.PrinterSettings.Copies = [int16]$copies

if (-not $doc.PrinterSettings.IsValid) {
    throw "Printer not found or not available: $printerName"
}

$width = if ($paperSize -match '58') { 228 } else { 315 }
$scale = ($width - 8) / [double]$script:image.Width
$height = [Math]::Max(220, [Math]::Min(32760, [Math]::Ceiling($script:image.Height * $scale) + 24))

$receiptPaper = New-Object System.Drawing.Printing.PaperSize('Receipt', $width, $height)
$doc.DefaultPageSettings.PaperSize = $receiptPaper
$doc.PrinterSettings.DefaultPageSettings.PaperSize = $receiptPaper
$doc.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(4, 4, 4, 4)
$doc.PrinterSettings.DefaultPageSettings.Margins = $doc.DefaultPageSettings.Margins
$doc.PrintController = New-Object System.Drawing.Printing.StandardPrintController

$doc.add_PrintPage({
    param($sender, $event)

    $graphics = $event.Graphics
    $graphics.PageUnit = [System.Drawing.GraphicsUnit]::Display
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

    $bounds = $event.MarginBounds
    $drawWidth = $bounds.Width
    $drawHeight = [Math]::Ceiling($script:image.Height * ($drawWidth / [double]$script:image.Width))
    $rect = New-Object System.Drawing.Rectangle($bounds.Left, $bounds.Top, $drawWidth, $drawHeight)

    $graphics.DrawImage($script:image, $rect)
    $event.HasMorePages = $false
})

try {
    $doc.Print()
} finally {
    $script:image.Dispose()
    $doc.Dispose()
}
PS1;

        return $this->runPowerShell($script, [
            'POS_PRINTER_NAME' => $printerName,
            'POS_PRINT_IMAGE' => $imageFile,
            'POS_PRINT_COPIES' => (string) $copies,
            'POS_PRINT_PAPER' => $paperSize,
        ]);
    }

    private function printTextRasterRaw(string $printerName, string $textFile, int $copies, string $paperSize, ?string $qrImageFile = null): array
    {
        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'

$printerName = $env:POS_PRINTER_NAME
$filePath = $env:POS_PRINT_FILE
$copies = [Math]::Max(1, [int]$env:POS_PRINT_COPIES)
$paperSize = $env:POS_PRINT_PAPER

Add-Type -AssemblyName System.Drawing

$content = Get-Content -LiteralPath $filePath -Raw -Encoding UTF8
$lines = $content -split "`r?`n"
$installedFonts = New-Object System.Drawing.Text.InstalledFontCollection
$fontNames = @($installedFonts.Families | ForEach-Object { $_.Name })
$fontFamily = @('Google Sans Devanagari', 'Google Sans', 'Nirmala UI', 'Mangal', 'Arial') | Where-Object { $fontNames -contains $_ } | Select-Object -First 1
if (-not $fontFamily) {
    $fontFamily = 'Arial'
}

$baseWidth = 315.0
$bitmapWidth = if ($paperSize -match '58') { 384 } else { 576 }
$scale = $bitmapWidth / $baseWidth
$estimatedLineHeight = [Math]::Ceiling(24 * $scale)
$estimatedExtraWrapLines = 0
foreach ($line in $lines) {
    $trimmedLine = ([string]$line).Trim()
    if ($trimmedLine.Length -gt 24) {
        $estimatedExtraWrapLines += [Math]::Floor($trimmedLine.Length / 24)
    }
}

$qrImagePath = $env:POS_PRINT_QR_IMAGE
$qrImage = $null
$qrHeight = 0
if ($qrImagePath -and (Test-Path $qrImagePath)) {
    try {
        $qrImage = [System.Drawing.Image]::FromFile($qrImagePath)
        $qrHeight = if ($paperSize -match '58') { [Math]::Round(140 * $scale) } else { [Math]::Round(180 * $scale) }
    } catch {}
}

$bitmapHeight = [Math]::Max(220, [Math]::Min(3000, (($lines.Length + $estimatedExtraWrapLines + 5) * $estimatedLineHeight) + $qrHeight + [Math]::Round(20 * $scale)))

$bitmap = New-Object System.Drawing.Bitmap($bitmapWidth, $bitmapHeight, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::White)
$graphics.PageUnit = [System.Drawing.GraphicsUnit]::Pixel
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$font = New-Object System.Drawing.Font($fontFamily, [float](10 * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$smallFont = New-Object System.Drawing.Font($fontFamily, [float](8.5 * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$boldFont = New-Object System.Drawing.Font($fontFamily, [float](10 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$headerFont = New-Object System.Drawing.Font($fontFamily, [float](11 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.Brushes]::Black
$rightFormat = New-Object System.Drawing.StringFormat
$rightFormat.Alignment = [System.Drawing.StringAlignment]::Far
$rightFormat.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
$centerFormat = New-Object System.Drawing.StringFormat
$centerFormat.Alignment = [System.Drawing.StringAlignment]::Center
$centerFormat.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip

$x = [Math]::Round(4 * $scale)
$y = [Math]::Round(4 * $scale)
$right = $bitmapWidth - [Math]::Round(4 * $scale)
$width = $right - $x
$lineHeight = [Math]::Ceiling($font.GetHeight($graphics)) + [Math]::Round(4 * $scale)
$qtyWidth = [Math]::Round(42 * $scale)
$amountWidth = [Math]::Round(88 * $scale)
$columnGap = [Math]::Round(12 * $scale)
$amountRight = $right - [Math]::Round(40 * $scale)
$amountLeft = $amountRight - $amountWidth
$qtyLeft = $amountLeft - $columnGap - $qtyWidth
$separatorCount = 0
$lastKotItem = $false

foreach ($line in $lines) {
    $rupeeSymbol = [string][char]0x20B9
    $mojibakeRupee = ([string][char]0x00E2) + ([string][char]0x201A) + ([string][char]0x00B9)
    $trimmed = ([string]$line).Trim().Replace($rupeeSymbol, 'Rs.').Replace($mojibakeRupee, 'Rs.')

    if ($trimmed -match '^-{6,}$') {
        $lastKotItem = $false
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 1)
        $pen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
        $graphics.DrawLine($pen, $x, $y + [Math]::Round(4 * $scale), $right, $y + [Math]::Round(4 * $scale))
        $pen.Dispose()
        $separatorCount++
        $y += [Math]::Round(4 * $scale)
        continue
    }

    if ($trimmed -eq '') {
        $lastKotItem = $false
        $y += [Math]::Round($lineHeight * 0.5)
        continue
    }

    if ($trimmed -match '^(\d{2}/\d{2}/\d{4}(?:\s+\S+)?)\s+(\d{1,2}:\d{2}\s*(?:AM|PM)?)$') {
        $lastKotItem = $false
        $dateText = $matches[1]
        $timeText = $matches[2]
        $dateSize = $graphics.MeasureString($dateText, $smallFont)
        $timeSize = $graphics.MeasureString($timeText, $headerFont)
        $gap = [Math]::Round(5 * $scale)
        $groupWidth = $dateSize.Width + $gap + $timeSize.Width
        $startX = $x + (($width - $groupWidth) / 2)
        $graphics.DrawString($dateText, $smallFont, $brush, [float]$startX, [float]($y + [Math]::Max(0, ($lineHeight - $dateSize.Height) / 2)))
        $graphics.DrawString($timeText, $headerFont, $brush, [float]($startX + $dateSize.Width + $gap), [float]$y)
        $y += $lineHeight
        continue
    }

    if ($trimmed -eq 'KOT' -or $trimmed -match '^Table No:') {
        $lastKotItem = $false
        $centerRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$width, [float]($lineHeight + [Math]::Round(6 * $scale)))
        $graphics.DrawString($trimmed, $headerFont, $brush, $centerRect, $centerFormat)
        $y += $lineHeight - [Math]::Round(2 * $scale)
        continue
    }

    if ($trimmed -match '^(Bill No:\s*\S+)\s+(.+)$') {
        $lastKotItem = $false
        $billRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]($width * 0.56), [float]($lineHeight + [Math]::Round(4 * $scale)))
        $tableRightPad = [Math]::Round(36 * $scale)
        $tableRect = [System.Drawing.RectangleF]::new([float]($x + ($width * 0.56)), [float]$y, [float]($width * 0.40 - $tableRightPad), [float]($lineHeight + [Math]::Round(4 * $scale)))
        $graphics.DrawString($matches[1], $font, $brush, $billRect)
        $graphics.DrawString($matches[2], $font, $brush, $tableRect, $rightFormat)
        $y += $lineHeight
        continue
    }

    if ($trimmed -match '^GRAND TOTAL:\s*(.*)$') {
        $lastKotItem = $false
        $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + [Math]::Round(6 * $scale)))
        $graphics.DrawString('GRAND TOTAL:', $boldFont, $brush, [float]$x, [float]$y)
        if ($matches[1].Trim() -ne '') {
            $graphics.DrawString($matches[1].Trim(), $boldFont, $brush, $amountRect, $rightFormat)
        }
        $y += $lineHeight + [Math]::Round(3 * $scale)
        continue
    }

    if ($trimmed -match '^Total\s+(\d+)\s+(.+)$') {
        $lastKotItem = $false
        $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + [Math]::Round(4 * $scale)))
        $amountRect = [System.Drawing.RectangleF]::new([float]$amountLeft, [float]$y, [float]$amountWidth, [float]($lineHeight + [Math]::Round(4 * $scale)))
        $graphics.DrawString('Total', $boldFont, $brush, [float]$x, [float]$y)
        $graphics.DrawString($matches[1], $boldFont, $brush, $qtyRect, $rightFormat)
        $graphics.DrawString($matches[2], $boldFont, $brush, [float]$amountRight, [float]$y, $rightFormat)
        $y += $lineHeight + [Math]::Round(3 * $scale)
        continue
    }

    if ($trimmed -match '^\s*Item\s+Q\s+T\s*$') {
        $lastKotItem = $false
        $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + [Math]::Round(4 * $scale)))
        $graphics.DrawString('Item', $boldFont, $brush, [float]$x, [float]$y)
        $graphics.DrawString('Q', $boldFont, $brush, $qtyRect, $rightFormat)
        $graphics.DrawString('T', $boldFont, $brush, [float]$amountRight, [float]$y, $rightFormat)
        $y += $lineHeight
        continue
    }

    if ($trimmed -match '^(.+?)\s+(\d{1,4})\s+((?:Rs\.\s*)?[0-9,]+(?:\.[0-9]+)?)$' -and $separatorCount -ge 2) {
        $lastKotItem = $false
        $qtyRect = [System.Drawing.RectangleF]::new([float]$qtyLeft, [float]$y, [float]$qtyWidth, [float]($lineHeight + [Math]::Round(4 * $scale)))
        $itemWidth = [Math]::Max([Math]::Round(70 * $scale), $qtyLeft - $x - $columnGap)
        $itemText = $matches[1].Trim()
        $itemSize = $graphics.MeasureString($itemText, $font, [int]$itemWidth)
        $rowHeight = [Math]::Max($lineHeight + [Math]::Round(2 * $scale), [Math]::Ceiling($itemSize.Height) + [Math]::Round(2 * $scale))
        $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$itemWidth, [float]($rowHeight + [Math]::Round(2 * $scale)))
        $graphics.DrawString($itemText, $font, $brush, $itemRect)
        $graphics.DrawString($matches[2], $font, $brush, $qtyRect, $rightFormat)
        $graphics.DrawString($matches[3], $font, $brush, [float]$amountRight, [float]$y, $rightFormat)
        $y += $rowHeight
        continue
    }

    if ($trimmed -match '^(\d+\.\s*.*?)\s+(\d{1,4})$') {
        $kotQtyWidth = [Math]::Round(50 * $scale)
        $kotQtyRight = $right - [Math]::Round(40 * $scale)
        $kotQtyLeft = $kotQtyRight - $kotQtyWidth
        $kotItemWidth = [Math]::Max([Math]::Round(80 * $scale), $kotQtyLeft - $x - $columnGap)
        $kotItemText = $matches[1].Trim()
        $kotItemSize = $graphics.MeasureString($kotItemText, $boldFont, [int]$kotItemWidth)
        $kotRowHeight = [Math]::Max($lineHeight + [Math]::Round(3 * $scale), [Math]::Ceiling($kotItemSize.Height) + [Math]::Round(3 * $scale))
        $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + [Math]::Round(2 * $scale)))
        $qtyText = $matches[2]
        $qtySize = $graphics.MeasureString($qtyText, $boldFont)
        $qtyX = [Math]::Max($kotQtyLeft, $kotQtyRight - $qtySize.Width)
        $graphics.DrawString($kotItemText, $boldFont, $brush, $itemRect)
        $graphics.DrawString($qtyText, $boldFont, $brush, [float]$qtyX, [float]$y)
        $y += $kotRowHeight
        $lastKotItem = $true
        continue
    }

    if ($trimmed -match '^(.+?)\s+(\d{1,4})$' -and -not ($trimmed -match '^(Note:|Table:|Date:|Bill No:|Ref:|Total:|GRAND TOTAL:|Mob\.|Tel:|GST:|Item Name)')) {
        $kotQtyWidth = [Math]::Round(50 * $scale)
        $kotQtyRight = $right - [Math]::Round(40 * $scale)
        $kotQtyLeft = $kotQtyRight - $kotQtyWidth
        $kotItemWidth = [Math]::Max([Math]::Round(80 * $scale), $kotQtyLeft - $x - $columnGap)
        $kotItemText = $matches[1].Trim()
        $qtyText = $matches[2].Trim()
        $kotItemSize = $graphics.MeasureString($kotItemText, $boldFont, [int]$kotItemWidth)
        $kotRowHeight = [Math]::Max($lineHeight + [Math]::Round(3 * $scale), [Math]::Ceiling($kotItemSize.Height) + [Math]::Round(3 * $scale))
        $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + [Math]::Round(2 * $scale)))
        $qtySize = $graphics.MeasureString($qtyText, $boldFont)
        $qtyX = [Math]::Max($kotQtyLeft, $kotQtyRight - $qtySize.Width)
        $graphics.DrawString($kotItemText, $boldFont, $brush, $itemRect)
        $graphics.DrawString($qtyText, $boldFont, $brush, [float]$qtyX, [float]$y)
        $y += $kotRowHeight
        $lastKotItem = $true
        continue
    }

    if ($lastKotItem) {
        $kotQtyWidth = [Math]::Round(50 * $scale)
        $kotQtyRight = $right - [Math]::Round(40 * $scale)
        $kotQtyLeft = $kotQtyRight - $kotQtyWidth
        $kotItemWidth = [Math]::Max([Math]::Round(80 * $scale), $kotQtyLeft - $x - $columnGap)
        $kotItemSize = $graphics.MeasureString($trimmed, $boldFont, [int]$kotItemWidth)
        $kotRowHeight = [Math]::Max($lineHeight + [Math]::Round(3 * $scale), [Math]::Ceiling($kotItemSize.Height) + [Math]::Round(3 * $scale))
        $itemRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$kotItemWidth, [float]($kotRowHeight + [Math]::Round(2 * $scale)))
        $graphics.DrawString($trimmed, $boldFont, $brush, $itemRect)
        $y += $kotRowHeight
        continue
    }

    $lastKotItem = $false
    if ($separatorCount -lt 2) {
        $centerRect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$width, [float]($lineHeight + [Math]::Round(6 * $scale)))
        if ($trimmed -match '^(Mob\.|Tel:|GST:)') {
            $graphics.DrawString($trimmed, $smallFont, $brush, $centerRect, $centerFormat)
        } else {
            $graphics.DrawString($trimmed, $headerFont, $brush, $centerRect, $centerFormat)
        }
    } else {
        $graphics.DrawString($trimmed, $font, $brush, [float]$x, [float]$y)
    }
    $y += $lineHeight
}

if ($qrImage) {
    $y += [Math]::Round(10 * $scale)
    $qrDrawSize = $qrHeight
    $qrX = [Math]::Round(($bitmapWidth - $qrDrawSize) / 2)
    $graphics.DrawImage($qrImage, $qrX, $y, $qrDrawSize, $qrDrawSize)
    $y += $qrDrawSize
    $qrImage.Dispose()
}

$finalHeight = [Math]::Max(1, [Math]::Min($bitmap.Height, $y + [Math]::Round(18 * $scale)))
$widthBytes = [Math]::Ceiling($bitmap.Width / 8)
$data = New-Object byte[] ($widthBytes * $finalHeight)
for ($yy = 0; $yy -lt $finalHeight; $yy++) {
    for ($xx = 0; $xx -lt $bitmap.Width; $xx++) {
        $pixel = $bitmap.GetPixel($xx, $yy)
        $luma = (0.299 * $pixel.R) + (0.587 * $pixel.G) + (0.114 * $pixel.B)
        if ($luma -lt 190) {
            $index = ($yy * $widthBytes) + [Math]::Floor($xx / 8)
            $data[$index] = $data[$index] -bor (0x80 -shr ($xx % 8))
        }
    }
}

$graphics.Dispose()
$font.Dispose()
$smallFont.Dispose()
$boldFont.Dispose()
$headerFont.Dispose()
$rightFormat.Dispose()
$centerFormat.Dispose()
$bitmap.Dispose()

$source = @"
using System;
using System.Runtime.InteropServices;

public class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);
    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);
    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, Int32 dwCount, out Int32 dwWritten);

    public static void SendBytes(string printerName, byte[] bytes)
    {
        IntPtr hPrinter;
        if (!OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        DOCINFOA di = new DOCINFOA();
        di.pDocName = "POS Raster Receipt";
        di.pDataType = "RAW";
        try {
            if (!StartDocPrinter(hPrinter, 1, di)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (!StartPagePrinter(hPrinter)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            int written;
            if (!WritePrinter(hPrinter, bytes, bytes.Length, out written)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
        } finally {
            ClosePrinter(hPrinter);
        }
    }
}
"@

Add-Type -TypeDefinition $source

$xL = [byte]($widthBytes % 256)
$xH = [byte][Math]::Floor($widthBytes / 256)
$yL = [byte]($finalHeight % 256)
$yH = [byte][Math]::Floor($finalHeight / 256)

$prefix = [byte[]](0x1B,0x40,0x1D,0x76,0x30,0x00,$xL,$xH,$yL,$yH)
$suffix = [byte[]](0x1D,0x56,0x42,0x00)
$payload = New-Object byte[] ($prefix.Length + $data.Length + $suffix.Length)
[Array]::Copy($prefix, 0, $payload, 0, $prefix.Length)
[Array]::Copy($data, 0, $payload, $prefix.Length, $data.Length)
[Array]::Copy($suffix, 0, $payload, $prefix.Length + $data.Length, $suffix.Length)

for ($i = 0; $i -lt $copies; $i++) {
    [RawPrinterHelper]::SendBytes($printerName, $payload)
}
PS1;

        $env = [
            'POS_PRINTER_NAME' => $printerName,
            'POS_PRINT_FILE' => $textFile,
            'POS_PRINT_COPIES' => (string) $copies,
            'POS_PRINT_PAPER' => $paperSize,
        ];
        if ($qrImageFile !== null) {
            $env['POS_PRINT_QR_IMAGE'] = $qrImageFile;
        }

        return $this->runPowerShell($script, $env);
    }

    private function isList(array $value): bool
    {
        return $value === [] || array_keys($value) === range(0, count($value) - 1);
    }

    private function runPowerShell(string $script, array $env = []): array
    {
        $scriptFile = tempnam(sys_get_temp_dir(), 'pos_ps_');
        if ($scriptFile === false) {
            return ['ok' => false, 'output' => '', 'error' => 'Unable to create PowerShell script.'];
        }

        $scriptPath = $scriptFile . '.ps1';
        @rename($scriptFile, $scriptPath);
        file_put_contents($scriptPath, $script);

        $descriptorSpec = [
            1 => ['pipe', 'w'],
            2 => ['pipe', 'w'],
        ];

        $process = proc_open(
            ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath],
            $descriptorSpec,
            $pipes,
            null,
            array_merge(getenv() ?: [], $env)
        );

        if (!is_resource($process)) {
            @unlink($scriptPath);
            return ['ok' => false, 'output' => '', 'error' => 'Unable to start PowerShell.'];
        }

        $output = stream_get_contents($pipes[1]) ?: '';
        $error = stream_get_contents($pipes[2]) ?: '';
        fclose($pipes[1]);
        fclose($pipes[2]);

        $exitCode = proc_close($process);
        @unlink($scriptPath);
        $output = $this->normalizeProcessText($output);
        $error = $this->normalizeProcessText($error);

        if ($exitCode !== 0) {
            @file_put_contents(
                __DIR__ . '/../../logs/print-agent-powershell-error.log',
                '[' . date('Y-m-d H:i:s') . "] exit={$exitCode}\n" . trim($error . "\n" . $output) . "\n\n",
                FILE_APPEND
            );
        }

        return [
            'ok' => $exitCode === 0,
            'output' => trim($output),
            'error' => trim($error),
        ];
    }

    private function normalizeProcessText(string $value): string
    {
        if ($value === '') {
            return '';
        }

        if (preg_match('//u', $value) === 1) {
            return $value;
        }

        foreach (['CP850', 'CP437', 'Windows-1252'] as $encoding) {
            $converted = @iconv($encoding, 'UTF-8//IGNORE', $value);
            if (is_string($converted) && $converted !== '') {
                return $converted;
            }
        }

        return preg_replace('/[^\x09\x0A\x0D\x20-\x7E]/', '', $value) ?? '';
    }
}
