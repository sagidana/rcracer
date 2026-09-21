# RCRACE

Arcade RC-car racing game made with **Unity 6 (6000.3.24f1)**, Universal Render Pipeline and the new Input System.

## Project map

```
.
├── Assets/
│   ├── Scenes/                       Menu (car + track select), Track_Street, Track_Desert, Track_Test,
│   │                                 Server (scene 0 of the dedicated server build), SampleScene (legacy)
│   ├── Resources/Cars/               the car prefabs the menu and the server both load by name
│   ├── Scripts/                      runtime code (ships in the .exe)
│   │   ├── CarInput.cs               keyboard / gamepad -> Throttle / Steer / Handbrake / Reset, or the network
│   │   │                             (SetNetworkInput) for a car the server or a replay drives
│   │   ├── CarController.cs          the car: 4 wheels, spring+damper suspension via sphere casts, drive/brake/side grip.
│   │   │                             Reads CarInput. Tick(dt) is the whole physics step, so a replay can drive it
│   │   │                             by hand instead of waiting for FixedUpdate.
│   │   ├── GameSelection.cs          what the menu chose (car, track), shared across scene loads
│   │   ├── GameVersion.cs            what build this is: the git stamp + the wire version, shown in the menu
│   │   │                             and on the in-race status line so two players can compare at a glance
│   │   ├── MenuController.cs         the menu: pick a car and a track, or quit
│   │   ├── RaceBootstrap.cs          in every track scene: spawns the car, wires the camera, adds NetClient -
│   │   │                             or NetServer instead when this is a dedicated server build
│   │   ├── ServerBoot.cs             dedicated server entry point: reads -track= and loads that scene
│   │   ├── CarTuning.cs              the inspector foldouts of CarController (Body, Suspension, Drive, Steering,
│   │   │                             Grip, Collision, Props, Debug) - every tunable number lives here
│   │   ├── CarWheel.cs               one physics wheel (mount point + current contact). Plain class, owned by CarController.
│   │   ├── CarWheelVisuals.cs        moves the 3D model's wheel meshes to match the physics (bounce, spin, steer)
│   │   ├── FollowCamera.cs           chase camera with smoothing and impact shake
│   │   ├── SpeedDisplay.cs           km/h text in the corner (OnGUI)
│   │   ├── KnockableProp.cs          light cones / trash cans / mailboxes that fly away and return home
│   │   ├── CarController_Old.cs      backup of the earlier "sliding box" car. Disabled, kept for reference.
│   │   ├── Net/                      online play (see the Multiplayer section for how it fits together)
│   │   │   ├── NetConfig.cs              server address and port, rates, reconciliation constants
│   │   │   ├── NetProtocol.cs            the wire format: one tag byte, then hand-packed fields
│   │   │   ├── NetClient.cs              sends this player's input ticks, applies corrections, shows the status line
│   │   │   ├── NetServer.cs              authoritative: one real car per player, one input tick per physics step
│   │   │   ├── NetPredictor.cs           a duplicate of the track in its own PhysicsScene, where the client
│   │   │   │                             re-simulates its unconfirmed input after a correction
│   │   │   ├── RemoteCarView.cs          other players: a visual body following the server's snapshots
│   │   │   └── NetSim.cs                 test-only: fake latency and jitter, so netcode bugs can be
│   │   │                                 reproduced and measured locally instead of guessed at
│   │   └── Editor/                   editor-only tools, all under the Tools menu (not in the .exe)
│   │       ├── RaceSceneSetup.cs         Tools > Setup Race Scene         flat ground + box car from scratch
│   │       ├── CarPhysicsSetup.cs        Tools > Apply New Car Physics   wires CarController + wheels on the Car
│   │       ├── CarModelSwitcher.cs       Tools > Car Model > ...         swaps the buggy / roadster / bike model
│   │       ├── TestTrackBuilder.cs       Tools > Build Test Track        closed loop with curbs, car on start line
│   │       ├── StreetTrackBuilder.cs     Tools > Build Street Track      giant suburban street track, one "StreetTrack" object
│   │       ├── StreetTrackPath.cs            waypoints -> smooth path with rounded corners
│   │       ├── StreetTrackRoad.cs            road, lines, curbs, sidewalks, crosswalks, start line, shortcut
│   │       ├── StreetTrackHouses.cs          houses, lawns, fences
│   │       ├── StreetTrackProps.cs           big props: parked cars, bins, mailboxes, cones
│   │       ├── StreetTrackKnockProps.cs      the small knockable props (uses KnockableProp)
│   │       ├── StreetTrackGeometry.cs        mesh helpers, colour palette, batching
│   │       ├── DesertTrackBuilder.cs     Tools > Build Desert Track      the second track
│   │       ├── MenuSceneBuilder.cs       Tools > Build Menu Scene
│   │       ├── TrackSceneBuilder.cs      Tools > Build Track Scenes
│   │       ├── ServerSceneBuilder.cs     Tools > Server > Build Server Scene
│   │       ├── CarPrefabBuilder.cs       Tools > Cars > Build Car Prefabs  writes Resources/Cars/*.prefab
│   │       ├── LightingPreset.cs         the shared lighting/post setup every track scene applies
│   │       ├── PropPhysicsTool.cs        Tools > Make Props Knockable
│   │       ├── PhysicsTestObstacles.cs   Tools > Add Physics Test Obstacles   bumps / steps / wall to test suspension
│   │       └── BuildScript.cs            Tools > Build > Windows (x64), and the entry point of build.sh / build.bat
│   ├── Settings/                     URP render pipeline assets (PC + Mobile), renderers, volume profiles
│   ├── InputSystem_Actions.inputactions   default Input System action map (CarInput reads the keyboard directly)
│   └── Free Adventure Vehicles/      car models used by Car Model switcher: Vehicle14 (bike), 16 (roadster), 19 (buggy)
├── Packages/manifest.json            Unity packages (URP 17.3, Input System 1.20, ...) - Unity keeps this in sync
├── ProjectSettings/                  project-wide settings: physics, quality, tags, input, build scenes, editor version
├── build.sh / build.bat              headless Windows build from WSL/Linux or Windows -> Build/Windows/RCRACE.exe
├── deploy/                           builds the Linux server and installs it as a systemd service over ssh
│   ├── deploy.sh                         build + upload + restart, all in one
│   ├── remote_install.sh                 what runs on the server itself
│   └── rcracer-server.service            the systemd unit
└── .gitignore / .gitattributes       what stays out of git; line-ending rules
```

