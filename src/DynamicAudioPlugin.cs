using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DynamicAudio
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class DynamicAudioPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "lexge.dynamicaudio";
        public const string PluginName = "Dynamic Audio";
        public const string PluginVersion = "1.5.0";

        private const int CurrentConfigVersion = 3;
        private const float PollInterval = 0.05f;
        private const float MinTrackDistance = 2.5f;
        private const float OffCutoff = 16500f;
        private const float EvictHysteresis = 200f;
        private const float EvictMinHold = 1.5f;

        public static DynamicAudioPlugin Instance { get; private set; }

        // ---- config (public: used by SettingsUi / TravelDelay)
        public ConfigEntry<bool> cfgEnabled;
        public ConfigEntry<float> cfgMaxSoundDistance;
        public ConfigEntry<KeyboardShortcut> cfgMenuKey;
        public ConfigEntry<bool> cfgOcclusionEnabled;
        public ConfigEntry<int> cfgRayCount;
        public ConfigEntry<float> cfgRaySpread;
        public ConfigEntry<bool> cfgUseGameMask;
        public ConfigEntry<float> cfgWallMuffleStrength;
        public ConfigEntry<float> cfgMinCutoffHz;
        public ConfigEntry<float> cfgOcclusionVolumeFloor;
        public ConfigEntry<bool> cfgReverbEnabled;
        public ConfigEntry<float> cfgReverbIntensity;
        public ConfigEntry<float> cfgRoomSizeMultiplier;
        public ConfigEntry<bool> cfgEchoEnabled;
        public ConfigEntry<bool> cfgShockwaveEnabled;
        public ConfigEntry<float> cfgShockwaveRadius;
        public ConfigEntry<float> cfgShockwaveCooldown;
        public ConfigEntry<bool> cfgTinnitusEnabled;
        public ConfigEntry<float> cfgTinnitusExplosionRadius;
        public ConfigEntry<float> cfgTinnitusSensitivity;
        public ConfigEntry<float> cfgTinnitusRingVolume;
        public ConfigEntry<float> cfgTinnitusMuffle;
        public ConfigEntry<float> cfgTinnitusCooldown;
        public ConfigEntry<bool> cfgPauseMenuButton;
        public ConfigEntry<float> cfgAirAbsorption;
        public ConfigEntry<bool> cfgSoundDelayEnabled;
        public ConfigEntry<float> cfgSoundSpeed;
        public ConfigEntry<float> cfgSoundDelayMinDistance;
        public ConfigEntry<float> cfgSoundDelayMax;
        public ConfigEntry<bool> cfgProcessGunshots;
        public ConfigEntry<bool> cfgProcessExplosions;
        public ConfigEntry<bool> cfgProcessImpacts;
        public ConfigEntry<bool> cfgProcessVehicles;
        public ConfigEntry<bool> cfgProcessFootsteps;
        public ConfigEntry<bool> cfgProcessEnvironment;
        public ConfigEntry<float> cfgScanInterval;
        public ConfigEntry<float> cfgNearUpdateInterval;
        public ConfigEntry<float> cfgMidUpdateInterval;
        public ConfigEntry<int> cfgMaxSimulatedSounds;
        public ConfigEntry<float> cfgNearDistance;
        public ConfigEntry<float> cfgMidDistance;
        public ConfigEntry<bool> cfgDebug;
        private ConfigEntry<int> cfgConfigVersion;

        // ---- systems
        private readonly OcclusionSystem occlusion = new OcclusionSystem();
        private readonly ListenerAudioSystem listenerAudio = new ListenerAudioSystem();
        private readonly TinnitusSystem tinnitus = new TinnitusSystem();
        private RoomSystem roomSystem;
        private SettingsUi settingsUi;
        private GameMenuIntegration gameMenu;
        private UnityEngine.EventSystems.EventSystem blockedEventSystem;

        // ---- source tracking
        private sealed class SrcState
        {
            public bool Playing;
            public bool FirstSeen = true;
            public bool Classified;
            public SoundCategory Category;
            public float QuarantineUntil;
            public bool OwnChecked;
            public bool MixerOwn;
            public bool WeaponLooked;
            public Weapon Weapon;
        }

        private struct PendingDisable
        {
            public AcousticGainFilter Target;
            public float At;
        }

        private readonly List<TrackedSound> tracked = new List<TrackedSound>(32);
        private readonly Dictionary<AudioSource, TrackedSound> trackedMap = new Dictionary<AudioSource, TrackedSound>(32);
        private readonly Dictionary<AudioSource, SrcState> states = new Dictionary<AudioSource, SrcState>(256);
        private readonly List<AudioSource> allSources = new List<AudioSource>(256);
        private readonly List<AudioSource> deadKeys = new List<AudioSource>();
        private readonly List<PendingDisable> pendingDisable = new List<PendingDisable>();
        private readonly HashSet<int> ownedLowPass = new HashSet<int>();
        private readonly List<AudioLowPassFilter> lowPassScratch = new List<AudioLowPassFilter>(4);

        private float nextScan;
        private float nextPoll;
        private float lastPollTime;
        private float nextRoomProbe;
        private float nextLoudSample;
        private float nextShockTime;
        private bool wasEnabled;
        private bool lastUseGameMask;

        // ---- failure handling (never touches the config)
        private float failWindowStart;
        private int failCount;
        private float safeModeUntil;

        // ---- config autosave / cursor
        private bool configDirty;
        private float configDirtyAt;
        private bool cursorOverridden;
        private CursorLockMode prevLock;
        private bool prevCursorVisible;
        private bool menuWasVisible;

        private GUIStyle debugStyle;

        // =====================================================================================
        // Public helpers
        // =====================================================================================
        public Transform Ear { get { return listenerAudio.Ear; } }

        public bool TravelDelayActive
        {
            get { return cfgEnabled != null && cfgEnabled.Value && cfgSoundDelayEnabled.Value && TravelDelay.Installed && Time.time >= safeModeUntil; }
        }

        public void LogWarn(string msg)
        {
            Logger.LogWarning(msg);
        }

        public SoundCategory ClassifyCached(AudioSource src)
        {
            SrcState st;
            if (!states.TryGetValue(src, out st))
            {
                st = new SrcState();
                states[src] = st;
            }
            if (!st.Classified)
            {
                st.Category = SoundClassifier.Classify(src);
                st.Classified = true;
            }
            return st.Category;
        }

        private SrcState GetState(AudioSource src)
        {
            SrcState st;
            if (!states.TryGetValue(src, out st))
            {
                st = new SrcState();
                states[src] = st;
            }
            return st;
        }

        private static bool MixerIsOwn(AudioSource s)
        {
            var g = s.outputAudioMixerGroup;
            if (g == null) return false;
            string n = g.name;
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            return n.Contains("firstperson") || n.Contains("playervehicle");
        }

        /// <summary>True for sounds that belong to the player (first-person weapon, own vehicle) or that sit on the listener.</summary>
        private bool IsOwnSource(AudioSource s, SrcState st, Transform ear, float dist, bool recheck)
        {
            if (!st.OwnChecked || recheck)
            {
                st.MixerOwn = MixerIsOwn(s);
                st.OwnChecked = true;
            }
            if (st.MixerOwn) return true;
            return ear != null && dist < 3f && s.transform.root == ear.root;
        }

        private bool IsReloading(AudioSource s, SrcState st)
        {
            if (!st.WeaponLooked)
            {
                st.Weapon = s.GetComponentInParent<Weapon>();
                st.WeaponLooked = true;
            }
            return st.Weapon != null && st.Weapon.reloading;
        }

        /// <summary>Used by TravelDelay: the player's own sounds and quiet reload sounds are never delayed.</summary>
        public bool IsDelayExempt(AudioSource src)
        {
            if (src == null) return true;
            SrcState st = GetState(src);
            Transform ear = listenerAudio.Ear;
            float d = ear != null ? Vector3.Distance(ear.position, src.transform.position) : 999f;
            if (IsOwnSource(src, st, ear, d, false)) return true;
            return IsReloading(src, st);
        }

        public bool CategoryEnabled(SoundCategory c)
        {
            switch (c)
            {
                case SoundCategory.Gunshot: return cfgProcessGunshots.Value;
                case SoundCategory.Explosion: return cfgProcessExplosions.Value;
                case SoundCategory.Impact: return cfgProcessImpacts.Value;
                case SoundCategory.Vehicle: return cfgProcessVehicles.Value;
                case SoundCategory.Footstep: return cfgProcessFootsteps.Value;
                default: return cfgProcessEnvironment.Value;
            }
        }

        // =====================================================================================
        // Config
        // =====================================================================================
        private ConfigEntry<float> BindF(string section, string key, float def, float min, float max, string desc)
        {
            return Config.Bind<float>(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        }

        private ConfigEntry<int> BindI(string section, string key, int def, int min, int max, string desc)
        {
            return Config.Bind<int>(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<int>(min, max)));
        }

        private ConfigEntry<bool> BindB(string section, string key, bool def, string desc)
        {
            return Config.Bind<bool>(section, key, def, desc);
        }

        private void BindConfig()
        {
            cfgEnabled = BindB("General", "Enabled", true, "Master switch for the whole acoustic simulation. (Never switched off automatically any more.)");
            cfgMaxSoundDistance = BindF("General", "MaxSoundDistance", 400f, 50f, 2000f, "Sounds farther than this are not simulated at all (meters).");
            cfgMenuKey = Config.Bind<KeyboardShortcut>("General", "MenuKey", new KeyboardShortcut(KeyCode.F11), "Key that opens the in-game Dynamic Audio settings menu.");

            cfgPauseMenuButton = BindB("General", "PauseMenuButton", true, "Add a 'Dynamic Audio' button to the in-game ESC > OPTIONS menu. The menu key still works either way.");

            cfgOcclusionEnabled = BindB("Occlusion", "Enabled", true, "Enable raycast occlusion of world sounds.");
            cfgRayCount = BindI("Occlusion", "RayCount", 5, 1, 9, "Rays cast per nearby sound (1-9). More rays = better openings/partial-wall detection.");
            cfgRaySpread = BindF("Occlusion", "RaySpread", 0.25f, 0f, 1f, "How far the outer rays spread around the emitter (0 = all rays aim at the same point).");
            cfgUseGameMask = BindB("Occlusion", "UseGameLineOfSightMask", true, "Use the same physics layers Ravenfield uses for its own sound line-of-sight test. Turn off to use the older 'everything except actors' mask.");
            cfgOcclusionVolumeFloor = BindF("Occlusion", "VolumeFloor", 0.06f, 0f, 1f, "Volume multiplier at 100% occlusion before thickness is considered.");

            cfgWallMuffleStrength = BindF("Filtering", "WallMuffleStrength", 1f, 0f, 3f, "Overall strength of through-wall muffling (low-pass filtering). 1 = realistic, 0.5 = gentle.");
            cfgMinCutoffHz = BindF("Filtering", "MinCutoffHz", 250f, 100f, 5000f, "Lowest low-pass cutoff for the most heavily occluded sounds (Hz).");

            cfgReverbEnabled = BindB("Reverb", "Enabled", true, "Enable dynamic room reverb on the listener.");
            cfgReverbIntensity = BindF("Reverb", "Intensity", 1f, 0f, 1f, "Overall reverb wetness scale (0-1).");
            cfgRoomSizeMultiplier = BindF("Reverb", "RoomSizeMultiplier", 1f, 0.3f, 3f, "Scales the influence of estimated room size on decay time.");
            cfgEchoEnabled = BindB("Echo", "Enabled", true, "Enable a short echo tail after LOUD sounds (gunshots, explosions) in large or open spaces. Reloading and footsteps never trigger it.");

            cfgShockwaveEnabled = BindB("Shockwave", "Enabled", true, "Short concussion effect for nearby explosions. No tinnitus.");
            cfgShockwaveRadius = BindF("Shockwave", "Radius", 30f, 5f, 150f, "Distance within which a newly started explosion sound triggers the shockwave effect (meters).");
            cfgShockwaveCooldown = BindF("Shockwave", "Cooldown", 0.8f, 0f, 10f, "Minimum seconds between two shockwave effects.");

            cfgTinnitusEnabled = BindB("Tinnitus", "Enabled", false, "DISABLED BY DEFAULT. Ear ringing after nearby explosions or sustained loud gunfire. Turning it off restores hearing instantly.");
            cfgTinnitusExplosionRadius = BindF("Tinnitus", "ExplosionRadius", 12f, 5f, 100f, "Explosions closer than this can trigger ringing (meters).");
            cfgTinnitusSensitivity = BindF("Tinnitus", "Sensitivity", 0.5f, 0.1f, 5f, "How quickly sustained loud noise causes ringing. Lower = rarer.");
            cfgTinnitusRingVolume = BindF("Tinnitus", "RingVolume", 0.2f, 0f, 1f, "Loudness of the ringing tone (0-1).");
            cfgTinnitusMuffle = BindF("Tinnitus", "MuffleAmount", 0.4f, 0f, 1f, "How much temporary high-frequency hearing loss accompanies the ring (0-1).");
            cfgTinnitusCooldown = BindF("Tinnitus", "Cooldown", 30f, 0f, 120f, "Minimum seconds of normal hearing after one ring ends before another can start.");

            cfgAirAbsorption = BindF("Distance", "AirAbsorption", 0.5f, 0f, 1f, "Extra distance attenuation and high-frequency air loss strength (0 = off, 1 = realistic).");

            cfgSoundDelayEnabled = BindB("TravelDelay", "Enabled", false, "EXPERIMENTAL. Delay the start of distant gunshots/explosions by distance / speed of sound (uses a Harmony hook on AudioSource.Play/PlayOneShot).");
            cfgSoundSpeed = BindF("TravelDelay", "SpeedOfSound", 343f, 50f, 1000f, "Speed of sound in m/s. 343 = realistic; lower values exaggerate the effect.");
            cfgSoundDelayMinDistance = BindF("TravelDelay", "MinDistance", 20f, 5f, 200f, "Sounds closer than this get no delay.");
            cfgSoundDelayMax = BindF("TravelDelay", "MaxDelay", 1.5f, 0.1f, 3f, "Maximum sound delay in seconds.");

            cfgProcessGunshots = BindB("Categories", "Gunshots", true, "Process gunshot sounds.");
            cfgProcessExplosions = BindB("Categories", "Explosions", true, "Process explosion sounds.");
            cfgProcessImpacts = BindB("Categories", "Impacts", true, "Process impact sounds (hits, fly-bys).");
            cfgProcessVehicles = BindB("Categories", "Vehicles", true, "Process vehicle engine/weapon sounds.");
            cfgProcessFootsteps = BindB("Categories", "Footsteps", false, "Process footstep sounds (off by default: many sources, low payoff).");
            cfgProcessEnvironment = BindB("Categories", "Environment", true, "Process general environment sounds.");

            cfgScanInterval = BindF("Performance", "ScanInterval", 0.5f, 0.25f, 5f, "How often the scene is scanned for new AudioSources (seconds).");
            cfgNearUpdateInterval = BindF("Performance", "NearUpdateInterval", 0.15f, 0.03f, 1f, "Simulation update interval for nearby important sounds (seconds).");
            cfgMidUpdateInterval = BindF("Performance", "MidUpdateInterval", 0.45f, 0.1f, 2f, "Simulation update interval for medium-distance sounds (seconds).");
            cfgMaxSimulatedSounds = BindI("Performance", "MaxSimulatedSounds", 16, 2, 64, "Maximum number of sounds simulated simultaneously (lowest priority dropped first).");
            cfgNearDistance = BindF("Performance", "NearDistance", 45f, 5f, 200f, "High-quality LOD distance (meters).");
            cfgMidDistance = BindF("Performance", "MidDistance", 120f, 20f, 600f, "Reduced-quality LOD distance (meters).");

            cfgDebug = BindB("Debug", "Enabled", false, "Show on-screen overlay with listener status, room estimate, occlusion and active sound count.");
            cfgConfigVersion = Config.Bind<int>("Internal", "ConfigVersion", 0, "Do not edit. Used to migrate settings between releases.");
        }

        private void MigrateConfig()
        {
            if (cfgConfigVersion.Value >= CurrentConfigVersion) return;

            if (cfgConfigVersion.Value < 2)
            {
                // V6 switched itself off after repeated errors and SAVED that to the config. Undo it, and drop
                // the old defaults that made V6 slow to notice new sounds.
                cfgEnabled.Value = true;
                cfgScanInterval.Value = (float)cfgScanInterval.DefaultValue;
                cfgNearUpdateInterval.Value = (float)cfgNearUpdateInterval.DefaultValue;
                cfgMidUpdateInterval.Value = (float)cfgMidUpdateInterval.DefaultValue;
                cfgMaxSimulatedSounds.Value = (int)cfgMaxSimulatedSounds.DefaultValue;
                cfgRayCount.Value = (int)cfgRayCount.DefaultValue;
                cfgRaySpread.Value = (float)cfgRaySpread.DefaultValue;
            }
            if (cfgConfigVersion.Value < 3)
            {
                // 1.5.0 final: tinnitus was far too sensitive. Move everyone to the gentler defaults.
                cfgTinnitusExplosionRadius.Value = (float)cfgTinnitusExplosionRadius.DefaultValue;
                cfgTinnitusSensitivity.Value = (float)cfgTinnitusSensitivity.DefaultValue;
                cfgTinnitusRingVolume.Value = (float)cfgTinnitusRingVolume.DefaultValue;
                cfgTinnitusMuffle.Value = (float)cfgTinnitusMuffle.DefaultValue;
                cfgTinnitusCooldown.Value = (float)cfgTinnitusCooldown.DefaultValue;
            }
            cfgConfigVersion.Value = CurrentConfigVersion;
            Config.Save();
            Logger.LogInfo("Config migrated to version " + CurrentConfigVersion + " (master switch re-enabled).");
        }

        public void ResetDefaults()
        {
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Config)
            {
                if (kv.Key.Section == "Internal") continue;
                kv.Value.BoxedValue = kv.Value.DefaultValue;
            }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            configDirty = true;
            configDirtyAt = Time.unscaledTime + 1.5f;
        }

        private void SaveConfigNow()
        {
            configDirty = false;
            try { Config.Save(); }
            catch (Exception ex) { Logger.LogWarning("Could not save config: " + ex.Message); }
        }

        // =====================================================================================
        // Unity messages
        // =====================================================================================
        private void Awake()
        {
            Instance = this;
            BindConfig();
            MigrateConfig();
            Config.SaveOnConfigSet = false;      // sliders no longer rewrite the file on every mouse move
            Config.SettingChanged += OnSettingChanged;

            lastUseGameMask = cfgUseGameMask.Value;
            int mask = BuildMask();
            occlusion.Configure(cfgRayCount.Value, cfgRaySpread.Value);
            occlusion.SetMask(mask);
            roomSystem = new RoomSystem(mask);
            tinnitus.SetHost(gameObject);
            listenerAudio.Refresh(Time.time, true);
            settingsUi = new SettingsUi(this);
            gameMenu = new GameMenuIntegration(this, settingsUi);
            Logger.LogInfo("Dynamic Audio v" + PluginVersion + " initialized (occlusion mask 0x" + mask.ToString("X") + ")");
        }

        private int BuildMask()
        {
            return cfgUseGameMask.Value ? OcclusionSystem.GameLineOfSightMask : OcclusionSystem.BuildExclusionMask();
        }

        private void OnDestroy()
        {
            try
            {
                ReleaseAll();
                listenerAudio.Shutdown();
                tinnitus.Shutdown();
                TravelDelay.Uninstall();
                RestoreCursor();
                UpdateEventSystemBlock(false);
                if (gameMenu != null) gameMenu.DestroyClones();
                SaveConfigNow();
            }
            catch (Exception) { }
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            SaveConfigNow();
        }

        private void Update()
        {
            try { HandleMenuKey(); }
            catch (Exception e) { RegisterFailure(e); }

            try { if (gameMenu != null) gameMenu.Tick(Time.unscaledTime, cfgPauseMenuButton.Value); }
            catch (Exception) { }

            if (configDirty && Time.unscaledTime >= configDirtyAt) SaveConfigNow();

            if (!cfgEnabled.Value)
            {
                if (wasEnabled) Deactivate();
                return;
            }
            wasEnabled = true;

            float now = Time.time;
            if (now < safeModeUntil) return;

            try { Simulate(now); }
            catch (Exception e) { RegisterFailure(e); }
        }

        private void LateUpdate()
        {
            bool visible = settingsUi != null && settingsUi.Visible;
            if (visible)
            {
                if (!cursorOverridden)
                {
                    prevLock = Cursor.lockState;
                    prevCursorVisible = Cursor.visible;
                    cursorOverridden = true;
                }
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }
            else if (cursorOverridden)
            {
                RestoreCursor();
            }
            UpdateEventSystemBlock(visible);
            if (menuWasVisible && !visible) SaveConfigNow();
            menuWasVisible = visible;
        }

        // While our IMGUI window is open, clicks must not also hit the game's menu buttons underneath it.
        private void UpdateEventSystemBlock(bool visible)
        {
            try
            {
                if (visible)
                {
                    UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                    if (blockedEventSystem == null && es != null && es.enabled)
                    {
                        es.enabled = false;
                        blockedEventSystem = es;
                    }
                }
                else if (blockedEventSystem != null)
                {
                    blockedEventSystem.enabled = true;
                    blockedEventSystem = null;
                }
            }
            catch (Exception) { blockedEventSystem = null; }
        }

        private void RestoreCursor()
        {
            if (!cursorOverridden) return;
            Cursor.lockState = prevLock;
            Cursor.visible = prevCursorVisible;
            cursorOverridden = false;
        }

        private void HandleMenuKey()
        {
            if (settingsUi == null) return;
            if (cfgMenuKey.Value.IsDown()) settingsUi.Visible = !settingsUi.Visible;
            else if (settingsUi.Visible && !settingsUi.CapturingKey && Input.GetKeyDown(KeyCode.Escape)) settingsUi.Visible = false;
        }

        // =====================================================================================
        // Failure handling: isolate, back off, never disable permanently
        // =====================================================================================
        private void RegisterFailure(Exception e)
        {
            float now = Time.realtimeSinceStartup;
            if (now - failWindowStart > 10f)
            {
                failWindowStart = now;
                failCount = 0;
            }
            failCount++;
            if (failCount <= 3) Logger.LogWarning("update error: " + e);
            if (failCount >= 60)
            {
                Logger.LogWarning("Too many errors; pausing Dynamic Audio for 5 seconds (settings are untouched).");
                safeModeUntil = Time.time + 5f;
                failCount = 0;
                failWindowStart = now;
                try { ReleaseAll(); } catch (Exception) { }
            }
        }

        private void Deactivate()
        {
            wasEnabled = false;
            ReleaseAll();
            listenerAudio.SetActive(false);
            listenerAudio.Tick(0f);
            tinnitus.Shutdown();
            TravelDelay.Flush();
        }

        // =====================================================================================
        // Main simulation
        // =====================================================================================
        private void Simulate(float now)
        {
            float dt = Time.deltaTime;

            if (cfgUseGameMask.Value != lastUseGameMask)
            {
                lastUseGameMask = cfgUseGameMask.Value;
                int m = BuildMask();
                occlusion.SetMask(m);
                roomSystem.SetMask(m);
            }
            if (cfgSoundDelayEnabled.Value) TravelDelay.EnsureInstalled(Logger);
            TravelDelay.Tick(now);

            listenerAudio.SetActive(true);
            listenerAudio.Configure(cfgReverbIntensity.Value, cfgRoomSizeMultiplier.Value, cfgEchoEnabled.Value);
            listenerAudio.Refresh(now, false);
            tinnitus.SetParams(cfgTinnitusSensitivity.Value, cfgTinnitusRingVolume.Value, cfgTinnitusMuffle.Value, cfgTinnitusCooldown.Value);

            Transform ear = listenerAudio.Ear;
            if (ear == null)
            {
                // No active listener right now (camera switch): hand every sound back to the game untouched.
                if (tracked.Count > 0) ReleaseAll();
                tinnitus.Update(dt, cfgTinnitusEnabled.Value);
                return;
            }

            bool tinn = cfgTinnitusEnabled.Value;
            if (tinn && now >= nextLoudSample)
            {
                nextLoudSample = now + 0.1f;
                tinnitus.SampleLoudness(listenerAudio.CurrentRoom.Enclosure);
            }
            tinnitus.Update(dt, tinn);
            listenerAudio.TinnitusCutoff = tinn ? tinnitus.MuffleCutoff : 22000f;

            Vector3 earPos = ear.position;
            occlusion.Configure(cfgRayCount.Value, cfgRaySpread.Value);

            ScanSources(now);

            if (now >= nextRoomProbe)
            {
                nextRoomProbe = now + 0.6f;
                RoomEstimate room = roomSystem.Probe(earPos);
                listenerAudio.ApplyRoom(ref room, cfgReverbEnabled.Value);
            }

            if (now >= nextPoll)
            {
                float pollDt = Mathf.Clamp(now - lastPollTime, PollInterval, 0.5f);
                lastPollTime = now;
                nextPoll = now + PollInterval;
                PollSources(earPos, now, pollDt);
            }

            UpdateTracked(earPos, now, dt);
            FlushPendingDisable(now);
            listenerAudio.Tick(Time.unscaledDeltaTime);
        }

        private void ScanSources(float now)
        {
            if (now < nextScan) return;
            nextScan = now + Mathf.Max(0.25f, cfgScanInterval.Value);

            allSources.Clear();
            allSources.AddRange(UnityEngine.Object.FindObjectsOfType<AudioSource>());

            deadKeys.Clear();
            foreach (KeyValuePair<AudioSource, SrcState> kv in states)
            {
                if (kv.Key == null) deadKeys.Add(kv.Key);
            }
            for (int i = 0; i < deadKeys.Count; i++) states.Remove(deadKeys[i]);
            deadKeys.Clear();
        }

        private static float Score(SoundCategory cat, float dist)
        {
            return SoundClassifier.Priority(cat) * 10000f - Mathf.Min(dist, 9999f);
        }

        private void PollSources(Vector3 earPos, float now, float pollDt)
        {
            Transform ear = listenerAudio.Ear;
            bool tinn = cfgTinnitusEnabled.Value;
            float maxDist = cfgMaxSoundDistance.Value;

            for (int i = 0; i < allSources.Count; i++)
            {
                AudioSource s = allSources[i];
                if (s == null) continue;

                SrcState st;
                if (!states.TryGetValue(s, out st))
                {
                    st = new SrcState();
                    states[s] = st;
                }

                bool playing = s.isPlaying && s.isActiveAndEnabled;
                bool justStarted = false;
                if (playing && !st.Playing)
                    justStarted = st.FirstSeen ? (!s.loop && (s.clip == null || s.time < 0.35f)) : true;
                st.Playing = playing;
                st.FirstSeen = false;

                if (!playing) continue;
                if (now < st.QuarantineUntil) continue;
                bool flat = s.spatialBlend < 0.5f;   // first-person sounds are often 2D: still needed for echo/tinnitus

                Transform tr = s.transform;
                float d = Vector3.Distance(earPos, tr.position);
                if (d > maxDist) continue;

                if (!st.Classified)
                {
                    st.Category = SoundClassifier.Classify(s);
                    st.Classified = true;
                }
                SoundCategory cat = st.Category;

                bool loudCat = cat == SoundCategory.Gunshot || cat == SoundCategory.Explosion;
                bool reloading = cat == SoundCategory.Gunshot && IsReloading(s, st);
                bool own = IsOwnSource(s, st, ear, d, justStarted);
                if (flat && !own) continue;

                // Echo is event driven: only a freshly fired shot / blast pulses it; reloading never does.
                if (justStarted && loudCat)
                {
                    if (reloading)
                    {
                        if (own) listenerAudio.SuppressEcho(0.8f);
                    }
                    else if (own)
                    {
                        listenerAudio.PulseEcho(cat == SoundCategory.Explosion ? 1f : 0.8f);
                    }
                    else
                    {
                        float reach = cat == SoundCategory.Explosion ? 250f : 140f;
                        float k0 = 1f - d / reach;
                        if (k0 > 0f) listenerAudio.PulseEcho(Mathf.Clamp01(0.25f + 0.6f * k0));
                    }
                }

                // The listener's own gear: never occluded or filtered; only feeds the tinnitus exposure meter.
                if (own)
                {
                    TrackedSound tsOwn;
                    if (trackedMap.TryGetValue(s, out tsOwn))
                    {
                        int idx = tracked.IndexOf(tsOwn);
                        if (idx >= 0) RemoveTracked(idx, now);
                        else trackedMap.Remove(s);
                    }
                    if (tinn && loudCat && !reloading)
                    {
                        float enc = 1f + 0.8f * Mathf.Clamp01(listenerAudio.CurrentRoom.Enclosure);
                        tinnitus.AddExposure(pollDt * 0.02f * enc * cfgTinnitusSensitivity.Value);
                    }
                    continue;
                }

                if (!CategoryEnabled(cat)) continue;

                if (justStarted && cat == SoundCategory.Explosion)
                {
                    if (cfgShockwaveEnabled.Value && d < cfgShockwaveRadius.Value && now >= nextShockTime)
                    {
                        float k = 1f - d / cfgShockwaveRadius.Value;
                        listenerAudio.TriggerShockwave(k * k);
                        nextShockTime = now + cfgShockwaveCooldown.Value;
                    }
                    if (tinn && d < cfgTinnitusExplosionRadius.Value)
                    {
                        float k2 = 1f - d / cfgTinnitusExplosionRadius.Value;
                        float sev = k2 * k2;
                        // A wall between you and the blast halves the severity.
                        if (Physics.Linecast(earPos, tr.position + Vector3.up * 0.5f, OcclusionSystem.GameLineOfSightMask, QueryTriggerInteraction.Ignore)) sev *= 0.5f;
                        tinnitus.TriggerExplosion(sev);
                    }
                }

                if (d < MinTrackDistance) continue;
                if (trackedMap.ContainsKey(s)) continue;
                TryTrack(s, cat, d, now);
            }
        }

        private void TryTrack(AudioSource s, SoundCategory cat, float dist, float now)
        {
            // Filters live on the GameObject, so only one tracked source per GameObject (avoids two
            // sources writing conflicting gain/cutoff values into the same filter).
            for (int i = 0; i < tracked.Count; i++)
            {
                AudioSource o = tracked[i].Source;
                if (o != null && o.gameObject == s.gameObject) return;
            }

            if (tracked.Count >= cfgMaxSimulatedSounds.Value)
            {
                int worst = -1;
                float worstScore = float.MaxValue;
                for (int i = 0; i < tracked.Count; i++)
                {
                    TrackedSound o = tracked[i];
                    if (now - o.TrackedSince < EvictMinHold) continue;
                    float sc = Score(o.Category, o.Distance);
                    if (sc < worstScore)
                    {
                        worstScore = sc;
                        worst = i;
                    }
                }
                if (worst < 0) return;
                if (Score(cat, dist) <= worstScore + EvictHysteresis) return;
                ReleaseTracked(tracked[worst], now);
                tracked.RemoveAt(worst);
            }

            TrackedSound t = new TrackedSound();
            t.Source = s;
            t.Category = cat;
            t.Distance = dist;
            t.TrackedSince = now;
            t.LastPlayingTime = now;
            t.NextUpdateTime = 0f;
            // Colliders under this transform are ignored by the occlusion rays (the emitter's own body).
            // Stop climbing at big containers (map roots, pools) so they do not disable occlusion entirely.
            Transform top = s.transform;
            for (int i = 0; i < 3 && top.parent != null; i++)
            {
                if (top.parent.childCount > 30) break;
                top = top.parent;
            }
            t.SourceTop = top;
            tracked.Add(t);
            trackedMap[s] = t;
        }

        private void UpdateTracked(Vector3 earPos, float now, float dt)
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                TrackedSound t = tracked[i];
                try
                {
                    AudioSource s = t.Source;
                    if (s == null || !s.isActiveAndEnabled)
                    {
                        RemoveTracked(i, now);
                        continue;
                    }

                    if (s.isPlaying) t.LastPlayingTime = now;
                    else if (now - t.LastPlayingTime > 1f)
                    {
                        RemoveTracked(i, now);
                        continue;
                    }

                    if (now >= t.NextUpdateTime)
                    {
                        t.Distance = Vector3.Distance(earPos, s.transform.position);
                        if (t.Distance > cfgMaxSoundDistance.Value + 20f)
                        {
                            RemoveTracked(i, now);
                            continue;
                        }

                        float interval;
                        if (t.Distance < cfgNearDistance.Value && SoundClassifier.Priority(t.Category) >= 3)
                        {
                            interval = cfgNearUpdateInterval.Value;
                            t.RayLod = 0;
                        }
                        else if (t.Distance < cfgMidDistance.Value)
                        {
                            interval = cfgMidUpdateInterval.Value;
                            t.RayLod = 1;
                        }
                        else
                        {
                            interval = cfgMidUpdateInterval.Value * 2f;
                            t.RayLod = -1;
                        }
                        t.NextUpdateTime = now + interval;
                        ProcessSound(t, earPos, interval);
                    }

                    ApplyTracked(t, dt);
                }
                catch (Exception e)
                {
                    // One bad sound must never take the rest of the simulation down with it.
                    try
                    {
                        SrcState st;
                        if (t.Source != null && states.TryGetValue(t.Source, out st)) st.QuarantineUntil = now + 5f;
                        if (i < tracked.Count && tracked[i] == t) RemoveTracked(i, now);
                    }
                    catch (Exception) { }
                    RegisterFailure(e);
                }
            }
        }

        private void RemoveTracked(int index, float now)
        {
            TrackedSound t = tracked[index];
            ReleaseTracked(t, now);
            tracked.RemoveAt(index);
        }

        private void ProcessSound(TrackedSound t, Vector3 earPos, float interval)
        {
            AudioSource s = t.Source;
            Vector3 pos = s.transform.position;
            float d = t.Distance;

            if (cfgTinnitusEnabled.Value && (t.Category == SoundCategory.Gunshot || t.Category == SoundCategory.Explosion) && d < 20f)
            {
                float f = 1f - d / 20f;
                float enc = 1f + 0.8f * Mathf.Clamp01(listenerAudio.CurrentRoom.Enclosure);
                tinnitus.AddExposure(interval * 0.03f * f * enc * cfgTinnitusSensitivity.Value);
            }

            float ratio = 0f, thick = 0f, hfLoss = 1f;
            bool covered = false;
            if (cfgOcclusionEnabled.Value && t.RayLod >= 0 && d > 1.5f)
            {
                int rays = (t.RayLod == 1) ? 1 : cfgRayCount.Value;
                OcclusionResult r = occlusion.Probe(earPos, pos, t.SourceTop, rays);
                ratio = r.Ratio;
                thick = r.ThicknessM;
                hfLoss = r.MaterialHfLoss;
                covered = r.SourceCovered;
            }
            if (t.Processed) ratio = Mathf.Lerp(t.Occlusion, ratio, 0.6f);   // damp ray flicker
            t.Occlusion = ratio;

            float strength = cfgWallMuffleStrength.Value;
            float gain = 1f;
            if (ratio > 0.001f)
            {
                float vol = Mathf.Lerp(1f, cfgOcclusionVolumeFloor.Value, ratio);
                float thickLoss = Mathf.Clamp01(thick / 6f) * 0.5f * strength;
                gain *= vol * (1f - Mathf.Clamp01(thickLoss * ratio));
                if (covered) gain *= 0.92f;     // emitter is indoors: the room swallows a little more
            }

            float air = Mathf.Clamp01(cfgAirAbsorption.Value);
            if (air > 0f && d > 20f)
                gain *= Mathf.Lerp(1f, 0.85f, Mathf.Clamp01((d - 20f) / 300f) * air);
            gain = Mathf.Clamp(gain, 0.02f, 1f);

            float cutoff = 22000f;
            if (ratio > 0.01f)
            {
                float m = Mathf.Pow(ratio, 0.75f) * strength;
                m *= Mathf.Clamp(1f + thick * 0.25f, 1f, 3f) * Mathf.Lerp(1f, hfLoss, 0.6f);
                if (covered) m *= 1.15f;
                // Log-space sweep: equal steps sound like equal changes in brightness.
                float lo = Mathf.Log(Mathf.Max(100f, cfgMinCutoffHz.Value));
                float hi = Mathf.Log(20000f);
                cutoff = Mathf.Exp(Mathf.Lerp(hi, lo, Mathf.Clamp01(m)));
            }
            if (air > 0f)
            {
                float a = Mathf.Lerp(20000f, 4500f, Mathf.Clamp01(d / Mathf.Max(1f, cfgMaxSoundDistance.Value)) * air);
                cutoff = Mathf.Min(cutoff, a);
            }

            if (float.IsNaN(gain) || float.IsInfinity(gain)) gain = 1f;
            if (float.IsNaN(cutoff) || float.IsInfinity(cutoff)) cutoff = 22000f;

            t.TargetGain = gain;
            t.TargetCutoff = cutoff >= OffCutoff ? 22000f : Mathf.Max(cutoff, 120f);
            if (!t.Processed)
            {
                t.CurrentGain = t.TargetGain;
                t.CurrentCutoff = t.TargetCutoff;
                t.Processed = true;
            }
        }

        private void ApplyTracked(TrackedSound t, float dt)
        {
            if (!t.Processed) return;
            GameObject go = t.Source.gameObject;

            // gain (audio-thread filter; AudioSource.volume is never touched)
            t.CurrentGain = Mathf.MoveTowards(t.CurrentGain, t.TargetGain, 5f * dt);
            bool gainNeeded = t.CurrentGain < 0.995f || t.TargetGain < 0.995f;
            if (gainNeeded)
            {
                if (t.Gain == null) t.Gain = GetOrAddGain(go);
                if (t.Gain != null)
                {
                    if (!t.Gain.enabled) t.Gain.enabled = true;
                    t.Gain.TargetGain = t.CurrentGain;
                }
            }
            else if (t.Gain != null)
            {
                t.Gain.TargetGain = 1f;
            }

            // low-pass, smoothed in the log domain
            float k = 1f - Mathf.Exp(-10f * dt);
            float lc = Mathf.Log(Mathf.Max(t.CurrentCutoff, 120f));
            float lt = Mathf.Log(Mathf.Max(t.TargetCutoff, 120f));
            t.CurrentCutoff = Mathf.Exp(Mathf.Lerp(lc, lt, k));
            bool lpNeeded = t.CurrentCutoff < OffCutoff || t.TargetCutoff < OffCutoff;
            if (lpNeeded)
            {
                if (t.LowPass == null) t.LowPass = GetOrAddLowPass(go);
                if (t.LowPass != null)
                {
                    if (!t.LowPass.enabled) t.LowPass.enabled = true;
                    t.LowPass.cutoffFrequency = Mathf.Clamp(t.CurrentCutoff, 120f, 22000f);
                }
            }
            else if (t.LowPass != null && t.LowPass.enabled)
            {
                t.LowPass.enabled = false;
            }
        }

        private AcousticGainFilter GetOrAddGain(GameObject go)
        {
            AcousticGainFilter g = go.GetComponent<AcousticGainFilter>();
            if (g == null) g = go.AddComponent<AcousticGainFilter>();
            return g;
        }

        private AudioLowPassFilter GetOrAddLowPass(GameObject go)
        {
            // Only ever touch low-pass filters this mod created itself.
            go.GetComponents<AudioLowPassFilter>(lowPassScratch);
            for (int i = 0; i < lowPassScratch.Count; i++)
            {
                if (lowPassScratch[i] != null && ownedLowPass.Contains(lowPassScratch[i].GetInstanceID()))
                {
                    AudioLowPassFilter found = lowPassScratch[i];
                    lowPassScratch.Clear();
                    return found;
                }
            }
            lowPassScratch.Clear();
            AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
            lp.enabled = false;
            ownedLowPass.Add(lp.GetInstanceID());
            return lp;
        }

        private bool GainInUse(AcousticGainFilter g, TrackedSound except)
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] != except && tracked[i].Gain == g) return true;
            }
            return false;
        }

        private bool LowPassInUse(AudioLowPassFilter lp, TrackedSound except)
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] != except && tracked[i].LowPass == lp) return true;
            }
            return false;
        }

        /// <summary>Gives the AudioSource back to the game: gain 1, low-pass off. Does not remove from the list.</summary>
        private void ReleaseTracked(TrackedSound t, float now)
        {
            if (!object.ReferenceEquals(t.Source, null)) trackedMap.Remove(t.Source);

            if (t.Gain != null && !GainInUse(t.Gain, t))
            {
                t.Gain.TargetGain = 1f;
                PendingDisable p = new PendingDisable();
                p.Target = t.Gain;
                p.At = now + 0.35f;
                pendingDisable.Add(p);
            }
            if (t.LowPass != null && !LowPassInUse(t.LowPass, t)) t.LowPass.enabled = false;
        }

        private void FlushPendingDisable(float now)
        {
            for (int i = pendingDisable.Count - 1; i >= 0; i--)
            {
                PendingDisable p = pendingDisable[i];
                if (now < p.At) continue;
                pendingDisable.RemoveAt(i);
                if (p.Target != null && !GainInUse(p.Target, null)) p.Target.enabled = false;
            }
        }

        private void ReleaseAll()
        {
            float now = Time.time;
            for (int i = 0; i < tracked.Count; i++) ReleaseTracked(tracked[i], now);
            tracked.Clear();
            trackedMap.Clear();
            // gain filters are switched off immediately here: their target is already 1
            for (int i = 0; i < pendingDisable.Count; i++)
            {
                if (pendingDisable[i].Target != null) pendingDisable[i].Target.enabled = false;
            }
            pendingDisable.Clear();
        }

        // =====================================================================================
        // GUI
        // =====================================================================================
        private void OnGUI()
        {
            if (gameMenu != null)
            {
                try { gameMenu.DrawFallback(); }
                catch (Exception e) { if (e is ExitGUIException) throw; }
            }
            if (settingsUi != null && settingsUi.Visible)
            {
                try { settingsUi.Draw(); }
                catch (Exception e)
                {
                    // keep the window open; layout exceptions are usually transient
                    if (e is ExitGUIException) throw;
                }
            }

            if (!cfgDebug.Value || !cfgEnabled.Value) return;
            try
            {
                if (debugStyle == null)
                {
                    debugStyle = new GUIStyle(GUI.skin.label);
                    debugStyle.fontSize = 14;
                    debugStyle.normal.textColor = Color.yellow;
                }
                int occluded = 0;
                for (int i = 0; i < tracked.Count; i++)
                {
                    if (tracked[i].Occlusion > 0.5f) occluded++;
                }
                RoomEstimate room = listenerAudio.CurrentRoom;
                StringBuilder sb = new StringBuilder(512);
                sb.Append("Dynamic Audio v").Append(PluginVersion).Append("\nListener: ").Append(listenerAudio.ListenerInfo);
                sb.Append("\nRoom: ").Append(RoomSystem.Describe(ref room));
                sb.Append("\nTracked sounds: ").Append(tracked.Count).Append(" (occluded>50%: ").Append(occluded).Append(")");
                if (safeModeUntil > Time.time) sb.Append("\nSAFE MODE (too many errors), resuming soon");
                sb.Append("\n");
                if (cfgTinnitusEnabled.Value)
                    sb.Append("Tinnitus: ").Append(tinnitus.Ringing ? "RINGING" : "armed, exposure " + (tinnitus.Exposure01 * 100f).ToString("F0") + "%").Append("\n");

                List<TrackedSound> copy = new List<TrackedSound>(tracked);
                copy.Sort(delegate (TrackedSound a, TrackedSound b) { return a.Distance.CompareTo(b.Distance); });
                int n = Mathf.Min(6, copy.Count);
                for (int i = 0; i < n; i++)
                {
                    TrackedSound t = copy[i];
                    if (t == null || t.Source == null) continue;
                    string nm = t.Source.clip != null ? t.Source.clip.name : t.Source.gameObject.name;
                    sb.Append("[").Append(t.Category).Append("] ").Append(nm)
                      .Append("  d=").Append(t.Distance.ToString("F0")).Append("m  occl=").Append((t.Occlusion * 100f).ToString("F0"))
                      .Append("%  gain=").Append(t.CurrentGain.ToString("F2"))
                      .Append("  lp=").Append(t.CurrentCutoff < OffCutoff ? t.CurrentCutoff.ToString("F0") + "Hz" : "off").Append("\n");
                }
                GUI.Label(new Rect(10f, 10f, 1000f, 300f), sb.ToString(), debugStyle);
            }
            catch (Exception) { }
        }
    }
}
