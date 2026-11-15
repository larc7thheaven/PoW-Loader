using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * Wardrobe > Effects: the colours of skill and other effects (ModAPI's EffectColours). Effects appear in the list once
     * the game has loaded them (use the skill once), the most recent first. Hue, saturation and brightness change the
     * effect at once (seen the next time it plays); Save writes the line into ModResources\EffectColours\colours.txt,
     * which also works without Dev Tools and can be edited by hand.
     */
    internal static class EffectsPanel
    {
        private static Type _type;
        private static object _colours;

        //ModAPI's EffectColours, null with a ModAPI from before effect colours
        private static object Colours
        {
            get
            {
                if (_colours != null) return _colours;
                _type = Type.GetType("ModAPI.EffectColours, PoW_ModAPI");
                _colours = _type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return _colours;
            }
        }

        private static object Call(string method, params object[] args)
        {
            return _type.GetMethod(method).Invoke(_colours, args);
        }

        private static string _search = "", _searchShown;
        private static string[] _recent = new string[0];
        private static List<string> _shown = new List<string>();
        private static float _nextRefresh;
        private static Vector2 _scroll;
        private static string _selected, _loadedFor, _drawnSelected;
        //Who the colour is for: "" everyone, "player", or a character's model
        private static int _forMode;
        private static bool _needModel;
        private static string _forModel = "";
        private static string Target { get { return _forMode == 1 ? "player" : _forMode == 2 ? (_forModel ?? "").Trim() : ""; } }
        private static float _hue, _sat = 1f, _bright = 1f;
        private static float _applyAt = -1f;
        private static string _message = "";
        private static GUIStyle _small;

        public static void Draw()
        {
            if (Colours == null)
            {
                GUILayout.Label("Effect colours need the new PoW_ModAPI.dll.");
                return;
            }
            if (_small == null) _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            bool layout = Event.current.type == EventType.Layout;

            //The list follows the effects the game loads, read again twice a second (on layout only, the same rows every event)
            if (layout && (Time.realtimeSinceStartup >= _nextRefresh || _searchShown != _search))
            {
                _nextRefresh = Time.realtimeSinceStartup + 0.5f;
                _searchShown = _search;
                try { _recent = (string[])Call("Recent"); } catch (Exception e) { Debug.LogError("[DevTools] Effects: " + e.Message); _recent = new string[0]; }
                string s = _search.Trim();
                _shown = _recent.Where(n => s.Length == 0 || n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            GUILayout.Label("Effects the game loaded, the most recent first. Clear list, then use a skill: the list holds only its effects.", _small);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Search", GUILayout.Width(50f));
                _search = GUILayout.TextField(_search ?? "");
                //A fresh list: only the effects played after this are listed, e.g. those of the next skill used
                if (GUILayout.Button("Clear list", GUILayout.Width(80f)))
                {
                    try { Call("ClearRecent"); } catch (Exception e) { Debug.LogError("[DevTools] Effects: " + e.Message); }
                    _search = "";
                    _selected = null;
                    _loadedFor = null;
                    _nextRefresh = 0f;
                    _message = "List cleared: use a skill, its effects show up here.";
                }
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(230f));
            foreach (string name in _shown)
            {
                bool hasRule = (bool)Call("HasRule", name);
                string label = (name == _selected ? "<b>" : "") + name + (hasRule ? "  <color=#ff6a6a>●</color>" : "") + (name == _selected ? "</b>" : "");
                if (GUILayout.Button(label, _rowStyle ?? (_rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true }))) _selected = name;
            }
            if (_shown.Count == 0) GUILayout.Label(_recent.Length == 0 ? "No effects loaded yet." : "No effect matches.", _small);
            GUILayout.EndScrollView();

            //The effect drawn below is the one picked by the last layout (a click changes it for the next frame)
            if (layout) _drawnSelected = _selected;
            string sel = _drawnSelected;
            if (sel == null)
            {
                GUILayout.Label("Pick an effect.", _small);
                GUILayout.Label(_message, _small);
                return;
            }
            GUILayout.Label("<b>" + sel + "</b>", _small);
            string last = (string)Call("LastCaster", sel);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("For", GUILayout.Width(30f));
                if (GUILayout.Toggle(_forMode == 0, "Everyone", GUI.skin.button)) _forMode = 0;
                if (GUILayout.Toggle(_forMode == 1, "Player", GUI.skin.button)) _forMode = 1;
                if (GUILayout.Toggle(_forMode == 2, "Character:", GUI.skin.button)) _forMode = 2;
                _forModel = GUILayout.TextField(_forModel ?? "", GUILayout.Width(80f));
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Last used by: " + (last.Length > 0 ? last : "unknown (no caster, only \"Everyone\" works)"), _small);
                if (last.Length > 0 && last != "player" && GUILayout.Button("Use", GUILayout.Width(50f)))
                {
                    _forMode = 2;
                    _forModel = last;
                }
            }
            //Decided on layout only: a click or a key changes the controls drawn on the next frame, not in the middle of this one
            if (layout) _needModel = _forMode == 2 && Target.Length == 0;
            if (_needModel)
            {
                GUILayout.Label("Type the character's model (e.g. in0104) or press Use.", _small);
                GUILayout.Label(_message, _small);
                return;
            }

            string key = sel + "|" + Target;
            if (_loadedFor != key)
            {
                _loadedFor = key;
                float[] v = (float[])Call("Get", sel, Target);
                _hue = v[0]; _sat = v[1]; _bright = v[2];
                _applyAt = -1f;
            }

            float hue = Slider("Hue " + _hue.ToString("0"), _hue, -180f, 180f);
            float sat = Slider("Saturation " + _sat.ToString("0.00"), _sat, 0f, 2f);
            float bright = Slider("Brightness " + _bright.ToString("0.00"), _bright, 0f, 3f);
            if (hue != _hue || sat != _sat || bright != _bright)
            {
                _hue = hue; _sat = sat; _bright = bright;
                //Recolouring copies the effect's textures: done once the slider rests a moment, not on every step
                _applyAt = Time.realtimeSinceStartup + 0.3f;
            }
            if (_applyAt > 0f && Time.realtimeSinceStartup >= _applyAt)
            {
                _applyAt = -1f;
                Apply(sel);
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply")) Apply(sel);
                if (GUILayout.Button("Reset"))
                {
                    _hue = 0f; _sat = 1f; _bright = 1f;
                    Apply(sel);
                }
                if (GUILayout.Button("Save to file"))
                {
                    Apply(sel);
                    _message = (string)Call("Save", sel, Target);
                }
                if (GUILayout.Button("Reload files"))
                {
                    int changed = (int)Call("Reload");
                    _loadedFor = null;
                    _message = "Effect colour files read again, " + changed + " loaded effect(s) changed.";
                }
            }
            GUILayout.Label("Changes show the next time the effect plays. Save writes ModResources\\EffectColours\\colours.txt (edit it by hand too: [player: | model:] name = hue, saturation, brightness). A line for one character wins over the line for everyone.", _small);
            GUILayout.Label(_message, _small);
        }

        private static GUIStyle _rowStyle;

        private static float Slider(string label, float value, float min, float max)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(120f));
                return GUILayout.HorizontalSlider(value, min, max);
            }
        }

        private static void Apply(string sel)
        {
            try
            {
                bool shown = (bool)Call("Set", sel, Target, _hue, _sat, _bright);
                string who = Target.Length == 0 ? "everyone" : Target;
                _message = shown ? sel + " for " + who + ": " + _hue.ToString("0") + ", " + _sat.ToString("0.00") + ", " + _bright.ToString("0.00") + " (not saved yet)"
                    : "Set, the game has not loaded " + sel + " yet.";
            }
            catch (Exception e)
            {
                _message = "Could not change the effect: " + (e.InnerException ?? e).Message;
                Debug.LogError("[DevTools] Effect colours: " + e);
            }
        }
    }
}
