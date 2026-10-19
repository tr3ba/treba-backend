[CmdletBinding()]
param([Parameter(Mandatory = $true)][int]$Port, [int]$TimeoutMilliseconds = 3000)

# PostgreSQL must accept SSL through the complete SSH route before the apps start.
# This sends only SSLRequest, never a username, password or SQL statement.
$client = New-Object Net.Sockets.TcpClient
try {
    $connection = $client.ConnectAsync('127.0.0.1', $Port)
    if (!$connection.Wait($TimeoutMilliseconds)) { return $false }
    $client.ReceiveTimeout = $TimeoutMilliseconds
    $client.SendTimeout = $TimeoutMilliseconds
    $stream = $client.GetStream()
    $request = [byte[]]@(0, 0, 0, 8, 4, 210, 22, 47)
    $stream.Write($request, 0, $request.Length)
    return $stream.ReadByte() -eq 83
} catch { return $false }
finally { $client.Dispose() }
