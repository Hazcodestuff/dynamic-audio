# Dynamic Audio Plugin - Fixes Summary v5.3.0

## Version 5.3.0 - Performance & Realism Update

### Key Changes:

#### 1. **Tinnitus Disabled by Default**
   - Changed default value from `true` to `false` in config
   - Tinnitus now only triggers in loud, reverberant environments (high reverb + high enclosure)
   - Fixed issue where tinnitus would trigger in helicopters or quiet areas
   - Added environment severity check that requires BOTH high reverb AND high enclosure

#### 2. **Fixed Distant Audio Muffling**
   - Air absorption now only applies at VERY long distances (>50m)
   - Sounds at medium distances (10-50m) remain clear and bright as they should be
   - Minimum cutoff frequency raised from 4kHz to 8kHz for more realistic high-frequency preservation
   - Realistic behavior: distant sounds should be LOUD and ECHOEY, not muffled

#### 3. **Performance Optimizations for Many Bots**
   - **CheckSourcesOcclusionAndDoppler**: Now limits checks per frame based on source count (prevents lag with many bots)
   - **UpdateAudioCueDistances**: Reduced max tracked cues when there are >50 sources
   - **DetectWallOcclusions**: 
     - Reduces ray count by half when there are many sources
     - Only checks closest 16 sources for wall occlusion
     - Removed expensive secondary ray checks (was using 4 rays per source, now uses 1)
     - Skips very far sources for wall occlusion

#### 4. **Code Quality Improvements**
   - Better comments explaining performance optimizations
   - Cleaner logic flow in wall detection
   - More efficient sorting and early-exit patterns

### Configuration Changes:
```
Tinnitus.Enable Tinnitus = false (was true)
```

### Technical Details:

#### Before (v5.2.0):
- Air absorption applied to all distances equally
- Wall occlusion checked ALL audio sources with 4 rays each
- Source occlusion checked up to cfg_maxSourcesPerCheck every frame
- Tinnitus accumulated noise regardless of environment

#### After (v5.3.0):
- Air absorption only kicks in after 50m distance
- Wall occlusion checks max 16 closest sources with 1 ray each
- Source occlusion dynamically limits checks based on total source count
- Tinnitus only accumulates in environments with severity > 0.5 (loud + enclosed)

### Expected Results:
- **Significantly better FPS** when playing with many bots (50+)
- **Clearer audio** for distant gunshots and explosions
- **No more random tinnitus** in helicopters or open areas
- **More realistic sound propagation** - echoes and reverb instead of muffling
