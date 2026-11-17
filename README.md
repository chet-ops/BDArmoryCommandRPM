# BDArmoryCommandRPM

> **Unofficial add-on.** BDArmoryCommandRPM is a fan-made mod that connects **BDArmory Plus** and
> **RasterPropMonitor**. It is **not** made, endorsed or supported by the BDArmory Plus, BDArmory Extended,
> RasterPropMonitor or ASET teams. Please report problems with this mod here, not to the original mods' authors.
> It does not include any files from those mods: you need to install them separately.

Turns the **RasterPropMonitor** MFDs in your IVA (KSP 1) into a weapons panel for **BDArmory Plus**,
including the parts from **BDArmory Extended**.

The MFD's **CREW** button becomes **WEAPONS**. Pressing it switches the whole screen to a BDArmory interface where
**every MFD button** controls your equipment: radar, RWR and electronic warfare, weapons, the targeting camera and
system power. **EXIT** returns to the original menu.

Supported screens:
- **ASET ALCOR MFD 40x20** and **ALCOR MFD 60x30** (ASET Props)
- the **basic RasterPropMonitor MFD** (used by RPMPodPatches and many cockpits)
- **NMB-MFD-F18** (NMB F-18C and J-10C cockpits)

## Features

- **RADAR** – nose-up PPI with BDArmory's radar, sonar and IRST contacts, animated sweep, target cycling and locking.
- **EW/RWR** – threats by bearing, ECM jammers, cloaking and flare/chaff/smoke release.
- **SMS** – weapon list, ARM/GUARD, fire, engagement modes, AI pilot, and a **DATA page** per weapon:
  hand the camera's target to the weapon, manage GPS points and change missile settings.
- **TGP** – the **real image** of BDArmory's targeting camera, with all its controls on the MFD keys, plus
  mouse slewing and zoom on the panel screen.
- **SYS** – switch every BDArmory system on and off from the panel.
- **Cockpit sounds** – F/A-18 (AN/ALR-67) style RWR tones and avionics cues, all synthesized (no recordings).
- **Sharp text** on RPM screens and **hidden BDArmory windows** while you are in IVA.

## Requirements

- KSP 1.12.x
- **BDArmory Plus** (built and tested against v1.13.1)
- **BDArmory Extended** (optional – it is a parts pack that uses BDA+ modules, nothing extra needed)
- **RasterPropMonitor**, plus **ASET Props** and/or the NMB cockpits for the supported MFDs
- **ModuleManager**
- Do **not** use MOARdV's Avionics Systems (MAS) to convert the ASET props: MAS screens are not RPM screens.

## Supported mods

| Mod | Role | Tested with |
|---|---|---|
| **BDArmory Plus** | Required – weapons, radar, RWR, camera, AI | 1.13.1.0 |
| **RasterPropMonitor** (Adopted) | Required – the MFD screens | plugin 1.1.0.1 |
| **ModuleManager** | Required – applies the MFD patches | 4.2.3 |
| **BDArmory Extended** | Optional – its missiles, VLS and magazines show up like any BDA+ weapon | current release |
| **ASET Props** | Optional – ALCOR MFD 40x20 and 60x30 get the WEAPONS interface | 1.5 |
| **NMB cockpits** | Optional – NMB-MFD-F18 (F-18C, J-10C) gets the WEAPONS interface; NMB-MFD3 gets the sharp font only | – |
| **RPM basic MFD** (RPMPodPatches and cockpits that use it, e.g. NMB F-14D) | Optional – gets the WEAPONS interface | – |

Not supported: MOARdV's Avionics Systems (MAS) screens, and MAS's patch that converts ASET props to MAS.

## Installation

Download `BDArmoryCommandRPM-<version>.zip` from the Releases page and copy the `GameData/BDArmoryCommandRPM` folder into your
KSP `GameData` folder.

To uninstall, delete `GameData/BDArmoryCommandRPM`.

> Upgrading from an earlier build called **BDArmoryRPM**? Delete `GameData/BDArmoryRPM` first so the two are never
> loaded together (`build.bat` does this for you). Settings now live in the `BDARMORYCOMMANDRPM_*` nodes.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer (the project targets .NET Framework 4.8;
the reference assemblies are downloaded from NuGet).

```
build.bat "C:\path\to\Kerbal Space Program"
```

