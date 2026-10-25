# Dynamic Audio 1.5.1 (for Ravenfield)

A BepInEx plugin that makes Ravenfield sound like a real battlefield: sounds get muffled behind walls, rooms add reverb, loud shots leave a short echo in open spaces, and (optionally) your ears ring after a nearby explosion.

> **1.5.x is a full rebuild of V6.** It fixes the master switch turning itself off, the menu not opening, sounds overloading then going silent, and random muffled audio in vehicles. See [docs/V6_BUG_REPORT.md](docs/V6_BUG_REPORT.md) for what was wrong and how it was fixed.

---

## Contents

1. [Features](#features)
2. [Installation](#installation)
3. [Opening the settings menu](#opening-the-settings-menu)
4. [Settings explained](#settings-explained)
5. [Customize the mod yourself (step-by-step tutorial)](#customize-the-mod-yourself-step-by-step-tutorial)
6. [Troubleshooting](#troubleshooting)
7. [What changed in 1.5.0](#what-changed-in-150)
8. [Credits](#credits)

---

## Features

| Feature | What it does | Default |
|---|---|---|
| **Sound occlusion** | Casts rays from you to each sound. Walls muffle (low-pass filter) and quieten it, depending on how thick and what material the wall is. Sounds from a roofed-over building are muffled a little more. | On |
| **Room reverb** | Measures the space around you (small room, hallway, tunnel, warehouse, open field...) and adjusts reverb to match. | On |
| **Echo** | A short echo tail after **loud** sounds only (gunshots, explosions) in large or open spaces. Reloading, footsteps and your own quiet sounds never trigger it. | On |
| **Shockwave** | A brief muffled "concussion" when an explosion goes off close to you. | On |
| **Air absorption** | Far-away sounds lose a little volume and high frequencies. | On |
| **Tinnitus** | Ear ringing after a very close explosion or long loud gunfire. Gentle by default, with a rest time between rings. | **Off** |
| **Travel delay** (experimental) | Delays distant gunshots and explosions by distance / speed of sound. Your own weapon and reloads are never delayed. | **Off** |
| **Sigma Mode** | The simple, bug-proof mode: ONLY the room reverb stays on. No muffling, echo, shockwave, tinnitus or delay, and nothing is ever attached to game sounds. See below. | Off |
| **In-game menu** | Settings live inside the game: ESC > OPTIONS > **DYNAMIC AUDIO**. F11 also works. | On |

Your own weapon, your own vehicle and reload sounds are detected using the game's own audio mixer groups, so they are never muffled, delayed, or echoed by mistake.

---

## Installation

1. Install **BepInEx 5** (the 5.4.x x86/x64 build matching your game) into your Ravenfield folder, and run the game once so the `BepInEx` folder is created. If you don't know how, see the video: https://youtu.be/JWvBX3oYIs8
2. Download `DynamicAudio.dll` from the **Releases** tab.
3. Put it into `Ravenfield/BepInEx/plugins/`.
4. Start the game. The config file is created automatically at `Ravenfield/BepInEx/config/lexge.dynamicaudio.cfg`.

To **update**, just replace the old DLL. Your settings are kept. Old V6 settings are migrated automatically (the master switch is switched back on, and tinnitus is moved to the new gentler defaults).

---

## Opening the settings menu

There are two ways, and both do the same thing:

- **In the pause menu:** press **ESC** > **OPTIONS** > click the new **DYNAMIC AUDIO** button.
- **Hotkey:** press **F11** at any time (you can rebind it inside the menu).

Press **ESC** or click **Close** to close the window. Changes apply live and are saved automatically.

If the extra button doesn't show up in the pause menu (for example, a future game update changes the menu layout), a small "Dynamic Audio" button appears in the top-right corner of the screen while the game is paused. F11 always works. You can turn the button off in `[General] PauseMenuButton`.

---

## Sigma Mode (reverb only)

Not everyone wants tinnitus, delay, muffling and echo. **Sigma Mode** is a single switch for those players: it keeps **only the room reverb** and turns everything else off. Because the mod then never touches individual game sounds, it is the simplest and safest way to run it. If you ever hear an audio oddity, switch Sigma Mode on to check whether the mod is involved.

Turn it on in the menu (**General > SIGMA MODE**) or in the config: `[General] SigmaMode = true`. Reverb settings (intensity, room size) still apply.

---

## Settings explained

Every setting is in the in-game menu and in the config file. The ones most people want to change:

| I want... | Change this |
|---|---|
| Less muffling through walls | `[Filtering] WallMuffleStrength` (try 0.5), or raise `MinCutoffHz` |
| Less reverb | `[Reverb] Intensity` |
| Only reverb, nothing else | `[General] SigmaMode = true` |
| No echo at all | `[Echo] Enabled = false` |
| Ringing ears | `[Tinnitus] Enabled = true`. Then tune `Sensitivity`, `RingVolume`, `ExplosionRadius`, `Cooldown` |
| Rarer / quieter tinnitus | Lower `Sensitivity`, lower `RingVolume`, raise `Cooldown` |
| Better FPS | `[Performance] MaxSimulatedSounds` lower, `RayCount` lower |
| Realistic distant-sound delay | `[TravelDelay] Enabled = true` (experimental) |
| See what the mod is doing | `[Debug] Enabled = true` shows an on-screen overlay |

If anything feels wrong, use **Reset to defaults** at the bottom of the menu.

---

## Customize the mod yourself (step-by-step tutorial)

The mod is open source. You do not need to know how to code: you can ask an AI assistant (like Claude) to change it for you, then build it yourself. Follow every step in order.

### What you need (one-time setup)

1. **Ravenfield with BepInEx installed** (see [Installation](#installation)). You must have run the game once with BepInEx so that `BepInEx/core/BepInEx.dll` exists.
2. **Visual Studio Code**: https://code.visualstudio.com
3. **.NET SDK 8** (or newer): https://dotnet.microsoft.com/download. This is what turns the code into a `.dll`. After installing, restart your computer, or at least close and reopen VS Code.
4. Optional but handy: the **C# Dev Kit** extension in VS Code (Extensions icon on the left > search "C#").
5. An AI assistant such as Claude (https://claude.ai).

### Step 1: Get the code

1. On this GitHub page, click the green **Code** button > **Download ZIP**.
2. Unzip it somewhere easy, like `Documents\DynamicAudio`.
3. Inside it you will find a folder named **`src`**. That folder is the entire mod.

### Step 2: Ask Claude to change it

1. Go to https://claude.ai and start a new chat.
2. Upload **all the files inside the `src` folder** (select them all and drag them into the chat, or zip the `src` folder and upload the zip).
3. Describe what you want in plain words. Good examples:
   - *"Make the muffling through walls weaker, and make explosions louder in tunnels."*
   - *"Add a new setting that makes reverb stronger in tunnels."*
   - *"Make tinnitus last only 2 seconds."*
   - *"Disable the shockwave effect completely."*
4. Be specific about what you want to *hear*, not just what to change. Say "keep the same settings menu style" if you want that.
5. Claude will send back changed files (or a new zip). **Replace the old files in your `src` folder with the new ones.** Keep the same file names.

> Tip: tell Claude *"also add every new option to the settings menu and the config file"*. Otherwise a new option might exist only in the code.

### Step 3: Open the project in VS Code

1. Open **Visual Studio Code**.
2. **File > Open Folder...** and choose your **`src`** folder (the one that contains `DynamicAudio.csproj`).
3. If VS Code asks "Do you trust the authors?", choose **Yes**.

### Step 4: Tell the project where your game is

Open `DynamicAudio.csproj` in VS Code. Near the top you will see the default game path:

```xml
<RavenfieldDir ...>C:\Program Files (x86)\Steam\steamapps\common\Ravenfield</RavenfieldDir>
```

If your Ravenfield is installed somewhere else, you have two options:

- Change that path in the file, **or**
- Pass the path when building (Step 5, recommended, no file editing needed).

Not sure where your game is? In Steam, right-click Ravenfield > **Manage** > **Browse local files**, then copy the folder path from the address bar.

### Step 5: Build

1. In VS Code, open the terminal: **Terminal > New Terminal**. Make sure it is in the `src` folder.
2. Run this (change the path to your own Ravenfield folder; keep the quotes):

   ```
   dotnet build -c Release -p:RavenfieldDir="C:\Program Files (x86)\Steam\steamapps\common\Ravenfield"
   ```

3. Wait until you see **Build succeeded**. Your new file is here:

   ```
   src\bin\Release\net472\DynamicAudio.dll
   ```

**If the build fails**, copy the red error text (the lines containing `error CS...`), paste it back to Claude along with the file it mentions, and ask it to fix the code. Repeat Steps 2-5 until it builds. This is completely normal when making changes.

Common build errors:

| Error | Meaning / fix |
|---|---|
| `Could not find ... UnityEngine.CoreModule.dll` | Your `RavenfieldDir` path is wrong. Check it points at the folder containing `ravenfield_Data`. |
| `Could not find ... BepInEx.dll` | BepInEx isn't installed in that game folder. Install it and run the game once. |
| `CS0012: The type ... is defined in an assembly that is not referenced` | Tell Claude the module name in the message; it will add a `<Reference>` line to the `.csproj`. |
| `dotnet is not recognized` | The .NET SDK isn't installed, or VS Code needs to be restarted. |
| `error CS...` in a `.cs` file | Paste it to Claude. |

### Step 6: Install your build

1. **Close Ravenfield.**
2. Delete or move the old `DynamicAudio.dll` from `Ravenfield\BepInEx\plugins\` (two copies of the mod fight each other).
3. Copy your new `DynamicAudio.dll` from `src\bin\Release\net472\` into `Ravenfield\BepInEx\plugins\`.

### Step 7: Test

1. Start Ravenfield and load a match.
2. Open the menu (ESC > OPTIONS > DYNAMIC AUDIO, or F11) and check your change.
3. Turn on `[Debug] Enabled` to see the overlay of tracked sounds if you need to understand what is happening.
4. If something sounds wrong, check `Ravenfield\BepInEx\LogOutput.log` and send the lines mentioning "Dynamic Audio" to Claude.

### Where to ask for changes (what each file does)

You don't need to read the code, but this tells you what to mention to the AI:

| File | What it controls |
|---|---|
| `DynamicAudioPlugin.cs` | The brain: all settings, finding sounds, applying muffle and volume, the failure handling. |
| `OcclusionSystem.cs` | How walls are detected between you and a sound. |
| `Classifier.cs` | How sounds are labelled (gunshot, explosion, vehicle...) and the wall material table. |
| `RoomSystem.cs` | Measuring the room around you. |
| `ListenerAudioSystem.cs` | Reverb, echo, shockwave on your ears. |
| `TinnitusSystem.cs` | Ear ringing. |
| `TravelDelay.cs` | The experimental speed-of-sound delay. |
| `SettingsUi.cs` | The in-game settings window. |
| `GameMenuIntegration.cs` | The button in the ESC > OPTIONS menu. |
| `AcousticGainFilter.cs` | The volume filter (never touches the game's own volume). |
| `DynamicAudio.csproj` | Build settings and game paths. |

### Turning off the experimental delay hook

If you never want the travel delay (it uses a Harmony hook), build without it:

```
dotnet build -c Release -p:RavenfieldDir="C:\...\Ravenfield" -p:DefineConstants=NO_HARMONY
```

### Tips for safe experimenting

- **Keep a backup** of your working `DynamicAudio.dll` and `src` folder before asking for changes.
- Change **one thing at a time** so you know which change caused a problem.
- The mod never writes to the game's audio volumes, and a bug in one sound can't take down the rest, but a build error simply means the DLL isn't produced: your old one keeps working.
- Share your improvements! Open an Issue or Pull Request.

---

## Troubleshooting

| Problem | Try this |
|---|---|
| Nothing changes in game | Check `BepInEx/LogOutput.log` for "Dynamic Audio v1.5.1 initialized". If it's missing, the DLL isn't in `BepInEx/plugins`, or BepInEx isn't installed. |
| Menu button missing in ESC > OPTIONS | Use F11 or the corner button that shows while paused. |
| Too muffled | Lower `WallMuffleStrength` or raise `MinCutoffHz`. |
| Echo still sounds odd | Turn `[Echo] Enabled` off, and tell me which situation it happens in. |
| Tinnitus too much | Lower Sensitivity/RingVolume or raise Cooldown, or switch it off. |
| Lower FPS | Lower `MaxSimulatedSounds` and `RayCount`. |
| Odd audio of any kind | Turn on **Sigma Mode** (reverb only). If the problem disappears, one of the other effects was involved; please report which one and when. |
| "Safe mode" in the debug overlay | The mod hit many errors in a row and paused itself for 5 seconds. It recovers on its own and never changes your settings. Please report the log. |

---

## What changed in 1.5.1

- **Paused game:** all of the mod's filters switch off completely while the game is paused (ESC menu) and come back on resume. This fixes the stuttering/droning audio in the pause menu.
- **No more sudden volume jumps:** sounds are never snapped back to full volume (when the sound budget swaps sounds, or when a wall check flickers). Levels now ease in slowly and out quickly, in decibels. Listener filters (reverb/echo/low-pass) get their values set *before* they are switched on, so Unity's loud defaults are never heard.
- **Travel delay rebuilt:** the delayed sound is now played from a temporary copy at the spot where the shot happened, instead of re-using the game's recycled audio source. Only gunshots and explosions are delayed, with a hard limit on how many can wait at once, and nothing is delayed while paused.
- **Sigma Mode** added (reverb only).

## What changed in 1.5.0

- Master switch is never turned off automatically; failures are isolated per sound and recover by themselves.
- Menu opens reliably, handles the cursor properly, and is also reachable from ESC > OPTIONS.
- No more silent-audio overload: the mod no longer fights the game over `AudioSource.volume`; it uses audio-thread filters instead.
- Vehicles no longer get random muffling; the player's own vehicle and weapon are never processed.
- Echo is event-driven: only loud shots and blasts, never reloads or footsteps.
- Tinnitus is much less sensitive: closer trigger radius, line-of-sight check, lower exposure rate, shorter rings, quieter tone, and a cooldown between rings.
- Better muffling: log-scale filter curve, wall thickness and material, roofed-source detection, smoother transitions.
- Travel delay never delays your own sounds or reloads.
- Config migration keeps your settings while fixing bad V6 values.

---

## Credits

Original concept and development by the Dynamic Audio author.  
1.5.x rebuild with Claude (Anthropic).
