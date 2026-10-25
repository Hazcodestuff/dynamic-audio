using System.Collections.Generic;
using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// Owns the reverb / echo / low-pass filters on the ACTIVE AudioListener.
    /// Fixes vs V6: validates the listener every frame (isActiveAndEnabled), picks an enabled listener,
    /// restores filters when the master switch is toggled, uses a single low-pass shared by shockwave and
    /// tinnitus (no coroutine, no null-ref when the camera changes) and passes echo delay in milliseconds.
    /// </summary>
    public sealed class ListenerAudioSystem
    {
        private sealed class Rig
        {
            public AudioListener Listener;
            public AudioReverbFilter Reverb;
            public AudioEchoFilter Echo;
            public AudioLowPassFilter LowPass;
        }

        private readonly Dictionary<int, Rig> rigs = new Dictionary<int, Rig>();
        private Rig rig;
        private AudioListener listener;
        private Transform ear;
        private float retryAt;
        private bool active = true;

        private bool reverbWanted = true;
        private bool echoEnabled = true;
        private float reverbIntensity = 1f;
        private float reverbRoomSizeMult = 1f;

        private float tgtDecay = 0.3f, curDecay = 0.3f;
        private float tgtRoom = -4000f, curRoom = -4000f;
        private float tgtRev = -3000f, curRev = -3000f;
        private float tgtRefl = -2800f, curRefl = -2800f;
        private float tgtRefDel = 0.01f, curRefDel = 0.01f;
        private bool echoWanted;
        private float echoDelaySec = 0.1f;
        private float echoEnv;
        private float echoHold;
        private float echoSuppress;

        private bool shockActive;
        private float shockStart;
        private float shockDuration;
        private float shockT;

        /// <summary>Low-pass cutoff requested by the tinnitus effect (22000 = none).</summary>
        public float TinnitusCutoff = 22000f;

        private RoomEstimate currentRoom;
        private string listenerInfo = "not found";

        public RoomEstimate CurrentRoom { get { return currentRoom; } }
        public string ListenerInfo { get { return listenerInfo; } }
        public Transform Ear { get { return ear; } }
        public bool Ready { get { return rig != null && listener != null; } }

        public GameObject ListenerObject
        {
            get { return listener != null ? listener.gameObject : null; }
        }

        public void Configure(float intensity, float roomSizeMult, bool echoOn)
        {
            reverbIntensity = intensity;
            reverbRoomSizeMult = roomSizeMult;
            echoEnabled = echoOn;
        }

        public void SetActive(bool on)
        {
            active = on;
        }

        /// <summary>Re-validates the listener; searches again immediately when it was lost.</summary>
        public void Refresh(float now, bool force)
        {
            if (listener != null && listener.isActiveAndEnabled) return;
            if (listener != null || rig != null) Detach();
            if (!force && now < retryAt) return;
            retryAt = now + 0.5f;

            AudioListener found = null;
            AudioListener[] all = UnityEngine.Object.FindObjectsOfType<AudioListener>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].isActiveAndEnabled)
                {
                    found = all[i];
                    break;
                }
            }
            if (found == null)
            {
                listenerInfo = "AudioListener not found";
                return;
            }
            Attach(found);
        }

        private void Attach(AudioListener l)
        {
            GameObject go = l.gameObject;
            Rig r;
            int id = l.GetInstanceID();
            if (!rigs.TryGetValue(id, out r))
            {
                r = new Rig();
                r.Listener = l;
                rigs[id] = r;
            }
            if (r.Reverb == null) r.Reverb = go.AddComponent<AudioReverbFilter>();
            if (r.Echo == null) r.Echo = go.AddComponent<AudioEchoFilter>();
            if (r.LowPass == null) r.LowPass = go.AddComponent<AudioLowPassFilter>();

            r.Reverb.reverbPreset = AudioReverbPreset.User;
            r.Reverb.dryLevel = 0f;
            r.Reverb.diffusion = 100f;
            r.Reverb.density = 100f;
            r.Reverb.enabled = false;
            // Unity's defaults (echo wetMix 1.0 / 500 ms, low-pass 5 kHz) are loud or dull: neutralise them while disabled.
            r.Echo.wetMix = 0f;
            r.Echo.dryMix = 1f;
            r.Echo.delay = 100f;
            r.Echo.decayRatio = 0.3f;
            r.Echo.enabled = false;
            r.LowPass.cutoffFrequency = 22000f;
            r.LowPass.enabled = false;

            listener = l;
            ear = l.transform;
            rig = r;

            curDecay = tgtDecay = 0.3f;
            curRoom = tgtRoom = -4000f;
            curRev = tgtRev = -3000f;
            curRefl = tgtRefl = -2800f;
            curRefDel = tgtRefDel = 0.01f;
            shockActive = false;
            echoEnv = 0f;
            echoHold = 0f;
            // forget rigs of listeners that no longer exist
            List<int> dead = null;
            foreach (KeyValuePair<int, Rig> kv in rigs)
            {
                if (kv.Value.Listener == null)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) rigs.Remove(dead[i]);
            }
            listenerInfo = "on " + go.name;
        }

        private void Detach()
        {
            DisableRig(rig);
            rig = null;
            listener = null;
            ear = null;
            shockActive = false;
        }

        private static void DisableRig(Rig r)
        {
            if (r == null) return;
            if (r.Reverb != null) r.Reverb.enabled = false;
            if (r.Echo != null) r.Echo.enabled = false;
            if (r.LowPass != null) r.LowPass.enabled = false;
        }

        /// <summary>Master switch off / plugin unload: leave the listener exactly as the game made it.</summary>
        public void Shutdown()
        {
            shockActive = false;
            DisableRig(rig);
        }

        public void ApplyRoom(ref RoomEstimate room, bool reverbOn)
        {
            currentRoom = room;
            reverbWanted = reverbOn;

            float intensity = Mathf.Clamp01(reverbIntensity);
            float size = Mathf.Pow(Mathf.Clamp(room.Volume, 8f, 200000f), 0.333333f) * reverbRoomSizeMult;
            size = Mathf.Max(size, 2f);
            float enclosure = Mathf.Pow(Mathf.Clamp01(room.Enclosure), 0.75f) * (room.HasCeiling ? 1f : 0.7f);

            float decay = 0.35f * Mathf.Pow(size / 3f, 0.85f);
            if (room.Type == RoomType.Tunnel) decay *= 1.35f;
            else if (room.Type == RoomType.Hallway) decay *= 1.1f;
            decay = Mathf.Clamp(decay, 0.25f, 3.2f);

            float refl = Mathf.Lerp(-120f, -1400f, Mathf.Clamp01((size - 2f) / 14f));
            float rev = Mathf.Lerp(-1000f, -150f, Mathf.Clamp01((size - 2f) / 28f));
            float roomLvl = Mathf.Lerp(-350f, -160f, Mathf.Clamp01((size - 2f) / 28f));
            if (room.Type == RoomType.Tunnel) refl += 150f;

            float avg = (room.Width + room.Length + room.Height) / 3f;
            float reflDelay = Mathf.Clamp((2f * Mathf.Min(avg, 60f)) / 343f, 0.006f, 0.25f);

            float open = 1f - enclosure;
            float openPow = Mathf.Pow(open, 1.25f);
            refl = Mathf.Lerp(refl, -2600f, openPow);
            rev = Mathf.Lerp(rev, -3000f, openPow);
            roomLvl = Mathf.Lerp(roomLvl, -4000f, openPow);
            decay = Mathf.Lerp(decay, 0.3f, open * open);

            refl = Mathf.Lerp(-2600f, refl, intensity);
            rev = Mathf.Lerp(-3000f, rev, intensity);
            roomLvl = Mathf.Lerp(-4000f, roomLvl, intensity);

            tgtDecay = decay;
            tgtRoom = roomLvl;
            tgtRev = rev;
            tgtRefl = refl;
            tgtRefDel = reflDelay;

            float big = Mathf.Max(room.Width, room.Length);
            echoWanted = echoEnabled && (room.Type == RoomType.Tunnel || room.Type == RoomType.Warehouse
                || room.Type == RoomType.Courtyard || big > 25f);
            if (echoWanted) echoDelaySec = Mathf.Clamp((2f * Mathf.Min(big, 120f)) / 343f, 0.05f, 0.45f);
        }

        /// <summary>A loud sound (gunshot/explosion) just happened: let the echo fade in for a moment.</summary>
        public void PulseEcho(float strength01)
        {
            if (echoSuppress > 0f) return;
            strength01 = Mathf.Clamp01(strength01);
            if (strength01 > echoEnv) echoEnv = strength01;
            echoHold = 0.25f;
        }

        /// <summary>The player is doing something quiet (reloading): kill the echo quickly and keep it off.</summary>
        public void SuppressEcho(float seconds)
        {
            if (seconds > echoSuppress) echoSuppress = seconds;
            echoHold = 0f;
        }

        public void TriggerShockwave(float severity01)
        {
            if (!active || rig == null) return;
            severity01 = Mathf.Clamp01(severity01);
            if (severity01 < 0.15f) return;
            float start = Mathf.Lerp(8000f, 500f, severity01);
            float duration = Mathf.Lerp(0.15f, 0.6f, severity01);
            if (shockActive)
            {
                float k = shockDuration > 0f ? Mathf.Clamp01(shockT / shockDuration) : 1f;
                float now = Mathf.Lerp(shockStart, 20000f, k * k);
                if (start >= now) return;   // current effect is already stronger
            }
            shockActive = true;
            shockStart = start;
            shockDuration = duration;
            shockT = 0f;
        }

        public void Tick(float dt)
        {
            if (rig == null) return;
            if (rig.Reverb == null || rig.Echo == null || rig.LowPass == null)
            {
                Detach();
                return;
            }
            if (!active)
            {
                DisableRig(rig);
                return;
            }

            float step = Mathf.Max(dt, 0.0001f);
            curDecay = Mathf.MoveTowards(curDecay, tgtDecay, 2.5f * step);
            curRoom = Mathf.MoveTowards(curRoom, tgtRoom, 2500f * step);
            curRev = Mathf.MoveTowards(curRev, tgtRev, 2500f * step);
            curRefl = Mathf.MoveTowards(curRefl, tgtRefl, 2500f * step);
            curRefDel = Mathf.MoveTowards(curRefDel, tgtRefDel, 0.5f * step);

            // Parameters first, THEN enable: a freshly enabled Unity filter starts from its defaults otherwise.
            if (reverbWanted)
            {
                rig.Reverb.decayTime = curDecay;
                rig.Reverb.room = Mathf.RoundToInt(Mathf.Clamp(curRoom, -10000f, 0f));
                rig.Reverb.reverbLevel = Mathf.RoundToInt(Mathf.Clamp(curRev, -10000f, 0f));
                rig.Reverb.reflectionsLevel = Mathf.RoundToInt(Mathf.Clamp(curRefl, -10000f, 0f));
                rig.Reverb.reflectionsDelay = curRefDel;
                rig.Reverb.reverbDelay = Mathf.Clamp(curRefDel * 1.5f, 0.01f, 0.1f);   // Unity range 0-0.1
            }
            if (rig.Reverb.enabled != reverbWanted) rig.Reverb.enabled = reverbWanted;

            // The echo filter sits on the listener, i.e. it affects EVERYTHING (reloads, footsteps, UI).
            // So it is event-driven: it only fades in after a loud sound (gunshot/explosion) and fades out
            // quickly, and it is damped hard while the player is reloading.
            if (echoSuppress > 0f) echoSuppress -= dt;
            if (echoHold > 0f) echoHold -= dt;
            else echoEnv = Mathf.MoveTowards(echoEnv, 0f, dt * (echoSuppress > 0f ? 5f : 1.1f));

            bool echoOn = echoWanted && echoEnv > 0.03f;
            if (echoOn)
            {
                bool tunnel = currentRoom.Type == RoomType.Tunnel;
                rig.Echo.delay = echoDelaySec * 1000f;      // AudioEchoFilter.delay is MILLISECONDS
                rig.Echo.decayRatio = tunnel ? 0.45f : 0.3f;
                rig.Echo.wetMix = (tunnel ? 0.18f : 0.10f) * echoEnv;
                rig.Echo.dryMix = 1f;
            }
            if (rig.Echo.enabled != echoOn) rig.Echo.enabled = echoOn;

            float cutoff = 22000f;
            if (shockActive)
            {
                shockT += dt;
                float k = shockDuration > 0f ? shockT / shockDuration : 1f;
                if (k >= 1f) shockActive = false;
                else cutoff = Mathf.Lerp(shockStart, 20000f, k * k);
            }
            if (TinnitusCutoff < cutoff) cutoff = TinnitusCutoff;

            bool lpOn = cutoff < 19500f;
            if (lpOn) rig.LowPass.cutoffFrequency = Mathf.Clamp(cutoff, 200f, 22000f);
            if (rig.LowPass.enabled != lpOn) rig.LowPass.enabled = lpOn;
        }
    }
}
