// EchoProbe.cs
// BepInEx plugin for Ravenfield — TOF reverb + tinnitus + per-source occlusion (volume-only) + Doppler/flyby + material early-reflection boost
// Version: 1.5.1-no-muffle
// C# 7.3 compatible.

using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace Ravenfield.EchoProbe
{
    [BepInPlugin("zim.echo.probe", "Echo Probe (Advanced Audio Engine)", "2.0")]
    public class EchoProbePlugin : BaseUnityPlugin
    {
        // ---------------- Advanced Tunables ----------------
        public float probeInterval = 0.08f;        // Faster updates for better responsiveness
        public int rays = 64;                      // More rays for better accuracy
        public float maxProbeDistance = 80f;       // Extended range for larger environments
        public float upCone = 0.6f;                // y >= this → treated as "upward" for open-sky test
        public LayerMask hitMask = ~0;

        // Advanced Reverb System
        public float minDecay = 0.2f;              // seconds
        public float maxDecay = 8.0f;              // seconds - extended for large spaces
        public float minReverbLevel = -1800f;      // dB
        public float maxReverbLevel = -20f;        // dB - more prominent reverb
        public float minRoomMb = -2000f;           // mB
        public float maxRoomMb = 100f;             // mB - extended range
        public float maxReflectionsDelay = 0.6f;   // seconds - longer for bigger spaces

        // Per-source occlusion with frequency filtering
        public float maxOcclusionDistance = 100f;  // Extended range
        public int maxSourcesPerCheck = 40;        // More sources for better immersion
        public float sourceOcclusionMinVolume = 0.1f; // More realistic minimum
        public LayerMask occlusionMask = ~0;

        // Advanced Parameter smoothing
        public float paramLerp = 5.0f;             // Smoother transitions

        // Advanced Loudness / Hearing Damage System
        public float loudnessSampleInterval = 0.02f; // More frequent sampling
        public int loudnessSamples = 2048;         // Larger buffer for accuracy
        public float exposureGain = 18f;           // Balanced exposure
        public float enclosureGain = 2.5f;         // Enhanced enclosure effect
        public float reverbGain = 0.0035f;         // More responsive to reverb
        public float exposureDecay = 4.0f;         // More realistic decay
        public float exposureQuietRms = 0.05f;     // Lower quiet threshold
        public float tinnitusThreshold = 10f;      // Lower threshold for realism
        public float tinnitusMax = 50f;            // Higher max for dramatic effect
        public float tinnitusMinSeconds = 2f;      // Minimum duration
        public float tinnitusMaxSeconds = 15f;     // Maximum duration

        // Advanced Shock System
        public float explosionPeakThreshold = 0.7f; // Lower threshold for more responsiveness
        public float shockMuteSeconds = 0.15f;
        public float shockMuffleSeconds = 3.5f;

        // Advanced Doppler & Audio Positioning
        public float dopplerStrength = 0.8f;       // Realistic but noticeable
        public float dopplerMaxPitch = 1.5f;       // Extended range
        public float dopplerMinPitch = 0.6f;
        public float velocitySmoothing = 8.0f;     // Smoother velocity tracking

        // Advanced Flyby detection
        public float flybyVelocityThreshold = 20f;
        public float flybyMinDistance = 3.0f;
        public float flybyPitchBoost = 1.3f;       // More pronounced effect
        public float flybyVolumeBoost = 1.5f;
        public float flybyDecaySeconds = 0.5f;

        // Material properties class for advanced audio simulation
        private class MaterialAudioProperties
        {
            public float reflectionBoost;    // How much the material reflects sound
            public float absorptionFactor;  // How much the material absorbs sound
            public float frequencyCutoff;   // Frequency characteristics of the material
            
            public MaterialAudioProperties(float reflection, float absorption, float frequency)
            {
                reflectionBoost = reflection;
                absorptionFactor = absorption;
                frequencyCutoff = frequency;
            }
        }

        // Enhanced Material responses with more detailed properties
        private readonly Dictionary<string, MaterialAudioProperties> materialProperties = new Dictionary<string, MaterialAudioProperties>()
        {
            { "Material_Concrete", new MaterialAudioProperties(1.15f, 0.8f, 1200f) },
            { "Material_Metal", new MaterialAudioProperties(1.25f, 0.7f, 3000f) },
            { "Material_Wood", new MaterialAudioProperties(1.05f, 0.9f, 2000f) },
            { "Material_Glass", new MaterialAudioProperties(1.02f, 0.95f, 4000f) },
            { "Material_Carpet", new MaterialAudioProperties(0.7f, 0.4f, 800f) },
            { "Material_Rock", new MaterialAudioProperties(1.2f, 0.75f, 1500f) },
            { "Material_Earth", new MaterialAudioProperties(0.8f, 0.5f, 600f) },
            { "Material_Water", new MaterialAudioProperties(0.9f, 0.85f, 1000f) },
            { "Material_Fabric", new MaterialAudioProperties(0.75f, 0.45f, 700f) },
            { "Material_Default", new MaterialAudioProperties(1.0f, 0.85f, 2200f) }
        };

        // HRTF and spatial audio enhancement
        public bool enableHRTF = true;             // Head-related transfer function
        public float hrtfDistance = 15f;           // Distance threshold for HRTF
        public float spatialBlendDistance = 30f;   // Distance for spatial blend adjustment

        // Environmental audio effects
        public bool enableWindSimulation = true;   // Wind ambient sound
        public float windBaseVolume = 0.05f;
        public float windVariation = 0.03f;
        public bool enableAmbientReverb = true;    // Environmental ambient reverb
        
        // Distance-based attenuation
        public float distanceAttenuationNear = 1.0f;   // Near distance for attenuation
        public float distanceAttenuationFar = 100.0f;  // Far distance for attenuation
        public AnimationCurve distanceAttenuationCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1), new Keyframe(0.5f, 0.7f), new Keyframe(1, 0)); // Custom attenuation curve

        // Advanced Flyby detection
        public float flybyHighpassStart = 8000f;   // For whiz effect
        public float flybyHighpassEnd = 200f;
        
        // Advanced Shock System
        public float shockHighpassFrequency = 1200f; // For muffled effect
        public float hearingRecoveryRate = 0.8f;   // Rate of hearing recovery
        
        // Per-source occlusion with frequency filtering
        public float occlusionLowpassFrequency = 800f; // Frequency for occluded sounds

        // ---------------- State ----------------
        private AudioListener listener;
        private Transform ear;
        private AudioReverbFilter reverb;
        private AudioLowPassFilter lowpass; // used for tinnitus / shock only
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

        // tinnitus & shock
        private GameObject tinnitusGO;
        private AudioSource tinnitusSrc;
        private AudioClip tinnitusClip;
        private float noiseExposure;
        private float tinnitusTimeLeft;
        private float tinnitusSeverity;
        private float shockTimeLeft;
        private float infoNextLog;

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
        
        // Additional state variables for advanced features
        private AudioHighPassFilter highpass; // for shock effects
        private AudioSource windSource; // for environmental wind
        private GameObject windGO; // wind audio source object
        private float windSampleTime; // for wind variation timing
        private float windVolume; // current wind volume
        private float lastWindUpdate; // for wind variation

        private void Awake()
        {
            Logger.LogInfo("[EchoProbe] Awake (Doppler+Flyby build)");
            SceneManager.sceneLoaded += OnSceneLoaded;
            BuildDirections(rays);
            loudBuf = new float[loudnessSamples];
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Logger.LogInfo("[EchoProbe] Scene loaded → (re)initialize.");
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
                Logger.LogWarning("[EchoProbe] No AudioListener found yet.");
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
            
            // High pass filter for shock effects
            if (!highpass) highpass = listener.GetComponent<AudioHighPassFilter>();
            if (!highpass) highpass = listener.gameObject.AddComponent<AudioHighPassFilter>();
            highpass.enabled = true;
            highpass.cutoffFrequency = 10000f;

            // Tinnitus source
            if (!tinnitusGO)
            {
                tinnitusGO = new GameObject("TinnitusSource");
                Object.DontDestroyOnLoad(tinnitusGO);
                tinnitusSrc = tinnitusGO.AddComponent<AudioSource>();
                tinnitusSrc.spatialBlend = 0f;
                tinnitusSrc.loop = true;
                tinnitusSrc.playOnAwake = false;
                tinnitusSrc.volume = 0f;
                tinnitusSrc.bypassEffects = false;
                tinnitusSrc.bypassListenerEffects = false;
                tinnitusSrc.bypassReverbZones = true;
                tinnitusClip = CreateTinnitusClip(48000, 1.0f);
                tinnitusSrc.clip = tinnitusClip;
                tinnitusSrc.Play();
            }
            
            // Wind source for environmental audio
            if (enableWindSimulation && !windGO)
            {
                windGO = new GameObject("WindSource");
                Object.DontDestroyOnLoad(windGO);
                windSource = windGO.AddComponent<AudioSource>();
                windSource.spatialBlend = 0f;
                windSource.loop = true;
                windSource.playOnAwake = false;
                windSource.volume = 0f;
                windSource.bypassEffects = false;
                windSource.bypassListenerEffects = false;
                windSource.bypassReverbZones = false;
                AudioClip windClip = CreateWindClip(48000, 3.0f);
                windSource.clip = windClip;
                windSource.Play();
            }

            originalListenerVolume = AudioListener.volume;

            // seed params
            curDecay = tgtDecay = minDecay;
            curRoom = tgtRoom = minRoomMb;
            curRev = tgtRev = minReverbLevel;
            curRefDel = tgtRefDel = 0.03f;
            Apply();

            Logger.LogInfo("[EchoProbe] Ready: reverb(User) + lowpass + highpass + tinnitus source + wind source.");
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

            // update listener velocity
            listenerVel = Vector3.Lerp(listenerVel, (ear.position - prevEarPos) / Mathf.Max(0.0001f, Time.unscaledDeltaTime), Time.unscaledDeltaTime * velocitySmoothing);
            prevEarPos = ear.position;

            // probe
            if (Time.unscaledTime - lastProbe >= probeInterval)
            {
                lastProbe = Time.unscaledTime;
                Probe(ear.position);
            }

            // loudness sampling
            if (Time.unscaledTime - lastLoudSampleTime >= loudnessSampleInterval)
            {
                lastLoudSampleTime = Time.unscaledTime;
                SampleLoudness();
                UpdateExposureAndSymptoms();
            }

            // per-source occlusion checks (right after Probe)
            if (Time.unscaledTime - lastProbe < 0f)
            {
                CheckSourcesOcclusionAndDoppler();
            }

            // occasionally re-scan sources
            if (Time.unscaledTime - lastSourceScanTime > 3.0f)
            {
                lastSourceScanTime = Time.unscaledTime;
                ScanAudioSources();
            }
            
            // update wind simulation if enabled
            if (enableWindSimulation && windSource)
            {
                UpdateWindSimulation();
            }

            // chase targets
            float k = paramLerp * Time.unscaledDeltaTime;
            curDecay = Mathf.MoveTowards(curDecay, tgtDecay, k);
            curRoom = Mathf.MoveTowards(curRoom, tgtRoom, k * 2000f);
            curRev = Mathf.MoveTowards(curRev, tgtRev, k * 1800f);
            curRefDel = Mathf.MoveTowards(curRefDel, tgtRefDel, k);
            Apply();

            // handle tinnitus/shock interplay
            UpdateTinnitusAudio(Time.unscaledDeltaTime);

            if (Time.unscaledTime >= infoNextLog)
            {
                infoNextLog = Time.unscaledTime + 1.5f;
                Logger.LogInfo(string.Format(
                    "[EchoProbe] decay={0:F2}s room={1:F0}mB revLvl={2:F0}dB refDel={3}ms | RMS={4:F2} Peak={5:F2} Expo={6:F1} Tin={7:F2}/{8:F1}s TrackedSrcs={9} WindVol={10:F2}",
                    curDecay, curRoom, curRev, curRefDel * 1000f, lastRms, lastPeak, noiseExposure, tinnitusSeverity, tinnitusTimeLeft, trackedSources.Count, windVolume));
            }
        }

        // ---------------- Reverb Core ----------------

        private void Apply()
        {
            reverb.decayTime = curDecay;
            reverb.room = Mathf.RoundToInt(curRoom);
            reverb.reverbLevel = curRev;
            reverb.reflectionsDelay = curRefDel;
            reverb.reverbDelay = Mathf.Clamp(curRefDel * 1.5f, 0.01f, 0.40f);
            
            // Additional reverb parameters for more realistic simulation
            reverb.hfReference = 5000f; // Standard HF reference
            reverb.roomHF = Mathf.Clamp(Mathf.RoundToInt(curRoom * 0.8f), -10000, 0); // HF room effect
        }

        private void Probe(Vector3 origin)
        {
            if (dirs.Count != rays) BuildDirections(rays);

            int hitCount = 0;
            float sumDist = 0f;
            int upTotal = 0, upMiss = 0;
            float nearest = maxProbeDistance;

            for (int i = 0; i < dirs.Count; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(origin, d, out hit, maxProbeDistance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    hitCount++;
                    sumDist += hit.distance;
                    if (hit.distance < nearest) nearest = hit.distance;
                }
                else
                {
                    if (d.y >= upCone) upMiss++;
                }

                if (d.y >= upCone) upTotal++;
            }

            lastCoverage = hitCount / Mathf.Max(1f, (float)dirs.Count);
            lastOpenSky = (upTotal > 0) ? (upMiss / (float)upTotal) : 0f;
            lastAvgDist = (hitCount > 0) ? (sumDist / hitCount) : maxProbeDistance;

            lastEnclosure = Mathf.Clamp01(lastCoverage * (1f - 0.65f * lastOpenSky));

            // ---- Map to targets (LONGER tails for big closed areas) ----
            float roomSize = Mathf.Clamp01(lastAvgDist / maxProbeDistance);
            float effectiveEnclosure = Mathf.Clamp01(lastEnclosure * (1f - 0.75f * lastOpenSky));

            float decay01 = Mathf.Clamp01(0.2f + 0.8f * roomSize) * Mathf.Clamp01(0.35f + 0.75f * effectiveEnclosure);
            tgtDecay = Mathf.Lerp(minDecay, maxDecay, decay01);

            tgtRoom = Mathf.Lerp(minRoomMb, maxRoomMb, Mathf.Pow(effectiveEnclosure, 0.9f));

            float tail01 = Mathf.Clamp01(effectiveEnclosure * (0.6f + 0.8f * roomSize));
            // material-based early reflection boost: if many hits are on reflective tags, boost a bit
            float matBoost = 1.0f;
            float absorptionFactor = 1.0f;
            float frequencyFactor = 2200f; // Base frequency response
            int matHits = 0;
            // (enhanced heuristic: probe some directions again and check detailed material properties)
            int checks = Mathf.Min(12, dirs.Count); // Increased checks for better material detection
            for (int i = 0; i < checks; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(origin, d, out hit, maxProbeDistance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    string tag = hit.collider ? hit.collider.gameObject.tag : null;
                    if (!string.IsNullOrEmpty(tag) && materialProperties.ContainsKey(tag))
                    {
                        MaterialAudioProperties props = materialProperties[tag];
                        matBoost += (props.reflectionBoost - 1.0f) * 0.15f; // Reflection contribution
                        absorptionFactor *= props.absorptionFactor; // Absorption multiplication
                        frequencyFactor = Mathf.Lerp(frequencyFactor, props.frequencyCutoff, 0.2f); // Frequency blending
                        matHits++;
                    }
                }
            }
            // Normalize material effects based on hit ratio
            float matRatio = matHits / (float)checks;
            matBoost = 1.0f + (matBoost - 1.0f) * matRatio;
            absorptionFactor = Mathf.Pow(absorptionFactor, matRatio); // Geometric mean
            
            // Apply material effects to reverb parameters
            tail01 = Mathf.Clamp01(tail01 * Mathf.Clamp(matBoost, 0.8f, 1.4f));
            tgtRev = Mathf.Lerp(minReverbLevel, maxReverbLevel, tail01 * absorptionFactor);
            
            // Adjust frequency response based on materials
            if (lowpass && enableAmbientReverb)
            {
                float freqResponse = Mathf.Lerp(1000f, frequencyFactor, Mathf.Clamp01(tail01));
                lowpass.cutoffFrequency = Mathf.MoveTowards(lowpass.cutoffFrequency, freqResponse, 2000f * Time.unscaledDeltaTime);
            }

            float echoTOF = Mathf.Clamp((lastAvgDist * 2f) / 343f, 0.01f, maxReflectionsDelay);
            tgtRefDel = echoTOF;

            // clamps
            tgtDecay = Mathf.Clamp(tgtDecay, minDecay, maxDecay);
            tgtRoom = Mathf.Clamp(tgtRoom, minRoomMb, maxRoomMb);
            tgtRev = Mathf.Clamp(tgtRev, minReverbLevel, maxReverbLevel);
            tgtRefDel = Mathf.Clamp(tgtRefDel, 0.01f, maxReflectionsDelay);

            Logger.LogInfo(string.Format(
                "[EchoProbe] hits={0}/{1}, coverage={2:P0}, openSky={3:P0}, avgDist={4:F1}m, TOF={5}ms, enclosure={6:F2}, matHits={7}, matBoost={8:F2}",
                hitCount, dirs.Count, lastCoverage, lastOpenSky, lastAvgDist, Mathf.RoundToInt(tgtRefDel * 1000f), lastEnclosure, matHits, matBoost));
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

            if (lastPeak >= explosionPeakThreshold)
            {
                // keep shock handling for tinnitus timing (explosion immersion removed)
                shockTimeLeft = shockMuteSeconds + shockMuffleSeconds;
            }
        }

        private void UpdateExposureAndSymptoms()
        {
            float revStrength = Mathf.InverseLerp(minReverbLevel, maxReverbLevel, curRev);
            revStrength = Mathf.Clamp01(revStrength);

            float envWeight = 1f + enclosureGain * lastEnclosure;
            float revWeight = 1f + revStrength * (reverbGain * 1000f);

            float add = lastRms * exposureGain * envWeight * revWeight;
            float recovery = exposureDecay * (lastRms < exposureQuietRms ? (1.0f + lastOpenSky) : 0.5f);

            noiseExposure += add * Time.unscaledDeltaTime;
            noiseExposure -= recovery * Time.unscaledDeltaTime;
            noiseExposure = Mathf.Clamp(noiseExposure, 0f, tinnitusMax);

            if (noiseExposure >= tinnitusThreshold && tinnitusTimeLeft <= 0f)
            {
                float over = Mathf.Clamp01((noiseExposure - tinnitusThreshold) / (tinnitusMax - tinnitusThreshold));
                tinnitusSeverity = Mathf.Clamp01(0.25f + 0.75f * over);
                tinnitusTimeLeft = Mathf.Lerp(tinnitusMinSeconds, tinnitusMaxSeconds, tinnitusSeverity);
            }
        }

        private void UpdateTinnitusAudio(float dt)
        {
            // Shock handling with high pass filter
            if (shockTimeLeft > 0f)
            {
                float before = shockTimeLeft;
                shockTimeLeft -= dt;

                if (before > shockMuffleSeconds)
                {
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, 0.1f * originalListenerVolume, 12f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 500f, 10f * dt);
                    if (highpass) highpass.cutoffFrequency = Mathf.Lerp(highpass.cutoffFrequency, shockHighpassFrequency, 8f * dt);
                }
                else
                {
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.6f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 1800f, 2.5f * dt);
                    if (highpass) highpass.cutoffFrequency = Mathf.Lerp(highpass.cutoffFrequency, 10000f, 3f * dt);
                }
            }
            else
            {
                // Gradual hearing recovery when no shock
                AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.8f * dt);
                
                // Gradual recovery from hearing damage
                if (noiseExposure > 0f)
                {
                    noiseExposure = Mathf.MoveTowards(noiseExposure, 0f, hearingRecoveryRate * dt);
                }
                
                // Reset filters to normal
                if (lowpass) lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 22000f, 1.5f * dt);
                if (highpass) highpass.cutoffFrequency = Mathf.Lerp(highpass.cutoffFrequency, 10000f, 1.5f * dt);
            }

            // Tinnitus ring (no scene-muffle influence — muffle removed)
            float tinnitusLpTarget = 22000f;
            float ringTargetVol = 0f;

            if (tinnitusTimeLeft > 0f)
            {
                tinnitusTimeLeft -= dt;
                float life01 = Mathf.Clamp01(tinnitusTimeLeft / Mathf.Max(0.0001f, Mathf.Lerp(tinnitusMinSeconds, tinnitusMaxSeconds, tinnitusSeverity)));
                float sev = tinnitusSeverity * Mathf.SmoothStep(0f, 1f, life01);
                float reactive = Mathf.Clamp01(lastRms * 2.5f);
                ringTargetVol = Mathf.Clamp01(0.08f + sev * 0.35f + reactive * 0.12f);
                tinnitusLpTarget = Mathf.Lerp(6000f, 1500f, sev);

                if (shockTimeLeft > 0f) tinnitusLpTarget = Mathf.Min(tinnitusLpTarget, 1800f);
            }
            else
            {
                // Gradual reduction of tinnitus if exposure is decreasing
                if (noiseExposure < tinnitusThreshold && tinnitusSeverity > 0f)
                {
                    tinnitusSeverity = Mathf.MoveTowards(tinnitusSeverity, 0f, hearingRecoveryRate * 0.1f * dt);
                }
            }

            if (tinnitusSrc != null)
            {
                float trem = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6.283f * 3.1f);
                float v = ringTargetVol * trem;
                tinnitusSrc.volume = Mathf.MoveTowards(tinnitusSrc.volume, v, 1.2f * Time.unscaledDeltaTime);
            }

            if (lowpass != null)
            {
                float lpTarget = Mathf.Clamp(tinnitusLpTarget, 800f, 22000f);
                lowpass.cutoffFrequency = Mathf.MoveTowards(lowpass.cutoffFrequency, lpTarget, 8000f * Time.unscaledDeltaTime);
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
            Logger.LogInfo("[EchoProbe] Scanned audio sources, tracked: " + trackedSources.Count);
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

            for (int i = 0; i < trackedSources.Count && checkedCount < maxSourcesPerCheck; i++)
            {
                AudioSource src = trackedSources[i];
                if (src == null) continue;

                // compute estimated source velocity
                Vector3 prevPos = prevSourcePos.ContainsKey(src) ? prevSourcePos[src] : src.transform.position;
                Vector3 currentPos = src.transform.position;
                Vector3 estVel = (currentPos - prevPos) / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                // smooth velocity to reduce jitter
                Vector3 smoothed = Vector3.Lerp(sourceVel.ContainsKey(src) ? sourceVel[src] : Vector3.zero, estVel, Time.unscaledDeltaTime * velocitySmoothing);
                sourceVel[src] = smoothed;
                prevSourcePos[src] = currentPos;

                float d = Vector3.Distance(currentPos, ear.position);
                if (d > maxOcclusionDistance) // out of range
                {
                    RestoreSourceVolume(src);
                    // still update doppler to keep smoothing consistent
                    ApplyDopplerToSource(src, smoothed, listenerVel, d, 0f);
                    continue;
                }

                // skip not playing sources
                if (!src.isPlaying) { RestoreSourceVolume(src); ApplyDopplerToSource(src, smoothed, listenerVel, d, 0f); continue; }

                // Quick same-room heuristic
                float srcEnclosure = QuickLocalEnclosure(src.transform.position);
                bool sameRoom = Mathf.Abs(srcEnclosure - lastEnclosure) < 0.22f && Mathf.Abs(Vector3.Distance(src.transform.position, ear.position) - lastAvgDist) < (lastAvgDist * 0.6f + 1f);

                // Direct LOS test: if there is clear line, no occlusion
                Vector3 dir = (ear.position - src.transform.position).normalized;
                RaycastHit hitInfo;
                bool hasObstacle = Physics.Raycast(src.transform.position + dir * 0.02f, dir, out hitInfo, d - 0.04f, occlusionMask, QueryTriggerInteraction.Ignore);

                if (!hasObstacle || sameRoom)
                {
                    // no blocking geometry or same room -> restore volume
                    RestoreSourceVolume(src);
                }
                else
                {
                    // There are hits between source and listener: compute an occlusion factor based on number of hits and rough collider sizes
                    RaycastHit[] hits = Physics.RaycastAll(src.transform.position + dir * 0.02f, dir, d - 0.04f, occlusionMask, QueryTriggerInteraction.Ignore);
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
                    float volMul = Mathf.Lerp(1f, sourceOcclusionMinVolume, occ);

                    // Apply volume relative to stored original volume (don't permanently overwrite)
                    float orig = 1f;
                    if (originalSourceVolume.ContainsKey(src)) orig = originalSourceVolume[src];
                    float targetVol = orig * volMul;

                    src.volume = Mathf.MoveTowards(src.volume, targetVol, 1.2f * Time.unscaledDeltaTime);
                    
                    // Apply lowpass filter for occluded sounds
                    if (src.GetComponent<AudioLowPassFilter>() == null)
                    {
                        src.gameObject.AddComponent<AudioLowPassFilter>();
                    }
                    AudioLowPassFilter lowpassFilter = src.GetComponent<AudioLowPassFilter>();
                    if (lowpassFilter != null)
                    {
                        lowpassFilter.enabled = true;
                        float cutoffFreq = Mathf.Lerp(22000f, occlusionLowpassFrequency, occ);
                        lowpassFilter.cutoffFrequency = Mathf.MoveTowards(lowpassFilter.cutoffFrequency, cutoffFreq, 5000f * Time.unscaledDeltaTime);
                    }
                }
                
                // Apply distance-based attenuation using the custom curve
                float distanceFactor = Mathf.Clamp01((d - distanceAttenuationNear) / (distanceAttenuationFar - distanceAttenuationNear));
                float attenuation = distanceAttenuationCurve.Evaluate(distanceFactor);
                
                // Apply spatial blend adjustment based on distance for HRTF simulation
                if (enableHRTF && d < spatialBlendDistance)
                {
                    float hrtfBlend = Mathf.Clamp01(1.0f - (d / hrtfDistance));
                    src.spatialBlend = Mathf.Lerp(0.5f, 1.0f, hrtfBlend);
                }
                else if (d >= spatialBlendDistance)
                {
                    src.spatialBlend = 1.0f; // Full 3D for distant sounds
                }

                // Apply Doppler / flyby
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
        private void ApplyDopplerToSource(AudioSource src, Vector3 srcVelocity, Vector3 listenerVelocityLocal, float distanceToListener, float dt)
        {
            if (src == null) return;

            // relative velocity along the LOS
            Vector3 LOS = (ear.position - src.transform.position).normalized;
            float vSourceAlong = Vector3.Dot(srcVelocity, LOS); // positive -> moving towards listener
            float vListenerAlong = Vector3.Dot(listenerVelocityLocal, LOS); // positive -> listener moving towards source
            // physical doppler ratio (approx): (c + v_listener) / (c - v_source)
            float c = 343f;
            float rawRatio = (c + vListenerAlong) / Mathf.Max(0.0001f, (c - vSourceAlong));
            float ratio = Mathf.Clamp(rawRatio, dopplerMinPitch, dopplerMaxPitch);
            // apply strength and clamp
            float pitchTarget = Mathf.Clamp(Mathf.Pow(ratio, dopplerStrength), dopplerMinPitch, dopplerMaxPitch);

            // Flyby detection: quick lateral speed near listener
            // compute lateral velocity (component perpendicular to LOS)
            Vector3 relVel = srcVelocity - listenerVelocityLocal;
            Vector3 lateral = relVel - Vector3.Dot(relVel, LOS) * LOS;
            float lateralSpeed = lateral.magnitude;

            // compute closest approach estimate: if source moving fast and will sweep by close -> trigger flyby
            float currentClosest = distanceToListener;
            bool isFlyby = lateralSpeed >= flybyVelocityThreshold && currentClosest <= Mathf.Max(flybyMinDistance, distanceToListener * 0.75f);

            // manage per-source flyby timer (for smoothing)
            float timer = flybyTimers.ContainsKey(src) ? flybyTimers[src] : 0f;
            if (isFlyby)
            {
                timer = flybyDecaySeconds; // reset timer when close fast flyby detected
            }
            else
            {
                timer = Mathf.Max(0f, timer - dt);
            }
            flybyTimers[src] = timer;

            // compute final pitch & volume multipliers
            float flybyFactor = Mathf.Clamp01(timer / flybyDecaySeconds); // 0..1
            float pitchFinal = pitchTarget * (1f + (flybyPitchBoost - 1f) * flybyFactor);
            float volFinalMul = 1f + (flybyVolumeBoost - 1f) * flybyFactor;
            
            // Apply flyby highpass filter for whiz effect
            if (src.GetComponent<AudioHighPassFilter>() == null)
            {
                src.gameObject.AddComponent<AudioHighPassFilter>();
            }
            AudioHighPassFilter highpassFilter = src.GetComponent<AudioHighPassFilter>();
            if (highpassFilter != null)
            {
                highpassFilter.enabled = true;
                float hpFreq = Mathf.Lerp(flybyHighpassEnd, flybyHighpassStart, flybyFactor); // High frequency when flyby is active
                highpassFilter.cutoffFrequency = Mathf.MoveTowards(highpassFilter.cutoffFrequency, hpFreq, 10000f * Time.unscaledDeltaTime);
            }

            // Apply pitch and small smoothing (don't stomp other mods that change pitch)
            src.pitch = Mathf.MoveTowards(src.pitch, pitchFinal, 3f * Time.unscaledDeltaTime);

            // Apply volume boost multiplicatively but relative to stored original
            float origVol = 1f;
            if (originalSourceVolume.ContainsKey(src)) origVol = originalSourceVolume[src];
            float desiredVol = Mathf.Clamp(origVol * volFinalMul, 0f, 2f);
            src.volume = Mathf.MoveTowards(src.volume, desiredVol, 2.5f * Time.unscaledDeltaTime);
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
                if (Physics.Raycast(pos, d, out hit, range, hitMask, QueryTriggerInteraction.Ignore))
                    hits++;
            }
            return (float)hits / (float)checks; // 0..1
        }

        // ---------------- Utilities ----------------

        private AudioClip CreateTinnitusClip(int sampleRate, float seconds)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            float[] data = new float[samples];
            float f1 = 6200f;
            float f2 = 9000f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float s = 0.6f * Mathf.Sin(2f * Mathf.PI * f1 * t)
                        + 0.4f * Mathf.Sin(2f * Mathf.PI * f2 * t);
                s += 0.02f * (Random.value * 2f - 1f);
                data[i] = s * 0.2f;
            }
            data[samples - 1] = data[0];
            AudioClip clip = AudioClip.Create("TinnitusRing", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
        
        // Update wind simulation for environmental audio
        private void UpdateWindSimulation()
        {
            if (Time.unscaledTime - lastWindUpdate > 0.1f) // Update wind every 100ms
            {
                lastWindUpdate = Time.unscaledTime;
                
                // Calculate wind volume based on environment (simplified - could be enhanced with weather system)
                float baseWind = windBaseVolume;
                
                // Add variation based on environment openness (more open = more wind)
                float envFactor = 1.0f + (1.0f - lastEnclosure) * 0.5f; // More open areas have more wind
                
                // Add random variation
                float variation = windVariation * Mathf.PerlinNoise(Time.unscaledTime * 0.5f, ear.position.x * 0.01f);
                
                windVolume = Mathf.Clamp(baseWind * envFactor + variation, 0f, baseWind * 2f);
                
                // Apply wind volume with smoothing
                if (windSource)
                {
                    windSource.volume = Mathf.MoveTowards(windSource.volume, windVolume * 0.3f, 0.1f * Time.unscaledDeltaTime);
                }
            }
        }
        
        // Create a wind audio clip
        private AudioClip CreateWindClip(int sampleRate, float seconds)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            float[] data = new float[samples];
            
            // Create a low-frequency noise that sounds like wind
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                
                // Low frequency base tone
                float baseFreq = 40f + 20f * Mathf.PerlinNoise(t * 0.5f, 100f); // Varying between 40-60Hz
                
                // Add some higher frequency turbulence
                float turbulence = 0.3f * Mathf.PerlinNoise(t * 10f, 200f);
                
                // Combine for wind-like sound
                float wind = Mathf.Sin(2f * Mathf.PI * baseFreq * t) * 0.3f + turbulence * 0.2f;
                
                // Add low-pass filtered noise for realism
                float noise = (Random.value * 2f - 1f) * 0.4f;
                
                data[i] = (wind + noise) * 0.1f;
            }
            
            data[samples - 1] = data[0]; // Make loop seamless
            AudioClip clip = AudioClip.Create("WindAmbience", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