How the pieces talk to each other at runtime:

```
keyboard -> CarInput -> CarController (physics, 4x CarWheel) -> Rigidbody
                              |                      \-> KnockableProp (collisions with props)
                              +-> CarWheelVisuals (wheel meshes follow the physics)
                              +-> FollowCamera (position, shake on impact)
                              +-> SpeedDisplay (reads the Rigidbody speed)
```

The scene is generated, not hand-placed: the editor tools build the Car, the tracks and the props into
`SampleScene`, and running a tool again replaces what it built before. The track scripts share one class
(`StreetTrackBuilder`, split across the `StreetTrack*.cs` files).

Everything Unity generates on your machine (`Library/`, `Logs/`, `Temp/`, `UserSettings/`, `Build/`, `.csproj`)
is ignored and rebuilt when the project is opened.

## Requirements

* Unity Hub with editor **6000.3.24f1** (the version in `ProjectSettings/ProjectVersion.txt`).
  When installing the editor, tick the **Windows Build Support (Mono)** module.
* On WSL you can use either the Linux editor (installed by Unity Hub for Linux) or the Windows editor already on the machine (see below).

## Clone and open

```bash
git clone https://github.com/sagidana/rcracer.git
```

Open the folder in Unity Hub (`Add project from disk`) and press Play in `SampleScene`.
The first open takes a few minutes: Unity regenerates the `Library/` folder, which is not in git.

## Build a Windows .exe

Both scripts run the editor headless, build `SampleScene` for Windows x64 and put the result in `Build/Windows/RCRACE.exe`.
Zip the whole `Build/Windows` folder to share it (the `.exe` needs the `_Data` folder next to it).

### From Windows

```bat
build.bat
```

Optional: `build.bat C:\some\output\dir`, or `set UNITY=C:\path\to\Unity.exe` if the editor is not in the default Hub location.
You can also build from inside the editor: **Tools > Build > Windows (x64)**.

### From WSL / Linux

```bash
./build.sh
```

