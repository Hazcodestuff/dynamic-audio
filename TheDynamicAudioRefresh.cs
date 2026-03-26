// TheDynamicAudioRefresh.cs
// BepInEx plugin for Ravenfield — Stable distance-based reverb system
// Version: 1.0.0 - "The Dynamic Audio Refresh"
// Features: Distance calculation to walls, dynamic reverb in enclosed spaces, sound delay simulation
// C# 7.3 compatible.

using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;

namespace Ravenfield.DynamicAudio
{
    [BepInPlugin("dynamic.audio.refresh", "The Dynamic Audio Refresh", "1.0.0")]
    public class DynamicAudioRefreshPlugin : BaseUnityPlugin
    {
        // ---------------- Configuration ----------------
        private ConfigEntry<float> cfg_probeInterval;
        private ConfigEntry<int> cfg_probeRays;
        private ConfigEntry<float> cfg_maxProbeDistance;
        
        // Reverb settings
        private ConfigEntry<float> cfg_minDecay;
        private ConfigEntry<float> cfg_maxDecay;
        private ConfigEntry<float> cfg_minReverbLevel;
        private ConfigEntry<float> cfg_maxReverbLevel;
        private ConfigEntry<float> cfg_minRoom;
        private ConfigEntry<float> cfg_maxRoom;
        
        // Sound delay simulation
        private ConfigEntry<bool> cfg_enableSoundDelay;
        private ConfigEntry<float> cfg_soundSpeed;
        private ConfigEntry<float> cfg_maxSoundDelay;
        
        // Smoothing
        private ConfigEntry<float> cfg_smoothingSpeed;
        
        // Debug
        private ConfigEntry<bool> cfg_enableDebug;
        
        // ---------------- State ----------------
        private AudioListener listener;
        private Transform ear;
        private AudioReverbFilter reverb;
        
        private float targetDecay, currentDecay;
        private float targetRoom, currentRoom;
        private float targetReverbLevel, currentReverbLevel;
        private float targetReflectionsDelay, currentReflectionsDelay;
        
        private float lastProbeTime;
        private List<Vector3> probeDirections = new List<Vector3>();
        
        // Last probe metrics
        private float lastEnclosureFactor;
        private float lastAverageDistance;
        private float lastHitRatio;
        
        // Sound delay state
        private readonly Dictionary<AudioSource, float> soundDelayTimers = new Dictionary<AudioSource, float>();
        private readonly Dictionary<AudioSource, float> sourceDistances = new Dictionary<AudioSource, float>();
        private float lastDistanceCheckTime;
        
        private void Awake()
        {
            Logger.LogInfo("[DynamicAudioRefresh] Initializing The Dynamic Audio Refresh v1.0.0");
            
            SetupConfig();
            SceneManager.sceneLoaded += OnSceneLoaded;
            BuildProbeDirections(cfg_probeRays.Value);
        }
        
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        // ---------------- Configuration Setup ----------------
        private void SetupConfig()
        {
            // General
            cfg_probeInterval = Config.Bind("General", "Probe Interval", 0.1f, "How often to probe the environment (seconds). Lower = more responsive but higher CPU usage.");
            cfg_probeRays = Config.Bind("General", "Probe Rays", 32, "Number of rays to cast for environment probing. Higher = more accurate but higher CPU usage.");
            cfg_maxProbeDistance = Config.Bind("General", "Max Probe Distance", 50f, "Maximum distance for environment probing (meters).");
            
            // Reverb
            cfg_minDecay = Config.Bind("Reverb", "Min Decay Time", 0.5f, "Minimum reverb decay time for open areas (seconds).");
            cfg_maxDecay = Config.Bind("Reverb", "Max Decay Time", 4.0f, "Maximum reverb decay time for enclosed spaces (seconds).");
            cfg_minReverbLevel = Config.Bind("Reverb", "Min Reverb Level", -1800f, "Minimum reverb level (dB) for open areas.");
            cfg_maxReverbLevel = Config.Bind("Reverb", "Max Reverb Level", -100f, "Maximum reverb level (dB) for enclosed spaces.");
            cfg_minRoom = Config.Bind("Reverb", "Min Room Size", -2000f, "Minimum room size (mB) for open areas.");
            cfg_maxRoom = Config.Bind("Reverb", "Max Room Size", 0f, "Maximum room size (mB) for enclosed spaces.");
            
            // Sound Delay
            cfg_enableSoundDelay = Config.Bind("SoundDelay", "Enable Sound Delay", true, "Simulate light traveling faster than sound. Distant explosions will be seen before heard.");
            cfg_soundSpeed = Config.Bind("SoundDelay", "Sound Speed", 343f, "Speed of sound in m/s. Use 343 for realism, lower values for dramatic effect.");
            cfg_maxSoundDelay = Config.Bind("SoundDelay", "Max Delay", 3.0f, "Maximum sound delay (seconds) to prevent excessive delays.");
            
            // Smoothing
            cfg_smoothingSpeed = Config.Bind("Smoothing", "Smoothing Speed", 5.0f, "How quickly audio parameters interpolate (higher = snappier response).");
            
            // Debug
            cfg_enableDebug = Config.Bind("Debug", "Enable Debug Logging", false, "Log detailed information about audio processing to the console.");
        }
        
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("[DynamicAudioRefresh] Scene loaded - reinitializing");
            Initialize();
        }
        
