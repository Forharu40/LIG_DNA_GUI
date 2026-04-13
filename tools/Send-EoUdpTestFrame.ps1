param(
    [string]$Host = "127.0.0.1",
    [int]$Port = 5000,
    [int]$FrameCount = 120,
    [int]$IntervalMs = 100,
    [int]$Width = 640,
    [int]$Height = 360
)

Add-Type -AssemblyName System.Drawing

function Write-UInt64BE {
    param([byte[]]$Buffer, [int]$Offset, [UInt64]$Value)
    for ($i = 0; $i -lt 8; $i++) {
        $shift = (7 - $i) * 8
        $Buffer[$Offset + $i] = [byte](($Value -shr $shift) -band 0xFF)
    }
}

function Write-UInt32BE {
    param([byte[]]$Buffer, [int]$Offset, [UInt32]$Value)
    for ($i = 0; $i -lt 4; $i++) {
        $shift = (3 - $i) * 8
        $Buffer[$Offset + $i] = [byte](($Value -shr $shift) -band 0xFF)
    }
}

function Write-UInt16BE {
    param([byte[]]$Buffer, [int]$Offset, [UInt16]$Value)
    $Buffer[$Offset] = [byte](($Value -shr 8) -band 0xFF)
    $Buffer[$Offset + 1] = [byte]($Value -band 0xFF)
}

$udp = [System.Net.Sockets.UdpClient]::new()

try {
    for ($frameIndex = 0; $frameIndex -lt $FrameCount; $frameIndex++) {
        $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()

        try {
            $background = [System.Drawing.Color]::FromArgb(32, 36, 48)
            $graphics.Clear($background)

            $brushA = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, 125, 216))
            $brushB = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 193, 69))
            $textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(80, 255, 255, 255), 2)
            $font = [System.Drawing.Font]::new("Segoe UI", 28, [System.Drawing.FontStyle]::Bold)

            try {
                for ($x = 0; $x -le $Width; $x += 80) {
                    $graphics.DrawLine($pen, $x, 0, $x, $Height)
                }

                for ($y = 0; $y -le $Height; $y += 80) {
                    $graphics.DrawLine($pen, 0, $y, $Width, $y)
                }

                $x1 = 40 + (($frameIndex * 7) % ($Width - 180))
                $x2 = 120 + (($frameIndex * 5) % ($Width - 220))
                $graphics.FillEllipse($brushA, $x1, 70, 180, 120)
                $graphics.FillEllipse($brushB, $x2, 210, 210, 90)
                $graphics.DrawString("EO UDP TEST", $font, $textBrush, 26, 18)
                $graphics.DrawString("Frame $frameIndex", $font, $textBrush, 26, 64)
            }
            finally {
                $brushA.Dispose()
                $brushB.Dispose()
                $textBrush.Dispose()
                $pen.Dispose()
                $font.Dispose()
            }

            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Jpeg)
            $jpegBytes = $stream.ToArray()

            $header = New-Object byte[] 20
            $utcMillis = [UInt64][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            Write-UInt64BE -Buffer $header -Offset 0 -Value $utcMillis
            Write-UInt32BE -Buffer $header -Offset 8 -Value ([UInt32]$frameIndex)
            Write-UInt32BE -Buffer $header -Offset 12 -Value ([UInt32]$jpegBytes.Length)
            Write-UInt16BE -Buffer $header -Offset 16 -Value ([UInt16]$Width)
            Write-UInt16BE -Buffer $header -Offset 18 -Value ([UInt16]$Height)

            $packet = New-Object byte[] ($header.Length + $jpegBytes.Length)
            [System.Buffer]::BlockCopy($header, 0, $packet, 0, $header.Length)
            [System.Buffer]::BlockCopy($jpegBytes, 0, $packet, $header.Length, $jpegBytes.Length)

            [void]$udp.Send($packet, $packet.Length, $Host, $Port)
            Write-Host "Sent EO test frame $frameIndex to $Host`:$Port"
            Start-Sleep -Milliseconds $IntervalMs
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
            $stream.Dispose()
        }
    }
}
finally {
    $udp.Dispose()
}