The script looks for the matching editor in this order:

1. `$UNITY` if set, e.g. `UNITY=/opt/unity/Editor/Unity ./build.sh`
2. Linux editor from Unity Hub: `~/Unity/Hub/Editor/6000.3.24f1/Editor/Unity`
3. Windows editor through WSL interop: `/mnt/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe`

Keep the repo on a Windows drive (e.g. `C:\dev\rcracer`, which is `/mnt/c/dev/rcracer` in WSL): the Windows
editor opens it directly and `./build.sh` builds it in place. Close the Unity editor before building; a batch
build cannot open a project the editor already has open (the script checks `Temp/UnityLockfile` and stops).

If the repo lives inside the WSL filesystem (`/home/...`) instead, Unity.exe cannot open it: it refuses
case-sensitive filesystems, and `\\wsl.localhost` is slow anyway. The script then syncs `Assets/`, `Packages/`
and `ProjectSettings/` to `%LOCALAPPDATA%\RCRACE-build` (override with `STAGE=/mnt/c/...`), builds there with a
cached `Library/`, and copies the result back to `Build/Windows/`. Needs `rsync` (`sudo apt install rsync`).
Run that build from the Windows-drive copy the script prints: launching an exe through `\\wsl.localhost\...`
fails with "dstorage.dll was not found" because Windows does not load the DLLs next to it from a network path.

The full editor log is written to `Build/build.log`.

### Editor version

Both scripts look for the exact version in `ProjectSettings/ProjectVersion.txt` first. If it is missing they fall back
to the newest installed editor of the same major version (e.g. any `6000.x`) and print a warning: Unity then upgrades
the project to that version on first open and rewrites `ProjectVersion.txt`. Keep everyone on the same version, so
either install the exact one from the Hub's **Archive** tab, or agree on the newer version and commit the upgrade once.

## Multiplayer

Every track is playable online: `RaceBootstrap` spawns your car locally as usual (so driving stays
responsive) and best-effort connects a `NetClient` to the dedicated server hardcoded in
`Assets/Scripts/Net/NetConfig.cs` (`142.132.187.130`, UDP port 7777). If the server never answers within
a few seconds, the game just carries on offline - no menu toggle, no error dialog.

The server is authoritative: it runs a real instance of each connected player's car (the same
`CarController` physics as the client, fed by network input instead of a keyboard) and is the only place
player-vs-player and player-vs-track collisions are actually resolved. Other players are purely visual on
your screen (`RemoteCarView`): their car moves by interpolating the server's snapshots, with no local
collider, since only the server's copy of them is real.

Your own car is predicted locally so it responds the instant you press a key, and corrected by replaying
input rather than by guessing:

- The client samples its controls **once per physics tick**, numbers each one, and keeps every tick the
  server has not acknowledged yet. Packets go out 30 times a second but carry the last 12 ticks, so a
  lost packet is backfilled by the next one instead of leaving a hole in the server's input stream.
- The server applies **exactly one input tick per physics step** and reports which tick it last applied
  in every snapshot. It keeps a small queue to absorb jitter, actively drained toward 2 ticks, since
  depth beyond what late packets actually need is just added lag.
