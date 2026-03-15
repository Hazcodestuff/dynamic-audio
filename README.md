# Dynamic Audio V3.1.1

**Dynamic Audio V3.1.1** is a complete audio overhaul mod that brings Ravenfield's soundscape to life with unprecedented realism and immersion. This isn't just a sound pack; it's a dynamic audio simulation engine that analyzes your environment in real-time to create an authentic audio experience that reacts to every wall, open field, and obstacle around you.

## 🎯 Key Features

### 🏛️ Dynamic Environmental Reverb
Hear the difference between fighting in a tight concrete bunker, a dense forest, or an open plain. The mod constantly "probes" your surroundings to apply realistic echo, decay, and reverb based on the space you're in.

### 🔇 Realistic Sound Occlusion
Sound is realistically muffled and blocked by terrain and objects. An enemy firing from behind a thick wall will sound muffled, giving you crucial audio cues about their location.

### ⚡ Light vs Sound Delay Simulation
**NEW IN V3!** Experience realistic physics where light travels faster than sound. When a distant explosion occurs, you'll see the flash first, then hear the boom after a realistic delay based on the distance. Configure the sound speed and maximum delay to your liking!

### 🌬️ Air Absorption
**NEW IN V3!** High frequencies are naturally absorbed as sound travels through air. This effect is influenced by humidity and temperature, making distant sounds more muffled and realistic.

### 🎭 Enhanced Doppler & Flyby Effects
Projectiles and fast-moving vehicles create realistic pitch shifts as they pass by you. The enhanced flyby system adds volume and pitch boosts for dramatic close calls.

### 🧠 Immersive Hearing System
Loud sounds have consequences! Sustained gunfire and nearby explosions build up "noise exposure," causing temporary muffling. Massive explosions can cause a "shell-shock" effect, briefly deafening you and applying a low-pass filter.

### 🔔 Optional Tinnitus Effect (DISABLED BY DEFAULT)
**NEW IN V3.1!** For ultimate realism, enable the optional tinnitus feature. After extremely loud explosions, experience a realistic high-pitched ringing that gradually fades over time. **DISABLED BY DEFAULT** as many players find it annoying - but hardcore realism enthusiasts can enable it in the config! Fully customizable: adjust trigger threshold, duration, volume, frequency, and decay rate.

### 🌤️ Environmental Effects (Optional)
Enable weather-based audio effects including wind influence on sound propagation and ground reflection boosts.

### ⚙️ Fully Configurable
All parameters are customizable via the `DynamicAudio.cfg` configuration file. Adjust reverb, occlusion, sound delay, air absorption, tinnitus settings, and more to match your preferences!

## 📦 Installation

1. Download the latest release DLL from the 'Releases' tab
2. Place the `.dll` file into your `BepInEx/plugins` folder
3. Launch Ravenfield - the config file will be automatically generated at `BepInEx/config/DynamicAudio.cfg`
4. Edit the config file to customize settings to your liking!

📺 Full installation tutorial: https://youtu.be/Bu-uv_aegKs

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
- **Tinnitus**: Enable/disable (OFF by default), trigger threshold, duration, volume, frequency, decay rate
- **Environment**: Wind effects, ground reflections

## 🛠️ Open Source!

This mod is fully open source! Download the `EchoProbe.cs` file and modify it to your heart's content. Want to add new features or tweak existing ones? Go ahead! The code is well-commented and C# 7.3 compatible.

Feel free to experiment and share your improvements with the community!

## 📝 Version History

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
- Dynamic environmental reverb
- Per-source occlusion
- Doppler and flyby effects
- Noise exposure system
- Shell shock effects

## 💬 Credits

Original concept and development by zim  
Enhanced and updated by the Ravenfield modding community

Enjoy the immersive soundscape! 🎧
