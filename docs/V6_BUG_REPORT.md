# Dynamic Audio V6 (internal 1.4.1) - bug analysis

Method: the V6 `DynamicAudio.dll` was disassembled (metadata + IL) and compared with the user reports and with
Ravenfield's own audio code (`Assembly-CSharp/*.txt` in this repo). The V6 source was never committed (`EchoProbe.cs` /
`TheDynamicAudioRefresh.cs` are older, different code bases), so the 1.5.0 source in `src/` is rebuilt from that analysis.

## Reported symptoms -> root causes

### 1. "Master switch keeps turning off" / "couldn't open the editor"
* `DynamicAudioPlugin.Update` wraps everything in one `try/catch`, increments `exceptionCount` on every exception,
  **never resets it**, and at `> 200` does `cfgEnabled.Value = false`. BepInEx writes that to the `.cfg`, so the mod
  stays off in the next game too. At 60 fps a repeating error reaches 200 in about 3 seconds.
* One failing sound aborted the whole frame for *all* sounds, so errors repeated every frame.
* `SettingsUi.Draw` errors closed the window immediately (looks like "cannot open the editor"). The window also never
  unlocked the mouse cursor, so while Ravenfield had the cursor locked the menu could not be clicked.
* Turning the master switch off then on never re-enabled reverb: `Shutdown()` disabled the filter but `FindListener`
  returned early because the listener was still valid.

### 2. "Sounds get overloaded and almost everything goes silent"
* **Sound delay filter** (`SoundDelayFilter`, on by default): delays sources that are *already playing*. A voice ends
  when its clip ends, so the first `delay` seconds were silenced and the delayed tail never played. Anything shorter than
  its delay (distant gunshots: 0.2-1.5 s) disappears. It also allocated `1.5 s * 48 kHz * 6 slots * channels` floats
  (3.5 MB or more) per filter **on the audio thread**, zero-filled ~400k floats on every `HardSync`, and was destroyed and
  recreated whenever a vehicle crossed the 20 m threshold. `HardSync` fires on every voice-count change, silencing the
  source for the whole delay. The delay glide changed 10 % per block (pitch warble).
* **Volume hack**: `ApplyGain`/`Restore` did `volume = volume / lastGain * newGain` on `AudioSource.volume`. Ravenfield
  writes absolute volumes (`SoundBank.SetVolume`: `volume = originalVolume * x`) and ramps incrementally
  (`SoundBank.MoveVolume`), so the two fought: attenuation lost, sudden full-volume blasts when the ratio flipped, and
  stuck-quiet pooled sources.
* Tracking thrash: eviction compared fresh candidate distances with stale tracked distances every frame, so sounds swapped
  in and out when more than 16 were audible, each swap rebuilding filters.
* New sources were only discovered every 2 s, so most gunshots and explosions were never processed.

### 3. "Audio cuts out / muffles at a random point in a vehicle, returns after a while"
* Listener handling: `FindListener` only checked `activeInHierarchy`, used `FindObjectOfType` (may return a *disabled*
  listener) and retried once per second. While there was no listener the whole `Update` returned early, leaving every
  tracked sound muffled / attenuated until the next listener was found (camera switches when entering or leaving vehicles).
* The shockwave coroutine dereferenced `shockLowPass`, which `FindListener` nulls on a camera change -> NullReference,
  leaving a 500 Hz low-pass on the old listener.
* Volume hack above (engines are driven by `SoundBank.SetVolume/MoveVolume` every frame).
* Tinnitus created a second `AudioLowPassFilter` on the listener that the shockwave code could also pick up
  (`GetComponent`), so the two effects switched each other off or left it stuck.

## Other defects fixed
* `AudioEchoFilter.delay` is in **milliseconds**; V6 passed 0.05-0.45, i.e. a 10 ms comb filter (metallic ringing)
  instead of an echo.
* Occlusion rays were *fanned out by angle* and cast to the full source distance, so the outer rays ended in the ground or
  sky; the lower ray hit terrain for every ground-level emitter. `MaterialHfLoss` defaulted to 0 when only outer rays hit.
  Rays stopped at the first ignorable collider. Now every ray targets a point around the emitter, `RaycastNonAlloc`
  looks past ignored colliders, and the layer mask defaults to the one Ravenfield uses for its own sound line-of-sight test
  (`8392705`).
* `RoomSystem` used `RaycastNonAlloc` with 4 slots (arbitrary, not nearest, hits) -> jittery room estimates.
* Config: every slider drag rewrote the `.cfg` (`SaveOnConfigSet`); no value ranges (a hand-edited `MaxSoundDistance=0`
  produced NaN filters). Tinnitus tone looped with a click (modulation not periodic in 2 s).

## Migration
`[Internal] ConfigVersion` < 2 re-enables the master switch (V6 may have saved `Enabled=false`) and resets the old
slow performance defaults.
