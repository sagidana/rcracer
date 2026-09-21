# Opens / closes the Windows firewall for the MCP servers used from WSL.
# Called by tools/mcp-firewall.sh (elevated for open/close). Do not run by hand unless you know why.
#   open   : allow inbound TCP on each port, and forward 0.0.0.0:9876 -> 127.0.0.1:9876
#            (the Blender addon only listens on localhost)
#   close  : remove those rules and the port forward
#   status : show what is currently there
param(
    [string]$Action = "status",
    [string]$Ports = "8080,9876"     # comma separated; a plain string because -File passes strings
)
$ErrorActionPreference = "Stop"
$Ports = @($Ports -split "[, ]+" | Where-Object { $_ -ne "" } | ForEach-Object { [int]$_ })
$log = Join-Path $env:TEMP "wsl-mcp-firewall.log"
$lines = @()
$BlenderPort = 9876

try {
    switch ($Action) {
        "open" {
            foreach ($p in $Ports) {
                $name = "WSL MCP $p"
                if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
                    $lines += "rule '$name' already exists"
                } else {
                    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $p -Action Allow | Out-Null
                    $lines += "rule '$name' added (inbound TCP $p allowed)"
                }
            }
            if ($Ports -contains $BlenderPort) {
                netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderPort | Out-Null
                netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$BlenderPort connectaddress=127.0.0.1 connectport=$BlenderPort | Out-Null
                $lines += "port forward 0.0.0.0:$BlenderPort -> 127.0.0.1:$BlenderPort set"
            }
        }
        "close" {
            foreach ($p in $Ports) {
                $name = "WSL MCP $p"
                if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
                    Remove-NetFirewallRule -DisplayName $name
                    $lines += "rule '$name' removed"
                } else {
                    $lines += "rule '$name' not present"
                }
            }
            if ($Ports -contains $BlenderPort) {
                netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$BlenderPort | Out-Null
                $lines += "port forward for $BlenderPort removed"
            }
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