- When a snapshot arrives, the client resets a hidden copy of its car (`NetPredictor`, a duplicate of the
  track's static geometry in its own hand-stepped `PhysicsScene`) to the server's verified state and
  re-simulates every unconfirmed tick on it. That result is not an estimate of where the car should be,
  it is where the car *would* be, computed the same way the server computed its half.
- The corrected velocity and heading are taken immediately; the remaining position difference is fed in
  over `NetConfig.CorrectionSmoothing`, so a correction reads as the car settling rather than teleporting.

Measured driving at full throttle with continuous weaving, the worst single-frame movement the car's own
velocity does not account for is 0.34m at 60ms round trip and 0.29m at 250ms, against 0.24m for the same
driving with no networking at all. Standing still while connected, the car does not move at all.

### When a connection drops

The server destroys a car whose player has gone quiet for `PlayerTimeout` (8s), so a client that
stopped sending for that long is no longer in the snapshot broadcast - it would sit there reporting
"Online", sending input nobody applies, seeing nobody, and being seen by nobody, until the player
restarted the game. Two things prevent that now:

* the game keeps running when its window is not focused (`runInBackground`). Alt-tabbing used to
  freeze it outright, which is 8 seconds of silence about as often as someone reads a message.
* the client watches for snapshots, not just for a socket: 3 seconds without one and it drops back to
  the Hello handshake and rejoins (`NetClient.KeepAlive`), throwing away the state of the session it
  lost - including the server tick counter, so it also recovers from the server itself restarting.
  Unlike the first connect, a reconnect keeps knocking rather than falling back to offline play.

Rejoining means the server spawns a new car at the start line, so a player who genuinely dropped out
mid-race comes back there rather than where they were - the server did not keep their car.

The dedicated server caps its frame rate (`ServerBoot`). Left uncapped, the headless loop runs as fast
as the machine allows and Unity's clock runs with it: measured 155 physics steps per real second on an
idle 16-core box and 121 on the live server, against the 100 a client produces. The server runs out of
input to apply, repeats the last controls it saw on a third of its steps without acknowledging them,
and the client's unconfirmed buffer never drains - every snapshot then lands as a correction, which
plays as constant rubber-banding.

### Versions: are we on the same build?

The menu (bottom left) and the in-race status line both show the same string:

```
v2026-09-21.0e3e12e (net 1)
```

* the first part is the commit the build was made from (`+edits` if the tree was dirty). `BuildScript`
  reads it from git at build time - nothing to bump by hand. Two players with the same string are
  running the exact same code.
* `net N` is `NetProtocol.Version`, the **wire format**. This is the one that decides whether a client
  and the server can talk: the client sends it in its Hello, and a server that speaks a different
  version refuses the join and says so on the client's screen ("version mismatch ... pull and rebuild")
  instead of letting it in to misread every packet.

Different builds of the *same* `net` version still race together fine - that is most commits. Bump
`NetProtocol.Version` in the same commit as any change to what the messages contain, because a client
one version behind does not read slightly stale data, it reads a different packet entirely: a field
added to the snapshot shifts every field after it, so one side reads a tick counter as a player count
and gives up on the packet. On screen that looks exactly like a netcode bug - both players "Online",
neither able to see the other - which is what the version check now turns into a clear message.

After a protocol change, redeploy the server (`deploy/deploy.sh`) **and** rebuild every client;
old clients from before versioning existed simply fall back to "could not reach the server".

### Running the server

`Tools > Server > Build Server Scene` generates `Assets/Scenes/Server.unity` (just a `ServerBoot` object -
nothing to render, the server always runs `-batchmode -nographics`). `Tools > Build > Linux Server` (or
`deploy/deploy.sh`, see below) builds it: needs the **Linux Dedicated Server Build Support** module from
Unity Hub, in addition to the editor itself.

The built server takes which track to host from the command line, default `Street`:

```
RCRACE-server -batchmode -nographics -track=Street   # or Desert / Test
```

### Deploying

```bash
deploy/deploy.sh          # builds the Linux server, deploys to `ssh game`, installs it as systemd unit rcracer-server
deploy/deploy.sh myhost   # deploy to a different preconfigured ssh host instead
```

Requires passwordless SSH to the target already working (`ssh <host>` with no prompt) and sudo there;
`deploy/remote_install.sh` runs on the remote end to install/restart the systemd service.
Logs: `ssh game 'journalctl -u rcracer-server -f'`.

**Firewall:** the server needs inbound **UDP port 7777** open (see `NetConfig.cs`) - both on the host
itself (`ufw allow 7777/udp`, `firewall-cmd --add-port=7777/udp`, or an `iptables` rule) and, if the
machine sits behind a cloud provider's own firewall/security group, there too.

## Working with Claude Code from WSL (optional)

`tools/mcp-firewall.sh open|close|status` opens or closes the Windows firewall for the MCP servers
(Unity MCP on 8080, and 9877 forwarded to the Blender addon on localhost:9876).
`open`/`close` show a UAC prompt on Windows.

## What is in git

Only source and project settings: `Assets/`, `Packages/`, `ProjectSettings/`, the build scripts and this file.
`Library/`, `Logs/`, `UserSettings/`, `Build/`, IDE files and generated `.csproj`/`.sln` are ignored and regenerated by Unity.
`.gitattributes` normalizes line endings so files look the same on Windows and Linux.
