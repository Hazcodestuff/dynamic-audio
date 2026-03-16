# Dynamic Audio V5

**Dynamic Audio V5** is a complete audio overhaul mod that brings Ravenfield's soundscape to life with unprecedented realism and immersion. This isn't just a sound pack; it's a dynamic audio simulation engine that analyzes your environment in real-time to create an authentic audio experience that reacts to every wall, open field, and obstacle around you.

## 🎯 Key Features

### 🏛️ Dynamic Environmental Reverb
Hear the difference between fighting in a tight concrete bunker, a dense forest, or an open plain. The mod constantly "probes" your surroundings to apply realistic echo, decay, and reverb based on the space you're in.

### 🔇 Realistic Sound Occlusion
Sound is realistically muffled and blocked by terrain and objects. An enemy firing from behind a thick wall will sound muffled, giving you crucial audio cues about their location.

### ⚡ Light vs Sound Delay Simulation
**NEW IN V3!** Experience realistic physics where light travels faster than sound. When a distant explosion occurs, you'll see the flash first, then hear the boom after a realistic delay based on the distance. Configure the sound speed and maximum delay to your liking!

### 🌬️ Air Absorption (IMPROVED IN V5)
**NEW IN V3!** High frequencies are naturally absorbed as sound travels through air. This effect is influenced by humidity and temperature, making distant sounds more muffled and realistic.
**V5 IMPROVEMENT**: Air absorption now only affects sounds beyond 50m, keeping medium-distance sounds (10-50m) clear and bright for more realistic audio!

### 🎭 Enhanced Doppler & Flyby Effects
Projectiles and fast-moving vehicles create realistic pitch shifts as they pass by you. The enhanced flyby system adds volume and pitch boosts for dramatic close calls.

### 🧠 Immersive Hearing System
Loud sounds have consequences! Sustained gunfire and nearby explosions build up "noise exposure," causing temporary muffling. Massive explosions can cause a "shell-shock" effect, briefly deafening you and applying a low-pass filter.

### 🔔 Tinnitus Effect with Custom Audio Support (DISABLED BY DEFAULT)
**NEW IN V3.1, IMPROVED IN V3.2, REVOLUTIONIZED IN V3.3, SMARTER IN V5!** Experience realistic hearing damage with customizable tinnitus sounds:
- **External Audio File Support**: Place your own `tinnitus.mp3` (or wav/ogg) file in the plugins folder for a custom ringing sound
- **Automatic Fallback**: If no audio file is found, generates a realistic high-pitched tone automatically
- **Smart Trigger System (V5)**: Now only triggers in LOUD, REVERBERANT environments (requires BOTH high reverb AND high enclosure)
  - No more random tinnitus in helicopters or open areas!
  - Triggers from extremely loud explosions in enclosed spaces
  - Sustained gunfire in tight spaces can still build up to trigger it
- **Fully Configurable**: Adjust trigger sensitivity, duration, volume, decay rate, and audio file path

**DISABLED BY DEFAULT** in V5+ - enable it in the config if you want the effect!

### 🌤️ Environmental Effects (Optional)
Enable weather-based audio effects including wind influence on sound propagation and ground reflection boosts.

### ⚙️ Fully Configurable
All parameters are customizable via the `DynamicAudio.cfg` configuration file. Adjust reverb, occlusion, sound delay, air absorption, tinnitus settings, and more to match your preferences!

**V5 Performance Features**:
- Dynamic scaling automatically reduces computational load during intense battles with many bots
- Configurable limits for ray count, source tracking, and wall detection
- Optimized defaults provide best balance between quality and performance

## 📦 Installation

1. Download the latest release DLL from the 'Releases' tab
2. Place the `.dll` file into your `BepInEx/plugins` folder
3. **(Optional)** Place a custom `tinnitus.mp3` file in the same `BepInEx/plugins` folder for a personalized tinnitus sound
4. Launch Ravenfield - the config file will be automatically generated at `BepInEx/config/DynamicAudio.cfg`
5. Edit the config file to customize settings to your liking!

📺 Full installation tutorial: https://youtu.be/JWvBX3oYIs8

## ⚙️ Configuration

After first launch, edit `BepInEx/config/DynamicAudio.cfg` to customize:

- **General**: Probe interval, ray count, max distances
- **Reverb**: Decay time, room size, reflection delays
- **Occlusion**: Max distance, volume reduction when blocked
- **Sound Delay**: Enable/disable light-vs-sound simulation, sound speed, max delay
- **Air Absorption**: Frequency absorption rates, humidity, temperature
- **Doppler & Flyby**: Strength, pitch/volume boosts, decay times
- **Exposure & Shock**: Noise buildup rates, explosion thresholds, recovery times
  - **⚠️ IMPORTANT**: Exposure Gain default changed from 22 to **1.0** in V3.1.1 to prevent over-muffling of vanilla weapons!
