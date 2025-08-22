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
    [BepInPlugin("zim.echo.probe", "Echo Probe (TOF Reverb) - Doppler + Flyby", "1.5.1")]
    public class EchoProbePlugin : BaseUnityPlugin
    {
        // ---------------- Tunables ----------------
        public float probeInterval = 0.12f;
        public int rays = 40;
        public float maxProbeDistance = 40f;  // meters
        public float upCone = 0.6f;           // y >= this → treated as "upward" for open-sky test
        public LayerMask hitMask = ~0;

        // Reverb intent
        public float minDecay = 0.35f;        // seconds
        public float maxDecay = 5.50f;        // seconds
        public float minReverbLevel = -1800f; // dB
        public float maxReverbLevel = -60f;   // dB
        public float minRoomMb = -2000f;      // mB
        public float maxRoomMb = 0f;          // mB
        public float maxReflectionsDelay = 0.4f; // seconds

        // Per-source occlusion (volume-only)
        public float maxOcclusionDistance = 60f; // only check sources closer than this
        public int maxSourcesPerCheck = 24;      // limit how many sources we test per probe
        public float sourceOcclusionMinVolume = 0.25f; // minimum volume multiplier for fully occluded source
        public LayerMask occlusionMask = ~0;    // which layers block sound between src and listener

        // Parameter smoothing
        public float paramLerp = 3.0f;        // higher = snappier

        // Loudness / Tinnitus
        public float loudnessSampleInterval = 0.05f;
        public int loudnessSamples = 1024;
        public float exposureGain = 22f;
        public float enclosureGain = 1.8f;
        public float reverbGain = 0.0022f;
        public float exposureDecay = 6.0f;
        public float exposureQuietRms = 0.08f;
        public float tinnitusThreshold = 12f;
        public float tinnitusMax = 40f;
        public float tinnitusMinSeconds = 0f;
        public float tinnitusMaxSeconds = 0f;

        // Shock (explosion) - kept only for tinnitus/shock timing; explosion immersion removed by user request
        public float explosionPeakThreshold = 0.85f;
        public float shockMuteSeconds = 0.25f;
        public float shockMuffleSeconds = 2.0f;

        // ---------------- Doppler & Flyby Tunables ----------------
        // Doppler
        public float dopplerStrength = 0f; // overall multiplier for Doppler pitch shift (1 = physical)
        public float dopplerMaxPitch = 1f; // clamp max pitch
        public float dopplerMinPitch = 1f; // clamp min pitch
        public float velocitySmoothing = 5.0f; // smoothing for estimated velocities

        // Flyby detection
        public float flybyVelocityThreshold = 25f; // m/s along lateral axis to consider "fast"
        public float flybyMinDistance = 2.0f; // closest approach to trigger whiz
        public float flybyPitchBoost = 1.15f; // temporary multiplicative boost on pitch
        public float flybyVolumeBoost = 1.2f; // temporary multiplicative boost on volume
        public float flybyDecaySeconds = 0.35f; // time to fade the flyby effect

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

            originalListenerVolume = AudioListener.volume;

            // seed params
            curDecay = tgtDecay = minDecay;
            curRoom = tgtRoom = minRoomMb;
            curRev = tgtRev = minReverbLevel;
            curRefDel = tgtRefDel = 0.03f;
            Apply();

            Logger.LogInfo("[EchoProbe] Ready: reverb(User) + lowpass + tinnitus source.");
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
                    "[EchoProbe] decay={0:F2}s room={1:F0}mB revLvl={2:F0}dB refDel={3}ms | RMS={4:F2} Peak={5:F2} Expo={6:F1} Tin={7:F2}/{8:F1}s TrackedSrcs={9}",
                    curDecay, curRoom, curRev, curRefDel * 1000f, lastRms, lastPeak, noiseExposure, tinnitusSeverity, tinnitusTimeLeft, trackedSources.Count));
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
            // (cheap heuristic: probe some directions again and check tags; keep tiny cost)
            int checks = Mathf.Min(8, dirs.Count);
            for (int i = 0; i < checks; i++)
            {
                Vector3 d = dirs[i];
                RaycastHit hit;
                if (Physics.Raycast(origin, d, out hit, maxProbeDistance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    string tag = hit.collider ? hit.collider.gameObject.tag : null;
                    if (!string.IsNullOrEmpty(tag) && materialEarlyReflection.ContainsKey(tag))
                        matBoost += (materialEarlyReflection[tag] - 1.0f) * 0.25f; // small weighted contribution
                }
            }
            tail01 = Mathf.Clamp01(tail01 * Mathf.Clamp(matBoost, 0.9f, 1.25f));
            tgtRev = Mathf.Lerp(minReverbLevel, maxReverbLevel, tail01);

            float echoTOF = Mathf.Clamp((lastAvgDist * 2f) / 343f, 0.01f, maxReflectionsDelay);
            tgtRefDel = echoTOF;

            // clamps
            tgtDecay = Mathf.Clamp(tgtDecay, minDecay, maxDecay);
            tgtRoom = Mathf.Clamp(tgtRoom, minRoomMb, maxRoomMb);
            tgtRev = Mathf.Clamp(tgtRev, minReverbLevel, maxReverbLevel);
            tgtRefDel = Mathf.Clamp(tgtRefDel, 0.01f, maxReflectionsDelay);

            Logger.LogInfo(string.Format(
                "[EchoProbe] hits={0}/{1}, coverage={2:P0}, openSky={3:P0}, avgDist={4:F1}m, TOF={5}ms, enclosure={6:F2}",
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
            // Shock handling (kept)
            if (shockTimeLeft > 0f)
            {
                float before = shockTimeLeft;
                shockTimeLeft -= dt;

                if (before > shockMuffleSeconds)
                {
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, 0.1f * originalListenerVolume, 12f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 500f, 10f * dt);
                }
                else
                {
                    AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.6f * dt);
                    lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, 1800f, 2.5f * dt);
                }
            }
            else
            {
                AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalListenerVolume, 0.8f * dt);
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
    }
}
