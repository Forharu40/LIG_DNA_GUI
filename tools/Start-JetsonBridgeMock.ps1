param(
    [int]$Port = 7001,
    [int]$MaxRequests = 0
)

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()

Write-Host "Mock Jetson bridge listening on 127.0.0.1:$Port"
Write-Host "Press Ctrl+C to stop."

$handled = 0

try {
    while ($true) {
        if ($MaxRequests -gt 0 -and $handled -ge $MaxRequests) {
            break
        }

        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $false, 1024, $true)
            $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::UTF8, 1024, $true)
            $writer.NewLine = "`n"
            $writer.AutoFlush = $true

            $line = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $request = $line | ConvertFrom-Json
            $replyText = "Mock response from local bridge. model=$($request.model), prompt=$($request.prompt)"

            $response = [ordered]@{
                requestId = $request.requestId
                status = "ok"
                response = $replyText
                error = ""
                elapsedMs = 12
            }

            $writer.WriteLine(($response | ConvertTo-Json -Compress))
            $handled += 1
            Write-Host "Handled request #$handled"
        }
        catch {
            Write-Warning $_
        }
        finally {
            $client.Dispose()
        }
    }
}
finally {
    $listener.Stop()
}