        private void Start()
        {
            Initialize();
        }
        
        private void Initialize()
        {
            listener = FindObjectOfType<AudioListener>();
            ear = listener ? listener.transform : null;
            
            if (!listener || !ear)
            {
                Logger.LogWarning("[DynamicAudioRefresh] No AudioListener found yet, will retry...");
                return;
            }
            
            // Get or add reverb filter
            reverb = listener.GetComponent<AudioReverbFilter>();
            if (!reverb)
            {
                reverb = listener.gameObject.AddComponent<AudioReverbFilter>();
            }
            
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;
            reverb.diffusion = 100f;
            reverb.density = 100f;
            
            // Initialize parameters
            currentDecay = targetDecay = cfg_minDecay.Value;
            currentRoom = targetRoom = cfg_minRoom.Value;
            currentReverbLevel = targetReverbLevel = cfg_minReverbLevel.Value;
            currentReflectionsDelay = targetReflectionsDelay = 0.03f;
            
            ApplyReverb();
            
            Logger.LogInfo("[DynamicAudioRefresh] Ready - Dynamic reverb based on enclosure enabled");
        }
        
        private void Update()
        {
            if (!listener || !ear || !reverb)
            {
                if (Time.frameCount % 60 == 0) Initialize();
                return;
            }
            
            // Environment probing
            if (Time.unscaledTime - lastProbeTime >= cfg_probeInterval.Value)
            {
                lastProbeTime = Time.unscaledTime;
                ProbeEnvironment(ear.position);
            }
            
            // Sound delay simulation
            if (cfg_enableSoundDelay.Value && Time.unscaledTime - lastDistanceCheckTime >= 0.05f)
            {
                lastDistanceCheckTime = Time.unscaledTime;
                UpdateSoundDelays();
            }
            
            // Smooth parameter transitions
            float deltaTime = Time.unscaledDeltaTime;
            float smoothFactor = cfg_smoothingSpeed.Value * deltaTime;
            
            currentDecay = Mathf.MoveTowards(currentDecay, targetDecay, smoothFactor);
            currentRoom = Mathf.MoveTowards(currentRoom, targetRoom, smoothFactor * 1500f);
            currentReverbLevel = Mathf.MoveTowards(currentReverbLevel, targetReverbLevel, smoothFactor * 1200f);
            currentReflectionsDelay = Mathf.MoveTowards(currentReflectionsDelay, targetReflectionsDelay, smoothFactor);
            
            ApplyReverb();
            
            // Debug logging
            if (cfg_enableDebug.Value && Time.frameCount % 30 == 0)
            {
                Logger.LogInfo($"[DynamicAudioRefresh] Enclosure={lastEnclosureFactor:F2} AvgDist={lastAverageDistance:F1}m Decay={currentDecay:F2}s Room={currentRoom:F0}mB Reverb={currentReverbLevel:F0}dB");
            }
        }
        
        // ---------------- Core Functions ----------------
        
        private void BuildProbeDirections(int rayCount)
        {
            probeDirections.Clear();
            
            // Fibonacci sphere algorithm for even distribution
            float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
            
            for (int i = 0; i < rayCount; i++)
            {
                float theta = 2f * Mathf.PI * i / goldenRatio;
                float phi = Mathf.Acos(1f - 2f * (i + 0.5f) / rayCount);
                
                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Sin(phi) * Mathf.Sin(theta);
                float z = Mathf.Cos(phi);
                
                probeDirections.Add(new Vector3(x, y, z));
            }
        }
        
