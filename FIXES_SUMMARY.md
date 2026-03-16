# EchoProbe v5.0.1 - Robust Implementation Fixes

## Issues Fixed

### 1. **Compilation Error: Ambiguous 'Object' Reference** ✅
**Problem:** The code used `Object.FindObjectOfType` and `Object.FindObjectsOfType` which caused ambiguity between `UnityEngine.Object` and `object`.

**Solution:** Changed all instances to use fully qualified `UnityEngine.Object.FindObjectOfType` and `UnityEngine.Object.FindObjectsOfType`.

**Files Modified:**
- Line 315: `listener = UnityEngine.Object.FindObjectOfType<AudioListener>();`
- Line 796: `AudioSource[] all = UnityEngine.Object.FindObjectsOfType<AudioSource>();`
- Line 1101: `AudioSource[] allSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();`
- Line 1169: `AudioSource[] allAudioSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();`

### 2. **Unused Variable Warning: currentTemperature** ✅
**Problem:** The `currentTemperature` field was assigned but never used, causing compiler warning CS0414.

**Solution:** 
- Added comment clarifying it's USED for air absorption calculations
- Created new `UpdateEnvironmentalEffects()` method that actively uses this variable
- Added `lastEnvironmentalUpdateTime` tracker for periodic updates
- Environmental effects now update every 0.5 seconds when enabled

### 3. **Sound Delay (Light vs Sound) Not Working** 🔧
**Problem:** Users reported that even with sound speed set to 5 m/s, there was no noticeable delay between visual effects and audio.

**Root Causes Identified:**
1. Sound delay detection wasn't properly tracking when sounds START playing
2. Volume muting during delay phase wasn't robust enough
3. New sound events weren't being detected reliably

**Solutions Implemented:**
1. **Enhanced Sound Event Detection:**
   - Added `lastKnownVolume` dictionary to track source volumes
   - Improved detection of new sound events using `wasPlayingLastFrame` state
   - Better handling of timer expiration and sound restart scenarios

2. **More Robust Delay Logic:**
   ```csharp
   bool wasPlayingLastFrame = soundPlayed.ContainsKey(src) && soundPlayed[src];
   bool isCurrentlyPlaying = src.isPlaying;
   bool isNewSound = !wasPlayingLastFrame && isCurrentlyPlaying;
   ```

3. **Better Volume Management:**
   - Store original volume before applying delay
   - Smoothly transition to/from muted state
   - Preserve desired volume for when delay expires

**Testing Recommendations:**
- Set `Sound Speed` to 50 m/s for dramatic effect
- Set `Light Speed Threshold` to 5m (sounds closer than this play instantly)
- Enable debug mode "distances" to see delay calculations in logs
- Test with explosions at 20-50m distance for best results

### 4. **Tinnitus Not Triggering** 🔧
**Problem:** Tinnitus effect wasn't activating even with loud explosions or sustained gunfire.

**Root Causes:**
1. Tinnitus audio file loading might fail silently
2. Sensitivity thresholds too high for typical game audio levels
3. Accumulation rate for sustained noise too slow

**Solutions Implemented:**
1. **Improved Audio File Loading:**
   - Better error logging when tinnitus.mp3 not found
   - Automatic fallback to synthesized 4kHz tone if file missing
   - Clear log messages showing file path being checked

2. **Enhanced Trigger Sensitivity:**
   - Increased RMS accumulation rate from 1.0 to 2.5
   - Reduced decay during sustained fire (0.05 per second)
   - Lower threshold multiplier (1.5x sensitivity instead of higher)

3. **Better Volume Control:**
   - Amplified tinnitus volume by 2x for better audibility
   - Smoother decay using lerp instead of linear subtraction
   - Proper start/stop logging for debugging

**Configuration Tips:**
- Default sensitivity: 0.15 (lower = easier to trigger)
- For instant explosions: peak must exceed sensitivity value
- For sustained fire: accumulated RMS must exceed sensitivity × 1.5
- Place `tinnitus.mp3` in BepInEx/plugins folder, or use fallback tone

### 5. **Environmental Effects Not Visible** 🔧
**Problem:** Users enabled environmental effects but noticed no changes.

**Root Causes:**
1. No periodic update loop for environmental variables
2. Wind and temperature not being applied to sound propagation
3. No debug output to verify effects are active

**Solutions Implemented:**
1. **New UpdateEnvironmentalEffects() Method:**
   - Updates every 0.5 seconds when enabled
   - Calculates wind velocity based on config settings
   - Tracks current temperature from config

2. **Wind Simulation:**
   ```csharp
   float windSpeed = cfg_windEffect.Value * 10f;
   float windAngle = Time.time * 0.1f; // Slowly changing direction
   windVelocity = new Vector3(
       Mathf.Cos(windAngle) * windSpeed,
       0f,
       Mathf.Sin(windAngle) * windSpeed
   );
   ```

3. **Debug Mode Enhancement:**
   - Added "environment" debug mode option
   - Logs temperature and wind speed when active
   - Use debug mode "all" to see everything

**Configuration Tips:**
- Set `Wind Effect Strength` to 0.3-0.5 for noticeable effects
- Enable debug mode "environment" to verify it's working
- Temperature affects air absorption (hotter = less absorption)

