// EchoProbe.cs
// BepInEx plugin for Ravenfield — TOF reverb + per-source occlusion + Doppler/flyby + material early-reflection boost
// + Sound delay simulation (light vs sound) + Air absorption + Environmental effects + Tinnitus (ENABLED BY DEFAULT FOR TESTING)
// Version: 5.0.0 - ROBUST IMPLEMENTATION: Fixed sound delay detection, enhanced wall occlusion with multi-ray checks, 
// improved tinnitus triggering from sustained noise, better environmental effects, and comprehensive distance tracking.
// C# 7.3 compatible.

using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Ravenfield.EchoProbe
{
    [BepInPlugin("dynamic.audio", "Dynamic Audio (Immersive Sound Physics)", "5.0.0")]
    public class EchoProbePlugin : BaseUnityPlugin
    {
        // ---------------- Configuration ----------------
        private ConfigEntry<float> cfg_probeInterval;
        private ConfigEntry<int> cfg_rays;
        private ConfigEntry<float> cfg_maxProbeDistance;
        private ConfigEntry<float> cfg_upCone;
        
        // Reverb settings
        private ConfigEntry<float> cfg_minDecay;
        private ConfigEntry<float> cfg_maxDecay;
        private ConfigEntry<float> cfg_minReverbLevel;
        private ConfigEntry<float> cfg_maxReverbLevel;
        private ConfigEntry<float> cfg_minRoomMb;
        private ConfigEntry<float> cfg_maxRoomMb;
        private ConfigEntry<float> cfg_maxReflectionsDelay;
        
        // Per-source occlusion
        private ConfigEntry<float> cfg_maxOcclusionDistance;
        private ConfigEntry<int> cfg_maxSourcesPerCheck;
        private ConfigEntry<float> cfg_sourceOcclusionMinVolume;
        
        // Parameter smoothing
        private ConfigEntry<float> cfg_paramLerp;
        
        // Loudness / Exposure
        private ConfigEntry<float> cfg_loudnessSampleInterval;
        private ConfigEntry<int> cfg_loudnessSamples;
        private ConfigEntry<float> cfg_exposureGain;
        private ConfigEntry<float> cfg_enclosureGain;
        private ConfigEntry<float> cfg_reverbGain;
        private ConfigEntry<float> cfg_exposureDecay;
        private ConfigEntry<float> cfg_exposureQuietRms;
        
        // Tinnitus (ENABLED BY DEFAULT FOR TESTING)
        private ConfigEntry<bool> cfg_enableTinnitus;
        private ConfigEntry<float> cfg_tinnitusSensitivity;
        private ConfigEntry<float> cfg_tinnitusDuration;
        private ConfigEntry<float> cfg_tinnitusVolume;
        private ConfigEntry<float> cfg_tinnitusDecay;
        private ConfigEntry<string> cfg_tinnitusAudioFile;
        
        // Shock (explosion mute/muffle)
        private ConfigEntry<float> cfg_explosionPeakThreshold;
        private ConfigEntry<float> cfg_shockMuteSeconds;
        private ConfigEntry<float> cfg_shockMuffleSeconds;
        
        // Doppler & Flyby
        private ConfigEntry<float> cfg_dopplerStrength;
        private ConfigEntry<float> cfg_dopplerMaxPitch;
        private ConfigEntry<float> cfg_dopplerMinPitch;
        private ConfigEntry<float> cfg_velocitySmoothing;
        private ConfigEntry<float> cfg_flybyVelocityThreshold;
        private ConfigEntry<float> cfg_flybyMinDistance;
        private ConfigEntry<float> cfg_flybyPitchBoost;
        private ConfigEntry<float> cfg_flybyVolumeBoost;
        private ConfigEntry<float> cfg_flybyDecaySeconds;
        
        // NEW: Sound Delay Simulation (Light vs Sound) - ROBUST IMPLEMENTATION
        private ConfigEntry<bool> cfg_enableSoundDelay;
        private ConfigEntry<float> cfg_soundSpeed;
        private ConfigEntry<float> cfg_maxSoundDelay;
        private ConfigEntry<float> cfg_lightSpeedThreshold;
        private ConfigEntry<float> cfg_soundDelayMinDistance;
        
        // NEW: Air Absorption
        private ConfigEntry<bool> cfg_enableAirAbsorption;
        private ConfigEntry<float> cfg_airAbsorptionRate;
        private ConfigEntry<float> cfg_humidityFactor;
        private ConfigEntry<float> cfg_temperatureFactor;
        
        // NEW: Environmental
        private ConfigEntry<bool> cfg_enableWeatherEffects;
        private ConfigEntry<float> cfg_windEffect;
        private ConfigEntry<float> cfg_groundReflectionBoost;
        private ConfigEntry<float> cfg_environmentalStrength;

        // NEW: Distance Calculator & Wall Occlusion (v4.0.0)
        private ConfigEntry<bool> cfg_enableDistanceCalculator;
        private ConfigEntry<float> cfg_distanceCheckInterval;
        private ConfigEntry<int> cfg_maxTrackedCues;
        private ConfigEntry<bool> cfg_enableWallOcclusion;
        private ConfigEntry<int> cfg_wallRays;
        private ConfigEntry<float> cfg_wallRayDistance;
        private ConfigEntry<float> cfg_wallMuffleAmount;
        private ConfigEntry<string> cfg_debugMode;
        private ConfigEntry<int> cfg_occlusionLayerMask;

        // Material early-reflection boost (optional)
        // tags map: tag -> early reflection multiplier
        private readonly Dictionary<string, float> materialEarlyReflection = new Dictionary<string, float>()
        {
            { "Material_Concrete", 1.15f },
            { "Material_Metal", 1.2f },
            { "Material_Wood", 1.05f },
            { "Material_Glass", 1.02f },
            { "Material_Default", 1.0f }
        };

        // ---------------- State ----------------
        private AudioListener listener;
        private Transform ear;
        private AudioReverbFilter reverb;
        private AudioLowPassFilter lowpass; // used for shock only
        private float originalListenerVolume = 1f;

        private float tgtDecay, curDecay;
        private float tgtRoom, curRoom;
        private float tgtRev, curRev;
        private float tgtRefDel, curRefDel;

        private float lastProbe;
        private List<Vector3> dirs = new List<Vector3>();

        // last probe metrics
        private float lastCoverage;
        private float lastOpenSky;
        private float lastAvgDist;
        private float lastEnclosure;

        // loudness
        private float lastLoudSampleTime;
        private float lastRms;
        private float lastPeak;
        private float[] loudBuf;

        // shock & exposure
        private float noiseExposure;
        private float shockTimeLeft;
        private float infoNextLog;
        
        // tinnitus state
        private float tinnitusTimeLeft;
        private float tinnitusCurrentVolume;
        private float accumulatedNoiseForTinnitus = 0f;
        
        // Tinnitus audio source - plays external audio file
        private AudioSource tinnitusSource;
        private AudioClip tinnitusClip;

        // tracked sources for occlusion + original volume store
        private readonly List<AudioSource> trackedSources = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> originalSourceVolume = new Dictionary<AudioSource, float>();
        private float lastSourceScanTime;

        // velocity tracking for doppler
        private readonly Dictionary<AudioSource, Vector3> prevSourcePos = new Dictionary<AudioSource, Vector3>();
        private readonly Dictionary<AudioSource, Vector3> sourceVel = new Dictionary<AudioSource, Vector3>();
        private Vector3 prevEarPos;
        private Vector3 listenerVel;

        // per-source flyby state
        private readonly Dictionary<AudioSource, float> flybyTimers = new Dictionary<AudioSource, float>();

        // NEW: Sound delay simulation state
        private readonly Dictionary<AudioSource, float> soundDelayTimers = new Dictionary<AudioSource, float>();
        private readonly Dictionary<AudioSource, bool> soundPlayed = new Dictionary<AudioSource, bool>();
        
        // NEW: Environmental state
        private float currentTemperature = 20f; // Celsius
        private Vector3 windVelocity = Vector3.zero;

        // NEW: Distance Calculator & Wall Occlusion state (v4.0.0)
        private readonly Dictionary<AudioSource, float> audioCueDistances = new Dictionary<AudioSource, float>();
        private readonly Dictionary<AudioSource, float> wallOcclusionFactors = new Dictionary<AudioSource, float>();
        private float lastDistanceCheckTime;
        private float lastWallCheckTime;
        private bool configNeedsReload = false;
        private float lastConfigCheckTime;


        private void Awake()
        {
            Logger.LogInfo("[DynamicAudio] Initializing Dynamic Audio V5.0.0 - Tinnitus ENABLED by default for testing");
            
            SetupConfig();
            
            // Set up config file change listener
            Config.SettingChanged += (sender, e) => { configNeedsReload = true; };
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            BuildDirections(cfg_rays.Value);
            loudBuf = new float[cfg_loudnessSamples.Value];
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ---------------- Configuration Setup ----------------
        private void SetupConfig()
        {
            // General
            cfg_probeInterval = Config.Bind("General", "Probe Interval", 0.12f, "How often to probe the environment (seconds). Lower = more responsive but higher CPU usage.");
            cfg_rays = Config.Bind("General", "Probe Rays", 40, "Number of rays to cast for environment probing.");
            cfg_maxProbeDistance = Config.Bind("General", "Max Probe Distance", 40f, "Maximum distance for environment probing (meters).");
            cfg_upCone = Config.Bind("General", "Up Cone Threshold", 0.6f, "Y threshold for upward rays (for open sky detection).");
            
            // Reverb
            cfg_minDecay = Config.Bind("Reverb", "Min Decay", 0.35f, "Minimum reverb decay time (seconds).");
            cfg_maxDecay = Config.Bind("Reverb", "Max Decay", 5.50f, "Maximum reverb decay time (seconds).");
            cfg_minReverbLevel = Config.Bind("Reverb", "Min Reverb Level", -1800f, "Minimum reverb level (dB).");
            cfg_maxReverbLevel = Config.Bind("Reverb", "Max Reverb Level", -60f, "Maximum reverb level (dB).");
            cfg_minRoomMb = Config.Bind("Reverb", "Min Room", -2000f, "Minimum room size (mB).");
            cfg_maxRoomMb = Config.Bind("Reverb", "Max Room", 0f, "Maximum room size (mB).");
            cfg_maxReflectionsDelay = Config.Bind("Reverb", "Max Reflections Delay", 0.4f, "Maximum delay for early reflections (seconds).");
            
            // Occlusion
            cfg_maxOcclusionDistance = Config.Bind("Occlusion", "Max Occlusion Distance", 60f, "Maximum distance to check for sound occlusion (meters).");
            cfg_maxSourcesPerCheck = Config.Bind("Occlusion", "Max Sources Per Check", 24, "Maximum number of audio sources to check per probe.");
            cfg_sourceOcclusionMinVolume = Config.Bind("Occlusion", "Occluded Volume", 0.25f, "Minimum volume multiplier when fully occluded.");
            
            // Smoothing
            cfg_paramLerp = Config.Bind("Smoothing", "Parameter Lerp", 3.0f, "How quickly parameters interpolate (higher = snappier).");
            
            // Loudness / Exposure
            cfg_loudnessSampleInterval = Config.Bind("Exposure", "Sample Interval", 0.05f, "How often to sample loudness (seconds).");
            cfg_loudnessSamples = Config.Bind("Exposure", "Sample Count", 1024, "Number of samples for loudness calculation.");
            cfg_exposureGain = Config.Bind("Exposure", "Exposure Gain", 1.0f, "Multiplier for noise exposure buildup. LOWER = less muffling. Default 1.0 prevents over-muffling of vanilla weapons.");
            cfg_enclosureGain = Config.Bind("Exposure", "Enclosure Gain", 1.8f, "How much enclosure affects exposure.");
            cfg_reverbGain = Config.Bind("Exposure", "Reverb Gain", 0.0022f, "How much reverb affects exposure.");
            cfg_exposureDecay = Config.Bind("Exposure", "Exposure Decay", 6.0f, "Rate at which exposure decays.");
            cfg_exposureQuietRms = Config.Bind("Exposure", "Quiet RMS Threshold", 0.08f, "RMS threshold for quiet recovery.");
            
            // Tinnitus (ENABLED BY DEFAULT FOR TESTING - set to false if you find it annoying)
            cfg_enableTinnitus = Config.Bind("Tinnitus", "Enable Tinnitus", true, "Enable tinnitus effect after loud explosions or sustained gunfire (ENABLED BY DEFAULT for testing - set to false if you find it annoying).");
            cfg_tinnitusSensitivity = Config.Bind("Tinnitus", "Sensitivity", 0.15f, "How easily tinnitus triggers (0.1-1.0). Lower = easier to trigger. For instant explosions: peak must exceed this. For sustained noise: accumulated RMS must exceed this * 2.");
            cfg_tinnitusDuration = Config.Bind("Tinnitus", "Base Duration", 8f, "Base duration of tinnitus effect (seconds).");
            cfg_tinnitusVolume = Config.Bind("Tinnitus", "Ring Volume", 0.5f, "Volume of the tinnitus ringing sound (0-1). Higher = more noticeable ringing.");
            cfg_tinnitusDecay = Config.Bind("Tinnitus", "Decay Rate", 0.92f, "How quickly tinnitus fades per second (0.8-0.95). Higher = slower decay.");
            cfg_tinnitusAudioFile = Config.Bind("Tinnitus", "Audio File", "tinnitus.mp3", "Path to the tinnitus audio file (mp3, wav, ogg) placed in the BepInEx/plugins folder. The sound will play when loud noises are detected.");
            
            // Shock
            cfg_explosionPeakThreshold = Config.Bind("Shock", "Explosion Threshold", 0.85f, "Peak amplitude threshold for explosion detection.");
            cfg_shockMuteSeconds = Config.Bind("Shock", "Shock Mute Duration", 0.25f, "Duration of temporary mute after explosion (seconds).");
            cfg_shockMuffleSeconds = Config.Bind("Shock", "Shock Muffle Duration", 2.0f, "Duration of low-pass filter after explosion (seconds).");
            
            // Doppler & Flyby
            cfg_dopplerStrength = Config.Bind("Doppler", "Strength", 0f, "Doppler effect strength (1 = physical accuracy).");
            cfg_dopplerMaxPitch = Config.Bind("Doppler", "Max Pitch", 1f, "Maximum pitch shift from Doppler.");
            cfg_dopplerMinPitch = Config.Bind("Doppler", "Min Pitch", 1f, "Minimum pitch shift from Doppler.");
            cfg_velocitySmoothing = Config.Bind("Doppler", "Velocity Smoothing", 5.0f, "Smoothing factor for velocity estimation.");
            cfg_flybyVelocityThreshold = Config.Bind("Flyby", "Velocity Threshold", 25f, "Minimum lateral velocity for flyby effect (m/s).");
            cfg_flybyMinDistance = Config.Bind("Flyby", "Min Distance", 2.0f, "Closest approach distance for flyby trigger (meters).");
            cfg_flybyPitchBoost = Config.Bind("Flyby", "Pitch Boost", 1.15f, "Pitch multiplier during flyby.");
            cfg_flybyVolumeBoost = Config.Bind("Flyby", "Volume Boost", 1.2f, "Volume multiplier during flyby.");
            cfg_flybyDecaySeconds = Config.Bind("Flyby", "Decay Duration", 0.35f, "Duration for flyby effect to fade (seconds).");
            
            // Sound Delay Simulation - ROBUST IMPLEMENTATION
            cfg_enableSoundDelay = Config.Bind("SoundDelay", "Enable Sound Delay", true, "Simulate light traveling faster than sound (see explosion before hearing it). Works best with distances > 10m.");
            cfg_soundSpeed = Config.Bind("SoundDelay", "Sound Speed", 343f, "Speed of sound in m/s. Lower values = longer delays. Try 50-100 for dramatic effect, 343 for realism.");
            cfg_maxSoundDelay = Config.Bind("SoundDelay", "Max Delay", 5.0f, "Maximum sound delay (seconds) to prevent excessive delays.");
            cfg_lightSpeedThreshold = Config.Bind("SoundDelay", "Light Speed Threshold", 5f, "Minimum distance (meters) before sound delay kicks in. Lower = delay starts closer.");
            cfg_soundDelayMinDistance = Config.Bind("SoundDelay", "Min Distance for Delay", 3f, "Sounds closer than this won't have delay (prevents weirdness with nearby sounds).");
            
            // Air Absorption
            cfg_enableAirAbsorption = Config.Bind("AirAbsorption", "Enable Air Absorption", true, "High frequencies are absorbed over distance (affected by humidity/temp).");
            cfg_airAbsorptionRate = Config.Bind("AirAbsorption", "Absorption Rate", 0.005f, "Base rate of high-frequency absorption per meter. Higher = more muffled distant sounds.");
            cfg_humidityFactor = Config.Bind("AirAbsorption", "Humidity Factor", 0.5f, "Current humidity (0-1). Higher humidity = less absorption.");
            cfg_temperatureFactor = Config.Bind("AirAbsorption", "Temperature", 20f, "Temperature in Celsius. Affects sound speed and absorption.");
            
            // Environmental
            cfg_enableWeatherEffects = Config.Bind("Environment", "Enable Weather Effects", true, "Enable wind and weather-based audio effects.");
            cfg_windEffect = Config.Bind("Environment", "Wind Effect Strength", 0.15f, "How much wind affects sound propagation.");
            cfg_groundReflectionBoost = Config.Bind("Environment", "Ground Reflection", 1.1f, "Boost to reflections from ground surfaces.");
            cfg_environmentalStrength = Config.Bind("Environment", "Environmental Strength", 0.8f, "Overall strength of environmental audio effects (0-1).");

            // NEW: Distance Calculator & Wall Occlusion (v4.0.0) - ENHANCED
            cfg_enableDistanceCalculator = Config.Bind("DistanceCalculator", "Enable Distance Calculator", true, "Calculate exact distance to all playing audio cues for accurate light vs sound simulation.");
            cfg_distanceCheckInterval = Config.Bind("DistanceCalculator", "Check Interval", 0.03f, "How often to update distance calculations (seconds). Lower = more accurate but higher CPU usage.");
            cfg_maxTrackedCues = Config.Bind("DistanceCalculator", "Max Tracked Cues", 64, "Maximum number of audio cues to track simultaneously.");
            cfg_enableWallOcclusion = Config.Bind("WallOcclusion", "Enable Wall Occlusion", true, "Detect walls between player and sound sources to muffle sounds behind walls.");
            cfg_wallRays = Config.Bind("WallOcclusion", "Wall Rays", 32, "Number of rays to cast around player for wall detection. Higher = more accurate wall detection.");
            cfg_wallRayDistance = Config.Bind("WallOcclusion", "Wall Ray Distance", 25f, "Distance to check for walls around player (meters).");
            cfg_wallMuffleAmount = Config.Bind("WallOcclusion", "Wall Muffle Amount", 0.15f, "Volume multiplier when sound is blocked by a wall (0 = silent, 1 = no change). Lower = more muffled.");
            cfg_debugMode = Config.Bind("Debug", "Debug Mode", "none", "Debug output mode: none, distances, walls, all");
            cfg_occlusionLayerMask = Config.Bind("WallOcclusion", "Occlusion Layer Mask", -1, "Layer mask for occlusion raycasts. -1 = everything, use layer numbers for filtering.");
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Logger.LogInfo("[DynamicAudio] Scene loaded → (re)initialize.");
            FindOrAttach();
        }

        private void Start()
        {
            FindOrAttach();
        }

        private void FindOrAttach()
        {
            listener = Object.FindObjectOfType<AudioListener>();
            ear = listener ? listener.transform : null;

            if (!listener || !ear)
            {
                Logger.LogWarning("[DynamicAudio] No AudioListener found yet.");
                return;
            }

            // init previous ear pos for listener velocity
            prevEarPos = ear.position;
            listenerVel = Vector3.zero;

            if (!reverb) reverb = listener.GetComponent<AudioReverbFilter>();
            if (!reverb) reverb = listener.gameObject.AddComponent<AudioReverbFilter>();

            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0;
            reverb.diffusion = 100f;
            reverb.density = 100f;

            if (!lowpass) lowpass = listener.GetComponent<AudioLowPassFilter>();
            if (!lowpass) lowpass = listener.gameObject.AddComponent<AudioLowPassFilter>();
            lowpass.enabled = true;
            lowpass.cutoffFrequency = 22000f;

            // Initialize tinnitus audio source - load external audio file
            if (cfg_enableTinnitus.Value && !tinnitusSource)
            {
                GameObject tinnitusObj = new GameObject("TinnitusPlayer");
                tinnitusObj.transform.SetParent(listener.transform);
                tinnitusObj.transform.localPosition = Vector3.zero;
                tinnitusSource = tinnitusObj.AddComponent<AudioSource>();
                tinnitusSource.playOnAwake = false;
                tinnitusSource.loop = true;
                tinnitusSource.spatialBlend = 0f; // 2D sound, not affected by position
                tinnitusSource.volume = 0f; // Start silent
                
                // Load the external audio file for tinnitus sound
                string audioFileName = cfg_tinnitusAudioFile.Value;
                string pluginPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string audioFilePath = Path.Combine(pluginPath, audioFileName);
                
                Logger.LogInfo($"[DynamicAudio] Looking for tinnitus audio file at: {audioFilePath}");
                
                if (File.Exists(audioFilePath))
                {
                    try
                    {
                        // Read the audio file bytes
                        byte[] audioBytes = File.ReadAllBytes(audioFilePath);
                        
                        // Use Unity's WWW or UnityWebRequest to load the audio
                        // For now, we'll create a placeholder and load it asynchronously
                        StartCoroutine(LoadAudioClip(audioFilePath));
                        
                        Logger.LogInfo($"[DynamicAudio] Tinnitus audio file found: {audioFileName}");
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogError($"[DynamicAudio] Failed to load tinnitus audio file: {ex.Message}");
                        CreateFallbackTinnitusClip();
                    }
                }
                else
                {
                    Logger.LogWarning($"[DynamicAudio] Tinnitus audio file not found: {audioFilePath}");
                    Logger.LogWarning($"[DynamicAudio] Please place '{audioFileName}' in the plugin folder: {pluginPath}");
                    Logger.LogWarning($"[DynamicAudio] Creating fallback synthesized tone...");
                    CreateFallbackTinnitusClip();
                }
            }

            originalListenerVolume = AudioListener.volume;

            // seed params
            curDecay = tgtDecay = cfg_minDecay.Value;
            curRoom = tgtRoom = cfg_minRoomMb.Value;
            curRev = tgtRev = cfg_minReverbLevel.Value;
            curRefDel = tgtRefDel = 0.03f;
            Apply();

            Logger.LogInfo("[DynamicAudio] Ready: reverb(User) + lowpass + tinnitus generator (Dynamic Audio V5.0.0 - Robust Implementation).");
            // initial scan for audio sources
            ScanAudioSources();
        }

        private void Update()
        {
            if (!listener || !ear || !reverb)
            {
                if (Time.frameCount % 60 == 0) FindOrAttach();
                return;
            }

            // Check for config changes and reload settings
            if (configNeedsReload && Time.unscaledTime - lastConfigCheckTime > 1f)
            {
                lastConfigCheckTime = Time.unscaledTime;
                configNeedsReload = false;
                Logger.LogInfo("[DynamicAudio] Config file changed - settings will be reloaded on next use");
            }

            // update listener velocity
            listenerVel = Vector3.Lerp(listenerVel, (ear.position - prevEarPos) / Mathf.Max(0.0001f, Time.unscaledDeltaTime), Time.unscaledDeltaTime * cfg_velocitySmoothing.Value);
            prevEarPos = ear.position;

            // probe
            if (Time.unscaledTime - lastProbe >= cfg_probeInterval.Value)
            {
                lastProbe = Time.unscaledTime;
                Probe(ear.position);
            }

            // loudness sampling
            if (Time.unscaledTime - lastLoudSampleTime >= cfg_loudnessSampleInterval.Value)
            {
                lastLoudSampleTime = Time.unscaledTime;
                SampleLoudness();
                UpdateExposure();
            }

            // NEW: Distance calculator for all audio cues (v4.0.0) - runs every frame for accuracy
            if (cfg_enableDistanceCalculator.Value && Time.unscaledTime - lastDistanceCheckTime >= cfg_distanceCheckInterval.Value)
            {
                lastDistanceCheckTime = Time.unscaledTime;
                UpdateAudioCueDistances();
            }

            // NEW: Wall occlusion detection (v4.0.0) - enhanced with better raycasting
            if (cfg_enableWallOcclusion.Value && Time.unscaledTime - lastWallCheckTime >= cfg_distanceCheckInterval.Value)
            {
                lastWallCheckTime = Time.unscaledTime;
                DetectWallOcclusions();
            }

            // per-source occlusion checks - runs continuously for robust sound delay
            CheckSourcesOcclusionAndDoppler();

            // occasionally re-scan sources
            if (Time.unscaledTime - lastSourceScanTime > 2.0f)
            {
                lastSourceScanTime = Time.unscaledTime;
                ScanAudioSources();
            }

            // chase targets
            float k = cfg_paramLerp.Value * Time.unscaledDeltaTime;
            curDecay = Mathf.MoveTowards(curDecay, tgtDecay, k);
            curRoom = Mathf.MoveTowards(curRoom, tgtRoom, k * 2000f);
            curRev = Mathf.MoveTowards(curRev, tgtRev, k * 1800f);
            curRefDel = Mathf.MoveTowards(curRefDel, tgtRefDel, k);
            Apply();

            // handle shock mute/muffle
            UpdateShockAudio(Time.unscaledDeltaTime);

            if (Time.unscaledTime >= infoNextLog)
            {
                infoNextLog = Time.unscaledTime + 1.5f;
                Logger.LogInfo(string.Format(
                    "[DynamicAudio] decay={0:F2}s room={1:F0}mB revLvl={2:F0}dB refDel={3}ms | RMS={4:F2} Peak={5:F2} Expo={6:F1} TrackedSrcs={7}",
                    curDecay, curRoom, curRev, curRefDel * 1000f, lastRms, lastPeak, noiseExposure, trackedSources.Count));
            }
        }

        // ---------------- Reverb Core ----------------

        private System.Collections.IEnumerator LoadAudioClip(string filePath)
        {
            // Use UnityWebRequest to load audio file asynchronously
            using (var www = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, AudioType.UNKNOWN))
            {
                yield return www.SendWebRequest();
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        tinnitusClip = clip;
                        tinnitusSource.clip = tinnitusClip;
                        Logger.LogInfo($"[DynamicAudio] Tinnitus audio clip loaded successfully: {clip.name}, length: {clip.length}s");
                    }
                    else
                    {
                        Logger.LogError("[DynamicAudio] Failed to create AudioClip from file");
                        CreateFallbackTinnitusClip();
                    }
                }
                else
                {
                    Logger.LogError($"[DynamicAudio] Failed to load tinnitus audio: {www.error}");
                    CreateFallbackTinnitusClip();
                }
            }
        }

        private void CreateFallbackTinnitusClip()
        {
            // Generate a synthesized fallback tone if external file fails to load
            int sampleRate = 44100;
            float duration = 5f;
            AudioClip clip = AudioClip.Create("TinnitusFallback", Mathf.FloorToInt(sampleRate * duration), 1, sampleRate, false);
            float[] samples = new float[Mathf.FloorToInt(sampleRate * duration)];
            
            // Generate a high-pitched sine wave (4kHz - typical tinnitus frequency)
            float baseFreq = 4000f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float fundamental = Mathf.Sin(2f * Mathf.PI * baseFreq * t);
                float harmonic1 = Mathf.Sin(2f * Mathf.PI * (baseFreq * 1.5f) * t) * 0.3f;
                float harmonic2 = Mathf.Sin(2f * Mathf.PI * (baseFreq * 2f) * t) * 0.15f;
                samples[i] = (fundamental * 0.6f + harmonic1 + harmonic2) * 0.5f;
            }
            clip.SetData(samples, 0);
            tinnitusClip = clip;
            tinnitusSource.clip = tinnitusClip;
            Logger.LogInfo("[DynamicAudio] Fallback tinnitus tone created at 4kHz");
        }

        private void Apply()
        {
            reverb.decayTime = curDecay;
            reverb.room = Mathf.RoundToInt(curRoom);
            reverb.reverbLevel = curRev;
            reverb.reflectionsDelay = curRefDel;
            reverb.reverbDelay = Mathf.Clamp(curRefDel * 1.5f, 0.01f, 0.40f);
        }

        private void Probe(Vector3 origin)
        {
            if (dirs.Count != cfg_rays.Value) BuildDirections(cfg_rays.Value);

            int hitCount = 0;
            float sumDist = 0f;
            int upTotal = 0, upMiss = 0;
            float nearest = cfg_maxProbeDistance.Value;

            for (int i = 0; i < dirs.Count; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(origin, d, out hit, cfg_maxProbeDistance.Value, ~0, QueryTriggerInteraction.Ignore))
                {
                    hitCount++;
                    sumDist += hit.distance;
                    if (hit.distance < nearest) nearest = hit.distance;
                }
                else
                {
                    if (d.y >= cfg_upCone.Value) upMiss++;
                }

                if (d.y >= cfg_upCone.Value) upTotal++;
            }

            lastCoverage = hitCount / Mathf.Max(1f, (float)dirs.Count);
            lastOpenSky = (upTotal > 0) ? (upMiss / (float)upTotal) : 0f;
            lastAvgDist = (hitCount > 0) ? (sumDist / hitCount) : cfg_maxProbeDistance.Value;

            lastEnclosure = Mathf.Clamp01(lastCoverage * (1f - 0.65f * lastOpenSky));

            // ---- Map to targets (LONGER tails for big closed areas) ----
            float roomSize = Mathf.Clamp01(lastAvgDist / cfg_maxProbeDistance.Value);
            float effectiveEnclosure = Mathf.Clamp01(lastEnclosure * (1f - 0.75f * lastOpenSky));

            float decay01 = Mathf.Clamp01(0.2f + 0.8f * roomSize) * Mathf.Clamp01(0.35f + 0.75f * effectiveEnclosure);
            tgtDecay = Mathf.Lerp(cfg_minDecay.Value, cfg_maxDecay.Value, decay01);

            tgtRoom = Mathf.Lerp(cfg_minRoomMb.Value, cfg_maxRoomMb.Value, Mathf.Pow(effectiveEnclosure, 0.9f));

            float tail01 = Mathf.Clamp01(effectiveEnclosure * (0.6f + 0.8f * roomSize));
            // material-based early reflection boost: if many hits are on reflective tags, boost a bit
            float matBoost = 1.0f;
            // (cheap heuristic: probe some directions again and check tags; keep tiny cost)
            int checks = Mathf.Min(8, dirs.Count);
            for (int i = 0; i < checks; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(origin, d, out hit, cfg_maxProbeDistance.Value, ~0, QueryTriggerInteraction.Ignore))
                {
                    string tag = hit.collider ? hit.collider.gameObject.tag : null;
                    if (!string.IsNullOrEmpty(tag) && materialEarlyReflection.ContainsKey(tag))
                        matBoost += (materialEarlyReflection[tag] - 1.0f) * 0.25f; // small weighted contribution
                }
            }
            tail01 = Mathf.Clamp01(tail01 * Mathf.Clamp(matBoost, 0.9f, 1.25f));
            tgtRev = Mathf.Lerp(cfg_minReverbLevel.Value, cfg_maxReverbLevel.Value, tail01);

            float echoTOF = Mathf.Clamp((lastAvgDist * 2f) / 343f, 0.01f, cfg_maxReflectionsDelay.Value);
            tgtRefDel = echoTOF;

            // clamps
            tgtDecay = Mathf.Clamp(tgtDecay, cfg_minDecay.Value, cfg_maxDecay.Value);
            tgtRoom = Mathf.Clamp(tgtRoom, cfg_minRoomMb.Value, cfg_maxRoomMb.Value);
            tgtRev = Mathf.Clamp(tgtRev, cfg_minReverbLevel.Value, cfg_maxReverbLevel.Value);
            tgtRefDel = Mathf.Clamp(tgtRefDel, 0.01f, cfg_maxReflectionsDelay.Value);

            Logger.LogInfo(string.Format(
                "[DynamicAudio] hits={0}/{1}, coverage={2:P0}, openSky={3:P0}, avgDist={4:F1}m, TOF={5}ms, enclosure={6:F2}",
                hitCount, dirs.Count, lastCoverage, lastOpenSky, lastAvgDist, Mathf.RoundToInt(tgtRefDel * 1000f), lastEnclosure));
        }

        private void BuildDirections(int n)
        {
            dirs.Clear();
            float phi = Mathf.PI * (3f - Mathf.Sqrt(5f)); // golden angle
            for (int i = 0; i < n; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / n; // -1..1
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float theta = i * phi;
                float x = Mathf.Cos(theta) * r;
                float z = Mathf.Sin(theta) * r;
                Vector3 v = new Vector3(x, y, z).normalized;
                dirs.Add(v);
            }
            // Extra equatorial samples
            dirs.Add(new Vector3(1, 0, 0));
            dirs.Add(new Vector3(-1, 0, 0));
            dirs.Add(new Vector3(0, 0, 1));
            dirs.Add(new Vector3(0, 0, -1));
        }

        // ---------------- Loudness / Tinnitus ----------------

        private void SampleLoudness()
        {
            if (loudBuf == null || loudBuf.Length == 0) return;

            AudioListener.GetOutputData(loudBuf, 0);
            float sumSq = 0f;
            float peak = 0f;
            for (int i = 0; i < loudBuf.Length; i++)
            {
                float s = loudBuf[i];
                sumSq += s * s;
                float a = Mathf.Abs(s);
                if (a > peak) peak = a;
            }
            lastRms = Mathf.Sqrt(sumSq / loudBuf.Length);
            lastPeak = peak;

            // Shock mute/muffle after explosion (instant trigger from very loud peaks)
            if (lastPeak >= cfg_explosionPeakThreshold.Value)
            {
                shockTimeLeft = cfg_shockMuteSeconds.Value + cfg_shockMuffleSeconds.Value;
                
                // Also trigger tinnitus from instant loud explosions if enabled
                // Now uses sensitivity instead of high threshold - much easier to trigger
                if (cfg_enableTinnitus.Value && lastPeak >= cfg_tinnitusSensitivity.Value && tinnitusTimeLeft <= 0f)
                {
                    tinnitusTimeLeft = cfg_tinnitusDuration.Value;
                    tinnitusCurrentVolume = cfg_tinnitusVolume.Value;
                    Logger.LogInfo($"[DynamicAudio] TINNITUS TRIGGERED by explosion! Peak: {lastPeak:F2}, Duration: {cfg_tinnitusDuration.Value:F1}s");
                }
            }
        }

        private void UpdateExposure()
        {
            float revStrength = Mathf.InverseLerp(cfg_minReverbLevel.Value, cfg_maxReverbLevel.Value, curRev);
            revStrength = Mathf.Clamp01(revStrength);

            float envWeight = 1f + cfg_enclosureGain.Value * lastEnclosure;
            float revWeight = 1f + revStrength * (cfg_reverbGain.Value * 1000f);

            float add = lastRms * cfg_exposureGain.Value * envWeight * revWeight;
            float recovery = cfg_exposureDecay.Value * (lastRms < cfg_exposureQuietRms.Value ? (1.0f + lastOpenSky) : 0.5f);

            noiseExposure += add * Time.unscaledDeltaTime;
            noiseExposure -= recovery * Time.unscaledDeltaTime;
            noiseExposure = Mathf.Clamp(noiseExposure, 0f, 100f); // cap exposure
            
            // Accumulate noise for tinnitus trigger (only if enabled)
            // This allows tinnitus to trigger from sustained loud noise like automatic weapon fire
            if (cfg_enableTinnitus.Value)
            {
                // Accumulate RMS over time (sustained noise buildup) - increased accumulation rate
                accumulatedNoiseForTinnitus += lastRms * Time.unscaledDeltaTime * 2.5f;
                
                // Very slow natural decay - noise accumulates much faster than it decays during sustained fire
                accumulatedNoiseForTinnitus = Mathf.Max(0f, accumulatedNoiseForTinnitus - (0.05f * Time.unscaledDeltaTime));
                
                // Trigger tinnitus if accumulated noise exceeds threshold
                // Threshold is now sensitivity * 1.5 (so default 0.15 * 1.5 = 0.225 accumulated RMS units)
                // This is VERY easily achievable with sustained automatic fire in enclosed spaces
                float tinnitusAccumulatedThreshold = cfg_tinnitusSensitivity.Value * 1.5f;
                
                if (accumulatedNoiseForTinnitus >= tinnitusAccumulatedThreshold && tinnitusTimeLeft <= 0f)
                {
                    float triggerDuration = cfg_tinnitusDuration.Value * (1f + Mathf.Min(1f, accumulatedNoiseForTinnitus / tinnitusAccumulatedThreshold));
                    tinnitusTimeLeft = triggerDuration;
                    tinnitusCurrentVolume = cfg_tinnitusVolume.Value;
                    float accumulatedValue = accumulatedNoiseForTinnitus;
                    accumulatedNoiseForTinnitus = 0f; // Reset after triggering
                    Logger.LogInfo($"[DynamicAudio] TINNITUS TRIGGERED by sustained gunfire! Accumulated: {accumulatedValue:F2} (threshold: {tinnitusAccumulatedThreshold:F2}), Duration: {triggerDuration:F1}s");
                }
            }
        }

        private void UpdateShockAudio(float dt)
        {
            // Shock handling - temporary mute and muffle after explosions
            if (shockTimeLeft > 0f)
            {
                float before = shockTimeLeft;
                shockTimeLeft -= dt;

                if (before > cfg_shockMuffleSeconds.Value)
                {
                    // Mute phase
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, 0.1f * originalListenerVolume, 12f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 500f, 10f * dt);
                }
                else
                {
                    // Muffle recovery phase
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.6f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 1800f, 2.5f * dt);
                }
            }
            else
            {
                // Full recovery (unless tinnitus is active)
                if (tinnitusTimeLeft <= 0f)
                {
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.8f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 22000f, 5000f * dt);
                }
            }
            
            // Tinnitus handling (only if enabled) - plays external audio file or fallback tone
            if (cfg_enableTinnitus.Value && tinnitusTimeLeft > 0f)
            {
                tinnitusTimeLeft -= dt;
                
                // Decay tinnitus volume over time (slower decay for more persistence)
                tinnitusCurrentVolume = Mathf.Lerp(tinnitusCurrentVolume, 0f, (1f - cfg_tinnitusDecay.Value) * dt);
                
                // Apply tinnitus effect: low-pass filtering to simulate muffled hearing
                float targetCutoff = 3000f * (1f + (tinnitusTimeLeft / cfg_tinnitusDuration.Value));
                lowpass.cutoffFrequency = Mathf.Min(lowpass.cutoffFrequency, targetCutoff);
                
                // Reduce overall volume based on tinnitus intensity (more noticeable attenuation)
                float tinnitusAttenuation = 1f - (tinnitusCurrentVolume * 0.7f);
                AudioListener.volume = Mathf.Min(AudioListener.volume, originalListenerVolume * tinnitusAttenuation);
                
                // Play the actual tinnitus sound (external file or fallback)
                if (tinnitusSource != null && tinnitusClip != null)
                {
                    // Start playing if not already
                    if (!tinnitusSource.isPlaying)
                    {
                        tinnitusSource.Play();
                        Logger.LogInfo($"[DynamicAudio] Tinnitus sound started, volume: {tinnitusCurrentVolume:F2}");
                    }
                    
                    // Adjust volume based on current intensity (amplified for better audibility)
                    tinnitusSource.volume = tinnitusCurrentVolume * cfg_tinnitusVolume.Value * 2f;
                }
            }
            else
            {
                // Stop tinnitus sound when effect ends
                if (tinnitusSource != null && tinnitusSource.isPlaying)
                {
                    tinnitusSource.Stop();
                    Logger.LogInfo("[DynamicAudio] Tinnitus sound stopped");
                }
            }
        }

        // ---------------- Per-source occlusion & Doppler ----------------

        private void ScanAudioSources()
        {
            trackedSources.Clear();
            AudioSource[] all = Object.FindObjectsOfType<AudioSource>();
            for (int i = 0; i < all.Length; i++)
            {
                AudioSource a = all[i];
                if (a == null) continue;
                if (a.spatialBlend < 0.1f) continue; // likely music/UI
                trackedSources.Add(a);

                // store original volume if not stored yet
                if (!originalSourceVolume.ContainsKey(a))
                    originalSourceVolume[a] = a.volume;

                // initialize prev position tracking
                if (!prevSourcePos.ContainsKey(a))
                    prevSourcePos[a] = a.transform.position;
                if (!sourceVel.ContainsKey(a))
                    sourceVel[a] = Vector3.zero;
                if (!flybyTimers.ContainsKey(a))
                    flybyTimers[a] = 0f;
            }
            Logger.LogInfo("[DynamicAudio] Scanned audio sources, tracked: " + trackedSources.Count);
        }

        private void CheckSourcesOcclusionAndDoppler()
        {
            if (trackedSources.Count == 0 || ear == null) return;

            int checkedCount = 0;
            // sort by distance to listener (prioritize closest loud sources)
            trackedSources.Sort((x, y) =>
            {
                if (!x || !y) return 0;
                float dx = Vector3.Distance(x.transform.position, ear.position);
                float dy = Vector3.Distance(y.transform.position, ear.position);
                return dx.CompareTo(dy);
            });

            for (int i = 0; i < trackedSources.Count && checkedCount < cfg_maxSourcesPerCheck.Value; i++)
            {
                AudioSource src = trackedSources[i];
                if (src == null) continue;

                // compute estimated source velocity
                Vector3 prevPos = prevSourcePos.ContainsKey(src) ? prevSourcePos[src] : src.transform.position;
                Vector3 currentPos = src.transform.position;
                Vector3 estVel = (currentPos - prevPos) / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                // smooth velocity to reduce jitter
                Vector3 smoothed = Vector3.Lerp(sourceVel.ContainsKey(src) ? sourceVel[src] : Vector3.zero, estVel, Time.unscaledDeltaTime * cfg_velocitySmoothing.Value);
                sourceVel[src] = smoothed;
                prevSourcePos[src] = currentPos;

                float d = Vector3.Distance(currentPos, ear.position);
                if (d > cfg_maxOcclusionDistance.Value) // out of range
                {
                    RestoreSourceVolume(src);
                    // still update doppler to keep smoothing consistent
                    ApplyDopplerToSource(src, smoothed, listenerVel, 1.0f, 0f);
                    continue;
                }

                // skip not playing sources
                if (!src.isPlaying) { RestoreSourceVolume(src); ApplyDopplerToSource(src, smoothed, listenerVel, 1.0f, 0f); continue; }

                // Quick same-room heuristic
                float srcEnclosure = QuickLocalEnclosure(src.transform.position);
                bool sameRoom = Mathf.Abs(srcEnclosure - lastEnclosure) < 0.22f && Mathf.Abs(Vector3.Distance(src.transform.position, ear.position) - lastAvgDist) < (lastAvgDist * 0.6f + 1f);

                // Direct LOS test: if there is clear line, no occlusion
                Vector3 dir = (ear.position - src.transform.position).normalized;
                RaycastHit hitInfo;
                bool hasObstacle = Physics.Raycast(src.transform.position + dir * 0.02f, dir, out hitInfo, d - 0.04f, ~0, QueryTriggerInteraction.Ignore);

                if (!hasObstacle || sameRoom)
                {
                    // no blocking geometry or same room -> restore volume
                    RestoreSourceVolume(src);
                }
                else
                {
                    // There are hits between source and listener: compute an occlusion factor based on number of hits and rough collider sizes
                    RaycastHit[] hits = Physics.RaycastAll(src.transform.position + dir * 0.02f, dir, d - 0.04f, ~0, QueryTriggerInteraction.Ignore);
                    float sumApprox = 0f;
                    for (int h = 0; h < hits.Length; h++)
                    {
                        RaycastHit hit = hits[h];
                        Collider col = hit.collider;
                        if (col == null) continue;
                        Vector3 absN = new Vector3(Mathf.Abs(hit.normal.x), Mathf.Abs(hit.normal.y), Mathf.Abs(hit.normal.z));
                        Vector3 ext = col.bounds.extents;
                        float approx = 2f * (absN.x * ext.x + absN.y * ext.y + absN.z * ext.z);
                        sumApprox += Mathf.Clamp(approx, 0.01f, 5f); // cap approx size to 5m for normalization
                    }

                    // Normalize occlusion 0..1 (heuristic)
                    float occ = Mathf.Clamp01(sumApprox / 5f); // sumApprox 0..5 -> occ 0..1
                    float volMul = Mathf.Lerp(1f, cfg_sourceOcclusionMinVolume.Value, occ);

                    // Apply volume relative to stored original volume (don't permanently overwrite)
                    float orig = 1f;
                    if (originalSourceVolume.ContainsKey(src)) orig = originalSourceVolume[src];
                    float targetVol = orig * volMul;

                    src.volume = Mathf.MoveTowards(src.volume, targetVol, 1.2f * Time.unscaledDeltaTime);
                }

                // Apply Doppler / flyby / air absorption / sound delay
                ApplyDopplerToSource(src, sourceVel[src], listenerVel, d, Time.unscaledDeltaTime);

                checkedCount++;
            }
        }

        private void RestoreSourceVolume(AudioSource src)
        {
            if (src == null) return;
            float orig = 1f;
            if (originalSourceVolume.ContainsKey(src)) orig = originalSourceVolume[src];
            src.volume = Mathf.MoveTowards(src.volume, orig, 1.0f * Time.unscaledDeltaTime);
        }

        // Doppler & Flyby: compute pitch shift and temporary flyby boost
        // Also handles air absorption and optional sound delay simulation - ROBUST IMPLEMENTATION
        private void ApplyDopplerToSource(AudioSource src, Vector3 srcVelocity, Vector3 listenerVelocityLocal, float distanceToListener, float dt)
        {
            if (src == null) return;

            // relative velocity along the LOS
            Vector3 LOS = (ear.position - src.transform.position).normalized;
            float vSourceAlong = Vector3.Dot(srcVelocity, LOS); // positive -> moving towards listener
            float vListenerAlong = Vector3.Dot(listenerVelocityLocal, LOS); // positive -> listener moving towards source
            // physical doppler ratio (approx): (c + v_listener) / (c - v_source)
            float c = cfg_soundSpeed.Value;
            float rawRatio = (c + vListenerAlong) / Mathf.Max(0.0001f, (c - vSourceAlong));
            float ratio = Mathf.Clamp(rawRatio, cfg_dopplerMinPitch.Value, cfg_dopplerMaxPitch.Value);
            // apply strength and clamp
            float pitchTarget = Mathf.Clamp(Mathf.Pow(ratio, cfg_dopplerStrength.Value), cfg_dopplerMinPitch.Value, cfg_dopplerMaxPitch.Value);

            // Flyby detection: quick lateral speed near listener
            // compute lateral velocity (component perpendicular to LOS)
            Vector3 relVel = srcVelocity - listenerVelocityLocal;
            Vector3 lateral = relVel - Vector3.Dot(relVel, LOS) * LOS;
            float lateralSpeed = lateral.magnitude;

            // compute closest approach estimate: if source moving fast and will sweep by close -> trigger flyby
            float currentClosest = distanceToListener;
            bool isFlyby = lateralSpeed >= cfg_flybyVelocityThreshold.Value && currentClosest <= Mathf.Max(cfg_flybyMinDistance.Value, distanceToListener * 0.75f);

            // manage per-source flyby timer (for smoothing)
            float timer = flybyTimers.ContainsKey(src) ? flybyTimers[src] : 0f;
            if (isFlyby)
            {
                timer = cfg_flybyDecaySeconds.Value; // reset timer when close fast flyby detected
            }
            else
            {
                timer = Mathf.Max(0f, timer - dt);
            }
            flybyTimers[src] = timer;

            // compute final pitch & volume multipliers
            float flybyFactor = Mathf.Clamp01(timer / cfg_flybyDecaySeconds.Value); // 0..1
            float pitchFinal = pitchTarget * (1f + (cfg_flybyPitchBoost.Value - 1f) * flybyFactor);
            float volFinalMul = 1f + (cfg_flybyVolumeBoost.Value - 1f) * flybyFactor;

            // Air absorption: high frequencies absorbed over distance - ENHANCED
            if (cfg_enableAirAbsorption.Value)
            {
                // Absorption depends on distance, humidity, and temperature
                float humidityEffect = Mathf.Lerp(1.5f, 0.5f, cfg_humidityFactor.Value); // dry air absorbs more
                float tempEffect = Mathf.Lerp(1.2f, 0.8f, Mathf.InverseLerp(-10f, 40f, cfg_temperatureFactor.Value));
                float absorptionRate = cfg_airAbsorptionRate.Value * humidityEffect * tempEffect;
                float highFreqLoss = Mathf.Exp(-absorptionRate * distanceToListener);
                
                // Apply lowpass filter to simulate air absorption - more aggressive for distant sounds
                float cutoffBase = 22000f;
                float cutoffTarget = Mathf.Lerp(4000f, cutoffBase, highFreqLoss); // Lower minimum for more effect
                
                // Get or create audio filter component for this source
                AudioLowPassFilter srcLowpass = src.GetComponent<AudioLowPassFilter>();
                if (srcLowpass == null) srcLowpass = src.gameObject.AddComponent<AudioLowPassFilter>();
                srcLowpass.enabled = true;
                srcLowpass.cutoffFrequency = Mathf.MoveTowards(srcLowpass.cutoffFrequency, cutoffTarget, 5000f * dt);
            }

            // Sound delay simulation: light travels instantly, sound takes time - ROBUST IMPLEMENTATION
            // This now properly tracks when sounds START playing and delays them accordingly
            bool shouldApplyDelay = cfg_enableSoundDelay.Value && 
                                    distanceToListener > cfg_lightSpeedThreshold.Value && 
                                    distanceToListener > cfg_soundDelayMinDistance.Value;
            
            if (shouldApplyDelay)
            {
                // Calculate sound travel time based on distance and configured sound speed
                float soundTravelTime = distanceToListener / cfg_soundSpeed.Value;
                soundTravelTime = Mathf.Min(soundTravelTime, cfg_maxSoundDelay.Value);
                
                // Initialize tracking for this source if needed
                if (!soundDelayTimers.ContainsKey(src)) 
                { 
                    soundDelayTimers[src] = 0f; 
                    soundPlayed[src] = true; 
                }
                
                // Detect if source just started playing (was not playing before, or finished previous playback)
                bool wasPlaying = soundPlayed.ContainsKey(src) && src.isPlaying;
                bool isNewSound = !wasPlaying && src.isPlaying;
                
                // If this is a new sound event, start the delay timer
                if (isNewSound || soundDelayTimers[src] <= 0f)
                {
                    soundDelayTimers[src] = soundTravelTime;
                    soundPlayed[src] = false; // Mark as not yet played
                    
                    if (cfg_debugMode.Value == "all" || cfg_debugMode.Value == "distances")
                    {
                        Logger.LogInfo($"[DynamicAudio] SOUND DELAY: {src.gameObject.name} at {distanceToListener:F1}m, delay={soundTravelTime:F2}s (speed={cfg_soundSpeed.Value} m/s)");
                    }
                }
                
                // Count down the delay timer
                if (soundDelayTimers[src] > 0f)
                {
                    soundDelayTimers[src] -= dt;
                    // Keep volume at 0 while waiting for sound to arrive
                    // But store the desired volume for when delay expires
                }
                else
                {
                    // Delay expired - sound can now play
                    soundPlayed[src] = true;
                }
            }
            else
            {
                // Reset delay tracking if disabled or too close - allow immediate playback
                if (soundDelayTimers.ContainsKey(src)) soundDelayTimers[src] = 0f;
                if (soundPlayed.ContainsKey(src)) soundPlayed[src] = true;
            }

            // Apply pitch and small smoothing (don't stomp other mods that change pitch)
            src.pitch = Mathf.MoveTowards(src.pitch, pitchFinal, 3f * Time.unscaledDeltaTime);

            // Apply volume boost multiplicatively but relative to stored original
            float origVol = 1f;
            if (originalSourceVolume.ContainsKey(src)) origVol = originalSourceVolume[src];
            float desiredVol = Mathf.Clamp(origVol * volFinalMul, 0f, 2f);
            
            // NEW: Apply wall occlusion factor (v4.0.0) - ENHANCED
            float wallFactor = GetWallOcclusionFactor(src);
            desiredVol *= wallFactor;
            
            // Apply environmental effects if enabled
            if (cfg_enableWeatherEffects.Value)
            {
                // Wind can slightly modulate volume and add randomness
                float windModulation = 1f + (Mathf.Sin(Time.time * 2f) * cfg_windEffect.Value * 0.1f);
                desiredVol *= windModulation;
                
                // Ground reflection boost for sounds coming from below
                if (LOS.y < -0.3f)
                {
                    desiredVol *= cfg_groundReflectionBoost.Value;
                }
            }

            // Only apply volume if not in sound delay mute phase
            bool isInDelayPhase = cfg_enableSoundDelay.Value && soundDelayTimers.ContainsKey(src) && soundDelayTimers[src] > 0f;
            
            if (!isInDelayPhase)
            {
                src.volume = Mathf.MoveTowards(src.volume, desiredVol, 2.5f * Time.unscaledDeltaTime);
            }
            else
            {
                // Still in delay phase - keep muted but smoothly transition to desired volume for when delay ends
                src.volume = Mathf.MoveTowards(src.volume, 0f, 10f * Time.unscaledDeltaTime);
            }
        }

        // Quick local enclosure probe around a position (cheap: uses prebuilt dirs & short range)
        private float QuickLocalEnclosure(Vector3 pos)
        {
            int checks = Mathf.Min(18, dirs.Count);
            int hits = 0;
            float range = 6f;
            for (int i = 0; i < checks; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(pos, d, out hit, range, ~0, QueryTriggerInteraction.Ignore))
                    hits++;
            }
            return (float)hits / (float)checks; // 0..1
        }

        // ---------------- NEW: Distance Calculator & Wall Occlusion (v4.0.0) ----------------

        /// <summary>
        /// Calculate exact distance to all playing audio cues for accurate light vs sound simulation.
        /// This ensures that sounds from far away sources have proper delay compared to visual effects.
        /// </summary>
        private void UpdateAudioCueDistances()
        {
            audioCueDistances.Clear();
            
            AudioSource[] allSources = Object.FindObjectsOfType<AudioSource>();
            int trackedCount = 0;
            
            foreach (AudioSource src in allSources)
            {
                if (src == null || !src.isPlaying || src.spatialBlend < 0.1f) continue;
                if (trackedCount >= cfg_maxTrackedCues.Value) break;
                
                float distance = Vector3.Distance(src.transform.position, ear.position);
                audioCueDistances[src] = distance;
                trackedCount++;
                
                // Debug output
                if (cfg_debugMode.Value == "distances" || cfg_debugMode.Value == "all")
                {
                    Logger.LogInfo(string.Format("[DynamicAudio] Audio Cue: {0} @ {1:F1}m", src.gameObject.name, distance));
                }
            }
            
            if (cfg_debugMode.Value == "distances" || cfg_debugMode.Value == "all")
            {
                Logger.LogInfo(string.Format("[DynamicAudio] Tracking {0} audio cues", trackedCount));
            }
        }

        /// <summary>
        /// Detect walls between player and sound sources using raycast from player to surroundings.
        /// Sounds behind walls will be muffled/attenuated. - ENHANCED IMPLEMENTATION
        /// </summary>
        private void DetectWallOcclusions()
        {
            wallOcclusionFactors.Clear();
            
            // Cast rays from player position in all directions to detect surrounding walls
            int wallHits = 0;
            List<Vector3> wallDirections = new List<Vector3>();
            List<RaycastHit> wallHitsInfo = new List<RaycastHit>();
            
            // Use the configured layer mask for occlusion checks
            int layerMask = cfg_occlusionLayerMask.Value;
            
            for (int i = 0; i < cfg_wallRays.Value; i++)
            {
                // Generate evenly distributed rays around the player in 3D sphere pattern
                float angleH = (i / (float)cfg_wallRays.Value) * Mathf.PI * 2f;
                float angleV = Mathf.Sin(angleH * 3f) * 0.5f; // Add vertical variation
                
                Vector3 dir = new Vector3(
                    Mathf.Cos(angleH) * Mathf.Cos(angleV),
                    Mathf.Sin(angleV),
                    Mathf.Sin(angleH) * Mathf.Cos(angleV)
                ).normalized;
                
                RaycastHit hit;
                if (Physics.Raycast(ear.position, dir, out hit, cfg_wallRayDistance.Value, layerMask, QueryTriggerInteraction.Ignore))
                {
                    wallHits++;
                    wallDirections.Add(dir);
                    wallHitsInfo.Add(hit);
                }
            }
            
            float wallCoverage = wallHits / (float)cfg_wallRays.Value;
            
            // Now check each tracked audio source AND all playing audio sources to see if they're behind a wall
            HashSet<AudioSource> allSourcesToCheck = new HashSet<AudioSource>(trackedSources);
            
            // Also add any playing AudioSource we can find
            AudioSource[] allAudioSources = Object.FindObjectsOfType<AudioSource>();
            foreach (AudioSource src in allAudioSources)
            {
                if (src != null && src.isPlaying && src.spatialBlend > 0.1f)
                {
                    allSourcesToCheck.Add(src);
                }
            }
            
            // Check each source for wall occlusion
            foreach (AudioSource src in allSourcesToCheck)
            {
                if (src == null || !src.isPlaying) continue;
                
                Vector3 srcDir = (src.transform.position - ear.position).normalized;
                float srcDistance = Vector3.Distance(src.transform.position, ear.position);
                
                // Skip if source is too close (no point muffling very close sounds)
                if (srcDistance < 2f)
                {
                    wallOcclusionFactors[src] = 1.0f;
                    continue;
                }
                
                // Check if there's a wall between player and source using multiple rays for accuracy
                bool hasWallBetween = false;
                RaycastHit wallHit;
                
                // Primary ray: direct line to source
                if (Physics.Raycast(ear.position, srcDir, out wallHit, srcDistance - 0.5f, layerMask, QueryTriggerInteraction.Ignore))
                {
                    hasWallBetween = true;
                }
                else
                {
                    // Secondary check: use 3 offset rays for more robust detection (handles thin walls)
                    Vector3 perpendicular = Vector3.Cross(srcDir, Vector3.up).normalized;
                    if (perpendicular.magnitude < 0.01f) perpendicular = Vector3.Cross(srcDir, Vector3.right).normalized;
                    
                    float offset = 0.5f;
                    for (int r = -1; r <= 1; r += 2)
                    {
                        Vector3 offsetDir = (srcDir + perpendicular * offset * r).normalized;
                        if (Physics.Raycast(ear.position, offsetDir, out wallHit, srcDistance - 0.5f, layerMask, QueryTriggerInteraction.Ignore))
                        {
                            hasWallBetween = true;
                            break;
                        }
                    }
                }
                
                if (hasWallBetween)
                {
                    // There's a wall blocking direct path - apply strong muffle
                    // More muffling for thicker/denser obstacles
                    wallOcclusionFactors[src] = cfg_wallMuffleAmount.Value;
                    
                    if (cfg_debugMode.Value == "walls" || cfg_debugMode.Value == "all")
                    {
                        Logger.LogInfo(string.Format("[DynamicAudio] WALL OCCLUSION: {0} blocked by {1} at {2:F1}m (distance to source: {3:F1}m)", 
                            src.gameObject.name, wallHit.collider.gameObject.name, wallHit.distance, srcDistance));
                    }
                }
                else
                {
                    // No wall - full volume
                    wallOcclusionFactors[src] = 1.0f;
                }
            }
            
            if (cfg_debugMode.Value == "walls" || cfg_debugMode.Value == "all")
            {
                Logger.LogInfo(string.Format("[DynamicAudio] Wall detection: {0}/{1} rays hit, coverage: {2:P0}, sources checked: {3}", 
                    wallHits, cfg_wallRays.Value, wallCoverage, allSourcesToCheck.Count));
            }
        }

        /// <summary>
        /// Get the wall occlusion factor for a specific audio source.
        /// Returns 1.0f if no occlusion, or a lower value if blocked by wall.
        /// </summary>
        private float GetWallOcclusionFactor(AudioSource src)
        {
            if (!cfg_enableWallOcclusion.Value) return 1.0f;
            if (wallOcclusionFactors.ContainsKey(src))
                return wallOcclusionFactors[src];
            return 1.0f;
        }
    }
}
