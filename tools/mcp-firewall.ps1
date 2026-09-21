# Opens / closes the Windows firewall for the MCP servers used from WSL.
# Called by tools/mcp-firewall.sh (elevated for open/close). Do not run by hand unless you know why.
#   open   : allow inbound TCP on each port, and forward 0.0.0.0:9877 -> 127.0.0.1:9876
#            (the Blender addon only listens on localhost:9876; the forward must use a different
#            outside port, otherwise the addon sees the port as taken and refuses to start)
#   close  : remove those rules and the port forward
#   status : show what is currently there
param(
    [string]$Action = "status",
    [string]$Ports = "8080,9877"     # comma separated; a plain string because -File passes strings
)
$ErrorActionPreference = "Stop"
$PortList = @($Ports -split "[, ]+" | Where-Object { $_ -ne "" } | ForEach-Object { [int]$_ })
$log = Join-Path $env:TEMP "wsl-mcp-firewall.log"
$lines = @()
$BlenderPort = 9876        # what the addon listens on (localhost only)
$BlenderProxy = 9877       # what WSL connects to

try {
    switch ($Action) {
        "open" {
            foreach ($p in $PortList) {
                $name = "WSL MCP $p"
                if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
                    $lines += "rule '$name' already exists"
                } else {
                    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $p -Action Allow | Out-Null
                    $lines += "rule '$name' added (inbound TCP $p allowed)"
                }
            }
            # a forward on 9876 itself (older version of this script) blocks the addon: always drop it
            netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderPort | Out-Null
            if (Get-NetFirewallRule -DisplayName "WSL MCP $BlenderPort" -ErrorAction SilentlyContinue) {
                Remove-NetFirewallRule -DisplayName "WSL MCP $BlenderPort"
                $lines += "stale rule 'WSL MCP $BlenderPort' removed"
            }
            if ($PortList -contains $BlenderProxy) {
                netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderProxy | Out-Null
                netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$BlenderProxy connectaddress=127.0.0.1 connectport=$BlenderPort | Out-Null
                $lines += "port forward 0.0.0.0:$BlenderProxy -> 127.0.0.1:$BlenderPort set"
            }
        }
        "close" {
            foreach ($p in $PortList) {
                $name = "WSL MCP $p"
                if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
                    Remove-NetFirewallRule -DisplayName $name
                    $lines += "rule '$name' removed"
                } else {
                    $lines += "rule '$name' not present"
                }
            }
            netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderPort | Out-Null
            netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderProxy | Out-Null
            $lines += "port forwards for $BlenderPort and $BlenderProxy removed"
        }
        "status" {
            $rules = Get-NetFirewallRule -DisplayName "WSL MCP *" -ErrorAction SilentlyContinue
            if ($rules) {
                foreach ($r in $rules) {
                    $port = ($r | Get-NetFirewallPortFilter).LocalPort
                    $lines += "rule '$($r.DisplayName)': port $port, enabled=$($r.Enabled), action=$($r.Action)"
                }
            } else {
                $lines += "no 'WSL MCP *' firewall rules"
            }
            $lines += "port forwards:"
            $lines += (netsh interface portproxy show v4tov4 | Where-Object { $_.Trim() -ne "" })
        }
        default { $lines += "unknown action '$Action' (use open | close | status)" }
    }
} catch {
    $lines += "ERROR: $($_.Exception.Message)"
}

$lines | Set-Content -Path $log
$lines | Write-Output