        private void ProbeEnvironment(Vector3 origin)
        {
            if (probeDirections.Count != cfg_probeRays.Value)
            {
                BuildProbeDirections(cfg_probeRays.Value);
            }
            
            int hitCount = 0;
            float totalDistance = 0f;
            float nearestHit = cfg_maxProbeDistance.Value;
            
            foreach (Vector3 direction in probeDirections)
            {
                RaycastHit hit;
                if (Physics.Raycast(origin, direction, out hit, cfg_maxProbeDistance.Value, ~0, QueryTriggerInteraction.Ignore))
                {
                    hitCount++;
                    totalDistance += hit.distance;
                    
                    if (hit.distance < nearestHit)
                    {
                        nearestHit = hit.distance;
                    }
                }
            }
            
            // Calculate metrics
            lastHitRatio = hitCount / (float)cfg_probeRays.Value;
            lastAverageDistance = hitCount > 0 ? totalDistance / hitCount : cfg_maxProbeDistance.Value;
            
            // Calculate enclosure factor (0 = open, 1 = fully enclosed)
            // Based on hit ratio and average distance
            float distanceFactor = 1f - Mathf.Clamp01(lastAverageDistance / cfg_maxProbeDistance.Value);
            lastEnclosureFactor = Mathf.Clamp01((lastHitRatio * 0.6f + distanceFactor * 0.4f));
            
            // Map enclosure to reverb parameters
            CalculateReverbParameters(lastEnclosureFactor, lastAverageDistance);
        }
        
        private void CalculateReverbParameters(float enclosure, float avgDistance)
        {
            // Decay time: longer in enclosed spaces
            targetDecay = Mathf.Lerp(cfg_minDecay.Value, cfg_maxDecay.Value, enclosure);
            
            // Room size: larger in enclosed spaces
            targetRoom = Mathf.Lerp(cfg_minRoom.Value, cfg_maxRoom.Value, enclosure);
            
            // Reverb level: stronger in enclosed spaces
            targetReverbLevel = Mathf.Lerp(cfg_minReverbLevel.Value, cfg_maxReverbLevel.Value, enclosure);
            
            // Reflections delay: shorter in smaller/enclosed spaces
            float roomSizeFactor = Mathf.Clamp01(avgDistance / cfg_maxProbeDistance.Value);
            targetReflectionsDelay = Mathf.Lerp(0.4f, 0.03f, roomSizeFactor);
        }
        
        private void ApplyReverb()
        {
            reverb.decayTime = currentDecay;
            reverb.room = Mathf.RoundToInt(currentRoom);
            reverb.reverbLevel = currentReverbLevel;
            reverb.reflectionsDelay = currentReflectionsDelay;
            reverb.reverbDelay = Mathf.Clamp(currentReflectionsDelay * 1.5f, 0.01f, 0.4f);
        }
        
        // ---------------- Sound Delay Simulation ----------------
        
        private void UpdateSoundDelays()
        {
            // Find all playing audio sources
            AudioSource[] allSources = FindObjectsOfType<AudioSource>();
            
            foreach (AudioSource source in allSources)
            {
                if (!source.isPlaying || source.clip == null) continue;
                
                // Calculate distance to source
                float distance = Vector3.Distance(ear.position, source.transform.position);
                sourceDistances[source] = distance;
                
                // Skip if too close (no noticeable delay)
                if (distance < 5f) continue;
                
                // Calculate delay based on distance and sound speed
                float delay = distance / cfg_soundSpeed.Value;
                delay = Mathf.Min(delay, cfg_maxSoundDelay.Value);
                
                // Handle delay timing
                if (!soundDelayTimers.ContainsKey(source))
                {
                    soundDelayTimers[source] = delay;
                }
                else
                {
                    soundDelayTimers[source] -= Time.unscaledDeltaTime;
                    
                    // Mute until delay expires
                    if (soundDelayTimers[source] > 0f)
                    {
                        source.volume = 0f;
                    }
                    else
                    {
                        source.volume = 1f;
                    }
                }
            }
            
            // Clean up finished timers
            var keysToRemove = new List<AudioSource>();
            foreach (var kvp in soundDelayTimers)
            {
                if (!kvp.Key.isPlaying)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (AudioSource key in keysToRemove)
            {
                soundDelayTimers.Remove(key);
                sourceDistances.Remove(key);
            }
        }
    }
}
