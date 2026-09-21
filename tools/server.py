#!/usr/bin/env python3
"""Run the dedicated server here in WSL, instead of on the Hetzner box.

    tools/server.py                  run it in the foreground (ctrl-c stops it), track Street
    tools/server.py run --track Desert
    tools/server.py start            same thing in the background
    tools/server.py stop             stop the background one
    tools/server.py status           is it up, what is it hosting, is the port answering
    tools/server.py log              the last 40 lines it printed

It runs Build/LinuxServer/RCRACE-server, the same binary deploy/deploy.sh uploads - so build it
first (deploy/deploy.sh, or Tools > Build > Linux Server in the editor) if that folder is empty or
stale. The version it was built from is printed on startup, and has to match the clients' - the
menu and the in-race status line of every client show the same string.

A server hosts one track: whoever joins must have picked that same one, or they are turned away.

For a friend to reach it over tailscale, the port has to be open as well: tools/tailscale.py open.
"""
import os
import signal
import socket
import subprocess
import sys
import time

PORT = 7777             # NetConfig.ServerPort
TRACKS = ["Street", "Desert", "Test"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(ROOT, "Build", "LinuxServer")
BINARY = os.path.join(BUILD, "RCRACE-server")
LOG = os.path.join(BUILD, "server.log")
PIDFILE = os.path.join(BUILD, "server.pid")


def fail(message):
    sys.stderr.write(message + "\n")
    raise SystemExit(1)


def require_binary():
    if os.path.exists(BINARY):
        return
    fail("no server build at " + BINARY + "\n"
         "build one first: deploy/deploy.sh (builds and deploys), or Tools > Build > Linux Server.")


def running_pid():
    if not os.path.exists(PIDFILE):
        return 0
    text = open(PIDFILE).read().strip()
    if not text.isdigit():
        return 0
    pid = int(text)
    try:
        os.kill(pid, 0)
    except OSError:
        return 0
    return pid


# Binding is the honest test: "is something answering on 7777", without having to trust a pid file
# that says yes while the process is gone, or a process that is up but failed to bind (which is
# exactly what happens when a second server is started by accident).
def port_taken():
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.bind(("0.0.0.0", PORT))
    except OSError:
        return True
    finally:
        probe.close()
    return False


def args_for(track):
    return [BINARY, "-batchmode", "-nographics", "-logFile", LOG, "-track=" + track]


# A command that is simply not installed here (tailscale, most likely) is an answer, not an error:
# the address list is information, and never a reason for the server not to start.
def output_of(command):
    try:
        found = subprocess.run(command, capture_output=True, text=True)
    except (FileNotFoundError, OSError):
        return ""
    if found.returncode != 0:
        return ""
    return found.stdout.strip()


def addresses():
    lines = []
    tailnet = output_of(["tailscale", "ip", "-4"])
    if tailnet:
        lines.append("tailscale (this wsl): " + tailnet.replace("\n", " "))
    else:
        lines.append("tailscale: not running inside wsl - if the Windows side has it, use that "
                     "address and open the port with tools/tailscale.py open")
    local = output_of(["hostname", "-I"])
    if local:
        lines.append("wsl address: " + local)
    return lines


def banner(track):
    print("hosting Track_" + track + " on UDP " + str(PORT))
    for line in addresses():
        print(line)
    print("clients must be built from the same commit and pick the same track.")
    print("")


def do_run(track):
    require_binary()
    if port_taken():
        fail("UDP " + str(PORT) + " is already in use - 'tools/server.py stop', or find what has it.")
    banner(track)
    try:
        subprocess.run(args_for(track), cwd=BUILD)
    except KeyboardInterrupt:
        print("\nstopped.")
    return 0


def do_start(track):
    require_binary()
    pid = running_pid()
    if pid:
        fail("already running as pid " + str(pid) + " ('tools/server.py status').")
    if port_taken():
        fail("UDP " + str(PORT) + " is already in use - something else has it.")
    handle = subprocess.Popen(args_for(track), cwd=BUILD, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, start_new_session=True)
    open(PIDFILE, "w").write(str(handle.pid))
    time.sleep(2)   # long enough for a failure to bind to have been logged
    if handle.poll() is not None:
        fail("it exited immediately - 'tools/server.py log' will say why.")
    banner(track)
    print("running in the background as pid " + str(handle.pid) + " ('tools/server.py stop' ends it)")
    return 0


def do_stop():
    pid = running_pid()
    if not pid:
        print("not running (nothing in " + PIDFILE + " that is still alive)")
        return 0
    os.kill(pid, signal.SIGTERM)
    for _ in range(40):
        time.sleep(0.25)
        if not running_pid():
            print("stopped pid " + str(pid))
            return 0
    fail("pid " + str(pid) + " did not stop within 10s.")


def do_status():
    pid = running_pid()
    if pid:
        print("running as pid " + str(pid))
    else:
        print("not running in the background")
    if port_taken():
        print("UDP " + str(PORT) + " is bound (something is listening)")
    else:
        print("UDP " + str(PORT) + " is free (nothing listening)")
    for line in addresses():
        print(line)
    print_log(6)
    return 0


def print_log(count):
    if not os.path.exists(LOG):
        print("no log yet at " + LOG)
        return
    lines = open(LOG, errors="replace").read().splitlines()
    print("--- " + LOG + " ---")
    for line in lines[-count:]:
        print(line)


def main(argv):
    action = "run"
    if argv and not argv[0].startswith("-"):
        action = argv[0]
        argv = argv[1:]

    track = "Street"
    if "--track" in argv:
        index = argv.index("--track")
        if index + 1 >= len(argv):
            fail("--track needs a name: " + ", ".join(TRACKS))
        track = argv[index + 1]
    if track not in TRACKS:
        fail("unknown track '" + track + "' (" + ", ".join(TRACKS) + ")")

    if action == "run":
        return do_run(track)
    if action == "start":
        return do_start(track)
    if action == "stop":
        return do_stop()
    if action == "status":
        return do_status()
    if action == "log":
        print_log(40)
        return 0
    fail("usage: tools/server.py run | start | stop | status | log  [--track " + "|".join(TRACKS) + "]")


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
