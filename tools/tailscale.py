#!/usr/bin/env python3
"""Open / close the Windows firewall for the RCRACE server, for Tailscale peers only.

    tools/tailscale.py open             allow inbound UDP 7777 from the tailnet
    tools/tailscale.py close            remove those rules again
    tools/tailscale.py status           what is open right now, plus this machine's tailscale IP
    tools/tailscale.py open 8000/tcp    a different port (no suffix means udp)

Run it from WSL: the packets arrive on the Windows side, so that is whose firewall has to let
them in - this drives it through powershell.exe. open/close need admin, so expect a UAC prompt.

The rules are scoped to 100.64.0.0/10, the range every tailscale address lives in, so opening a
port here exposes it to your tailnet (and whoever you shared the node with) and to nobody else -
not the coffee shop wifi, not the internet.

Not needed if you installed tailscale INSIDE wsl: that traffic terminates in the wsl instance and
never passes the Windows firewall. This is for the mirrored-networking setup, where the listener
is reached through the Windows host.
"""
import base64
import subprocess
import sys

TAILNET = "100.64.0.0/10"
RULE_PREFIX = "RCRACE tailscale"
LOG_NAME = "rcrace-tailscale.log"
DEFAULT_PORTS = [("UDP", 7777)]


def fail(message):
    sys.stderr.write(message + "\n")
    raise SystemExit(1)


def parse_ports(args):
    if not args:
        return DEFAULT_PORTS
    ports = []
    for arg in args:
        proto = "UDP"
        text = arg
        if "/" in arg:
            text, _, suffix = arg.partition("/")
            proto = suffix.upper()
        if proto not in ("UDP", "TCP"):
            fail("unknown protocol in '" + arg + "' (use udp or tcp)")
        if not text.isdigit():
            fail("not a port number: '" + arg + "'")
        ports.append((proto, int(text)))
    return ports


def rule_name(proto, port):
    return RULE_PREFIX + " " + proto + " " + str(port)


# Every action is one PowerShell script, so the elevated run is a single UAC prompt no matter how
# many ports were asked for. It writes what it did to a log file, which we print back here: an
# elevated Start-Process gets its own console window that closes instantly, taking its output with it.
def script_open(ports):
    lines = ['$ErrorActionPreference = "Stop"', "$out = @()"]
    for proto, port in ports:
        name = rule_name(proto, port)
        lines.append('if (Get-NetFirewallRule -DisplayName "' + name + '" -ErrorAction SilentlyContinue) {')
        lines.append('    $out += "already open: ' + name + '"')
        lines.append("} else {")
        lines.append('    New-NetFirewallRule -DisplayName "' + name + '" -Direction Inbound -Protocol ' + proto +
                     " -LocalPort " + str(port) + ' -RemoteAddress ' + TAILNET + ' -Action Allow | Out-Null')
        lines.append('    $out += "opened: ' + name + ' (from ' + TAILNET + ' only)"')
        lines.append("}")
    return "\n".join(lines) + "\n" + write_log()


def script_close(ports):
    lines = ['$ErrorActionPreference = "Stop"', "$out = @()"]
    for proto, port in ports:
        name = rule_name(proto, port)
        lines.append('if (Get-NetFirewallRule -DisplayName "' + name + '" -ErrorAction SilentlyContinue) {')
        lines.append('    Remove-NetFirewallRule -DisplayName "' + name + '"')
        lines.append('    $out += "closed: ' + name + '"')
        lines.append("} else {")
        lines.append('    $out += "was not open: ' + name + '"')
        lines.append("}")
    return "\n".join(lines) + "\n" + write_log()


def write_log():
    return '$out | Set-Content -Path (Join-Path $env:TEMP "' + LOG_NAME + '")\n$out | Write-Output'


def script_status():
    return (
        '$rules = Get-NetFirewallRule -DisplayName "' + RULE_PREFIX + ' *" -ErrorAction SilentlyContinue\n'
        "if ($rules) {\n"
        "    foreach ($r in $rules) {\n"
        "        $f = $r | Get-NetFirewallPortFilter\n"
        "        $a = $r | Get-NetFirewallAddressFilter\n"
        '        Write-Output "$($r.DisplayName): enabled=$($r.Enabled) from=$($a.RemoteAddress)"\n'
        "    }\n"
        "} else {\n"
        '    Write-Output "nothing open (no \'' + RULE_PREFIX + ' *\' rules)"\n'
        "}\n"
        "$ts = Get-Command tailscale.exe -ErrorAction SilentlyContinue\n"
        "if ($ts) {\n"
        '    Write-Output "this machine on the tailnet: $(& tailscale.exe ip -4)"\n'
        "} else {\n"
        '    Write-Output "tailscale.exe not found on the Windows side"\n'
        "}"
    )


# -EncodedCommand takes UTF-16LE base64, which is the one way to hand PowerShell a whole script
# through an argument list without every quote in it having to survive bash, wsl interop and
# Start-Process in turn.
def encode(script):
    return base64.b64encode(script.encode("utf-16-le")).decode("ascii")


def powershell(args):
    try:
        return subprocess.run(["powershell.exe"] + args, capture_output=True, text=True)
    except FileNotFoundError:
        fail("powershell.exe not found - run this from WSL on the Windows machine that hosts the server.")


def run_plain(script):
    done = powershell(["-NoProfile", "-EncodedCommand", encode(script)])
    sys.stdout.write(done.stdout.replace("\r", ""))
    if done.returncode != 0:
        sys.stderr.write(done.stderr.replace("\r", ""))
    return done.returncode


def log_path():
    done = powershell(["-NoProfile", "-Command", "$env:TEMP"])
    win = done.stdout.strip()
    if not win:
        return ""
    found = subprocess.run(["wslpath", "-u", win], capture_output=True, text=True)
    return found.stdout.strip() + "/" + LOG_NAME


def run_elevated(script):
    path = log_path()
    if path:
        open(path, "w").close()   # so a failed UAC prompt shows as nothing done, not as last run's output
    launch = ("Start-Process powershell -Verb RunAs -Wait -ArgumentList "
              "'-NoProfile','-EncodedCommand','" + encode(script) + "'")
    done = powershell(["-NoProfile", "-Command", launch])
    if done.returncode != 0:
        sys.stderr.write(done.stderr.replace("\r", ""))
        fail("the elevated PowerShell did not run (UAC declined?)")
    if not path:
        return 0
    for line in open(path, encoding="utf-8-sig"):
        sys.stdout.write(line.replace("\r", ""))
    return 0


def main(argv):
    action = "status"
    if argv:
        action = argv[0]
    ports = parse_ports(argv[1:])

    if action == "status":
        return run_plain(script_status())
    if action == "open":
        code = run_elevated(script_open(ports))
        print("")
        print("your friend connects to <this machine's tailscale ip>:" + str(ports[0][1]) +
              " - 'tools/tailscale.py status' prints it")
        return code
    if action == "close":
        return run_elevated(script_close(ports))
    fail("usage: tools/tailscale.py open | close | status  [port[/udp|/tcp] ...]")


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
