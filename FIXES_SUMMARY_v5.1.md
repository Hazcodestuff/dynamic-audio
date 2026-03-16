# Audio Fixes Summary - Version 5.1.0

## Issues Addressed

### 1. Tinnitus Inconsistency (FIXED)
**Problem:** Tinnitus was playing inconsistently - sometimes it would play, sometimes it would just muffle the sound without the actual ringing audio.

**Root Cause:** The tinnitus audio playback logic had several issues:
- Volume calculation was too complex and could result in zero volume
- Audio clip loading wasn't properly verified before attempting playback
- No smooth fade-in/out for the tinnitus sound
- The tinnitus effect was applied but the actual sound wasn't always triggered

**Fix:**
- Created a dedicated `UpdateTinnitus()` method with cleaner state management
- Added `tinnitusAudioFade` variable for smooth volume transitions
- Improved volume curve to peak early then decay naturally
- Added proper checks to ensure audio clip is loaded before playing
- Added warning logs when tinnitus audio file is missing
- Separated tinnitus handling from shock audio for better clarity

**Configuration Recommendations:**
- `Enable Tinnitus`: true (default)
- `Sensitivity`: 0.15 (lower = easier to trigger, try 0.1 for more frequent tinnitus)
- `Ring Volume`: 0.5-0.7 (higher = more noticeable ringing)
- `Base Duration`: 8-12 seconds
- **IMPORTANT**: Make sure you have a `tinnitus.mp3`, `tinnitus.wav`, or `tinnitus.ogg` file in your `BepInEx/plugins/` folder!

### 2. Sound vs Light Delay Not Working Properly (FIXED)
**Problem:** Setting sound speed to 5m/s didn't create proper delays. Far explosions were silent, medium distance sounds were muffled, only close sounds were clear.

**Root Cause:** The sound delay system was working BUT it was interacting poorly with other audio effects:
- Wall occlusion was being applied on top of delay, causing muffling
- The delay detection logic wasn't robust enough for continuous sounds
- Volume smoothing was too aggressive during delay phase

**Fix:**
- Enhanced debug logging to track delay state, volume, and timer values
- Improved new sound detection to handle both instant and continuous sounds
- Better separation between delay phase and occlusion effects
- Added comprehensive logging showing: distance, current volume, desired volume, delay timer state

**Configuration Recommendations:**
- `Enable Sound Delay`: true
- `Sound Speed`: 
  - For realism: 343 m/s (speed of sound in air)
  - For dramatic effect: 50-100 m/s (noticeable delays at shorter distances)
  - **DO NOT set to 5 m/s** - this is unrealistically slow and breaks the experience
- `Max Delay`: 3.0-5.0 seconds (prevents excessive delays)
- `Min Distance for Delay`: 3-5 meters (sounds closer than this play instantly)
- `Light Speed Threshold`: 1 meter (distance at which light/sound difference becomes noticeable)

**Example Calculations:**
- At 343 m/s (realistic): 100m = 0.29s delay, 500m = 1.45s delay
- At 100 m/s (dramatic): 100m = 1.0s delay, 500m = 5.0s delay (capped)
- At 50 m/s (very dramatic): 50m = 1.0s delay, 250m = 5.0s delay (capped)

### 3. Environmental Effects (WORKING)
**Status:** Working correctly - wind noises are audible.

**Configuration:**
- `Enable Weather Effects`: true
- `Wind Effect`: 0.1-0.3 (subtle volume modulation)
- `Ground Reflection Boost`: 1.05-1.15 (slight boost for sounds from below)
- `Environmental Strength`: 0.5-1.0

### 4. Wall Occlusion (WORKING - Could Be Improved)
**Status:** Working decently but could use refinement.

**Current Implementation:**
- Uses multi-ray casting (primary + 3 offset rays) for robust wall detection
- Handles thin walls better with offset ray checks
- Applies consistent muffle factor when walls are detected

**Potential Improvements:**
- Could add frequency-dependent filtering (high frequencies absorbed more)
- Could vary muffle amount based on wall material/thickness
- Could add smoother transitions when moving behind walls

**Configuration:**
- `Enable Wall Occlusion`: true
- `Wall Rays`: 32-64 (higher = more accurate but more CPU)
- `Wall Ray Distance`: 50-100 meters
- `Wall Muffle Amount`: 0.3-0.5 (lower = more muffled when behind walls)

## Debug Mode Usage

To diagnose issues, set `Debug Mode` config to one of:
- `"all"` - Shows all debug information
- `"distances"` - Shows sound delay and distance tracking
- `"walls"` - Shows wall occlusion detection
- `"none"` - No debug logging (default)

**Key Log Messages to Watch For:**
- `SOUND DELAY:` - Shows when a sound is being delayed and for how long
- `Source: [name], Distance: X m, Volume: Y, Desired: Z, InDelay: true/false` - Real-time status
- `WALL OCCLUSION:` - Shows when a sound is blocked by a wall
- `TINNITUS TRIGGERED:` - Shows when tinnitus activates and why
- `Tinnitus sound started/stopped` - Tracks tinnitus audio playback

## Recommended Configuration for Best Experience

```ini
[General]
Probe Interval = 0.12
Probe Rays = 40
Max Probe Distance = 40

[SoundDelay]
Enable Sound Delay = true
Sound Speed = 100  ; Dramatic but playable
Max Delay = 3.0
Min Distance for Delay = 5

[Tinnitus]
Enable Tinnitus = true
Sensitivity = 0.15
Base Duration = 8
Ring Volume = 0.6
Decay Rate = 0.92
Audio File = tinnitus.mp3  ; MAKE SURE THIS FILE EXISTS!

[WallOcclusion]
Enable Wall Occlusion = true
Wall Rays = 32
Wall Ray Distance = 50
Wall Muffle Amount = 0.4

[Environmental]
Enable Weather Effects = true
Wind Effect = 0.15
Ground Reflection Boost = 1.08
```

## Installation Notes

1. **Tinnitus Audio File Required**: Place a tinnitus sound file (mp3, wav, or ogg format) in your `BepInEx/plugins/` folder. The default name is `tinnitus.mp3`. You can download royalty-free tinnitus/ringing sounds from various sources.

2. **Config File Location**: After first run, edit `BepInEx/config/dynamic.audio.cfg` to customize settings.

3. **Debug Logging**: Enable BepInEx console to see debug messages. Set `Debug Mode = "all"` for maximum information.

## Testing Checklist

- [ ] Tinnitus triggers on loud explosions (check console for "TINNITUS TRIGGERED" message)
- [ ] Tinnitus sound actually plays (you should hear ringing)
- [ ] Distant explosions are seen before heard (with sound delay enabled)
- [ ] Medium distance sounds are clear, not muffled (unless behind walls)
- [ ] Close sounds play immediately without delay
- [ ] Wind effects are subtle but present
- [ ] Walls properly muffle sounds from the other side

## Known Limitations

1. **Continuous Sounds**: Some continuous sounds (like vehicle engines) may have slight volume fluctuations due to the delay tracking system. This is a trade-off for accurate explosion delays.

2. **Very Fast Sounds**: Extremely brief sounds at far distances might be missed if they finish before the delay timer expires.

3. **Tinnitus Stacking**: Rapid successive explosions will reset the tinnitus duration but won't stack intensity. This is intentional to prevent overwhelming the player.

4. **Wall Detection**: Thin or complex geometry may occasionally cause false positives/negatives in wall occlusion. The multi-ray system helps but isn't perfect.
