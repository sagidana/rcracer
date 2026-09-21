# RCRACE

Arcade RC-car racing game made with **Unity 6 (6000.3.24f1)**, Universal Render Pipeline and the new Input System.

## Project map

```
.
├── Assets/
│   ├── Scenes/SampleScene.unity      the one playable scene (Car, Ground, TestTrack, StreetTrack, camera, light, volume)
│   ├── Scripts/                      runtime code (ships in the .exe)
│   │   ├── CarInput.cs               keyboard -> Throttle / Steer / Handbrake / Reset. Knows nothing about physics.
│   │   ├── CarController.cs          the car: 4 wheels, spring+damper suspension via sphere casts, drive/brake/side grip.
│   │   │                             Reads CarInput, runs only in FixedUpdate.
│   │   ├── CarTuning.cs              the inspector foldouts of CarController (Body, Suspension, Drive, Steering,
│   │   │                             Grip, Collision, Props, Debug) - every tunable number lives here
│   │   ├── CarWheel.cs               one physics wheel (mount point + current contact). Plain class, owned by CarController.
│   │   ├── CarWheelVisuals.cs        moves the 3D model's wheel meshes to match the physics (bounce, spin, steer)
│   │   ├── FollowCamera.cs           chase camera with smoothing and impact shake
│   │   ├── SpeedDisplay.cs           km/h text in the corner (OnGUI)
│   │   ├── KnockableProp.cs          light cones / trash cans / mailboxes that fly away and return home
│   │   ├── CarController_Old.cs      backup of the earlier "sliding box" car. Disabled, kept for reference.
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
│   │       ├── PropPhysicsTool.cs        Tools > Make Props Knockable
│   │       ├── PhysicsTestObstacles.cs   Tools > Add Physics Test Obstacles   bumps / steps / wall to test suspension
│   │       └── BuildScript.cs            Tools > Build > Windows (x64), and the entry point of build.sh / build.bat
│   ├── Settings/                     URP render pipeline assets (PC + Mobile), renderers, volume profiles
│   ├── InputSystem_Actions.inputactions   default Input System action map (CarInput reads the keyboard directly)
│   └── Free Adventure Vehicles/      car models used by Car Model switcher: Vehicle14 (bike), 16 (roadster), 19 (buggy)
├── Packages/manifest.json            Unity packages (URP 17.3, Input System 1.20, ...) - Unity keeps this in sync
├── ProjectSettings/                  project-wide settings: physics, quality, tags, input, build scenes, editor version
├── build.sh / build.bat              headless Windows build from WSL/Linux or Windows -> Build/Windows/RCRACE.exe
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

With the Windows editor and a repo inside the WSL filesystem (`/home/...`), Unity.exe cannot open the project
directly: it refuses case-sensitive filesystems, and `\\wsl.localhost` is slow anyway. The script handles this by
syncing `Assets/`, `Packages/` and `ProjectSettings/` to a staging folder on the Windows drive
(`%LOCALAPPDATA%\RCRACE-build`, override with `STAGE=/mnt/c/...`), building there with a cached `Library/`
(so later builds are much faster), and copying the result back to `Build/Windows/`. Needs `rsync`
(`sudo apt install rsync`). A repo checked out under `/mnt/c/...`, or the Linux editor, builds in place.

The full editor log is written to `Build/build.log`.

### Editor version

Both scripts look for the exact version in `ProjectSettings/ProjectVersion.txt` first. If it is missing they fall back
to the newest installed editor of the same major version (e.g. any `6000.x`) and print a warning: Unity then upgrades
the project to that version on first open and rewrites `ProjectVersion.txt`. Keep everyone on the same version, so
either install the exact one from the Hub's **Archive** tab, or agree on the newer version and commit the upgrade once.

## What is in git

Only source and project settings: `Assets/`, `Packages/`, `ProjectSettings/`, the build scripts and this file.
`Library/`, `Logs/`, `UserSettings/`, `Build/`, IDE files and generated `.csproj`/`.sln` are ignored and regenerated by Unity.
`.gitattributes` normalizes line endings so files look the same on Windows and Linux.
