using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using BepInEx;

using UnityEngine;

namespace ModAPI
{
    /*
     * Colours of the skill and other effects (Effects/<name>.prefab): every .txt in ModResources\EffectColours holds lines
     *
     *     <effect name> = <hue>, <saturation>, <brightness>
     *
     * e.g. "eff_sword_slash01 = 180, 1.2, 1" turns that effect's colours half way round the colour wheel. Hue in degrees
     * (-180 to 180), saturation and brightness as factors (1 = unchanged). Lines starting with # are comments; with the
     * same effect in two files the file later in alphabetical order wins.
     *
     * When the game loads an effect with a line, its particle colours (start colour, colour over lifetime and by speed,
     * trails), trail and line renderer colours, lights, material tints and coloured textures are turned. The game keeps
     * the loaded effect for the session, so a changed line is applied to it again at once (Set, Reload): what it looked
     * like before is kept to start from. Only effects with a line are touched.
     */
    public class EffectColours
    {
        public static readonly string FolderName = "ModResources" + Path.DirectorySeparatorChar + "EffectColours";
        //The file Dev Tools writes into
        public const string EditFileName = "colours.txt";

        private static EffectColours _Instance;
        public static EffectColours Instance { get { return _Instance ?? (_Instance = new EffectColours()); } }

        public string Folder { get { return Path.Combine(Paths.GameRootPath, FolderName); } }
        public string EditFile { get { return Path.Combine(Folder, EditFileName); } }

        //Effect name => colour change, from the files (and from Set until saved)
        private Dictionary<string, ColourShift> _Rules = new Dictionary<string, ColourShift>(StringComparer.OrdinalIgnoreCase);
        //Effects the game loaded this session, newest last
        private readonly Dictionary<string, GameObject> _Seen = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _Order = new List<string>();
        //What a changed effect looked like, and what was made for it
        private readonly Dictionary<GameObject, Snapshot> _Snapshots = new Dictionary<GameObject, Snapshot>();
        //Materials and textures of an earlier colour, still used by effects playing when the colour changed: destroyed a while later
        private readonly List<KeyValuePair<float, UnityEngine.Object>> _Retired = new List<KeyValuePair<float, UnityEngine.Object>>();
        private const float RetiredLifetime = 60f;

