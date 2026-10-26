using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicAudio
{
    /// <summary>
    /// Puts a "DYNAMIC AUDIO" button into Ravenfield's own menu, right under the OPTIONS button
    /// (ESC > OPTIONS, and the main menu). It works by cloning the game's OPTIONS button, so it picks up the
    /// game's own look. If the game's menu cannot be found or cloned, a small IMGUI button is shown while
    /// the game is paused instead. The F11 key always works regardless.
    /// Everything here is wrapped so a failure can only ever mean "no button", never a broken game.
    /// </summary>
    public sealed class GameMenuIntegration
    {
        private readonly DynamicAudioPlugin plugin;
        private readonly SettingsUi ui;
        private readonly HashSet<int> clonedFrom = new HashSet<int>();
        private readonly HashSet<int> notOptions = new HashSet<int>();
        private readonly List<GameObject> clones = new List<GameObject>();
        private float nextScan;
        private int failures;
        private bool disabled;

        public GameMenuIntegration(DynamicAudioPlugin p, SettingsUi settings)
        {
            plugin = p;
            ui = settings;
        }

        /// <summary>True while at least one cloned in-game button is alive (the IMGUI fallback stays hidden).</summary>
        public bool HasInGameButton
        {
            get
            {
                for (int i = clones.Count - 1; i >= 0; i--)
                {
                    if (clones[i] == null) clones.RemoveAt(i);
                }
                for (int i = 0; i < clones.Count; i++)
                {
                    if (clones[i].activeInHierarchy) return true;
                }
                return false;
            }
        }

        public void Tick(float unscaledNow, bool wanted)
        {
            if (disabled) return;
            if (!wanted)
            {
                DestroyClones();
                return;
            }
            if (unscaledNow < nextScan) return;
            // The pause menu only exists while paused: look often then, and only rarely otherwise (main menu).
            nextScan = unscaledNow + (DynamicAudioPlugin.IsPaused ? 0.5f : 3f);
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                failures++;
                if (failures <= 2) plugin.LogWarn("Menu button scan failed: " + e.Message);
                if (failures >= 5)
                {
                    disabled = true;
                    plugin.LogWarn("Menu button integration switched off for this session (F11 still works).");
                }
            }
        }

        private void Scan()
        {
            PruneCaches();
            Button[] buttons = UnityEngine.Object.FindObjectsOfType<Button>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button b = buttons[i];
                if (b == null) continue;
                int id = b.GetInstanceID();
                if (clonedFrom.Contains(id) || notOptions.Contains(id)) continue;
                if (IsOurs(b.gameObject)) continue;
                if (!LooksLikeOptions(b))
                {
                    notOptions.Add(id);
                    continue;
                }
                clonedFrom.Add(id);
                Clone(b);
            }
        }

        private void PruneCaches()
        {
            if (notOptions.Count > 1024) notOptions.Clear();
            if (clonedFrom.Count > 256) clonedFrom.Clear();     // the parent.Find check below stops duplicates
        }

        private static bool IsOurs(GameObject go)
        {
            return go.name.StartsWith("DynamicAudio");
        }

        private static string LabelOf(Button b)
        {
            Text t = b.GetComponentInChildren<Text>(true);
            if (t == null || t.text == null) return string.Empty;
            return t.text.Trim().ToUpperInvariant();
        }

        private static bool LooksLikeOptions(Button b)
        {
            string l = LabelOf(b);
            return l == "OPTIONS" || l == "SETTINGS";
        }

        private void Clone(Button original)
        {
            Transform parent = original.transform.parent;
            if (parent == null) return;
            if (parent.Find("DynamicAudioMenuButton") != null) return;      // never twice

            GameObject go = UnityEngine.Object.Instantiate(original.gameObject, parent, false);
            go.name = "DynamicAudioMenuButton";
            go.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);

            // Keep only the visual/UI parts: any game script copied from the OPTIONS button could still open the game's own options.
            MonoBehaviour[] scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < scripts.Length; i++)
            {
                MonoBehaviour mb = scripts[i];
                if (mb == null) continue;
                string ns = mb.GetType().Namespace;
                if (ns != null && (ns.StartsWith("UnityEngine.UI") || ns.StartsWith("TMPro"))) continue;
                UnityEngine.Object.Destroy(mb);
            }

            Button nb = go.GetComponent<Button>();
            if (nb == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }
            nb.onClick = new Button.ButtonClickedEvent();
            nb.onClick.AddListener(OnClicked);

            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.text = "DYNAMIC AUDIO";

            // Without a layout group the buttons are placed by hand: put ours just underneath.
            RectTransform ort = original.GetComponent<RectTransform>();
            RectTransform nrt = go.GetComponent<RectTransform>();
            bool inLayout = parent.GetComponent<LayoutGroup>() != null;
            if (!inLayout && ort != null && nrt != null)
            {
                float h = Mathf.Max(20f, ort.rect.height);
                nrt.anchoredPosition = ort.anchoredPosition + new Vector2(0f, -(h + 6f));
            }
            go.SetActive(original.gameObject.activeSelf);
            clones.Add(go);
        }

        private void OnClicked()
        {
            try
            {
                ui.Visible = !ui.Visible;
            }
            catch (Exception) { }
        }

        public void DestroyClones()
        {
            for (int i = 0; i < clones.Count; i++)
            {
                if (clones[i] != null) UnityEngine.Object.Destroy(clones[i]);
            }
            clones.Clear();
            clonedFrom.Clear();
        }

        /// <summary>Fallback IMGUI button: only while the game is paused and no in-game button exists.</summary>
        public void DrawFallback()
        {
            if (!plugin.cfgPauseMenuButton.Value) return;
            if (ui.Visible) return;
            if (Time.timeScale > 0.001f) return;
            if (HasInGameButton) return;
            if (GUI.Button(new Rect(Screen.width - 190f, 10f, 180f, 30f), "Dynamic Audio"))
                ui.Visible = true;
        }
    }
}
