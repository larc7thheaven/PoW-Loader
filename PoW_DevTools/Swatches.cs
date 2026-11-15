using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * Colour samples for the recolour sliders: the colours a part or an effect has, and what the sliders make of them,
     * so a colour can be picked without replaying the skill or applying the recolour first.
     */
    internal static class Swatches
    {
        private const float Size = 20f;

        //A label and a row of colour boxes. The row has the same boxes in every event of a frame (the list is not changed while drawing).
        public static void Row(string label, IList<Color> colours)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(50f));
                if (colours == null || colours.Count == 0)
                {
                    GUILayout.Label("no colours found");
                    return;
                }
                Color old = GUI.color;
                foreach (Color c in colours)
                {
                    Rect r = GUILayoutUtility.GetRect(Size, Size, GUILayout.Width(Size), GUILayout.Height(Size));
                    if (Event.current.type == EventType.Repaint)
                    {
                        GUI.color = Color.black;
                        GUI.DrawTexture(r, Texture2D.whiteTexture);
                        GUI.color = Displayed(c);
                        GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), Texture2D.whiteTexture);
                    }
                }
                GUI.color = old;
                GUILayout.FlexibleSpace();
            }
        }

        //Glows brighter than white are shown at their colour, opaque
        private static Color Displayed(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max > 1f) c = new Color(c.r / max, c.g / max, c.b / max);
            c.a = 1f;
            return c;
        }

        //The effect colour change (as ModAPI's EffectColours does it)
        public static Color EffectShift(Color c, float hue, float saturation, float brightness)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = (h + hue / 360f + 1f) % 1f;
            s = Mathf.Clamp01(s * saturation);
            v *= brightness;
            Color result = Color.HSVToRGB(h, s, v, true);
            result.a = c.a;
            return result;
        }

        //The clothing recolour (as Recolor.Shifted does it, pixel by pixel)
        public static Color RecolourShift(Color c, HueShift shift)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (shift.KeepSkin && (h < 0.12f || h > 0.95f) && s > 0.12f && s < 0.6f && v > 0.35f) return c;
            if (s > 0.08f) h = (h + shift.Hue / 360f + 1f) % 1f;
            s = Mathf.Clamp01(s * shift.Saturation);
            v = Mathf.Clamp01(v * shift.Brightness);
            Color n = Color.HSVToRGB(h, s, v);
            n.a = c.a;
            return n;
        }

        //Main colours of a texture: the biggest groups of similar visible colours of a small copy, cached per texture
        private static readonly Dictionary<int, List<Color>> _textures = new Dictionary<int, List<Color>>();

        public static List<Color> TextureColours(Texture source, int count)
        {
            var result = new List<Color>();
            if (source == null) return result;
            if (_textures.TryGetValue(source.GetInstanceID(), out List<Color> cached)) return cached.Take(count).ToList();
            const int size = 32;
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var all = new List<Color>();
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                Color32[] px = read.GetPixels32();
                UnityEngine.Object.Destroy(read);
                var buckets = new Dictionary<int, KeyValuePair<int, Vector3>>();
                foreach (Color32 c in px)
                {
                    if (c.a < 50) continue;
                    int key = (c.r >> 5) << 6 | (c.g >> 5) << 3 | (c.b >> 5);
                    buckets.TryGetValue(key, out var b);
                    buckets[key] = new KeyValuePair<int, Vector3>(b.Key + 1, b.Value + new Vector3(c.r, c.g, c.b));
                }
                foreach (var b in buckets.Values.OrderByDescending(b => b.Key).Take(10))
                {
                    Vector3 avg = b.Value / (b.Key * 255f);
                    all.Add(new Color(avg.x, avg.y, avg.z, 1f));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Could not read the colours of " + source.name + ": " + e.Message);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            _textures[source.GetInstanceID()] = all;
            return all.Take(count).ToList();
        }
    }
}