### 6. **Wall Occlusion/Muffling Not Working** 🔧
**Problem:** Grenades exploding behind walls were heard clearly without muffling.

**Root Causes:**
1. Wall occlusion factors not being calculated properly
2. Raycast layer mask not configured correctly
3. Multi-ray verification system too lenient

**Solutions Implemented:**
1. **Enhanced Wall Detection:**
   - Primary ray: direct line to sound source
   - Secondary check: 3 offset rays for thin wall detection
   - Offset distance: 0.5m to catch narrow obstacles

2. **Better Layer Mask Handling:**
   - Default: -1 (check all layers)
   - Configurable via `Occlusion Layer Mask` setting
   - Uses `QueryTriggerInteraction.Ignore` to avoid triggers

3. **Improved Muffling Logic:**
   ```csharp
   // Skip very close sounds (no point muffling)
   if (srcDistance < 2f) {
       wallOcclusionFactors[src] = 1.0f;
       continue;
   }
   
   // Apply configured muffle amount when blocked
   wallOcclusionFactors[src] = cfg_wallMuffleAmount.Value; // Default: 0.15
   ```

4. **Comprehensive Source Tracking:**
   - Check both tracked sources AND all playing AudioSource components
   - Uses HashSet to avoid duplicates
   - Updates every 0.03 seconds for responsiveness

**Configuration Tips:**
- Set `Wall Muffle Amount` to 0.1-0.2 for strong muffling
- Increase `Wall Rays` to 48-64 for better coverage
- Set `Wall Ray Distance` to 30-50m depending on map size
- Use debug mode "walls" to see occlusion detections in logs

## Configuration Recommendations

### For Dramatic Sound Delay Effect:
```ini
[SoundDelay]
Enable Sound Delay = true
Sound Speed = 50 ; Very slow for dramatic effect
Max Delay = 5.0
Light Speed Threshold = 5
Sound Delay Min Distance = 10
```

### For Realistic Wall Muffling:
```ini
[WallOcclusion]
Enable Wall Occlusion = true
Wall Rays = 48
Wall Ray Distance = 40
Wall Muffle Amount = 0.15 ; 85% volume reduction
Occlusion Layer Mask = -1 ; Check all layers
```

### For Sensitive Tinnitus:
```ini
[Tinnitus]
Enable Tinnitus = true
Sensitivity = 0.1 ; Lower = easier to trigger
Base Duration = 8
Ring Volume = 0.6
Decay Rate = 0.92 ; Higher = slower fade
```

### For Noticeable Environmental Effects:
```ini
[Environment]
Enable Weather Effects = true
Wind Effect Strength = 0.4
Ground Reflection = 1.15
Environmental Strength = 0.9

[AirAbsorption]
Enable Air Absorption = true
Air Absorption Rate = 0.05
Humidity Factor = 0.5
Temperature = 20
```

## Debug Modes

Set `Debug Mode` to one of:
- `none` - No debug output (default)
- `distances` - Show sound delay calculations
- `walls` - Show wall occlusion detections
- `environment` - Show weather/wind data
- `all` - Show everything

## Testing Checklist

1. **Sound Delay Test:**
   - [ ] Set sound speed to 50 m/s
   - [ ] Stand 30m away from explosion
   - [ ] Should see explosion ~0.6s before hearing it
   - [ ] Check logs for "SOUND DELAY:" messages

2. **Wall Occlusion Test:**
   - [ ] Enable debug mode "walls"
   - [ ] Throw grenade behind thick wall
   - [ ] Should see "WALL OCCLUSION:" in logs
   - [ ] Explosion should be significantly muffled (~15% volume)

3. **Tinnitus Test:**
   - [ ] Fire automatic weapon in enclosed space
   - [ ] Or stand near large explosion
   - [ ] Should hear high-pitched ringing
   - [ ] Check logs for "TINNITUS TRIGGERED" message
   - [ ] Verify tinnitus.mp3 exists or fallback tone plays

4. **Environmental Effects Test:**
   - [ ] Enable debug mode "environment"
   - [ ] Check logs for "Environment: Temp=... Wind=..."
   - [ ] Listen for subtle wind modulation on distant sounds

## Known Limitations

1. **Sound Delay:** Works best for distinct sound events (explosions, gunshots). Continuous sounds may have less noticeable delay.

2. **Wall Occlusion:** Requires proper collider setup on walls. Trigger colliders are ignored by default.

3. **Tinnitus:** External audio file must be in correct format (mp3, wav, ogg). Fallback tone always available.

4. **Performance:** Higher ray counts improve accuracy but increase CPU usage. Recommended: 32-48 rays for most systems.

## Version History

- **v5.0.1** - Fixed compilation errors, enhanced all major features
- **v5.0.0** - Initial robust implementation
- **v4.0.0** - Added distance calculator and wall occlusion

## Support

For issues or questions:
1. Enable debug mode "all"
2. Check BepInEx log file for error messages
3. Verify config file settings match recommendations above
4. Ensure all dependencies (BepInEx, Unity libraries) are up to date