The script builds against your KSP, BDArmory and RasterPropMonitor, installs the mod into
`GameData\BDArmoryCommandRPM` and creates the release zip in `release\`. If the repository sits inside the KSP folder,
the path can be omitted.

Manual build: `dotnet build Source/BDArmoryCommandRPM.csproj -c Release -p:KSPDir="C:\path\to\Kerbal Space Program"`
(add `-p:InstallToKSP=true` to install and `-p:Package=true` to create the zip).

## The WEAPONS interface

### ALCOR MFD 40x20 and basic RPM MFD

```
 [ A ]   [ B ]   [ C ]   [ D ]   [ E ]   [ F ]   [ G ]
 RADAR | EW/RWR | SMS  |  TGP  |  SYS  |       | EXIT        <- select mode
 ┌───────────────────────────────────────────────────┐
 │                    mode screen                    │  ◄ ►   ▲ ▼ ✚ ✕ O
 └───────────────────────────────────────────────────┘  NEXT PREV
  F1   |  F2  |  F3  |  F4  |  F5  |  F6  |  F7         <- function keys (labelled)
 [R1]   [R2]   [R3]   [R4]   [R5]   [R6]   [R7]
```
- **Top row (A–G):** selects the mode. **G = EXIT** (**STBY** also works).
- **Bottom row (R1–R7):** functions of the current mode. The label is shown above each button and turns
  **green** when the function is active.
- **Side keys:** arrows, HOME, ENTER (✚), ESC (✕), NEXT (R9) and PREV (R10).

### ALCOR MFD 60x30

- **Top row (R1–R6):** functions of the mode. **STANDBY = EXIT**.
- **Bottom row (R7–R10):** RADAR | EW/RWR | SMS | TGP/SYS (press R10 again to toggle between TGP and SYS).
- **Side keys:** arrows and HOME as on the 40x20. NEXT acts as ENTER, PREV as ESC, Zoom− as R9 and Zoom+ as R10.

### NMB-MFD-F18 (F-18C, J-10C)

- WEAPONS is on **R5**. Top row (A–E): RADAR | EW/RWR | SMS | TGP | SYS.
- Bottom row (R3–R7): the first five functions of the mode. **STANDBY** returns to the original menu.

### Function keys

| Mode | F1 | F2 | F3 | F4 | F5 | F6 | F7 (40x20 only) |
|---|---|---|---|---|---|---|---|
| **RADAR** | Radar/sonar ON/OFF | Previous target | Next target | Unlock target | Unlock all | Slave turrets | Change range |
| **EW/RWR** | RWR ON/OFF | ECM ON/OFF | Cloak ON/OFF | Flare | Chaff | Smoke | — |
| **SMS** | ARM/SAFE | GUARD | **Fire** | **DATA** (weapon page) | AI pilot ON/OFF | Engagement mode | Next weapon |
| **SMS › DATA** | **TGT<TGP** (camera target to weapon) | Next GPS point | **Fire** | Back to list | Ripple ON/OFF | Delete GPS point | Camera → GPS |
| **SYS** | Radar ON/OFF | TGP camera ON/OFF | RWR ON/OFF | IRST ON/OFF | ECM ON/OFF | Cloak ON/OFF | All sensors ON (radar, camera, RWR, IRST) |
| **TGP** | **Camera power** | Lock/unlock point | Night vision | Send GPS | Point to GPS | CoM track | Slave turrets |

| Side key | RADAR | EW/RWR | SMS | TGP |
|---|---|---|---|---|
| ▲ / ▼ | Range + / − | ECM / Cloak | Previous / next weapon | **Slew camera** (hold) |
| ◄ / ► | Previous / next target | Flare / Chaff | Flare / Chaff | **Slew camera** (hold) |
| HOME | Radar ON/OFF | RWR ON/OFF | GUARD | Camera ON/OFF |
| ENTER (40x20) / NEXT (60x30) | Slave turrets | Smoke | ARM/SAFE | Radar → camera |
| ESC (40x20) / PREV (60x30) | Unlock target | — | Engagement mode | Reset camera |
| R9 (40x20) / Zoom− (60x30) | Previous weapon | — | DATA | Zoom − |
| R10 (40x20) / Zoom+ (60x30) | Next weapon | — | — | Zoom + |

Pressing **TGP** again while in TGP switches to the next camera. Pressing **SMS** again toggles between the weapon
list and the DATA page.

### SMS › DATA (weapon page)

With a missile or bomb selected, **DATA** shows its guidance (radar, IR, laser, GPS, anti-radiation, inertial or
unguided), where it gets its target from and whether it is ready (green) or something is missing (red), the
selected GPS point and the ripple setting.

- **TGT<TGP** hands the camera's target to the weapon (the camera locks on its own if it was not locked):
  - **laser:** the missile follows the point locked by the camera;
  - **GPS** or bomb: creates a GPS point where the camera is looking (named after the vessel, if there is one)
    and selects it;
  - **radar / IR / anti-radiation:** the radar locks the vessel the camera is looking at; without a radar, a GPS
    point is left instead.
- **GPS >** cycles through the saved GPS points (the same list as BDArmory's window), **GPS DEL** deletes the
  selected one and **CAM>GPS** points the camera at it.
- **Settings:** below, the missile settings BDArmory lets you change in flight (min/max launch range, max
  off-boresight, detonation distance, terminal guidance, cruise, ballistic...) and the weapon manager's missiles per
  target. ▲▼ selects, ◄► changes (within BDArmory's limits) and the change is applied to every identical missile
  on the vessel. HOME goes back to the list.
- Side keys in DATA: ENTER/NEXT = ARM, ESC/PREV = TGT<TGP, R9/Zoom− = GPS >, R10/Zoom+ = camera → GPS.
- On the F-18 screen the first five functions are available (TGT<TGP, GPS >, FIRE, BACK, RIPPLE).

### Mouse control of the TGP camera

With the camera on, put the pointer over the panel screen, **hold Alt** and
- **left-drag** to slew the camera (the same motion as in BDArmory's window – finer the more you zoom in);
- **scroll the wheel** to zoom in or out.

Without Alt, the mouse and wheel belong to the cockpit view as usual; with Alt held over that screen, the wheel only
zooms the camera. Releasing Alt stops the slewing. Configure it in `Settings.cfg` (`BDARMORYCOMMANDRPM_DISPLAY`):
`tgpMouseControl`, `tgpMouseKey` (another key or `None`), `tgpMouseSensitivity` and `tgpInvertWheel`.
The drag direction follows the mouse-invert options of BDArmory's camera window.

### SYS mode (system power)

Lists radar, camera, RWR, IRST, ECM, cloak, AI pilot and GUARD with their state. Besides the function keys:
▲/▼ select a system, ► (or ENTER) switches it on, ◄ (or ESC) switches it off, and HOME toggles all sensors at once.

### What each mode shows

- **RADAR:** nose-up radar display with range rings and a north mark. Contacts: hostile ■, friendly ○, missile ◆ and
  IR ○ with a slash, each with its velocity vector. Locked targets have brackets; the active target's data and the
  missile warning are shown at the bottom.
- **EW/RWR:** threats by bearing (SAM □, fighter △, AWACS ◇, launch ⊗ and missile lock, blinking), threat list,
  ECM and cloak state, and remaining flares, chaff and smoke on the labels.
- **SMS:** weapon list with type, guidance and remaining count (including BDA Extended VLS and magazines), ARM/GUARD
  state, engagement modes, health %, current target, countermeasures and missile warning.
- **TGP:** the **real image** of BDArmory's targeting camera, with reticle, zoom, LR/NO LR, range and coordinates
  of the locked point, gimbal limit warning and attitude indicator.

## Cockpit sounds

Played through the cockpit speaker (IVA only, unless you change it), even when the MFD is not on WEAPONS.

**F/A-18 style RWR (AN/ALR-67).** Replaces BDArmory's RWR beeps with tones built from the documented patterns of
the Hornet's RWR:

| Situation | Tone |
|---|---|
| New surface emitter (SAM, ground radar) | Single 455 Hz tone, 0.335 s |
| New airborne emitter (fighter, AWACS) | 0.335 s burst of four descending tones 550-520-490-470 Hz |
| You are locked (STT) | 455/555 Hz alternating every 0.25 s, continuous |
| Missile guidance or launch | 455 Hz (0.066 s) / 555 Hz (0.1 s) alternating fast, continuous |

No aircraft recordings are included: these are synthesized tones with those frequencies and timings.

**Other cues:**

| Event | Sound |
|---|---|
| Radar on / off | Rising spool-up / falling spool-down |
| Radar sweep | Soft tick on every pass (off by default – real aircraft don't have it) |
| New contact | Short blip |
| Target locked | Fast double tone ×3 |
| Target lost / unlocked | Two falling tones / short tone |
| GUARD on / off | Two rising / falling tones |
| ECM or cloak on / off | Rising / falling double beep |
| AI pilot on / off | Chime / autopilot disconnect wailer |
| Health ≤ 50 % / ≤ 25 % | "Ding-dong" caution / warning |
| Flares or chaff bingo / empty | BINGO (3 beeps) / long tone |
| TGP camera | Servo whine while slewing, zoom click and double beep on lock |
| MFD buttons | Click |

The IR missile growl, ARM and weapon change sounds remain BDArmory's own.

Everything is set in `GameData/BDArmoryCommandRPM/Settings.cfg`: volume, IVA only, clicks, sweep, and the BINGO and damage
thresholds. Alarms (caution, warning, BINGO, autopilot disconnect, target lost) and RWR tones are loud and slightly
saturated like a fighter's cockpit speaker: they use `alarmVolume` (default 1.0, the maximum) and only depend on the
game's "Ship" volume, not on `volume`. Any sound can be **replaced with your own recording** (.ogg/.wav) through its
`clip_...` line.

## What it changes in the MFDs

- On the **40x20** the crew page is removed. On the **60x30**, the **basic RPM MFD** and the **NMB F-18 MFD**, the
  crew and flight log pages are removed.
- The **CREW** label on every page becomes **WEAPONS**. To avoid touching ASET's files, when KSP starts the mod
  generates copies with the new label in `GameData/BDArmoryCommandRPM/Labels/` and the pages read those copies.
- **STBY** is marked as an unlock page so you can leave WEAPONS with it.

## Sharp text

RPM draws glyphs with a half-texel offset and bilinear filtering, which makes them blurry with slivers of the
neighbouring glyph. The mod:
- replaces the main font of the supported MFDs (and NMB-MFD3) with **BDAMainFont** (generated from DejaVu Sans Mono
  Bold, see `Fonts/LICENSE-DejaVu.txt`), with the same cell size, so no page layout moves;
- sets point filtering on the glyphs of every RPM screen and enables anisotropic filtering on the screen.

Disable it by deleting `Patches/SharpText.cfg` (font) and with `sharpText = false` in `Settings.cfg`.

## BDArmory windows in the cockpit

While you are in the cockpit (IVA), BDArmory's floating windows (radar, Target Camera, RWR, weapon manager...)
are hidden, even when you switch systems on from the screen. Everything keeps working and is controlled from the
MFDs. They come back when you leave the cockpit (C key).

- To see them without leaving the cockpit: press F2 twice.
- To disable this: `hideBDAWindowsInIVA = false` in the `BDARMORYCOMMANDRPM_DISPLAY` node of `Settings.cfg`.

## Customizing

Everything is in `GameData/BDArmoryCommandRPM/Patches/*.cfg`:
- `modeSlots`: which mode goes on each button of the mode row (`RADAR,EW,SMS,TGP,SYS,EXIT`, `NEXT`, `TGP/SYS` or empty).
- `keyMap`: which side key does each action.
- `defaultMode`: the mode it opens with.
- **Radar:** `rangeSteps` and `defaultRangeIndex`.
- **Sizes:** `contactSize`, `rwrSymbolSize`, `reticleSize` and `lineThickness`.
- **Colours:** `textColor`, `hostileColor`, `labelColor`, `labelActiveColor`, `labelCurrentColor`, etc.

## Notes

- The TGP mode shows the same image as BDArmory's *Target Camera* window.
- Some BDArmory internals (contact list, RWR pings and several camera controls) are accessed via reflection.
  If a future BDArmory version renames them, only that function stops responding.
- If something goes wrong, search for `[BDArmoryCommandRPM]` in `KSP.log`.

## License

BDArmoryCommandRPM is released under the **GNU LGPL v2.1** (see `LICENSE`). The bundled BDAMainFont is derived from
DejaVu Sans Mono and keeps its own licence (`GameData/BDArmoryCommandRPM/Fonts/LICENSE-DejaVu.txt`). The mods it works
with are not included and keep their own licences.

## Credits

This mod would not exist without the work of the original authors. All credit for the mods it builds on goes to them:

- **BDArmory** – originally created by **Paolo Encarnacion (BahamutoD)**. Continued as BDArmory Continued by
  **Joe Korinek (Papa_Joe)** and the BDArmory team (**jrodriguez, SpannerMonkey(smce), gomker, TheDog,
  DoctorDavinci**), and now developed as **BDArmory Plus** by the **BDA+ team** (forum thread by **DocNappers**).
  Licensed under CC-BY-SA 2.0.
- **BDArmory Extended** – weapons parts pack for BDArmory Plus by the BDA+ team
  ([BrettRyland/BDArmory-Extended](https://github.com/BrettRyland/BDArmory-Extended)).
- **RasterPropMonitor** – originally created by **Mihara**, maintained for years by **MOARdV**, and currently
  maintained by **JonnyOThan** (RasterPropMonitor Adopted), with many other contributors.
- **ASET Props** and **ASET Avionics** – by **alexustas**, now maintained as ASET Consolidated by **Stone Blue**.
  Licensed under CC-BY-NC-SA 3.0.
- **NMB** cockpits (NMB-MFD-F18 and the other NMB MFDs) – by their respective author.
- **DejaVu Sans Mono** font – by the DejaVu fonts team (see `GameData/BDArmoryCommandRPM/Fonts/LICENSE-DejaVu.txt`).
- **ModuleManager** – by sarbian and contributors.

All trademarks and mod names belong to their respective owners.
