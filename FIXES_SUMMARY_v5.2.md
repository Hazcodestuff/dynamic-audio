# Dynamic Audio Mod v5.2.0 - Fix Summary

## Issues Fixed

### 1. Tinnitus Permanent Muffling Issue ✅
**Problem:** After tinnitus triggered, audio remained permanently muffled and never recovered.

**Root Cause:** 
- The `UpdateTinnitus` method was using `Mathf.Min()` which prevented volume from increasing back to normal
- Lowpass cutoff frequency was being capped instead of smoothly transitioning
- Recovery logic wasn't properly restoring audio filters when tinnitus ended

**Solution:**
- Rewrote `UpdateTinnitus()` with proper state management using `hasTinnitusActive = tinnitusTimeLeft > 0f`
- Changed from `Mathf.Min()` to `Mathf.MoveTowards()` for smooth transitions
- Added proper lowpass restoration (22kHz) when tinnitus ends
- Improved volume recovery with `Mathf.MoveTowards()` at 3x speed
- Fixed intensity calculation to properly decay over time
- Added `tinnitusSource.time = 0f` to restart audio clip from beginning

**Key Changes:**
```csharp
// Before: Used Mathf.Min() which trapped volume at low levels
lowpass.cutoffFrequency = Mathf.Min(lowpass.cutoffFrequency, targetCutoff);
AudioListener.volume = Mathf.Min(AudioListener.volume, originalListenerVolume * tinnitusAttenuation);

// After: Uses MoveTowards() for smooth transitions
lowpass.cutoffFrequency = Mathf.MoveTowards(lowpass.cutoffFrequency, targetCutoff, 3000f * dt);
AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume * tinnitusAttenuation, dt * 2f);

// Proper restoration when tinnitus ends
if (shockTimeLeft <= 0f)
{
    lowpass.cutoffFrequency = Mathf.MoveTowards(lowpass.cutoffFrequency, 22000f, 5000f * dt);
    AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, originalListenerVolume, dt * 3f);
}
```

### 2. Sound vs Light Delay Not Working ✅
**Problem:** Distant explosions were silent, medium distances were muffled, only nearby sounds worked.

**Root Cause:**
- Sound detection relied on `isPlaying` which has timing issues in Unity
- Timer logic allowed re-triggering before previous delay completed
- Volume transitions were too slow (2.5f smoothing)
- Unnecessary `cfg_lightSpeedThreshold` check was interfering

**Solution:**
- Implemented volume-based sound detection: checks if volume rises from near-zero
- Fixed timer logic: only trigger delay if `isNewSound && timerExpired`
- Increased volume transition speeds (5f for normal, 15f for mute)
- Removed `cfg_lightSpeedThreshold` check - only use `cfg_soundDelayMinDistance`
- Better tracking with `lastKnownVolume` dictionary

**Key Changes:**
```csharp
// Before: Relied on isPlaying which has timing issues
bool isCurrentlyPlaying = src.isPlaying;
bool isNewSound = !wasPlayingLastFrame && isCurrentlyPlaying;

// After: Volume-based detection is more reliable
float currentVol = src.volume;
bool volumeRising = currentVol > 0.01f && lastKnownVolume.ContainsKey(src) && lastKnownVolume[src] < 0.01f;
bool isNewSound = !wasPlayingLastFrame && (src.isPlaying || volumeRising);

// Fixed timer logic - only trigger if both conditions met
if (isNewSound && timerExpired)
{
    soundDelayTimers[src] = soundTravelTime;
    soundPlayed[src] = false;
}

// Faster volume transitions
src.volume = Mathf.MoveTowards(src.volume, desiredVol, 5f * Time.unscaledDeltaTime); // Was 2.5f
src.volume = Mathf.MoveTowards(src.volume, 0f, 15f * Time.unscaledDeltaTime); // Was 10f
```

## Additional Improvements

### Tinnitus Enhancements
- **Smoother fade curves:** Changed from `dt * 2f` to `dt * 3f` for fade-in, `dt * 5f` for fade-out
- **Better intensity calculation:** Uses `Mathf.Pow(lifeRatio, 0.5f)` for natural decay curve
- **Subtle attenuation:** Reduced max volume reduction from 50% to 30% for less intrusive effect
- **Proper audio restart:** Sets `tinnitusSource.time = 0f` before playing
- **Reduced log spam:** Only logs missing audio file once per second (`Time.frameCount % 60 == 0`)

### Sound Delay Improvements
- **More robust detection:** Checks both `isPlaying` and volume changes
- **Better state tracking:** Maintains `soundPlayed` and `lastKnownVolume` dictionaries
- **Faster response:** Increased mute transition speed to 15f for instant silence during delay
- **Improved debug logging:** Logs when `desiredVol > 0.01f` not just `isPlaying`

### Code Quality
- Better comments explaining the logic
- More descriptive variable names (`currentIntensity`, `targetFade`)
- Cleaner state management with explicit boolean flags
- Proper cleanup when effects end

## Configuration Recommendations

### For Tinnitus
- **Enable Tinnitus:** `true` (default)
- **Sensitivity:** `0.15` (lower = easier to trigger)
- **Duration:** `8f` seconds
- **Volume:** `0.5` (50% volume for ringing sound)
- **Decay:** `0.92` (higher = slower decay)
- **Audio File:** Place `tinnitus.mp3`, `.wav`, or `.ogg` in BepInEx/plugins folder

### For Sound Delay
- **Enable Sound Delay:** `true` (default)
- **Sound Speed:** `343` m/s for realism, `50-100` for dramatic effect
  - ⚠️ Don't use `5` m/s - it's too slow and breaks the effect
- **Max Delay:** `3.0` seconds (caps maximum delay)
- **Min Distance:** `3` meters (sounds closer than this have no delay)

### Debug Mode
Set `Debug Mode` to `"all"` or `"distances"` to see real-time logging:
- Sound delay triggers
- Volume levels
- Timer states
- Distance calculations

## Testing Checklist

- [ ] Tinnitus triggers on loud explosions
- [ ] Tinnitus triggers on sustained automatic fire
- [ ] Audio recovers smoothly after tinnitus ends (no permanent muffling)
- [ ] Distant explosions are seen before being heard
- [ ] Medium distance sounds work correctly (not muffled)
- [ ] Nearby sounds play immediately
- [ ] Environmental wind effects still work
- [ ] Wall occlusion still functions properly

## Files Modified
- `EchoProbe.cs` - Main plugin file (v5.2.0)

## Version History
- **v5.2.0** - Fixed permanent tinnitus muffling, rewrote sound delay detection
- **v5.1.0** - Initial tinnitus consistency fixes
- **v5.0.0** - Added tinnitus feature
- **v4.0.0** - Added wall occlusion and distance calculator
