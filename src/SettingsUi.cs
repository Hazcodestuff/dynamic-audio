using BepInEx.Configuration;
using UnityEngine;

namespace DynamicAudio
{
    /// <summary>In-game settings window (F11 by default). Errors no longer close it.</summary>
    public sealed class SettingsUi
    {
        private const int WindowId = 748213;
        private static readonly string[] Tabs = new string[] { "General", "Occlusion", "Reverb", "Delay", "Effects", "Categories", "Performance" };

        public bool Visible;
        public bool CapturingKey { get { return capturingKey; } }

        private readonly DynamicAudioPlugin p;
        private Rect winRect = new Rect(0f, 0f, 580f, 720f);
        private Vector2 scroll;
        private int tab;
        private bool capturingKey;
        private bool positioned;
        private int errorCount;

        public SettingsUi(DynamicAudioPlugin plugin)
        {
            p = plugin;
        }

        public void Draw()
        {
            float w = Mathf.Min(580f, Screen.width - 24f);
            float h = Mathf.Min(700f, Screen.height * 0.88f);
            if (!positioned)
            {
                winRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                positioned = true;
            }
            winRect.width = w;
            winRect.height = h;
            if (winRect.x < 0f) winRect.x = 0f;
            if (winRect.y < 0f) winRect.y = 0f;
            if (winRect.xMax > Screen.width) winRect.x = Mathf.Max(0f, Screen.width - w);
            if (winRect.yMax > Screen.height) winRect.y = Mathf.Max(0f, Screen.height - h);

            winRect = GUILayout.Window(WindowId, winRect, DrawWindow,
                "Dynamic Audio v" + DynamicAudioPlugin.PluginVersion + "  (drag title bar to move)");
        }