- **Tinnitus**: Enable/disable (OFF by default in V5), smart trigger system, sensitivity, duration, volume, decay rate, **custom audio file path**
- **Environment**: Wind effects, ground reflections

## 🛠️ Open Source!

This mod is fully open source! Download the `EchoProbe.cs` file and modify it to your heart's content. Want to add new features or tweak existing ones? Go ahead! The code is well-commented and C# 7.3 compatible.

Feel free to experiment and share your improvements with the community!

## 📝 Version History

### V5
- ✅ **PERFORMANCE OPTIMIZATIONS** - Major improvements for battles with many bots!
  - Dynamic source occlusion scaling: checks fewer sources per frame when >50 sources present
  - Distance calculator reduces tracking limit under heavy load
  - Wall detection uses 75% fewer rays (1 ray instead of 4 per source)
  - Only checks closest 16 sources for wall detection instead of ALL sources
  - Halved ray count when many sources are present
- ✅ **Tinnitus Fixed & Improved**:
  - **DISABLED BY DEFAULT** - no more annoying random ringing!
  - **Smart Trigger System**: Now requires BOTH high reverb AND high enclosure to trigger
  - Fixed helicopter issue - tinnitus no longer triggers randomly while flying
  - Only activates in genuinely loud, reverberant environments
- ✅ **Distant Audio No Longer Muffled**:
  - Air absorption now only affects sounds beyond 50m distance
  - Medium-distance sounds (10-50m) stay clear and bright
  - Minimum cutoff raised from 4kHz to 8kHz for better clarity
  - Realistic behavior: distant gunshots/explosions are LOUD and ECHOEY, not muffled
- ✅ **More Realistic Sound Propagation**: Echoes instead of muffling for distant sounds

### V3.3.0
- ✅ **Tinnitus Now Uses External Audio Files!** - Simply place `tinnitus.mp3` in the plugins folder
- ✅ **Automatic Fallback**: Generates a realistic synthesized tone if no audio file is found
- ✅ **New Config Option**: `Tinnitus/Audio File` - specify any mp3/wav/ogg file to use
- ✅ **Improved Volume Control**: Tinnitus volume now properly scales with config settings
- ✅ **Better Logging**: Clear messages showing when audio file is loaded or fallback is created
- ✅ **Fixed**: Removed dependency on non-existent `cfg_tinnitusFrequency` config

### V3.2.0
- ✅ **FIXED Tinnitus Trigger System** - COMPLETELY REWRITTEN to actually work!
  - **Problem Fixed**: Old thresholds were mathematically impossible to reach (0.92+ peak values)
  - **New Sensitivity System**: Uses intuitive 0.1-1.0 sensitivity scale (default 0.5)
  - **Instant Trigger**: Explosions with peak amplitude >= sensitivity value
  - **Sustained Trigger**: Automatic fire accumulates noise; triggers at sensitivity * 3 (default 1.5 RMS units)
  - **Slower Decay**: Accumulated noise decays at 0.15/s instead of 0.3/s for easier buildup
  - **Dynamic Duration**: Longer exposure = longer tinnitus duration (up to 2x base duration)
  - **Better Logging**: Clear messages showing accumulated values and thresholds
- ✅ Changed config name from `Trigger Threshold` to `Sensitivity` for clarity
- ✅ Reduced default tinnitus volume from 0.3 to 0.15 for more subtle effect
- ✅ Improved decay rate from 0.8 to 0.92 for more gradual fade
- ✅ Now properly triggers from sustained automatic weapon fire in enclosed spaces!

### V3.1.1
- ✅ **FIXED**: Exposure Gain default reduced from 22 to **1.0** to prevent excessive muffling of vanilla weapons
- ✅ Updated config description to warn users about the Exposure Gain setting
- ✅ Vanilla weapons now sound natural without being overly muffled
- ✅ Users who want stronger muffling can still increase Exposure Gain manually in config

### V3.1.0
- ✅ **Optional Tinnitus Effect** - DISABLED BY DEFAULT for player comfort
- ✅ Customizable tinnitus settings: trigger threshold, duration, volume, frequency, decay rate
- ✅ Tinnitus integrates with shock system for realistic hearing damage
- ✅ All tinnitus parameters fully configurable

### V3.0.0
- ✅ Renamed to "Dynamic Audio"
- ✅ Custom config file (`DynamicAudio.cfg`)
- ✅ Light vs Sound delay simulation (see explosions before hearing them!)
- ✅ Air absorption based on distance, humidity, and temperature
- ✅ Removed annoying tinnitus effect (now optional in V3.1!)
- ✅ Enhanced logging and diagnostics
- ✅ All settings fully configurable

### V2.0.0
- ✅ Dynamic environmental reverb
- ✅ Per-source occlusion
- ✅ Doppler and flyby effects
- ✅ Noise exposure system
- ✅ Shell shock effects

## 💬 Credits

Original concept and development by me  
Enhanced and updated by the Qwen AI!

Enjoy the immersive soundscape! 🎧