        private void DestroyRetired(bool all)
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _Retired.Count - 1; i >= 0; i--)
            {
                if (all || now - _Retired[i].Key > RetiredLifetime)
                {
                    if (_Retired[i].Value != null)
                    {
                        UnityEngine.Object.Destroy(_Retired[i].Value);
                    }
                    _Retired.RemoveAt(i);
                }
            }
        }

        private EffectColours()
        {
            Scan();
        }

        public int Count { get { return _Rules.Count; } }

        //-------------------------------------------------------------- files
        public void Scan()
        {
            var rules = new Dictionary<string, ColourShift>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Folder))
            {
                var files = new List<string>(Directory.GetFiles(Folder, "*.txt", SearchOption.AllDirectories));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    string[] lines;
                    try
                    {
                        lines = File.ReadAllLines(file, Encoding.UTF8);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[EffectColours] Could not read " + file + ": " + e.Message);
                        continue;
                    }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!TryParseLine(lines[i], out string name, out ColourShift shift, out bool isRule))
                        {
                            if (isRule)
                            {
                                Debug.LogWarning("[EffectColours] " + Path.GetFileName(file) + " line " + (i + 1) + " is not \"name = hue, saturation, brightness\": " + lines[i]);
                            }
                            continue;
                        }
                        rules[name] = shift;
                    }
                }
            }
            _Rules = rules;
            Debug.Log("[EffectColours] " + _Rules.Count + " effect colour(s) in " + FolderName);
        }

        //isRule: the line is not empty and not a comment
        private static bool TryParseLine(string line, out string name, out ColourShift shift, out bool isRule)
        {
            name = null;
            shift = ColourShift.Neutral;
            string text = (line ?? "").Trim();
            isRule = text.Length > 0 && !text.StartsWith("#");
            if (!isRule)
            {
                return false;
            }
            int eq = text.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }
            name = text.Substring(0, eq).Trim();
            return name.Length > 0 && ColourShift.TryParse(text.Substring(eq + 1), out shift);
        }

        /// <summary>
        /// Writes the effect's current colour change into colours.txt (other lines and comments are kept), removes its
        /// line when it has no change. Returns a message for the player.
        /// </summary>
        public string Save(string effect)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var lines = File.Exists(EditFile) ? new List<string>(File.ReadAllLines(EditFile, Encoding.UTF8)) : new List<string>
                {
                    "# Effect colours: <effect name> = <hue -180..180>, <saturation>, <brightness>   (1 = unchanged)",
                    "# Made by Dev Tools (F8 > Effects) or by hand. Any other .txt in this folder works the same.",
                };
                bool has = _Rules.TryGetValue(effect, out ColourShift shift) && !shift.IsNeutral;
                string newLine = has ? effect + " = " + shift : null;
                bool written = false;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (TryParseLine(lines[i], out string name, out _, out _) && string.Equals(name, effect, StringComparison.OrdinalIgnoreCase))
                    {
                        if (newLine != null && !written)
                        {
                            lines[i] = newLine;
                            written = true;
                        }
                        else
                        {
                            lines.RemoveAt(i);
                        }
                    }
                }
                if (newLine != null && !written)
                {
                    lines.Add(newLine);
                }
                File.WriteAllLines(EditFile, lines.ToArray(), new UTF8Encoding(false));
                return has ? "Saved " + effect + " into " + FolderName + Path.DirectorySeparatorChar + EditFileName + "."
                    : effect + " removed from " + EditFileName + ".";
            }
            catch (Exception e)
            {
                Debug.LogError("[EffectColours] Could not save " + effect + ": " + e);
                return "Could not save: " + e.Message;
            }
        }

        /// <summary>Reads the files again and applies them to every effect loaded this session. Returns how many effects changed.</summary>
        public int Reload()
        {
            Scan();
            int changed = 0;
            foreach (var seen in _Seen.ToList())
            {
                if (seen.Value == null || !NeedsApply(seen.Key, seen.Value))
                {
                    continue;
                }
                if (Apply(seen.Key, seen.Value))
                {
                    changed++;
                }
            }
            return changed;
        }

        //-------------------------------------------------------------- for Dev Tools (plain types, used through reflection)
        /// <summary>Names of the effects loaded this session, the most recent first.</summary>
        public string[] Recent()
        {
            var list = new List<string>();
            for (int i = _Order.Count - 1; i >= 0; i--)
            {
                list.Add(_Order[i]);
            }
            return list.ToArray();
        }

        /// <summary>Hue, saturation and brightness of an effect (0, 1, 1 when it has no change).</summary>
        public float[] Get(string effect)
        {
            ColourShift shift = _Rules.TryGetValue(effect ?? "", out ColourShift s) ? s : ColourShift.Neutral;
            return new[] { shift.Hue, shift.Saturation, shift.Brightness };
        }

        public bool HasRule(string effect)
        {
            return effect != null && _Rules.ContainsKey(effect);
        }

        /// <summary>Changes an effect's colours now (not saved until Save). Returns false when the game did not load it yet.</summary>
        public bool Set(string effect, float hue, float saturation, float brightness)
        {
            if (string.IsNullOrEmpty(effect))
            {
                return false;
            }
            var shift = new ColourShift(hue, saturation, brightness);
            if (shift.IsNeutral)
            {
                _Rules.Remove(effect);
            }
            else
            {
                _Rules[effect] = shift;
            }
            if (!_Seen.TryGetValue(effect, out GameObject prefab) || prefab == null)
            {
                return false;
            }
            if (NeedsApply(effect, prefab))
            {
                Apply(effect, prefab);
            }
            return true;
        }

        //-------------------------------------------------------------- loading
        //Effect prefabs are "Effects/<name>.prefab" (GameConfig.EffectPrefabPathFormat)
        private static string EffectName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string p = path.Replace('\\', '/');
            int at = p.IndexOf("effects/", StringComparison.OrdinalIgnoreCase);
            if (at < 0 || (at > 0 && p[at - 1] != '/'))
            {
                return null;
            }
            return Path.GetFileNameWithoutExtension(p);
        }

        //Called by ResourceRedirectManager for every prefab the game loads
        public void OnPrefabLoaded(string path, GameObject prefab)
        {
            string name = EffectName(path);
            if (name == null || prefab == null)
            {
                return;
            }
            _Seen[name] = prefab;
            _Order.Remove(name);
            _Order.Add(name);
            if (_Order.Count > 300)
            {
                _Order.RemoveAt(0);
            }
            //The game loads the effect again every time it plays: it is only coloured again when its line changed
            if (NeedsApply(name, prefab))
            {
                Apply(name, prefab);
            }
        }

        //The effect's colours are not those of its line (or it has colours and no line any more)
        private bool NeedsApply(string name, GameObject prefab)
        {
            bool hasRule = _Rules.TryGetValue(name, out ColourShift shift) && !shift.IsNeutral;
            _Snapshots.TryGetValue(prefab, out Snapshot current);
            return hasRule ? current == null || !current.Applied.Same(shift) : current != null;
        }

        /// <summary>Empties the list of loaded effects (Dev Tools: effects played after this are the ones listed).</summary>
        public void ClearRecent()
        {
            _Order.Clear();
        }

        //-------------------------------------------------------------- colours
        //Puts the effect back the way the game had it, then turns its colours by its rule (if any). True when something changed.
        private bool Apply(string name, GameObject prefab)
        {
            try
            {
                DestroyRetired(false);
                if (_Snapshots.TryGetValue(prefab, out Snapshot old))
                {
                    old.Restore(_Retired);
                    _Snapshots.Remove(prefab);
                }
                if (!_Rules.TryGetValue(name, out ColourShift shift) || shift.IsNeutral)
                {
                    return old != null;
                }
                var snapshot = new Snapshot(prefab);
                snapshot.Shift(shift);
                snapshot.Applied = shift;
                _Snapshots[prefab] = snapshot;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[EffectColours] Could not colour " + name + ": " + e);
                return false;
            }
        }

        //What one effect looked like before, and the materials and textures made for it
        private class Snapshot
        {
            private readonly GameObject _Prefab;
            private readonly List<KeyValuePair<ParticleSystem, ParticleColours>> _Particles = new List<KeyValuePair<ParticleSystem, ParticleColours>>();
            private readonly List<KeyValuePair<Renderer, Material[]>> _Materials = new List<KeyValuePair<Renderer, Material[]>>();
            private readonly List<KeyValuePair<TrailRenderer, Gradient>> _Trails = new List<KeyValuePair<TrailRenderer, Gradient>>();
            private readonly List<KeyValuePair<LineRenderer, Gradient>> _Lines = new List<KeyValuePair<LineRenderer, Gradient>>();
            private readonly List<KeyValuePair<Light, Color>> _Lights = new List<KeyValuePair<Light, Color>>();
            private readonly List<UnityEngine.Object> _Made = new List<UnityEngine.Object>();
            public ColourShift Applied;

            private static readonly string[] ColourProperties = { "_TintColor", "_Color", "_MainColor", "_BaseColor", "_EmissionColor", "_RimColor" };
            private static readonly string[] TextureProperties = { "_MainTex", "_BaseMap" };

            public Snapshot(GameObject prefab)
            {
                _Prefab = prefab;
                foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    _Particles.Add(new KeyValuePair<ParticleSystem, ParticleColours>(ps, ParticleColours.Of(ps)));
                }
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    _Materials.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
                }
                foreach (TrailRenderer t in prefab.GetComponentsInChildren<TrailRenderer>(true))
                {
                    _Trails.Add(new KeyValuePair<TrailRenderer, Gradient>(t, t.colorGradient));
                }
                foreach (LineRenderer l in prefab.GetComponentsInChildren<LineRenderer>(true))
                {
                    _Lines.Add(new KeyValuePair<LineRenderer, Gradient>(l, l.colorGradient));
                }
                foreach (Light light in prefab.GetComponentsInChildren<Light>(true))
                {
                    _Lights.Add(new KeyValuePair<Light, Color>(light, light.color));
                }
            }

            public void Shift(ColourShift shift)
            {
                foreach (var p in _Particles)
                {
                    if (p.Key != null)
                    {
                        p.Value.Shifted(shift).ApplyTo(p.Key);
                    }
                }
                foreach (var t in _Trails)
                {
                    if (t.Key != null)
                    {
                        t.Key.colorGradient = shift.Of(t.Value);
                    }
                }
                foreach (var l in _Lines)
                {
                    if (l.Key != null)
                    {
                        l.Key.colorGradient = shift.Of(l.Value);
                    }
                }
                foreach (var light in _Lights)
                {
                    if (light.Key != null)
                    {
                        light.Key.color = shift.Of(light.Value);
                    }
                }
                //Materials are shared with other effects: this effect gets its own copies
                var copies = new Dictionary<Material, Material>();
                foreach (var m in _Materials)
                {
                    if (m.Key == null)
                    {
                        continue;
                    }
                    Material[] shared = m.Value;
                    var replaced = new Material[shared.Length];
                    for (int i = 0; i < shared.Length; i++)
                    {
                        Material original = shared[i];
                        if (original == null)
                        {
                            continue;
                        }
                        if (!copies.TryGetValue(original, out Material copy))
                        {
                            copy = ShiftedCopy(original, shift);
                            copies[original] = copy;
                        }
                        replaced[i] = copy;
                    }
                    m.Key.sharedMaterials = replaced;
                }
            }

            private Material ShiftedCopy(Material original, ColourShift shift)
            {
                var copy = new Material(original) { name = original.name };
                _Made.Add(copy);
                foreach (string p in ColourProperties)
                {
                    if (copy.HasProperty(p))
                    {
                        copy.SetColor(p, shift.Of(copy.GetColor(p)));
                    }
                }
                foreach (string p in TextureProperties)
                {
                    if (!copy.HasProperty(p))
                    {
                        continue;
                    }
                    Texture texture = copy.GetTexture(p);
                    Texture2D shifted = texture == null ? null : ShiftedTexture(texture, shift);
                    if (shifted != null)
                    {
                        _Made.Add(shifted);
                        copy.SetTexture(p, shifted);
                    }
                }
                return copy;
            }

            //A copy of a coloured texture with its colours turned (copied through the GPU: bundle textures can't be read directly).
            //Grey textures, coloured only by the particle colours, are left as they are (null).
            private static Texture2D ShiftedTexture(Texture source, ColourShift shift)
            {
                int w = source.width, h = source.height;
                if (w <= 0 || h <= 0 || w * h > 2048 * 2048)
                {
                    return null;
                }
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    var read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                    read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    Color32[] px = read.GetPixels32();
                    bool coloured = false;
                    for (int i = 0; i < px.Length && !coloured; i += 7)
                    {
                        Color32 c = px[i];
                        int max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
                        coloured = c.a > 8 && max > 24 && max - min > max / 8;
                    }
                    if (!coloured)
                    {
                        UnityEngine.Object.Destroy(read);
                        return null;
                    }
                    for (int i = 0; i < px.Length; i++)
                    {
                        Color c = shift.Of((Color)px[i]);
                        px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), px[i].a);
                    }
                    read.SetPixels32(px);
                    var result = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
                    {
                        name = source.name + "_effectcolour",
                        wrapMode = source.wrapMode,
                        filterMode = source.filterMode,
                    };
                    result.SetPixels32(px);
                    result.Apply(true);
                    UnityEngine.Object.Destroy(read);
                    return result;
                }
                catch (Exception e)
                {
                    Debug.LogError("[EffectColours] Could not recolour the texture " + source.name + ": " + e.Message);
                    return null;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            //The materials and textures made for this colour go to retired (effects still playing use them)
            public void Restore(List<KeyValuePair<float, UnityEngine.Object>> retired)
            {
                foreach (var p in _Particles)
                {
                    if (p.Key != null)
                    {
                        p.Value.ApplyTo(p.Key);
                    }
                }
                foreach (var m in _Materials)
                {
                    if (m.Key != null)
                    {
                        m.Key.sharedMaterials = m.Value;
                    }
                }
                foreach (var t in _Trails)
                {
                    if (t.Key != null)
                    {
                        t.Key.colorGradient = t.Value;
                    }
                }
                foreach (var l in _Lines)
                {
                    if (l.Key != null)
                    {
                        l.Key.colorGradient = l.Value;
                    }
                }
                foreach (var light in _Lights)
                {
                    if (light.Key != null)
                    {
                        light.Key.color = light.Value;
                    }
                }
                float now = Time.realtimeSinceStartup;
                foreach (UnityEngine.Object made in _Made)
                {
                    if (made != null)
                    {
                        retired.Add(new KeyValuePair<float, UnityEngine.Object>(now, made));
                    }
                }
                _Made.Clear();
            }
        }

        //The colour settings of one particle system
        private class ParticleColours
        {
            public ParticleSystem.MinMaxGradient Start, OverLifetime, BySpeed, TrailLifetime, TrailOverTrail;

            public static ParticleColours Of(ParticleSystem ps)
            {
                return new ParticleColours
                {
                    Start = ps.main.startColor,
                    OverLifetime = ps.colorOverLifetime.color,
                    BySpeed = ps.colorBySpeed.color,
                    TrailLifetime = ps.trails.colorOverLifetime,
                    TrailOverTrail = ps.trails.colorOverTrail,
                };
            }

            public ParticleColours Shifted(ColourShift shift)
            {
                return new ParticleColours
                {
                    Start = shift.Of(Start),
                    OverLifetime = shift.Of(OverLifetime),
                    BySpeed = shift.Of(BySpeed),
                    TrailLifetime = shift.Of(TrailLifetime),
                    TrailOverTrail = shift.Of(TrailOverTrail),
                };
            }

            public void ApplyTo(ParticleSystem ps)
            {
                var main = ps.main;
                main.startColor = Start;
                var lifetime = ps.colorOverLifetime;
                lifetime.color = OverLifetime;
                var speed = ps.colorBySpeed;
                speed.color = BySpeed;
                var trails = ps.trails;
                trails.colorOverLifetime = TrailLifetime;
                trails.colorOverTrail = TrailOverTrail;
            }
        }

        //A turn of the colour wheel with saturation and brightness factors
        public struct ColourShift
        {
            public float Hue, Saturation, Brightness;

            public ColourShift(float hue, float saturation, float brightness)
            {
                Hue = Mathf.Clamp(hue, -180f, 180f);
                Saturation = Mathf.Clamp(saturation, 0f, 4f);
                Brightness = Mathf.Clamp(brightness, 0f, 4f);
            }

            public static ColourShift Neutral { get { return new ColourShift(0f, 1f, 1f); } }

            public bool Same(ColourShift other)
            {
                return Mathf.Abs(Hue - other.Hue) < 0.01f && Mathf.Abs(Saturation - other.Saturation) < 0.0001f && Mathf.Abs(Brightness - other.Brightness) < 0.0001f;
            }

            public bool IsNeutral { get { return Mathf.Abs(Hue) < 0.5f && Mathf.Abs(Saturation - 1f) < 0.005f && Mathf.Abs(Brightness - 1f) < 0.005f; } }

            public static bool TryParse(string text, out ColourShift shift)
            {
                shift = Neutral;
                string[] parts = (text ?? "").Split(',');
                if (parts.Length != 3)
                {
                    return false;
                }
                var values = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    {
                        return false;
                    }
                }
                shift = new ColourShift(values[0], values[1], values[2]);
                return true;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0.00}, {2:0.00}", Hue, Saturation, Brightness);
            }

            //HDR colours (glows brighter than white) keep their intensity
            public Color Of(Color c)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = (h + Hue / 360f + 1f) % 1f;
                s = Mathf.Clamp01(s * Saturation);
                v *= Brightness;
                Color result = Color.HSVToRGB(h, s, v, true);
                result.a = c.a;
                return result;
            }

            public Gradient Of(Gradient g)
            {
                if (g == null)
                {
                    return null;
                }
                GradientColorKey[] keys = g.colorKeys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].color = Of(keys[i].color);
                }
                var result = new Gradient { mode = g.mode };
                result.SetKeys(keys, g.alphaKeys);
                return result;
            }

            public ParticleSystem.MinMaxGradient Of(ParticleSystem.MinMaxGradient m)
            {
                ParticleSystem.MinMaxGradient result;
                switch (m.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        return new ParticleSystem.MinMaxGradient(Of(m.color));
                    case ParticleSystemGradientMode.TwoColors:
                        return new ParticleSystem.MinMaxGradient(Of(m.colorMin), Of(m.colorMax));
                    case ParticleSystemGradientMode.Gradient:
                        return m.gradient == null ? m : new ParticleSystem.MinMaxGradient(Of(m.gradient));
                    case ParticleSystemGradientMode.TwoGradients:
                        return m.gradientMin == null || m.gradientMax == null ? m : new ParticleSystem.MinMaxGradient(Of(m.gradientMin), Of(m.gradientMax));
                    case ParticleSystemGradientMode.RandomColor:
                        if (m.gradient == null)
                        {
                            return m;
                        }
                        result = new ParticleSystem.MinMaxGradient(Of(m.gradient));
                        result.mode = ParticleSystemGradientMode.RandomColor;
                        return result;
                    default:
                        return m;
                }
            }
        }
    }
}