        private void DrawWindow(int id)
        {
            int newTab = GUILayout.SelectionGrid(tab, Tabs, 4);
            if (newTab != tab)
            {
                tab = newTab;
                scroll = Vector2.zero;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            try
            {
                switch (tab)
                {
                    case 0: GeneralTab(); break;
                    case 1: OcclusionTab(); break;
                    case 2: ReverbTab(); break;
                    case 3: DelayTab(); break;
                    case 4: EffectsTab(); break;
                    case 5: CategoriesTab(); break;
                    default: PerformanceTab(); break;
                }
            }
            catch (System.Exception e)
            {
                errorCount++;
                if (errorCount <= 3) p.LogWarn("settings UI error: " + e.Message);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset ALL to defaults", GUILayout.Height(26f))) p.ResetDefaults();
            if (GUILayout.Button("Close", GUILayout.Height(26f))) Visible = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void GeneralTab()
        {
            Section("General");
            Toggle(p.cfgEnabled, "Enabled (master switch)");
            Toggle(p.cfgPauseMenuButton, "Show button in ESC > OPTIONS menu");
            FloatSlider(p.cfgMaxSoundDistance, 50f, 1000f, " m - max simulated sound distance");

            Section("Menu");
            GUILayout.Label("Menu key: " + (capturingKey ? "press any key (Esc cancels)" : p.cfgMenuKey.Value.ToString()));
            if (GUILayout.Button(capturingKey ? "Cancel key capture" : "Change menu key...", GUILayout.Width(220f)))
                capturingKey = !capturingKey;

            if (capturingKey && Event.current != null && Event.current.type == EventType.KeyDown)
            {
                KeyCode k = Event.current.keyCode;
                Event.current.Use();
                if (k != KeyCode.None)
                {
                    if (k != KeyCode.Escape) p.cfgMenuKey.Value = new KeyboardShortcut(k);
                    capturingKey = false;
                }
            }

            Section("Debug");
            Toggle(p.cfgDebug, "Show debug overlay");
        }

        private void OcclusionTab()
        {
            Section("Occlusion (wall muffling)");
            Toggle(p.cfgOcclusionEnabled, "Enabled");
            Toggle(p.cfgUseGameMask, "Use the game's own line-of-sight layers (recommended)");
            IntSlider(p.cfgRayCount, 1, 9, " rays - more = better openings/partial walls");
            FloatSlider(p.cfgRaySpread, 0f, 1f, " ray spread around the emitter");
            FloatSlider(p.cfgOcclusionVolumeFloor, 0f, 0.5f, " - volume at 100% occlusion");
            Section("Filtering");
            FloatSlider(p.cfgWallMuffleStrength, 0f, 2f, " - wall muffle strength");
            FloatSlider(p.cfgMinCutoffHz, 100f, 2000f, " Hz - deepest muffle cutoff");
        }

        private void ReverbTab()
        {
            Section("Room reverb");
            Toggle(p.cfgReverbEnabled, "Enabled");
            FloatSlider(p.cfgReverbIntensity, 0f, 1f, " - overall reverb intensity");
            FloatSlider(p.cfgRoomSizeMultiplier, 0.3f, 2f, "x - room size influence on decay");
            Section("Echo (after loud shots/blasts only)");
            Toggle(p.cfgEchoEnabled, "Enabled");
        }

        private void DelayTab()
        {
            Section("Sound travel delay (EXPERIMENTAL, off by default)");
            Toggle(p.cfgSoundDelayEnabled, "Enabled");
            GUILayout.Label(TravelDelay.Installed ? "Hook status: installed"
                : (TravelDelay.Failed ? "Hook status: unavailable (see BepInEx log)" : "Hook status: installs when enabled"));
            FloatSlider(p.cfgSoundSpeed, 50f, 500f, " m/s - speed of sound (343 = real)");
            FloatSlider(p.cfgSoundDelayMinDistance, 5f, 100f, " m - delay starts beyond this");
            FloatSlider(p.cfgSoundDelayMax, 0.2f, 3f, " s - maximum delay");
            GUILayout.Label("Delays the START of gunshots/explosions/vehicle one-shots. Lower speed = longer delay.");
        }

        private void EffectsTab()
        {
            Section("Shockwave");
            Toggle(p.cfgShockwaveEnabled, "Enabled");
            FloatSlider(p.cfgShockwaveRadius, 5f, 80f, " m - trigger radius for explosions");
            FloatSlider(p.cfgShockwaveCooldown, 0f, 5f, " s - minimum time between shockwaves");
            Section("Tinnitus (ringing ears - OFF by default)");
            Toggle(p.cfgTinnitusEnabled, "Enabled (turning off restores hearing instantly)");
            FloatSlider(p.cfgTinnitusExplosionRadius, 5f, 60f, " m - explosion trigger radius");
            FloatSlider(p.cfgTinnitusSensitivity, 0.1f, 3f, " - sustained-noise sensitivity");
            FloatSlider(p.cfgTinnitusRingVolume, 0f, 1f, " - ring loudness");
            FloatSlider(p.cfgTinnitusMuffle, 0f, 1f, " - temporary hearing muffle");
            FloatSlider(p.cfgTinnitusCooldown, 0f, 120f, " s - rest time between rings");
            Section("Distance / air absorption");
            FloatSlider(p.cfgAirAbsorption, 0f, 1f, " - high-frequency loss over distance");
        }

        private void CategoriesTab()
        {
            Section("Sound categories");
            Toggle(p.cfgProcessGunshots, "Gunshots");
            Toggle(p.cfgProcessExplosions, "Explosions");
            Toggle(p.cfgProcessImpacts, "Impacts / fly-bys");
            Toggle(p.cfgProcessVehicles, "Vehicles / turrets");
            Toggle(p.cfgProcessFootsteps, "Footsteps");
            Toggle(p.cfgProcessEnvironment, "Environment");
        }

        private void PerformanceTab()
        {
            Section("Performance");
            FloatSlider(p.cfgScanInterval, 0.25f, 5f, " s - scene scan interval");
            FloatSlider(p.cfgNearUpdateInterval, 0.05f, 0.5f, " s - near-sound update interval");
            FloatSlider(p.cfgMidUpdateInterval, 0.15f, 1f, " s - mid-distance update interval");
            IntSlider(p.cfgMaxSimulatedSounds, 4, 48, " max simultaneously simulated sounds");
            FloatSlider(p.cfgNearDistance, 10f, 100f, " m - high-quality LOD distance");
            FloatSlider(p.cfgMidDistance, 30f, 300f, " m - reduced-quality LOD distance");
        }

        private static void Section(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Box(title, GUILayout.ExpandWidth(true));
        }

        private static void Toggle(ConfigEntry<bool> e, string label)
        {
            bool v = GUILayout.Toggle(e.Value, label);
            if (v != e.Value) e.Value = v;
        }

        // Slider limits come from the config entry's own AcceptableValueRange so that merely drawing a
        // slider can never clamp-and-rewrite a legal value; the min/max arguments are only a fallback.
        private static void FloatSlider(ConfigEntry<float> e, float min, float max, string suffix)
        {
            AcceptableValueRange<float> range = e.Description != null ? e.Description.AcceptableValues as AcceptableValueRange<float> : null;
            if (range != null)
            {
                min = range.MinValue;
                max = range.MaxValue;
            }
            GUILayout.Label(e.Definition.Key + ": " + e.Value.ToString("F2") + suffix);
            float v = GUILayout.HorizontalSlider(e.Value, min, max);
            if (Mathf.Abs(v - e.Value) > 0.0001f) e.Value = v;
        }

        private static void IntSlider(ConfigEntry<int> e, int min, int max, string suffix)
        {
            AcceptableValueRange<int> range = e.Description != null ? e.Description.AcceptableValues as AcceptableValueRange<int> : null;
            if (range != null)
            {
                min = range.MinValue;
                max = range.MaxValue;
            }
            GUILayout.Label(e.Definition.Key + ": " + e.Value + suffix);
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(e.Value, min, max));
            if (v != e.Value) e.Value = v;
        }
    }
}
