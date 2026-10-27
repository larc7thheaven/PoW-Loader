using System.Reflection;
using System.Text;

using HarmonyLib;

using Heluo.Data;
using Heluo.Manager;
using Heluo.Platform;
using Heluo.UI;

using UnityEngine;
using UnityEngine.UI;

namespace EnglishPatch.Hooks
{
    internal static class TextUtil
    {
        private static Font _Font;

        //Font for the English texts, chosen in the config file (see EnglishPatch.FontName). Looked up once, it is the same object every time.
        //A font that is not installed on this PC falls back to the Arial built into Unity.
        public static Font UIFont
        {
            get
            {
                if (_Font == null)
                {
                    _Font = LoadFont(EnglishPatch.FontName);
                }
                return _Font;
            }
        }

        private static Font _CombatFont;
        private static bool _CombatFontLoaded;

        //Font for the combat texts in the game's own font, chosen in the config file (see EnglishPatch.CombatFontName).
        //null when the setting is empty, those texts then keep the game's font.
        public static Font CombatFont
        {
            get
            {
                if (!_CombatFontLoaded)
                {
                    _CombatFontLoaded = true;
                    _CombatFont = string.IsNullOrEmpty(EnglishPatch.CombatFontName?.Trim()) ? null : LoadFont(EnglishPatch.CombatFontName);
                }
                return _CombatFont;
            }
        }

        private static Font _DialogueFont;
        private static bool _DialogueFontLoaded;

        //Font for the dialogue text in the talk box, chosen in the config file (see EnglishPatch.DialogueFontName).
        //The English font when the setting is empty.
        public static Font DialogueFont
        {
            get
            {
                if (!_DialogueFontLoaded)
                {
                    _DialogueFontLoaded = true;
                    _DialogueFont = string.IsNullOrEmpty(EnglishPatch.DialogueFontName?.Trim()) ? null : LoadFont(EnglishPatch.DialogueFontName);
                }
                return _DialogueFont ?? UIFont;
            }
        }

        //Dialogue texts of the talk box, registered by WGMessage_UpdateWidget_Hook
        public static readonly System.Collections.Generic.HashSet<WGText> DialogueTexts = new System.Collections.Generic.HashSet<WGText>();

        //Name Unity gives the game's own font (Kaiu, the Windows font DFKai-SB)
        private const string GameFont = "Kaiu";

        //Texts under root that are still written in the game's own font get the given font, the other fonts are left alone
        public static void ReplaceGameFont(Component root, Font font)
        {
            if (root == null || font == null)
            {
                return;
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.font != null && text.font.name == GameFont)
                {
                    text.font = font;
                }
            }
        }

        private static Font LoadFont(string name)
        {
            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (string.IsNullOrEmpty(name) || name.Trim().Equals("Arial", System.StringComparison.OrdinalIgnoreCase))
            {
                return arial;
            }

            name = name.Trim();
            string simplifiedName = Simplified(name);
            string[] installedFonts = Font.GetOSInstalledFontNames();

            //The exact name first, then the same name written with other spaces or capitals ("EdoSZ" for "Edo SZ")
            string match = null;
            foreach (string installed in installedFonts)
            {
                if (installed.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    match = installed;
                    break;
                }
            }
            if (match == null)
            {
                foreach (string installed in installedFonts)
                {
                    if (Simplified(installed) == simplifiedName)
                    {
                        match = installed;
                        break;
                    }
                }
            }

            if (match != null)
            {
                //Arial behind it for any character the chosen font does not have
                Font font = Font.CreateDynamicFontFromOSFont(new[] { match, "Arial" }, 20);
                if (font != null)
                {
                    Debug.Log("[EnglishPatch] Using the font " + match);
                    return font;
                }
            }

            //Helping to find the right name: the installed fonts that share the start of the name
            string start = simplifiedName.Substring(0, Mathf.Min(3, simplifiedName.Length));
            var similar = new System.Collections.Generic.List<string>();
            foreach (string installed in installedFonts)
            {
                if (start.Length > 0 && Simplified(installed).Contains(start))
                {
                    similar.Add(installed);
                }
            }
            if (similar.Count > 0)
            {
                Debug.LogWarning("[EnglishPatch] Installed fonts with a similar name: " + string.Join(", ", similar.ToArray()));
            }
            Debug.LogWarning("[EnglishPatch] The font '" + name + "' is not installed on this PC (or only for one Windows user), using Arial instead");
            return arial;
        }

        //Lower case, without spaces, dashes and underscores
        private static string Simplified(string fontName)
        {
            return fontName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        private static readonly AccessTools.FieldRef<WGText, Text> _WGTextText = AccessTools.FieldRefAccess<WGText, Text>("text");

        //The Text a WGText shows
        public static Text TextOf(WGText wgText)
        {
            return wgText == null ? null : _WGTextText(wgText);
        }

        private static readonly AccessTools.FieldRef<WGStringTable, string> _StringTableId = AccessTools.FieldRefAccess<WGStringTable, string>("id");

        //The StringTable id a label of the prefab shows (SecondaryInterface0107...)
        public static string StringId(WGStringTable label)
        {
            return label == null ? null : _StringTableId(label);
        }

        //Horizontal world position of the drawn text (not of its rect), using its one line width and alignment
        public static void VisibleX(Text text, out float left, out float right)
        {
            Rect rect = text.rectTransform.rect;
            float width = text.preferredWidth;
            float xMin;
            switch ((int)text.alignment % 3)
            {
                case 0: xMin = rect.xMin; break;
                case 1: xMin = rect.center.x - width / 2f; break;
                default: xMin = rect.xMax - width; break;
            }
            left = text.rectTransform.TransformPoint(new Vector3(xMin, 0f, 0f)).x;
            right = text.rectTransform.TransformPoint(new Vector3(xMin + width, 0f, 0f)).x;
        }

        //Font size each text had before ShrinkToWidth changed it, texts are reused for different strings
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalWidthFitSize = new System.Collections.Generic.Dictionary<Text, int>();

        //Keeps a text on one line and shrinks it (never below minSize) until it is not wider than its rect
        public static void ShrinkToWidth(Text text, int minSize, float margin = 0f)
        {
            if (text == null)
            {
                return;
            }

            if (!_OriginalWidthFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalWidthFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            float width = text.rectTransform.rect.width - margin;
            while (text.preferredWidth > width && text.fontSize > minSize)
            {
                text.fontSize--;
            }
        }

        //Font size each text had before ShrinkToBox changed it, texts are reused for different strings
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalBoxFitSize = new System.Collections.Generic.Dictionary<Text, int>();

        //Wraps a text at spaces and shrinks it (never below minSize) until it fits its rect without breaking a word.
        //Unlike best fit it never hides the text, at the minimum size it may overflow a little.
        //verticalPadding is room kept free above and below the text.
        public static void ShrinkToBox(Text text, int minSize, float verticalPadding = 0f)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            if (!_OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalBoxFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.text = text.text.Replace('\u00A0', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Rect rect = text.rectTransform.rect;
            string[] words = text.text.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            while (text.fontSize > minSize && (text.preferredHeight > rect.height - 2f * verticalPadding || WidestWord(text, words) > rect.width))
            {
                text.fontSize--;
            }
        }

        //Wraps a text at spaces and shrinks it (never below minSize) until it takes at most maxLines lines and no word is wider than its rect.
        //Shares the original sizes with ShrinkToBox, so RestoreBoxFit gives it back too.
        public static void ShrinkToLines(Text text, int maxLines, int minSize)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            if (!_OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalBoxFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.text = text.text.Replace('\u00A0', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Rect rect = text.rectTransform.rect;
            string[] words = text.text.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            while (text.fontSize > minSize && (LineCount(text) > maxLines || WidestWord(text, words) > rect.width))
            {
                text.fontSize--;
            }
        }

        //Only used to count lines, one is enough for all texts. Made on first use, from the game's own thread.
        private static TextGenerator _LineCounter;

        private static int LineCount(Text text)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(text.rectTransform.rect.width, 100000f));
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            if (_LineCounter == null)
            {
                _LineCounter = new TextGenerator();
            }
            _LineCounter.Populate(text.text, settings);
            return _LineCounter.lineCount;
        }

        //The text broken at the space that makes its longest line the shortest
        public static string TwoLines(Text text, string content)
        {
            string best = content;
            float bestWidth = float.MaxValue;
            for (int i = content.IndexOf(' '); i >= 0; i = content.IndexOf(' ', i + 1))
            {
                string split = content.Substring(0, i) + "\n" + content.Substring(i + 1);
                float width = LinesWidth(text, split);
                if (width < bestWidth)
                {
                    best = split;
                    bestWidth = width;
                }
            }
            return best;
        }

        //Width of the widest line of a text (lines split at \n, not wrapped) at the text's current size
        public static float LinesWidth(Text text, string content)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            float widest = 0f;
            foreach (string line in content.Split('\n'))
            {
                widest = Mathf.Max(widest, text.cachedTextGeneratorForLayout.GetPreferredWidth(line, settings) / text.pixelsPerUnit);
            }
            return widest;
        }

        //Gives a text shrunk by ShrinkToBox its original size back, for reused texts that should not be fitted anymore
        public static void RestoreBoxFit(Text text)
        {
            if (text != null && _OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                text.fontSize = originalSize;
            }
        }

        private static float WidestWord(Text text, string[] words)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            float widest = 0f;
            foreach (string word in words)
            {
                widest = Mathf.Max(widest, text.cachedTextGeneratorForLayout.GetPreferredWidth(word, settings) / text.pixelsPerUnit);
            }
            return widest;
        }

        /*
         * Unity's Text has no justified alignment, so we do it ourselves: we let Unity break the lines at the rect width,
         * then widen the spaces of every line until it reaches the right edge. Extra space finer than one space is a space
         * with a smaller <size>, smaller sizes do not make the line taller. The last line of a paragraph stays left aligned.
         * The lines are joined with line breaks and the text no longer wraps by itself, so Unity keeps our lines as they are.
         * Color tags are kept, texts with other rich text tags (they change the width) or not aligned to the left are left alone.
         */
        public static string Justified(Text text, string content)
        {
            if (text == null || text.font == null || string.IsNullOrEmpty(content) || (int)text.alignment % 3 != 0)
            {
                return content;
            }

            //Color tags do not change the width of the text, so the lines are worked out without them and they are put back
            //where they were. Any other tag (size, bold...) changes the width, those texts are left alone.
            var tags = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>();
            var plain = new StringBuilder();
            int from = 0;
            foreach (System.Text.RegularExpressions.Match tag in _ColorTag.Matches(content))
            {
                plain.Append(content, from, tag.Index - from);
                tags.Add(new System.Collections.Generic.KeyValuePair<int, string>(plain.Length, tag.Value));
                from = tag.Index + tag.Length;
            }
            plain.Append(content, from, content.Length - from);
            if (plain.ToString().IndexOf('<') >= 0)
            {
                return content;
            }
            string original = content;
            content = plain.ToString();

            float width = text.rectTransform.rect.width;
            if (width <= 0f)
            {
                return original;
            }

            content = content.Replace('\u00A0', ' ');
            TextGenerationSettings wrapped = text.GetGenerationSettings(new Vector2(width, 100000f));
            wrapped.horizontalOverflow = HorizontalWrapMode.Wrap;
            wrapped.verticalOverflow = VerticalWrapMode.Overflow;
            wrapped.resizeTextForBestFit = false;
            wrapped.richText = true;
            TextGenerationSettings oneLine = wrapped;
            oneLine.horizontalOverflow = HorizontalWrapMode.Overflow;

            TextGenerator generator = new TextGenerator();
            generator.Populate(content, wrapped);
            //A copy, measuring the lines below fills the generator again
            UILineInfo[] lines = generator.GetLinesArray();

            float scale = text.pixelsPerUnit;
            float pair = generator.GetPreferredWidth("xx", oneLine);
            float spaceWidth = (generator.GetPreferredWidth("x x", oneLine) - pair) / scale;
            if (spaceWidth <= 0f)
            {
                return original;
            }

            //Glyph widths are rounded to whole pixels, so the width of a space at every smaller size is measured instead of assumed
            float[] smallSpace = new float[text.fontSize];
            for (int size = 1; size < text.fontSize; size++)
            {
                smallSpace[size] = (generator.GetPreferredWidth("x<size=" + size + "> </size>x", oneLine) - pair) / scale;
            }

            var result = new StringBuilder();
            int nextTag = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int start = lines[i].startCharIdx;
                int end = i + 1 < lines.Length ? lines[i + 1].startCharIdx : content.Length;
                if (start >= content.Length)
                {
                    break;
                }
                end = Mathf.Min(Mathf.Max(start, end), content.Length);
                bool paragraphEnd = i + 1 >= lines.Length || end == start || content[end - 1] == '\n';

                //The line without the spaces and line break around it
                while (start < end && content[start] == ' ')
                {
                    start++;
                }
                while (end > start && (content[end - 1] == ' ' || content[end - 1] == '\n' || content[end - 1] == '\r'))
                {
                    end--;
                }
                string line = content.Substring(start, end - start);

                if (i > 0)
                {
                    result.Append('\n');
                }

                int gaps = 0;
                foreach (char c in line)
                {
                    if (c == ' ')
                    {
                        gaps++;
                    }
                }

                float extra = width - generator.GetPreferredWidth(line, oneLine) / scale - 0.5f;
                bool justify = !paragraphEnd && gaps > 0 && extra > 0f;

                //Spreading the extra width over the spaces, keeping track of the rounding so the line still ends at the edge
                float perGap = justify ? extra / gaps : 0f;
                float added = 0f;
                int gap = 0;
                for (int c = start; c < end; c++)
                {
                    //The color tags that were in front of this character
                    while (nextTag < tags.Count && tags[nextTag].Key <= c)
                    {
                        result.Append(tags[nextTag].Value);
                        nextTag++;
                    }

                    result.Append(content[c]);
                    if (!justify || content[c] != ' ')
                    {
                        continue;
                    }

                    gap++;
                    float missing = perGap * gap - added;
                    int spaces = Mathf.FloorToInt(missing / spaceWidth);
                    result.Append(' ', spaces);
                    added += spaces * spaceWidth;

                    //The rest is one smaller space, the size whose width comes closest
                    float rest = missing - spaces * spaceWidth;
                    int best = 0;
                    for (int size = 1; size < smallSpace.Length; size++)
                    {
                        if (Mathf.Abs(smallSpace[size] - rest) < Mathf.Abs(smallSpace[best] - rest))
                        {
                            best = size;
                        }
                    }
                    if (best > 0)
                    {
                        result.Append("<size=").Append(best).Append("> </size>");
                        added += smallSpace[best];
                    }
                }
            }
            for (; nextTag < tags.Count; nextTag++)
            {
                result.Append(tags[nextTag].Value);
            }

            //Nothing to justify (a single line, or only paragraph ends), the text stays as it was
            return lines.Length < 2 ? original : result.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex _ColorTag = new System.Text.RegularExpressions.Regex("<color=[^>]*>|</color>");

        //Justifies a text in place, see Justified
        public static void Justify(Text text)
        {
            if (text == null || text.font == null)
            {
                return;
            }

            string justified;
            try
            {
                justified = Justified(text, text.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[EnglishPatch] Could not justify '" + text.text + "': " + e);
                return;
            }
            if (justified != text.text)
            {
                text.supportRichText = true;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.text = justified;
            }
        }

        //Shrinks a wrapping text until it is not taller than its rect, never below minSize. It is not truncated, so a very long text may still overflow a little.
        public static void ShrinkToHeight(Text text, int minSize)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            text.text = text.text.Replace(' ', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            float height = text.rectTransform.rect.height;
            while (text.preferredHeight > height && text.fontSize > minSize)
            {
                text.fontSize--;
            }
        }

        //The screen a widget belongs to: the closest UIForm above it, also when they are hidden
        public static UIForm OwningForm(Component component)
        {
            for (Transform t = component.transform; t != null; t = t.parent)
            {
                UIForm form = t.GetComponent<UIForm>();
                if (form != null)
                {
                    return form;
                }
            }
            return null;
        }

        //A width in the local units of a rect, converted to world units
        public static float WorldWidth(RectTransform rect, float localWidth)
        {
            return rect.TransformVector(new Vector3(localWidth, 0f, 0f)).x;
        }

        private static readonly System.Text.RegularExpressions.Regex _SizeTag = new System.Text.RegularExpressions.Regex(@"<size=(\d+)>");

        //Scales the <size=N> rich text tags of a text, so they shrink together with the font size
        public static string ScaleSizeTags(string text, float scale)
        {
            return _SizeTag.Replace(text, m => "<size=" + Mathf.Max(1, Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * scale)) + ">");
        }

        //Shrinks a text to fit its rect and lets it wrap at spaces
        public static void FitToBox(Text text, int minSize)
        {
            text.text = text.text.Replace(' ', ' ');
            if (!text.resizeTextForBestFit)
            {
                text.resizeTextMaxSize = text.fontSize;
                text.resizeTextMinSize = minSize;
                text.resizeTextForBestFit = true;
            }
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        //Short texts stay on one line, longer ones wrap and may grow downwards
        public static void FitWrapping(Text text)
        {
            if (text.text.Length <= 20)
            {
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            else
            {
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }
    }

    [HarmonyPatch]
    public class WGText_setText_Hook
    {
        public static MethodBase TargetMethod()
        {
            return typeof(WGText).GetProperty("Text").GetSetMethod();
        }

        public static void Postfix(ref WGText __instance, ref Text ___text)
        {
            //Debug.Log("WGText_setText_Hook");
            bool dialogue = TextUtil.DialogueTexts.Contains(__instance);
            ___text.font = dialogue ? TextUtil.DialogueFont : TextUtil.UIFont;
            ___text.fontSize = dialogue && EnglishPatch.DialogueFontSize > 0 ? EnglishPatch.DialogueFontSize : 20;
            TextUtil.FitWrapping(___text);
        }
    }

    /*
     * The dialogue text of the talk box can have its own font and size (DialogueFont and DialogueFontSize in the config file).
     * The message is registered the first time it is shown, from then on WGText_setText_Hook gives it that font. The first
     * line was already set before it was registered, so it is styled here once more.
     */
    [HarmonyPatch(typeof(WGMessage), "UpdateWidget")]
    public class WGMessage_UpdateWidget_Hook
    {
        public static void Postfix(WGText ___message)
        {
            if (___message == null || !TextUtil.DialogueTexts.Add(___message))
            {
                return;
            }

            Text text = TextUtil.TextOf(___message);
            if (text != null)
            {
                text.font = TextUtil.DialogueFont;
                text.fontSize = EnglishPatch.DialogueFontSize > 0 ? EnglishPatch.DialogueFontSize : 20;
                TextUtil.FitWrapping(text);
            }
        }
    }

    /*
     * Quest results: rewards were "{item}x{count}" with nothing between them ("Lithospermum Angelica Oilx2"). They are written
     * "{item} 2 pcs" and "{item} 1 pc" instead. Money ("300 Money") has no count and stays as it is.
     */
    [HarmonyPatch(typeof(WGEvaluationReward), "UpdateWidget")]
    public class WGEvaluationReward_UpdateWidget_Hook
    {
        private static readonly System.Text.RegularExpressions.Regex _Count = new System.Text.RegularExpressions.Regex(@"\s*[xX×](\d+)$");

        public static void Postfix(WGText ___content, WGImage ___icon)
        {
            Text text = TextUtil.TextOf(___content);
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            text.text = _Count.Replace(text.text, m => " " + m.Groups[1].Value + (m.Groups[1].Value == "1" ? " pc" : " pcs"));

            //Long item names ran out of the reward box to the left, under the item icon
            TextUtil.ShrinkToWidth(text, MinSize);
            PlaceReward(text, ___icon);
        }

        /*
         * Rewards were centered while the criteria above them are left aligned. They are left aligned now and start where the
         * criteria texts start, never left of the item icon (with a small gap). A reward wider than the room from there to the
         * right edge of its box shrinks to fit. The rows are reused for every reward, so the position is always taken from the
         * one the game gave the text.
         */
        private static void PlaceReward(Text text, WGImage icon)
        {
            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
            text.alignment = TextAnchor.MiddleLeft;

            float boxLeft = rect.TransformPoint(new Vector3(rect.rect.xMin, 0f, 0f)).x;
            float boxRight = rect.TransformPoint(new Vector3(rect.rect.xMax, 0f, 0f)).x;

            //Where the criteria texts start, or the left of the box
            float start = boxLeft;
            Text criteria = CriteriaText(text);
            if (criteria != null)
            {
                TextUtil.VisibleX(criteria, out start, out _);
            }

            RectTransform iconRect = icon == null ? null : icon.transform as RectTransform;
            if (iconRect != null)
            {
                Vector3[] corners = new Vector3[4];
                iconRect.GetWorldCorners(corners);
                start = Mathf.Max(start, corners[2].x + rect.TransformVector(new Vector3(IconGap, 0f, 0f)).x);
            }

            float room = boxRight - start;
            while (rect.TransformVector(new Vector3(text.preferredWidth, 0f, 0f)).x > room && text.fontSize > MinSize)
            {
                text.fontSize--;
            }

            rect.anchoredPosition = origin + new Vector2(rect.InverseTransformVector(new Vector3(start - boxLeft, 0f, 0f)).x, 0f);
        }

        //Text of the first criteria line of the result screen the reward belongs to
        private static Text CriteriaText(Text reward)
        {
            UIEvaluation form = reward.GetComponentInParent<UIEvaluation>();
            WGEvaluationPoint[] points = form == null ? null : _Points(form);
            if (points == null)
            {
                return null;
            }

            foreach (WGEvaluationPoint point in points)
            {
                Text text = point == null ? null : TextUtil.TextOf(_Description(point));
                if (text != null)
                {
                    return text;
                }
            }
            return null;
        }

        private const int MinSize = 14;
        //Room left between the item icon and the text
        private const float IconGap = 8f;
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly AccessTools.FieldRef<UIEvaluation, WGEvaluationPoint[]> _Points = AccessTools.FieldRefAccess<UIEvaluation, WGEvaluationPoint[]>("evaluationPoint");
        private static readonly AccessTools.FieldRef<WGEvaluationPoint, WGText> _Description = AccessTools.FieldRefAccess<WGEvaluationPoint, WGText>("description");
    }

    /*
     * Some texts are written straight into a screen's prefab in Chinese, not taken from the text files, so no translation
     * reaches them. They are replaced when the screen is shown, and get the English font.
     */
    [HarmonyPatch(typeof(UIEvaluation), "Show")]
    public class UIEvaluation_Show_FixedTexts_Hook
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> _Texts = new System.Collections.Generic.Dictionary<string, string>
        {
            //Bottom of the quest result screen, same wording as SecondaryInterface2903
            { "按下任意鍵繼續遊戲", "Press Any Key to Continue" },
            { "按下任意键继续游戏", "Press Any Key to Continue" },
        };

        public static void Postfix(UIEvaluation __instance)
        {
            foreach (Text text in __instance.GetComponentsInChildren<Text>(true))
            {
                if (text.text != null && _Texts.TryGetValue(text.text.Trim(), out string english))
                {
                    text.text = english;
                    text.font = TextUtil.UIFont;
                    //The box is as wide as the Chinese text, the English one wrapped and its second line was cut off
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
        }
    }

    [HarmonyPatch(typeof(UICommission), "Show")]
    public class UICommission_Show_Hook
    {
        public static void Postfix(ref UICommission __instance)
        {
            //Debug.Log("UICommission_Show_Hook");
            //Adjusting some elements to make the UI less scuffed (bc English texts are longer)
            RectTransform dynamicCanvas = (RectTransform)__instance.transform.Find("DynamicCanvas");
            RectTransform proclamation = (RectTransform)dynamicCanvas.transform.Find("Proclamation");
            Transform limit = (Transform)proclamation.transform.Find("Limit");

            //Updating the Credits Button Text
            RectTransform limit_Icon = (RectTransform)limit.Find("Limit_Icon");
            RectTransform limit_Title = (RectTransform)limit.Find("Limit_Title");
            RectTransform limit_Content = (RectTransform)limit.Find("Limit_Content");

            limit_Icon.localPosition = new Vector3(-295f, -384f, 0);
            limit_Title.localPosition = new Vector3(-170f, -384f, 0);
            limit_Content.localPosition = new Vector3(-11.37f, -385.82f, 0);
        }
    }

    [HarmonyPatch(typeof(WGStringTable), "UpdateString")]
    public class WGStringTable_UpdateString_Hook
    {
        //Labels whose English text is wider than the box behind them, shrunk to the width of their box.
        //With each label id the room to keep free at both ends together, for labels on rounded backgrounds.
        private static readonly System.Collections.Generic.Dictionary<string, float> _FitWidth = new System.Collections.Generic.Dictionary<string, float>
        {
            //Relationship screen: Friendship bar label, Send Gifts and Outings buttons
            { "General_Favorability", 0f }, { "SecondaryInterface0808", 0f }, { "SecondaryInterface0803", 0f }, { "SecondaryInterface0804", 0f },
            //Relationship screen: the small "Ability" tag in front of every level
            { "SecondaryInterface0809", 10f },
            //Refine Medicine: "Amount to Refine", kept on one line
            { "Refining_Count", 0f },
        };
        private const int MinTitleSize = 14;

        //Labels of _FitWidth whose translation has a size tag ("<size=26>Amount to Refine</size>"), it is removed so they can shrink
        private static readonly System.Collections.Generic.HashSet<string> _DropSizeTags = new System.Collections.Generic.HashSet<string>
        {
            "Refining_Count",
        };
        private static readonly System.Text.RegularExpressions.Regex _SizeTags = new System.Text.RegularExpressions.Regex(@"</?size(=\d+)?>");

        //Labels a little smaller than the size the game gives them, and by how much
        private static readonly System.Collections.Generic.Dictionary<string, int> _Smaller = new System.Collections.Generic.Dictionary<string, int>
        {
            //"Select Gear" on the Gear tab
            { "Equip_Select", 2 },
        };

        //"Medicine" in the tab bar of the character screen (Status, Skills, Gear, Medicine, Items) ran into the slanted edge
        //of the Items tab. It is a little smaller and moves a little closer to its icon (measured at 1920x1080).
        private const string MedicineId = "General_Medicine";
        private const int MedicineSmaller = 2;
        private const float MedicineShift = -4f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _MedicineOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void FitMedicineTab(Text text, string id)
        {
            if (id != MedicineId || !(TextUtil.OwningForm(text) is UIFormTab))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalSize[text] = originalSize;
            }
            text.fontSize = originalSize - MedicineSmaller;

            RectTransform rect = text.rectTransform;
            if (!_MedicineOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _MedicineOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(MedicineShift, 0f);
        }

        //Size the game gave each of those labels, they may be updated more than once
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGStringTable __instance, string ___id)
        {
            //Debug.Log("WGStringTable_UpdateString_Hook");
            Text text = __instance.GetComponent<Text>();
            text.font = TextUtil.UIFont;
            TextUtil.FitWrapping(text);

            if (_FitWidth.TryGetValue(___id, out float margin))
            {
                //A size tag from the translation would keep the text at that size whatever font size we try
                if (_DropSizeTags.Contains(___id))
                {
                    text.text = _SizeTags.Replace(text.text, "");
                }
                TextUtil.ShrinkToWidth(text, MinTitleSize, margin);
            }

            SaveLoadTab.Fit(text, ___id);
            PawnshopTabs.Fit(text);
            FitMedicineTab(text, ___id);

            if (_Smaller.TryGetValue(___id, out int smaller))
            {
                if (!_OriginalSize.TryGetValue(text, out int originalSize))
                {
                    originalSize = text.fontSize;
                    _OriginalSize[text] = originalSize;
                }
                text.fontSize = originalSize - smaller;
            }
        }
    }

    [HarmonyPatch(typeof(UIBattleStatus), "UpdateView")]
    public class UIBattleStatus_UpdateView_Hook
    {
        public static void Postfix(ref UIBattleStatus __instance)
        {
            //Debug.Log("UIBattleStatus_UpdateView_Hook");
            __instance.FinalCounterAttack.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalCrit.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalDodge.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalHit.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalParry.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;

            //The bio broke in the middle of words (the game turns the spaces into non-breaking spaces) and ran over the top of
            //its box. It wraps at spaces again and shrinks until it fits the box. The text area (200 high) is a little taller
            //than the light box drawn behind it (about 190), so some room is kept free above and below, fonts with tall lines
            //(Yu Gothic) otherwise stuck out of the box.
            TextUtil.ShrinkToBox(TextUtil.TextOf(__instance.Introduction), BioMinSize, BioPadding);

            //The inner art and gear names under their icons took 3 lines and ran into the icon ("Martial Code of the Fierce
            //Marquis"), or were cut at the right ("Cordierite Handwraps"). They shrink until they take at most 2 lines.
            foreach (WGPropsIntroduction slot in new[] { __instance.Mantra, __instance.Weapon, __instance.Cloth, __instance.Jewelry })
            {
                if (slot != null)
                {
                    TextUtil.ShrinkToLines(TextUtil.TextOf(_PropsName(slot)), NameLines, NameMinSize);
                }
            }
        }

        private const int BioMinSize = 12;
        private const float BioPadding = 8f;
        private const int NameLines = 2;
        private const int NameMinSize = 12;

        private static readonly AccessTools.FieldRef<WGPropsIntroduction, WGText> _PropsName = AccessTools.FieldRefAccess<WGPropsIntroduction, WGText>("propsName");
    }

    /*
     * The label under a buff or debuff icon in combat sits on a dark tab (OverlayIcon), which reads poorly behind the English
     * labels. Its images are hidden (HideBuffLabelBackground in the config file), the round icon and the label text stay.
     * Without the tab the small label stood straight on the battlefield and looked washed out on light ground, so it gets a
     * thin dark outline instead. Done whenever an icon is set up or its label changes, the icons are reused for every buff.
     */
    [HarmonyPatch]
    public class WGBufferIcon_LabelBackground_Hook
    {
        private static readonly AccessTools.FieldRef<WGBufferIcon, GameObject> _Tab = AccessTools.FieldRefAccess<WGBufferIcon, GameObject>("OverlayIcon");
        private static readonly AccessTools.FieldRef<WGBufferIcon, Image> _Icon = AccessTools.FieldRefAccess<WGBufferIcon, Image>("Icon");
        private static readonly AccessTools.FieldRef<WGBufferIcon, Text> _Label = AccessTools.FieldRefAccess<WGBufferIcon, Text>("Overlay");

        private static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Vector2 OutlineDistance = new Vector2(1f, -1f);

        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WGBufferIcon), "SetBufferOverlay");
            yield return AccessTools.Method(typeof(WGBufferIcon), "SetIcon");
        }

        public static void Postfix(WGBufferIcon __instance)
        {
            GameObject tab = _Tab(__instance);
            if (!EnglishPatch.HideBuffLabelBackground || tab == null)
            {
                return;
            }

            Image icon = _Icon(__instance);
            foreach (Image image in tab.GetComponentsInChildren<Image>(true))
            {
                if (image != icon)
                {
                    image.enabled = false;
                }
            }

            Text label = _Label(__instance);
            if (label != null && label.GetComponent<Outline>() == null)
            {
                Outline outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = OutlineColor;
                outline.effectDistance = OutlineDistance;
            }
        }
    }

    /*
     * The label under a stacking buff icon in combat is "{name}\n{level}" ("Confused" in orange, "Lv 1" below). Its box is as
     * wide as the icon, so the name broke in the middle ("Conf / used") and pushed the level out of the box.
     * Each line stays whole now, and the label shrinks until its widest line fits the space between two icons (a little
     * less, so labels of icons next to each other never touch).
     */
    [HarmonyPatch(typeof(WGBufferIcon), "SetBufferOverlay")]
    public class WGBufferIcon_SetBufferOverlay_Hook
    {
        private const int MinSize = 10;
        //Part of the distance between two icons a label may take
        private const float RoomShare = 0.95f;

        private static readonly AccessTools.FieldRef<WGBufferIcon, Text> _Overlay = AccessTools.FieldRefAccess<WGBufferIcon, Text>("Overlay");
        private static readonly AccessTools.FieldRef<WGBufferGroup, System.Collections.Generic.List<WGBufferIcon>> _Icons =
            AccessTools.FieldRefAccess<WGBufferGroup, System.Collections.Generic.List<WGBufferIcon>>("bufferItems");

        //Size each label has in the prefab, the icons are reused for every buff
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGBufferIcon __instance, int overlay)
        {
            Text label = _Overlay(__instance);
            if (overlay == 0 || label == null || string.IsNullOrEmpty(label.text))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(label, out int size))
            {
                size = label.fontSize;
                _OriginalSize[label] = size;
            }
            label.fontSize = size;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = label.text.Replace('\u00A0', ' ');

            float room = Mathf.Abs(label.rectTransform.InverseTransformVector(new Vector3(WorldRoom(__instance), 0f, 0f)).x);
            while (label.fontSize > MinSize && TextUtil.LinesWidth(label, label.text) > room)
            {
                label.fontSize--;
            }
        }

        //The icon row each icon belongs to, found once (the icons stay in their row)
        private static readonly System.Collections.Generic.Dictionary<WGBufferIcon, WGBufferGroup> _Group = new System.Collections.Generic.Dictionary<WGBufferIcon, WGBufferGroup>();

        //The distance between two icons of the group, or the icon's own width when the group has a single icon
        private static float WorldRoom(WGBufferIcon icon)
        {
            if (!_Group.TryGetValue(icon, out WGBufferGroup group))
            {
                group = icon.GetComponentInParent<WGBufferGroup>();
                _Group[icon] = group;
            }
            System.Collections.Generic.List<WGBufferIcon> icons = group != null ? _Icons(group) : null;
            if (icons != null && icons.Count >= 2 && icons[0] != null && icons[1] != null)
            {
                return Mathf.Abs(icons[1].transform.position.x - icons[0].transform.position.x) * RoomShare;
            }
            RectTransform rect = icon.Icon != null ? icon.Icon.rectTransform : (RectTransform)icon.transform;
            return Mathf.Abs(rect.TransformVector(new Vector3(rect.rect.width, 0f, 0f)).x);
        }
    }

    //The skill row of the battle status window (a character's status in combat): the same as the skill bar
    [HarmonyPatch(typeof(UIBattleStatus), "set_all_button")]
    public class UIBattleStatus_SetAllButton_Hook
    {
        public static void Postfix(UIBattleStatus __instance)
        {
            UIBattleUnitMenu_SetAllButton_Hook.FitNames(__instance.Skill_Buttons, __instance.Special_Skill_Button);
        }
    }

    /*
     * The skill bar at the bottom of the battle screen: long skill names ran into the names next to them or were cut.
     * Each name gets the room between two skills (a little less, so neighbours keep a gap), goes on two lines if one is
     * too wide, and only shrinks if two lines are still too wide.
     */
    [HarmonyPatch(typeof(UIBattleUnitMenu), "set_all_button")]
    public class UIBattleUnitMenu_SetAllButton_Hook
    {
        private const int MinSize = 12;
        //Part of the distance between two skills a name may take
        private const float RoomShare = 0.92f;

        private static readonly AccessTools.FieldRef<WGSkillBtn, Text> _SkillName = AccessTools.FieldRefAccess<WGSkillBtn, Text>("skill_name");

        //Size each name has in the prefab, the buttons are reused for every unit
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGSkillBtn[] ___skill_buttons, WGSkillBtn ___special_skill_button)
        {
            FitNames(___skill_buttons, ___special_skill_button);
        }

        //Also used for the skill row of the battle status window (UIBattleStatus_SetAllButton_Hook)
        public static void FitNames(WGSkillBtn[] buttons, WGSkillBtn special)
        {
            if (buttons == null || buttons.Length < 2 || buttons[0] == null || buttons[1] == null)
            {
                return;
            }

            float room = Mathf.Abs(buttons[1].transform.position.x - buttons[0].transform.position.x) * RoomShare;
            foreach (WGSkillBtn button in buttons)
            {
                Fit(button, room);
            }
            Fit(special, room);
        }

        private static void Fit(WGSkillBtn button, float worldRoom)
        {
            Text name = button == null ? null : _SkillName(button);
            if (name == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(name, out int size))
            {
                size = name.fontSize;
                _OriginalSize[name] = size;
            }
            name.fontSize = size;
            name.resizeTextForBestFit = false;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.verticalOverflow = VerticalWrapMode.Overflow;

            float room = Mathf.Abs(name.rectTransform.InverseTransformVector(new Vector3(worldRoom, 0f, 0f)).x);
            string oneLine = name.text.Replace('\n', ' ').Replace('\u00A0', ' ').Trim();
            name.text = TextUtil.LinesWidth(name, oneLine) <= room ? oneLine : TextUtil.TwoLines(name, oneLine);
            while (name.fontSize > MinSize && TextUtil.LinesWidth(name, name.text) > room)
            {
                name.fontSize--;
            }
        }
    }

    [HarmonyPatch(typeof(UIQuest), "SetTrackedQuest")]
    public class UIQuest_SetTrackedQuest_Hook
    {
        public static void Postfix(ref UIQuest __instance, ref WGText ___questName, ref WGText ___description)
        {
            //Debug.Log("UIQuest_SetTrackedQuest_Hook");
            ___questName.GetComponent<Text>().fontSize = 18;
            ___description.GetComponent<Text>().fontSize = 16;
            //Longer quest texts still ran out of the box at 16, those shrink until they fit
            TextUtil.ShrinkToHeight(___description.GetComponent<Text>(), 12);
        }
    }

    [HarmonyPatch(typeof(WGBattleInfo), "UpdateInfo")]
    public class WGBattleInfo_UpdateInfo_Hook
    {
        public static void Postfix(Text ___Name)
        {
            //Debug.Log("WGBattleInfo_UpdateInfo_Hook");
            ___Name.font = TextUtil.UIFont;
            ___Name.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }

    /*
     * WGPaoMaDengText is a marquee: text taller (or wider) than its box scrolls through after 2 seconds.
     * For the skill effect and the course description we shrink the font until the text fits the box, so it can be read at once.
     * Text that does not even fit at the minimum size still scrolls like before. Other marquees (like the inner art effect) stay as they are.
     */
    [HarmonyPatch(typeof(WGPaoMaDengText), "AddMessage")]
    public class WGPaoMaDengText_AddMessage_Hook
    {
        private const int MinFontSize = 16;

        //Marquees that shrink to fit, registered by the widgets owning them (see below)
        public static readonly System.Collections.Generic.HashSet<WGPaoMaDengText> FitToBox = new System.Collections.Generic.HashSet<WGPaoMaDengText>();

        //Marquees whose text is also justified
        public static readonly System.Collections.Generic.HashSet<WGPaoMaDengText> Justify = new System.Collections.Generic.HashSet<WGPaoMaDengText>();

        //Font size the prefab gave each marquee text, before we started shrinking it
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalFontSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Prefix(WGPaoMaDengText __instance, ref string msg, Text ___pmdText, Image ___mask)
        {
            //The original measures the text right after setting it, so the English font has to be set before that
            ___pmdText.font = TextUtil.UIFont;

            if (msg == null || !FitToBox.Contains(__instance) || __instance.direction != WGPaoMaDengText.Direction.Vertical || ___mask == null)
            {
                return;
            }

            //StringTool.Replace turned the spaces into non-breaking spaces, which made English break in the middle of words
            msg = msg.Replace(' ', ' ');

            if (!_OriginalFontSize.TryGetValue(___pmdText, out int originalSize))
            {
                originalSize = ___pmdText.fontSize;
                _OriginalFontSize[___pmdText] = originalSize;
            }

            //A justified text was set to not wrap, measuring has to wrap again
            ___pmdText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float maxHeight = ___mask.rectTransform.rect.height;
            int size = originalSize;
            string fitted = msg;
            ___pmdText.fontSize = size;
            ___pmdText.text = fitted;
            while (___pmdText.preferredHeight > maxHeight && size > MinFontSize)
            {
                size--;
                fitted = TextUtil.ScaleSizeTags(msg, (float)size / originalSize);
                ___pmdText.fontSize = size;
                ___pmdText.text = fitted;
            }
            msg = fitted;

            if (Justify.Contains(__instance))
            {
                string justified = TextUtil.Justified(___pmdText, msg);
                if (justified != msg)
                {
                    ___pmdText.supportRichText = true;
                    ___pmdText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    msg = justified;
                }
            }
        }
    }

    /*
     * The item Info box (and Effect box) on the character screen:
     * Gear tab: the Info box reaches down to the bottom of the Effect box next to it, the texts shrink (down to 14) if they still do not fit.
     * Items tab (Gift, Books, Quest): nothing is below the Info box, every item gets the same taller box (see InfoBox.Grow).
     * Medicine tab: the Effect part moves down and gives up some height, the Info box takes that room (see InfoBox.Split).
     * WGPropsIntroduction is also used by popups (gear selection, gifts, battle items), the shop, forge, elixirs and battle status,
     * there only the Info text is justified.
     */
    [HarmonyPatch(typeof(WGPropsIntroduction), "UpdateWidget")]
    public class WGPropsIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;
        //The "Info" and "Effect" titles are 25 in the prefab
        private const int MedicineTitleSize = 20;
        //What the Effect box gives up for Info on the Medicine tab: it keeps room for three lines of size 20
        private const float MedicineEffectShrink = 22f;
        //Four more lines of size 20 (23 each) than the two the box was made for, plus the gap kept below the last line
        private const float ItemsExtraHeight = 100f;

        public static void Postfix(WGPropsIntroduction __instance, WGText ___description, WGText ___effectDescription, WGText ___conditionDescription)
        {
            Text info = TextUtil.TextOf(___description);
            Text effect = TextUtil.TextOf(___effectDescription);
            Text condition = TextUtil.TextOf(___conditionDescription);

            //"Suitable for:" got a space on both sides of the colon, on the Gear tab, in the gear selection and in the combat popup
            bool gearPanel = IsGearPanel(__instance);
            bool gearPopup = IsPanelOf<UIInventoryWindow>(__instance);
            bool battlePopup = IsPanelOf<UIBattleInventoryWindow>(__instance);
            if (gearPanel || gearPopup || battlePopup)
            {
                SpaceSuitableFor(condition);
            }

            //The character screen tabs, the gear selection and the combat item popup each get their own layout below.
            //The other places using this widget (gifts, shop, forge, elixirs...) are left as they are.
            if (gearPanel)
            {
                //The Info box always reaches down to the bottom of the Effect box next to it
                InfoBox.SideMargins(info);
                InfoBox.Grow(info, float.MaxValue, MinSize);
                TextUtil.ShrinkToHeight(effect, MinSize);
            }
            else if (IsPanelOf<UIInventory>(__instance) && effect == null)
            {
                //Items tab: every item gets the same box, with room for six lines, only longer texts make it bigger
                InfoBox.SideMargins(info);
                InfoBox.Grow(info, ItemsExtraHeight, MinSize);
            }
            else if (IsPanelOf<UIMedicine>(__instance) && effect != null)
            {
                //Medicine tab: the "Info" and "Effect" titles are a little smaller, and the Effect part below moves down
                //(and loses its empty space) to make room for Info
                SetTitleSize(info, MedicineTitleSize);
                SetTitleSize(effect, MedicineTitleSize);
                InfoBox.Split(info, effect, MedicineEffectShrink, MinSize);
            }
            else if (gearPopup)
            {
                //Gear selection popup: a long Info grows its band and pushes the parts below down
                InfoBox.GearPopup(info, condition, effect, MinSize);
                PopupTitles.SetSize(TextUtil.OwningForm(__instance));
            }
            else if (battlePopup)
            {
                //Item and gear popup in combat: Info is left aligned so it can be justified, Criteria and Effect stay centered,
                //and the Criteria and Effect parts move down to make room for a long Info (see BattlePopup)
                if (info != null)
                {
                    info.alignment = (TextAnchor)((int)info.alignment / 3 * 3);
                }
                BattlePopup.Layout(__instance, info, condition, effect, MinSize);
            }
            else if (TextUtil.OwningForm(__instance) is UIForm shop && (shop is UIPawnshop || shop is UIAlchemy))
            {
                //Refine Medicine: the "Effect" heading sat right under the Info text, it moves down to the middle between
                //the Info text and the effect
                if (shop is UIAlchemy)
                {
                    MoveTitleDown(effect, AlchemyEffectTitleDown);
                }

                //Pawn shop and Refine Medicine: the effect ("Restore 500 Qi") started at the left edge of the middle panel,
                //under the centered "Effect" heading. It is centered under that heading now.
                CenterUnderTitle(effect);
            }

            //The item Info is justified everywhere
            TextUtil.Justify(info);
        }

        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Weapon = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("weapon");
        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Armor = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("armor");
        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Accessories = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("accessories");
        private static readonly AccessTools.FieldRef<UIFormInventory, WGPropsIntroduction> _PropsIntroduction = AccessTools.FieldRefAccess<UIFormInventory, WGPropsIntroduction>("propsIntroduction");

        //One of the weapon, armor and accessories panels of the Gear tab
        private static bool IsGearPanel(WGPropsIntroduction widget)
        {
            UIEquip equip = TextUtil.OwningForm(widget) as UIEquip;
            return equip != null && (_Weapon(equip) == widget || _Armor(equip) == widget || _Accessories(equip) == widget);
        }

        //The item panel of that inventory screen itself (Items tab: UIInventory, Medicine tab: UIMedicine, gear selection: UIInventoryWindow)
        private static bool IsPanelOf<T>(WGPropsIntroduction widget) where T : UIFormInventory
        {
            T form = TextUtil.OwningForm(widget) as T;
            return form != null && _PropsIntroduction(form) == widget;
        }

        //How far the "Effect" heading of Refine Medicine moves down (measured at 1920x1080)
        private const float AlchemyEffectTitleDown = 20f;

        //Where each moved heading sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _TitleOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //Moves the heading of a text down (the text itself stays)
        private static void MoveTitleDown(Text text, float down)
        {
            RectTransform title = Heading(text)?.rectTransform;
            if (title == null)
            {
                return;
            }
            if (!_TitleOrigin.TryGetValue(title, out Vector2 origin))
            {
                origin = title.anchoredPosition;
                _TitleOrigin[title] = origin;
            }
            title.anchoredPosition = origin - new Vector2(0f, down);
        }

        //Where each centered effect text sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _EffectOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //String ids of the "Effect" heading
        private static readonly string[] _EffectTitleIds = { "General_Effect", "SecondaryInterface1406" };

        //The heading found for each text, looked up once
        private static readonly System.Collections.Generic.Dictionary<Text, Text> _Heading = new System.Collections.Generic.Dictionary<Text, Text>();

        //The "Effect" heading of an effect text: the label of its screen that shows "Effect" (by its string id or its text)
        //whose middle is closest above the middle of the effect text (on the pawn shop and Refine Medicine it is a sibling
        //named Text, the game gives it no fixed name)
        private static Text Heading(Text text)
        {
            if (text == null)
            {
                return null;
            }
            //Kept once found. Not found yet (the heading may not have its English text yet) or destroyed with its screen
            //(it then compares equal to null): looked up again.
            if (_Heading.TryGetValue(text, out Text heading) && heading != null)
            {
                return heading;
            }

            heading = null;
            UIForm form = TextUtil.OwningForm(text);
            if (form != null)
            {
                var words = new System.Collections.Generic.HashSet<string>();
                foreach (string id in _EffectTitleIds)
                {
                    string word = Heluo.Game.Data?.Get<StringTable>(id)?.Text;
                    if (!string.IsNullOrEmpty(word))
                    {
                        words.Add(word.Trim());
                    }
                }

                //Centers are compared: the text box can be much taller than its text, its top may be above the heading
                float center = text.rectTransform.TransformPoint(text.rectTransform.rect.center).y;
                float best = float.MaxValue;
                foreach (Text label in form.GetComponentsInChildren<Text>(true))
                {
                    if (label == text)
                    {
                        continue;
                    }
                    WGStringTable table = label.GetComponent<WGStringTable>();
                    bool isEffect = (table != null && System.Array.IndexOf(_EffectTitleIds, TextUtil.StringId(table)) >= 0)
                        || (label.text != null && words.Contains(label.text.Trim()));
                    float above = label.rectTransform.TransformPoint(label.rectTransform.rect.center).y - center;
                    if (isEffect && above > 0f && above < best)
                    {
                        best = above;
                        heading = label;
                    }
                }
            }

            _Heading[text] = heading;
            return heading;
        }

        //Centers a text and lines it up under its heading (just centered if there is none)
        private static void CenterUnderTitle(Text text)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            if (!_EffectOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _EffectOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin;
            text.alignment = (TextAnchor)((int)text.alignment / 3 * 3 + 1);

            Text title = Heading(text);
            if (title == null || string.IsNullOrEmpty(title.text))
            {
                return;
            }
            TextUtil.VisibleX(title, out float titleLeft, out float titleRight);
            float textCenter = rect.TransformPoint(rect.rect.center).x;
            rect.position += new Vector3((titleLeft + titleRight) / 2f - textCenter, 0f, 0f);
        }

        //"Suitable for:" is SecondaryInterface0502, the game puts the character names right after it, separated by the
        //Chinese comma 、. The names become "Suitable for : Zhao Zilong, Xiao Qianling". Only that first line is changed,
        //the game may add another condition on the next line.
        private static void SpaceSuitableFor(Text condition)
        {
            string label = Heluo.Game.Data?.Get<StringTable>("SecondaryInterface0502")?.Text;
            if (condition == null || string.IsNullOrEmpty(label) || condition.text == null || !condition.text.StartsWith(label, System.StringComparison.Ordinal))
            {
                return;
            }
            string spaced = label.TrimEnd().TrimEnd(':', '：').TrimEnd() + " : ";
            string rest = condition.text.Substring(label.Length).TrimStart();
            int lineEnd = rest.IndexOf('\n');
            string names = lineEnd < 0 ? rest : rest.Substring(0, lineEnd);
            string after = lineEnd < 0 ? "" : rest.Substring(lineEnd);
            condition.text = spaced + names.Replace("、", ", ") + after;
        }

        //The title ("Info", "Effect") is the Title child of the box the text sits in
        private static void SetTitleSize(Text text, int size)
        {
            Transform title = text != null && text.transform.parent != null ? text.transform.parent.Find("Title") : null;
            Text titleText = title != null ? title.GetComponent<Text>() : null;
            if (titleText != null)
            {
                titleText.fontSize = size;
            }
        }
    }

    /*
     * The item Info box is a background image with the text stretched inside it, sized for two lines of Chinese.
     * The box keeps its top and grows down: by at least minGrow (so it does not change size from item to item),
     * and more when the text needs it, but never past the bottom of the panel it sits in.
     * A text that still does not fit shrinks (never below minSize). The text always starts at the top of the box.
     */
    public static class InfoBox
    {
        //Size and position each box has in the prefab, so we always change it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Grow(Text text, float minGrow, int minSize)
        {
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            RectTransform panel = box != null ? box.parent as RectTransform : null;
            if (panel == null || box.GetComponent<Image>() == null)
            {
                return;
            }

            Restore(box);
            if (string.IsNullOrEmpty(text.text))
            {
                return;
            }

            TopAlign(text);
            NormalSpaces(text);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            float missing = text.preferredHeight - text.rectTransform.rect.height;
            float wanted = Mathf.Max(missing, minGrow);
            if (wanted <= 0f)
            {
                return;
            }

            float room = Mathf.Max(0f, Bottom(box, panel) - panel.rect.yMin);
            float grow = Mathf.Min(wanted, room);
            MoveEdges(box, 0f, grow);

            if (missing > grow)
            {
                TextUtil.ShrinkToHeight(text, minSize);
            }
        }

        /*
         * Two boxes above each other (Info and Effect on the Medicine tab), laid out the same for every item:
         * the lower box moves down to the bottom of the panel and gives up belowShrink of its height, the upper box
         * grows down into all of that room. Both texts start at the top of their box and shrink only when they do not fit.
         */
        public static void Split(Text text, Text below, float belowShrink, int minSize)
        {
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            RectTransform panel = box != null ? box.parent as RectTransform : null;
            RectTransform belowBox = below != null ? below.transform.parent as RectTransform : null;
            if (panel == null || belowBox == null || belowBox.parent != panel)
            {
                return;
            }

            Restore(box);
            Restore(belowBox);

            float drop = Mathf.Max(0f, Bottom(belowBox, panel) - panel.rect.yMin);
            float grow = drop + belowShrink;
            MoveEdges(box, 0f, grow);
            MoveEdges(belowBox, grow, drop);

            foreach (Text t in new[] { text, below })
            {
                TopAlign(t);
                NormalSpaces(t);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (t.preferredHeight > t.rectTransform.rect.height)
                {
                    TextUtil.ShrinkToHeight(t, minSize);
                }
            }
        }

        /*
         * The game turns the spaces into non-breaking spaces (StringTool.Replace). Measured like that, the text breaks in the
         * middle of words and looks a line shorter than it is once the justifying turns the spaces back, so the last line
         * ended up below the box. The text is measured with normal spaces instead.
         */
        private static void NormalSpaces(Text text)
        {
            if (text != null && text.text != null && text.text.IndexOf('\u00A0') >= 0)
            {
                text.text = text.text.Replace('\u00A0', ' ');
            }
        }

        //Space between the box edges and the text, as in the Effect boxes and on the Medicine tab
        private const float LeftMargin = 25f;
        private const float RightMargin = 20f;

        //The Info text on the Gear and Items tabs sat 5 further from the left edge than every other box text
        public static void SideMargins(Text text)
        {
            if (text == null)
            {
                return;
            }
            RectTransform rect = text.rectTransform;
            rect.offsetMin = new Vector2(LeftMargin, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-RightMargin, rect.offsetMax.y);
        }

        //Extra gap between the box edges and the text, at the top and the same at the bottom. The prefab leaves 9, which looked cramped.
        private const float Padding = 8f;

        //Top and bottom edge each text area has in the prefab
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _OriginalEdges = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The text starts at the top of its box, with the same small gap above the first line and below the last one
        private static void TopAlign(Text text)
        {
            text.alignment = (TextAnchor)((int)text.alignment % 3);
            RectTransform rect = text.rectTransform;
            if (!_OriginalEdges.TryGetValue(rect, out Vector2 edges))
            {
                edges = new Vector2(rect.offsetMax.y, rect.offsetMin.y);
                _OriginalEdges[rect] = edges;
            }
            rect.offsetMax = new Vector2(rect.offsetMax.x, edges.x - Padding);
            rect.offsetMin = new Vector2(rect.offsetMin.x, edges.y + Padding);
        }

        /*
         * The gear selection popup: the headings and the light bands behind the texts are part of the popup's art and
         * cannot move. The Info text area (75 high, 3 lines) sits inside its band, an Info text with more lines ran over
         * the "Criteria" heading. Such a text may now use the band down to just above that heading, and shrinks only
         * as much as it needs to fit. Texts of up to 3 lines are not changed. Only done when the popup has exactly the
         * Description, ConditionDescription and EffectDescription parts.
         */
        //How much further down the Info band goes than the text area, before the "Criteria" heading starts
        private const float PopupBandBelowText = 5f;

        public static void GearPopup(Text info, Text condition, Text effect, int minSize)
        {
            RectTransform infoHeader = info != null ? info.transform.parent as RectTransform : null;
            RectTransform conditionHeader = condition != null ? condition.transform.parent as RectTransform : null;
            RectTransform effectHeader = effect != null ? effect.transform.parent as RectTransform : null;
            if (infoHeader == null || conditionHeader == null || effectHeader == null
                || infoHeader.name != "Description" || conditionHeader.name != "ConditionDescription" || effectHeader.name != "EffectDescription"
                || infoHeader.parent != conditionHeader.parent || infoHeader.parent != effectHeader.parent)
            {
                return;
            }

            Restore(info.rectTransform);
            if (string.IsNullOrEmpty(info.text))
            {
                return;
            }

            NormalSpaces(info);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (info.preferredHeight <= info.rectTransform.rect.height)
            {
                return;
            }

            MoveEdges(info.rectTransform, 0f, PopupBandBelowText);
            TextUtil.ShrinkToHeight(info, minSize);
        }

        private static void Restore(RectTransform box)
        {
            if (box == null)
            {
                return;
            }
            if (!_Original.TryGetValue(box, out Vector2[] original))
            {
                original = new[] { box.sizeDelta, box.anchoredPosition };
                _Original[box] = original;
            }
            box.sizeDelta = original[0];
            box.anchoredPosition = original[1];
        }

        //Bottom edge of a box, in the panel's space
        private static float Bottom(RectTransform box, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            box.GetLocalCorners(corners);
            return panel.InverseTransformPoint(box.TransformPoint(corners[0])).y;
        }

        //Moves the top edge of a box down by topDown and its bottom edge down by bottomDown, whatever its pivot
        private static void MoveEdges(RectTransform box, float topDown, float bottomDown)
        {
            box.sizeDelta += new Vector2(0f, bottomDown - topDown);
            box.anchoredPosition += new Vector2(0f, -bottomDown + box.pivot.y * (bottomDown - topDown));
        }
    }

    //The skill effect box (reading screen and martial arts form) shrinks to fit
    [HarmonyPatch(typeof(WGSkillsIntroduction), "UpdateWidget")]
    public class WGSkillsIntroduction_UpdateWidget_Hook
    {
        //The skill effect shrinks to fit its box and is justified, like the course description
        public static void Prefix(WGPaoMaDengText ___effectDescription)
        {
            if (___effectDescription != null)
            {
                WGPaoMaDengText_AddMessage_Hook.FitToBox.Add(___effectDescription);
                WGPaoMaDengText_AddMessage_Hook.Justify.Add(___effectDescription);
            }
        }

        //The book description under the title is justified
        public static void Postfix(WGSkillsIntroduction __instance, WGText ___description, WGText ___name, WGText ___requestMP,
            WGText ___hurt, WGText ___range, WGText ___area, WGText ___cD, WGPaoMaDengText ___effectDescription)
        {
            TextUtil.Justify(TextUtil.TextOf(___description));

            if (TextUtil.OwningForm(__instance) is UIMartialArtsWindow)
            {
                RaiseStats(__instance, new[] { ___hurt, ___range, ___area, ___cD },
                    new Component[] { ___description, ___name, ___requestMP, ___effectDescription });
            }
        }

        /*
         * Skill selection popup: the Damage, Attack Distance, Attack Area and Cooldown Time rows sat low in their box
         * (about 22 above them and 13 below). The rows, names and numbers, move up a little. Only done when the part
         * holding the four numbers holds nothing else of the panel.
         */
        private const float StatsRaise = 6f;

        //Where each moved row is in the prefab, and the padding of a layout group holding the rows
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _OriginalRow = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<LayoutGroup, RectOffset> _OriginalPadding = new System.Collections.Generic.Dictionary<LayoutGroup, RectOffset>();

        private static void RaiseStats(WGSkillsIntroduction widget, WGText[] values, Component[] others)
        {
            Transform[] texts = System.Array.ConvertAll(values, v => TextUtil.TextOf(v) != null ? TextUtil.TextOf(v).transform : null);
            if (System.Array.IndexOf(texts, null) >= 0)
            {
                return;
            }

            //The smallest part of the panel holding all four numbers
            Transform rows = texts[0].parent;
            while (rows != null && !System.Array.TrueForAll(texts, t => t.IsChildOf(rows)))
            {
                rows = rows.parent;
            }
            //Holding only the numbers, it is the numbers column: the labels next to them are one level up
            if (rows != null && rows.GetComponentsInChildren<Text>(true).Length <= texts.Length)
            {
                rows = rows.parent;
            }
            if (rows == null || rows == widget.transform || !rows.IsChildOf(widget.transform))
            {
                return;
            }
            foreach (Component other in others)
            {
                if (other != null && other.transform.IsChildOf(rows))
                {
                    return;
                }
            }

            LayoutGroup layout = rows.GetComponent<LayoutGroup>();
            if (layout != null)
            {
                //The layout places the rows, so it gets less room on top and more below instead
                if (!_OriginalPadding.TryGetValue(layout, out RectOffset padding))
                {
                    padding = new RectOffset(layout.padding.left, layout.padding.right, layout.padding.top, layout.padding.bottom);
                    _OriginalPadding[layout] = padding;
                }
                layout.padding = new RectOffset(padding.left, padding.right, padding.top - (int)StatsRaise, padding.bottom + (int)StatsRaise);
                return;
            }

            foreach (Transform child in rows)
            {
                RectTransform row = child as RectTransform;
                if (row == null || child.GetComponentInChildren<Text>(true) == null)
                {
                    continue;
                }
                if (!_OriginalRow.TryGetValue(row, out Vector2 position))
                {
                    position = row.anchoredPosition;
                    _OriginalRow[row] = position;
                }
                row.anchoredPosition = position + new Vector2(0f, StatsRaise);
            }
        }
    }

    //The inner art book description is justified like the martial arts one
    [HarmonyPatch(typeof(WGMantraIntroduction), "UpdateWidget")]
    public class WGMantraIntroduction_UpdateWidget_Hook
    {
        public static void Postfix(WGText ___description)
        {
            TextUtil.Justify(TextUtil.TextOf(___description));
        }
    }

    //The course description on the elective screen shrinks to fit, and the learnable skill rows get room for their English names
    [HarmonyPatch(typeof(WGCourseIntroduction), "UpdateWidget")]
    public class WGCourseIntroduction_UpdateWidget_Hook
    {
        private const float Gap = 8f;
        private const int MinNameSize = 12;
        //The prefab label is bigger than the skill names (20), 18 keeps it a label next to them
        private const int LabelSize = 18;

        //Where each "Qi Usage" label sits in the prefab (inside its parent, so moving or scrolling the screen does not change it), so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _LabelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Prefix(WGPaoMaDengText ___description)
        {
            if (___description != null)
            {
                WGPaoMaDengText_AddMessage_Hook.FitToBox.Add(___description);
                WGPaoMaDengText_AddMessage_Hook.Justify.Add(___description);
            }
        }

        //Each skill row is: skill name, "Qi Usage" label, Qi number. Long English names ran into the label.
        //We move the label right up to the number, then shrink the name if it still does not end before the label.
        public static void Postfix(WGCourseIntroduction __instance, GameObject[] ___skillGo, WGText[] ___skills, WGText[] ___mps)
        {
            CenterDescriptionTitle(__instance);

            if (___skillGo == null || ___skills == null || ___mps == null)
            {
                return;
            }

            for (int i = 0; i < ___skillGo.Length && i < ___skills.Length && i < ___mps.Length; i++)
            {
                if (___skillGo[i] == null || !___skillGo[i].activeSelf)
                {
                    continue;
                }

                Text name = TextUtil.TextOf(___skills[i]);
                Text mp = TextUtil.TextOf(___mps[i]);
                if (name == null || mp == null)
                {
                    continue;
                }

                Text label = null;
                foreach (Text t in ___skillGo[i].GetComponentsInChildren<Text>(true))
                {
                    if (t != name && t != mp && !string.IsNullOrEmpty(t.text))
                    {
                        label = t;
                        break;
                    }
                }

                FitRow(name, label, mp);
            }
        }

        //The "Description" title ran into the diamond at the right end of its band. It gets the size the translation gives the
        //"Skills Learned" title above it (20.5), and is centered on that title, which sits in the middle of the same band.
        private const string DescriptionTitleId = "SecondaryInterface0107";
        private const string SkillsTitleId = "SecondaryInterface0106";
        private const int DescriptionTitleSize = 20;

        //Where the "Description" title sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _TitleOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The "Description" and "Skills Learned" titles of each course widget, found once
        private static readonly System.Collections.Generic.Dictionary<WGCourseIntroduction, Text[]> _Titles = new System.Collections.Generic.Dictionary<WGCourseIntroduction, Text[]>();

        private static void CenterDescriptionTitle(WGCourseIntroduction widget)
        {
            if (!_Titles.TryGetValue(widget, out Text[] titles))
            {
                UIForm form = TextUtil.OwningForm(widget);
                if (form == null)
                {
                    return;
                }
                titles = new Text[2];
                foreach (WGStringTable label in form.GetComponentsInChildren<WGStringTable>(true))
                {
                    string id = TextUtil.StringId(label);
                    if (id == DescriptionTitleId)
                    {
                        titles[0] = label.GetComponent<Text>();
                    }
                    else if (id == SkillsTitleId)
                    {
                        titles[1] = label.GetComponent<Text>();
                    }
                }
                _Titles[widget] = titles;
            }

            Text title = titles[0];
            Text skills = titles[1];
            if (title == null || string.IsNullOrEmpty(title.text))
            {
                return;
            }

            title.fontSize = DescriptionTitleSize;

            RectTransform rect = title.rectTransform;
            if (!_TitleOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _TitleOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin;

            if (skills == null || string.IsNullOrEmpty(skills.text))
            {
                return;
            }
            TextUtil.VisibleX(title, out float titleLeft, out float titleRight);
            TextUtil.VisibleX(skills, out float skillsLeft, out float skillsRight);
            rect.position += new Vector3((skillsLeft + skillsRight - titleLeft - titleRight) / 2f, 0f, 0f);
        }

        private static void FitRow(Text name, Text label, Text mp)
        {
            TextUtil.VisibleX(mp, out float mpLeft, out _);
            float rightLimit = mpLeft;

            if (label != null)
            {
                RectTransform labelRect = label.rectTransform;
                if (!_LabelOrigin.TryGetValue(labelRect, out Vector2 origin))
                {
                    origin = labelRect.anchoredPosition;
                    _LabelOrigin[labelRect] = origin;
                }
                labelRect.anchoredPosition = origin;
                label.fontSize = Mathf.Min(label.fontSize, LabelSize);

                TextUtil.VisibleX(label, out float labelLeft, out float labelRight);
                float shift = mpLeft - TextUtil.WorldWidth(labelRect, Gap) - labelRight;
                if (shift > 0)
                {
                    labelRect.position += new Vector3(shift, 0f, 0f);
                    labelLeft += shift;
                }
                rightLimit = labelLeft;
            }

            //One line, shrunk until it ends a gap before the label (or the number)
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            TextUtil.VisibleX(name, out float nameLeft, out float nameRight);
            float available = rightLimit - TextUtil.WorldWidth(name.rectTransform, Gap) - nameLeft;
            float width = nameRight - nameLeft;
            if (available > 0 && width > available)
            {
                name.fontSize = Mathf.Max(MinNameSize, Mathf.FloorToInt(name.fontSize * available / width));
            }
        }
    }

    [HarmonyPatch(typeof(WGNurturanceBtn), "UpdateButton")]
    public class WGNurturanceBtn_UpdateButton_Hook
    {
        public static void Postfix(WGNurturanceBtn __instance)
        {
            //Debug.Log("WGNurturanceBtn_UpdateButton_Hook");
            Text text = __instance.Btn_Name;
            text.font = TextUtil.UIFont;
            //Every label of the life menu (main row and all its pages) sits in the middle of its banner
            text.alignment = TextAnchor.MiddleCenter;
            ShrinkSkillLevel(__instance.SkillLevelText);

            //Only Return, Back/Next (previous and next page), Socialize and the character names inside Socialize are fitted to their box, the other buttons keep the default size
            Nurturance nurturance = __instance.tree?.Value;
            bool fit = nurturance != null
                && (nurturance.Fuction == NurturanceFunction.Back || nurturance.Fuction == NurturanceFunction.Previous || nurturance.Fuction == NurturanceFunction.Next
                    || nurturance.Fuction == NurturanceFunction.Firend || nurturance.Id == "Nurturance_05");

            //Martial arts and inner arts tabs: long names took up to 4 lines, they shrink until they take at most 2
            if (nurturance != null && (nurturance.Fuction == NurturanceFunction.Skill || nurturance.Fuction == NurturanceFunction.Mantra))
            {
                TextUtil.ShrinkToLines(text, SkillLines, SkillMinSize);
                RaiseLabel(text);
                return;
            }

            if (!fit)
            {
                TextUtil.RestoreBoxFit(text);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                RaiseLabel(text);
                return;
            }

            //Return, Back and Next have a tall narrow box made for Chinese written top to bottom, English is stacked letter by letter there
            Rect rect = text.rectTransform.rect;
            bool stacked = rect.height > rect.width * 1.5f;
            if (stacked && !string.IsNullOrEmpty(text.text))
            {
                text.text = string.Join("\n", System.Array.ConvertAll(text.text.Replace(" ", "").ToCharArray(), c => c.ToString()));
            }

            TextUtil.ShrinkToBox(text, MinSize);
            if (!stacked)
            {
                RaiseLabel(text);
            }
        }

        /*
         * Unity centers a line by the whole height of the font, room for descenders (g, p, y) included. The labels hardly have any,
         * so they sat low in their banner (with Arial about 7 pixels above the capitals but 4 below the letters at 1080p, more with
         * Bahnschrift). How low depends on the font, so it is measured: the label moves until the band from the baseline to the
         * top of a capital H, laid out by Unity in this label's font and size, is in the middle of the label box. If that cannot
         * be measured, it is raised by a tenth of its size (right for Arial). The buttons are updated more than once, so the
         * position is always taken from the one the game gave the label.
         */
        private static void RaiseLabel(Text text)
        {
            RectTransform rect = text.rectTransform;
            if (!_LabelOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _LabelOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(0f, CapsCenterOffset(text) ?? text.fontSize * LabelRaise);
        }

        /*
         * How far the letters of a one line text are below the middle of its box, null when Unity lays out nothing.
         * The labels are mostly lower case, so what looks centered is between the capitals (baseline to top of H) and the
         * lower case letters (baseline to top of x): the middle of both bands is put in the middle of the box. That is where
         * the Arial labels already looked right, and other fonts get the same placement from their own letters.
         */
        private static float? CapsCenterOffset(Text text)
        {
            Rect box = text.rectTransform.rect;
            TextGenerationSettings settings = text.GetGenerationSettings(box.size);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.textAnchor = TextAnchor.MiddleCenter;
            settings.richText = false;

            float? caps = GlyphMiddle("H", settings, text.pixelsPerUnit);
            float? lower = GlyphMiddle("x", settings, text.pixelsPerUnit);
            if (caps == null || lower == null)
            {
                return null;
            }

            //The vertices are relative to the pivot of the box
            return box.center.y - (caps.Value + lower.Value) / 2f;
        }

        //Middle height of one letter laid out with these settings, in the units of the box
        private static float? GlyphMiddle(string letter, TextGenerationSettings settings, float pixelsPerUnit)
        {
            _CapsGenerator.Populate(letter, settings);
            System.Collections.Generic.IList<UIVertex> verts = _CapsGenerator.verts;
            if (verts.Count < 4 || pixelsPerUnit <= 0f)
            {
                return null;
            }

            float bottom = float.MaxValue, top = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                bottom = Mathf.Min(bottom, verts[i].position.y);
                top = Mathf.Max(top, verts[i].position.y);
            }
            return (bottom + top) / 2f / pixelsPerUnit;
        }

        private const float LabelRaise = 0.1f;
        private static readonly TextGenerator _CapsGenerator = new TextGenerator();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _LabelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The skill level next to the martial arts icons ("10") was sized for the game's font, its digits are a lot bigger in the
        //English font the life screen now uses (see UIForm_Show_LifeFont_Hook). Buttons are updated more than once, so the size
        //is always taken from the one the game gave the label.
        private static void ShrinkSkillLevel(Text level)
        {
            if (level == null)
            {
                return;
            }

            if (!_LevelSize.TryGetValue(level, out int size))
            {
                size = level.fontSize;
                _LevelSize[level] = size;
            }
            level.fontSize = Mathf.RoundToInt(size * SkillLevelScale);
        }

        private const float SkillLevelScale = 0.8f;
        private static readonly System.Collections.Generic.Dictionary<Text, int> _LevelSize = new System.Collections.Generic.Dictionary<Text, int>();

        private const int MinSize = 10;
        private const int SkillLines = 2;
        private const int SkillMinSize = 12;
    }

    [HarmonyPatch(typeof(UIRegistration), "NameCheck")]
    public class UIRegistration_NameCheck_Hook
    {
        public static bool Prefix()
        {
            //Debug.Log("UIRegistration_NameCheck_Hook");
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamPlatform), "CheckWordLegality")]
    public class SteamPlatform_CheckWordLegality_Hook
    {
        public static bool Prefix(string word, ref bool __result)
        {
            //Debug.Log("SteamPlatform_CheckWordLegality_Hook");
            __result = true;
            return false;
        }
    }

    /*
     * The skill and inner art selection popup (Skills tab): the name area of each row was sized for a few Chinese
     * characters, English names wrapped into two or three lines and ran over the "Lv" below them. The name area is now
     * as wide as "Long Howl from Heavenly Gate" plus a little, its left end stays where it is, longer names take two lines.
     * The small scroll in front of "Lv" is drawn a little smaller, so it does not touch names of two lines.
     * Other lists with the same button keep their layout, some of them show a "New" badge on the right.
     */
    [HarmonyPatch(typeof(WGScrollBtn), "UpdateWidget")]
    public class WGScrollBtn_UpdateWidget_Hook
    {
        private const float NameWidth = 290f;
        //The small scroll in front of "Lv" touched names of two lines, it is drawn a little smaller
        private const float LevelIconScale = 0.75f;

        //Size each scroll has in the prefab, and size and place of what sits inside it ("Lv 10" is a child of the scroll)
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3> _OriginalIconScale = new System.Collections.Generic.Dictionary<Transform, Vector3>();
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3[]> _OriginalChild = new System.Collections.Generic.Dictionary<Transform, Vector3[]>();

        //Width and position each name area has in the prefab
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Postfix(WGScrollBtn __instance, WGText ___itemName, WGImage ___remarkIcon)
        {
            Text name = TextUtil.TextOf(___itemName);
            if (name == null || !(TextUtil.OwningForm(__instance) is UIMartialArtsWindow))
            {
                return;
            }

            Transform levelIcon = ___remarkIcon != null ? ___remarkIcon.transform : null;
            if (levelIcon != null)
            {
                if (!_OriginalIconScale.TryGetValue(levelIcon, out Vector3 scale))
                {
                    scale = levelIcon.localScale;
                    _OriginalIconScale[levelIcon] = scale;
                }
                levelIcon.localScale = scale * LevelIconScale;

                //What sits inside the scroll gets the opposite, so "Lv 10" keeps its size and place
                foreach (Transform child in levelIcon)
                {
                    if (!_OriginalChild.TryGetValue(child, out Vector3[] inside))
                    {
                        inside = new[] { child.localScale, child.localPosition };
                        _OriginalChild[child] = inside;
                    }
                    child.localScale = inside[0] / LevelIconScale;
                    child.localPosition = inside[1] / LevelIconScale;
                }
            }

            RectTransform rect = name.rectTransform;
            if (!_Original.TryGetValue(rect, out Vector2[] original))
            {
                original = new[] { rect.sizeDelta, rect.anchoredPosition };
                _Original[rect] = original;
            }
            rect.sizeDelta = original[0];
            rect.anchoredPosition = original[1];

            float grow = NameWidth - rect.rect.width;
            if (grow > 0f)
            {
                rect.sizeDelta += new Vector2(grow, 0f);
                rect.anchoredPosition += new Vector2(grow * rect.pivot.x, 0f);
            }
        }
    }

    [HarmonyPatch(typeof(WGBookBtn), "UpdateWidget")]
    public class WGBookBtn_UpdateWidget_Hook
    {
        public static void Postfix(WGBookBtn __instance, ref WGText ___itemName, System.Collections.Generic.List<WGImage> ___difficultIcons)
        {
            //Debug.Log("WGBookBtn_UpdateWidget_Hook");
            //Long book names took 3 lines and ran into "Remaining Chapters", they shrink until they take at most 2
            TextUtil.ShrinkToLines(TextUtil.TextOf(___itemName), NameLines, NameMinSize);
            AlignChapters(__instance, TextUtil.TextOf(___itemName), ___difficultIcons);

            //A button shown for the first time is not laid out yet ("Remaining Chapters" may not even have its English text),
            //so the move above is off and it jumped into place only on the next update. It is done again right before the
            //screen is drawn, after Unity has laid everything out.
            _Pending[__instance] = ___difficultIcons;
            if (!_Subscribed)
            {
                Canvas.willRenderCanvases += AlignPending;
                _Subscribed = true;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<WGBookBtn, System.Collections.Generic.List<WGImage>> _Pending = new System.Collections.Generic.Dictionary<WGBookBtn, System.Collections.Generic.List<WGImage>>();
        private static bool _Subscribed;
        private static readonly AccessTools.FieldRef<WGBookBtn, WGText> _ItemName = AccessTools.FieldRefAccess<WGBookBtn, WGText>("itemName");

        //Runs after Unity's own layout (it registered first), once for each button updated since the last frame
        private static void AlignPending()
        {
            if (_Pending.Count == 0)
            {
                return;
            }

            foreach (var pending in _Pending)
            {
                //A button destroyed in the meantime compares equal to null
                if (pending.Key != null)
                {
                    AlignChapters(pending.Key, TextUtil.TextOf(_ItemName(pending.Key)), pending.Value);
                }
            }
            _Pending.Clear();
        }

        private const int NameLines = 2;
        private const int NameMinSize = 14;

        /*
         * "Remaining Chapters" and the chapter icons after it started under the book cover, and the icons were tall enough to
         * reach into the book name above. They move right so "Remaining Chapters" starts where the book name starts,
         * and the icons get smaller.
         */
        private const string ChaptersId = "SecondaryInterface2207";
        private const float IconScale = 0.75f;

        //Position and scale each of them has in the prefab, so we always start from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3> _Scale = new System.Collections.Generic.Dictionary<Transform, Vector3>();

        //The "Remaining Chapters" label of each button, found once (the list reuses its buttons)
        private static readonly System.Collections.Generic.Dictionary<WGBookBtn, Text> _Label = new System.Collections.Generic.Dictionary<WGBookBtn, Text>();

        private static void AlignChapters(WGBookBtn button, Text name, System.Collections.Generic.List<WGImage> icons)
        {
            if (!_Label.TryGetValue(button, out Text label))
            {
                foreach (WGStringTable table in button.GetComponentsInChildren<WGStringTable>(true))
                {
                    if (TextUtil.StringId(table) == ChaptersId)
                    {
                        label = table.GetComponent<Text>();
                        break;
                    }
                }
                _Label[button] = label;
            }
            if (name == null || label == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            var parts = new System.Collections.Generic.List<Transform> { label.transform };
            if (icons != null)
            {
                foreach (WGImage icon in icons)
                {
                    if (icon == null)
                    {
                        continue;
                    }
                    if (!_Scale.TryGetValue(icon.transform, out Vector3 scale))
                    {
                        scale = icon.transform.localScale;
                        _Scale[icon.transform] = scale;
                    }
                    icon.transform.localScale = scale * IconScale;
                    parts.Add(icon.transform);
                }
            }

            //What gets moved: each part, or the layout group holding it (a layout group puts its children back), never something inside another one that moves
            var movers = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform part in parts)
            {
                Transform mover = part.parent != null && part.parent.GetComponent<LayoutGroup>() != null ? part.parent : part;
                if (mover is RectTransform rect && !movers.Contains(rect))
                {
                    movers.Add(rect);
                }
            }
            movers.RemoveAll(m => movers.Exists(other => other != m && m.IsChildOf(other)));

            foreach (RectTransform mover in movers)
            {
                if (!_Origin.TryGetValue(mover, out Vector2 origin))
                {
                    origin = mover.anchoredPosition;
                    _Origin[mover] = origin;
                }
                mover.anchoredPosition = origin;
            }

            TextUtil.VisibleX(name, out float nameLeft, out _);
            TextUtil.VisibleX(label, out float labelLeft, out _);
            float shift = nameLeft - labelLeft;
            if (shift <= 0f)
            {
                return;
            }
            foreach (RectTransform mover in movers)
            {
                mover.position += new Vector3(shift, 0f, 0f);
            }
        }
    }

    [HarmonyPatch(typeof(WGTraitBtn), "UpdateWidget")]
    public class WGTraitBtn_UpdateWidget_Hook
    {
        public static void Postfix(ref WGText ___itemName)
        {
            //Debug.Log("WGTraitBtn_UpdateWidget_Hook");
            Text text = ___itemName.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(-120f, -20f);
        }
    }

    [HarmonyPatch(typeof(WGTitleValue), "TitleFormat")]
    public class WGTitleValue_TitleFormat_Hook
    {
        public static void Postfix(ref Text ___Title)
        {
            //Debug.Log("WGTitleValue_TitleFormat_Hook");
            ___Title.font = TextUtil.UIFont;
            ___Title.fontSize = 24;
            TrainingTipArrow.Raise(___Title);
        }
    }

    /*
     * Combat screens (every screen whose class starts with UIBattle: the battle screen with the victory and failure conditions,
     * the skill bar, the battle status, the stance list, the item and gear popup, the result): their texts written in the game's
     * own font (Kaiu, which is the Windows font DFKai-SB) get the CombatFont from the config file, if one is set.
     * Done every time such a screen is shown, items added to its lists are copies of texts already changed.
     */
    [HarmonyPatch(typeof(UIForm), "Show")]
    public class UIForm_Show_CombatFont_Hook
    {
        public static void Postfix(UIForm __instance)
        {
            if (__instance == null || !__instance.GetType().Name.StartsWith("UIBattle", System.StringComparison.Ordinal))
            {
                return;
            }

            TextUtil.ReplaceGameFont(__instance, TextUtil.CombatFont);
        }
    }

    //The turn number of the battle screen kept the game's font, it gets the CombatFont directly every time the turn changes.
    //Turn 1 is shown before the first turn change, so also when the battle sets up its victory conditions.
    [HarmonyPatch]
    public class WgBattleRound_UpdateRound_Hook
    {
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WgBattleRound), "UpdateRound");
            yield return AccessTools.Method(typeof(WgBattleRound), "UpdateCondition");
        }

        public static void Postfix(WgBattleRound __instance, Text ___text_current, Text ___text_win_lose, GameObject ___tipObj)
        {
            Font font = TextUtil.CombatFont;
            if (font != null && ___text_current != null)
            {
                ___text_current.font = font;
            }

            if (___text_current != null)
            {
                Replacer.Run(__instance, () => TurnFirst(__instance, ___text_current, ___text_win_lose, ___tipObj));
            }
        }

        /*
         * The round shows "1  Turn", the number before a bigger "Turn". It now reads "Turn 1": the label starts where the
         * number started, the number follows it after a space, and sits on the same baseline.
         */
        private const float SpaceWidth = 0.3f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        //Made when first used, not when the hook is set up
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        private static void TurnFirst(WgBattleRound round, Text number, Text winLose, GameObject tip)
        {
            Text label = LabelOf(round, number, winLose, tip);
            if (label == null || number.rectTransform.parent == null || label.rectTransform.parent == null)
            {
                return;
            }

            LayoutGroup layout = number.transform.parent.GetComponent<LayoutGroup>();
            if (layout != null && label.transform.parent == number.transform.parent)
            {
                //A layout places them side by side, putting the label first is enough
                if (label.transform.GetSiblingIndex() > number.transform.GetSiblingIndex())
                {
                    label.transform.SetSiblingIndex(number.transform.GetSiblingIndex());
                }
                return;
            }

            Reset(number.rectTransform);
            Reset(label.rectTransform);
            Rect? numberInk = Ink(number);
            Rect? labelInk = Ink(label);
            float? numberBase = Baseline(number);
            float? labelBase = Baseline(label);
            if (numberInk == null || labelInk == null || numberBase == null || labelBase == null)
            {
                return;
            }

            float left = Mathf.Min(numberInk.Value.xMin, labelInk.Value.xMin);
            float space = Mathf.Abs(label.rectTransform.TransformVector(new Vector3(label.fontSize * SpaceWidth, 0f, 0f)).x);
            Vector3 labelMove = new Vector3(left - labelInk.Value.xMin, 0f, 0f);
            Vector3 numberMove = new Vector3(left + labelInk.Value.width + space - numberInk.Value.xMin, labelBase.Value - numberBase.Value, 0f);
            //One inside the other moves along with it
            if (label.transform.IsChildOf(number.transform))
            {
                labelMove -= numberMove;
            }
            else if (number.transform.IsChildOf(label.transform))
            {
                numberMove -= labelMove;
            }
            Move(label, labelMove);
            Move(number, numberMove);
        }

        //The "Turn" text: the closest other text of the round on the same height as the number, outside the conditions
        private static Text LabelOf(WgBattleRound round, Text number, Text winLose, GameObject tip)
        {
            Vector3[] own = new Vector3[4];
            number.rectTransform.GetWorldCorners(own);
            Vector3[] corners = new Vector3[4];
            Text best = null;
            float bestDistance = float.MaxValue;
            foreach (Text other in round.GetComponentsInChildren<Text>())
            {
                if (other == number || other == winLose || (tip != null && other.transform.IsChildOf(tip.transform))
                    || string.IsNullOrEmpty(other.text) || other.text.Trim().Length < 2)
                {
                    continue;
                }
                other.rectTransform.GetWorldCorners(corners);
                if (corners[1].y <= own[0].y || corners[0].y >= own[1].y)
                {
                    continue;
                }
                float distance = Mathf.Abs((corners[0].x + corners[2].x) / 2f - (own[0].x + own[2].x) / 2f);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void Reset(RectTransform rect)
        {
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
        }

        private static void Move(Text text, Vector3 world)
        {
            RectTransform rect = text.rectTransform;
            Vector3 local = rect.parent.InverseTransformVector(world);
            rect.anchoredPosition += new Vector2(local.x, local.y);
        }

        //World box of the drawn letters
        private static Rect? Ink(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, text.GetGenerationSettings(rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (UIVertex vert in verts)
            {
                min = Vector2.Min(min, vert.position);
                max = Vector2.Max(max, vert.position);
            }
            Vector3 a = rect.TransformPoint(min / text.pixelsPerUnit);
            Vector3 b = rect.TransformPoint(max / text.pixelsPerUnit);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        //World height of the line the text is written on: the bottom of an "H" laid out like it
        private static float? Baseline(Text text)
        {
            RectTransform rect = text.rectTransform;
            TextGenerationSettings settings = text.GetGenerationSettings(rect.rect.size);
            settings.richText = false;
            _Generator.Populate("H", settings);
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count < 4 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            float bottom = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                bottom = Mathf.Min(bottom, verts[i].position.y);
            }
            return rect.TransformPoint(new Vector3(0f, bottom / text.pixelsPerUnit, 0f)).y;
        }
    }

    /*
     * Screens that still had texts in the game's own font get the English font from the config file (Font, Arial by default)
     * like the rest of the English texts. Done every time the screen is shown.
     *  - UINurturance, the main life screen: energy text, "Martial arts training and reading bonus!", the stats total
     *  - UIDate, the date next to it: "Year 2 Month 6", "Mid"
     *  - UIRelationship, a character in Socialize: the relationship levels and their abilities, the hobbies
     *    (also done when another character is picked, see UIRelationship_UpdateRelationship_Hook)
     */
    [HarmonyPatch(typeof(UIForm), "Show")]
    public class UIForm_Show_LifeFont_Hook
    {
        private static readonly System.Collections.Generic.HashSet<string> _Forms = new System.Collections.Generic.HashSet<string>
        {
            "UINurturance", "UIDate", "UIRelationship"
        };

        public static void Postfix(UIForm __instance)
        {
            if (__instance == null || !_Forms.Contains(__instance.GetType().Name))
            {
                return;
            }

            TextUtil.ReplaceGameFont(__instance, TextUtil.UIFont);
        }
    }

    /*
     * The "Info", "Criteria" and "Effect" titles of the gear selection popup and of the item and gear popup in combat:
     * "Criteria" (24-25 in the prefab) ran into the diamonds at both ends of its banner. All three get the same smaller size.
     * These popups have two copies of the panel (StaticCanvas/PropsBG draws the visible titles, DynamicCanvas/PropsIntroduction
     * has hidden ones), both are changed. Done once per popup, nothing sets the size back.
     */
    internal static class PopupTitles
    {
        private const int Size = 19;

        //String ids of the "Info", "Criteria" and "Effect" titles
        private static readonly System.Collections.Generic.HashSet<string> _Ids = new System.Collections.Generic.HashSet<string>
        {
            "General_Introduction", "General_Condition", "General_PassCondition", "General_Effect"
        };

        private static readonly string[] _Paths =
        {
            "StaticCanvas/PropsBG/Description/Title", "StaticCanvas/PropsBG/ConditionDescription/Title", "StaticCanvas/PropsBG/EffectDescription/Title",
            "DynamicCanvas/PropsIntroduction/Description/Title", "DynamicCanvas/PropsIntroduction/ConditionDescription/Title", "DynamicCanvas/PropsIntroduction/EffectDescription/Title",
        };

        private static readonly System.Collections.Generic.HashSet<UIForm> _Done = new System.Collections.Generic.HashSet<UIForm>();

        public static void SetSize(UIForm popup)
        {
            if (popup == null || !_Done.Add(popup))
            {
                return;
            }

            foreach (WGStringTable label in popup.GetComponentsInChildren<WGStringTable>(true))
            {
                Text text = label.GetComponent<Text>();
                if (text != null && _Ids.Contains(TextUtil.StringId(label)))
                {
                    text.fontSize = Size;
                }
            }
            foreach (string path in _Paths)
            {
                Text title = popup.transform.Find(path)?.GetComponent<Text>();
                if (title != null)
                {
                    title.fontSize = Size;
                }
            }
        }
    }

    /*
     * The item and gear popup in combat (UIBattleInventoryWindow). It has two copies of the panel: StaticCanvas/PropsBG draws
     * the paper, the "Info", "Criteria" and "Effect" banners and their titles, DynamicCanvas/PropsIntroduction draws the same
     * banners again with the texts in them. The banners are separate objects (not part of the paper picture), so they can move.
     * The Info text area is 75 high and its 3rd line already touched the "Criteria" banner, a 4th line ran over it.
     * The Criteria and Effect parts (both copies of the banners, with their texts) move down as far as the Info text needs,
     * the Effect text keeps its bottom edge. They move at most as far as the Effect text still fits, a longer Info shrinks.
     */
    internal static class BattlePopup
    {
        //Gap kept between the Info text and the "Criteria" banner below it, and between the "Effect" banner and its text
        private const float Gap = 8f;

        //Position and size each part has in the prefab, the popup is updated for every item
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Layout(WGPropsIntroduction popup, Text info, Text condition, Text effect, int minSize)
        {
            RectTransform panel = popup.transform as RectTransform;
            RectTransform conditionBanner = condition != null ? condition.transform.parent as RectTransform : null;
            RectTransform effectBanner = effect != null ? effect.transform.parent as RectTransform : null;
            if (panel == null || info == null || conditionBanner == null || effectBanner == null
                || conditionBanner.name != "ConditionDescription" || effectBanner.name != "EffectDescription"
                || conditionBanner.parent != panel || effectBanner.parent != panel)
            {
                return;
            }

            Transform window = TextUtil.OwningForm(popup)?.transform;
            RectTransform staticConditionBanner = window?.Find("StaticCanvas/PropsBG/ConditionDescription") as RectTransform;
            RectTransform staticEffectBanner = window?.Find("StaticCanvas/PropsBG/EffectDescription") as RectTransform;
            PopupTitles.SetSize(TextUtil.OwningForm(popup));

            foreach (RectTransform rect in new[] { info.rectTransform, conditionBanner, effectBanner, effect.rectTransform, staticConditionBanner, staticEffectBanner })
            {
                Restore(rect);
            }

            foreach (Text text in new[] { info, effect })
            {
                text.text = text.text?.Replace('\u00A0', ' ');
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }

            //How far down Criteria and Effect have to go for the whole Info text, and how far they can go
            float infoTop = Top(info.rectTransform, panel);
            float conditionTop = Top(conditionBanner, panel);
            float wanted = conditionTop - (infoTop - info.preferredHeight - Gap);
            float room = Bottom(effectBanner, panel) - Gap - Bottom(effect.rectTransform, panel) - effect.preferredHeight;
            float shift = Mathf.Clamp(wanted, 0f, Mathf.Max(0f, room));

            if (shift > 0f)
            {
                foreach (RectTransform banner in new[] { conditionBanner, effectBanner, staticConditionBanner, staticEffectBanner })
                {
                    if (banner != null)
                    {
                        banner.anchoredPosition -= new Vector2(0f, shift);
                    }
                }
                //The Effect text moved down with its banner, its bottom edge goes back up to where it was
                MoveBottom(effect.rectTransform, -shift);
            }

            //Info may use the room down to the gap above the (moved) "Criteria" banner
            float infoBottom = conditionTop - shift + Gap;
            MoveBottom(info.rectTransform, Bottom(info.rectTransform, panel) - infoBottom);
            if (info.preferredHeight > info.rectTransform.rect.height)
            {
                TextUtil.ShrinkToHeight(info, minSize);
            }
            if (effect.preferredHeight > effect.rectTransform.rect.height)
            {
                TextUtil.ShrinkToHeight(effect, minSize);
            }
        }

        private static void Restore(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }
            if (!_Original.TryGetValue(rect, out Vector2[] original))
            {
                original = new[] { rect.anchoredPosition, rect.sizeDelta };
                _Original[rect] = original;
            }
            rect.anchoredPosition = original[0];
            rect.sizeDelta = original[1];
        }

        //Moves the bottom edge of a rect down by down (up when negative), the top edge stays, whatever its pivot
        private static void MoveBottom(RectTransform rect, float down)
        {
            rect.sizeDelta += new Vector2(0f, down);
            rect.anchoredPosition -= new Vector2(0f, down * (1f - rect.pivot.y));
        }

        //Top and bottom edge of a rect, in the panel's space
        private static float Top(RectTransform rect, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[1]).y;
        }

        private static float Bottom(RectTransform rect, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[0]).y;
        }
    }

    /*
     * The tabs of the pawn shop (Weapon, Armor, Accessories, Medicine, Gift): the names were centered on the tab, so they ran
     * over their icon and into the slanted edge of the next tab. Each tab button (184 wide) holds an Icon (43 x 43) with the
     * name inside it (TabBtn_Weapon/Icon/Text). The icons get smaller (their left edge stays), and each name is centered in
     * the room between its icon and the slanted edge, shrinking only if it does not fit. Done when a name gets its English
     * text (WGStringTable.UpdateString), the shop is not laid out with English names before that.
     */
    internal static class PawnshopTabs
    {
        private const float IconScale = 0.8f;
        private const int MinSize = 12;
        //Gap between the icon and the name, in pixels at 1920x1080
        private const float Gap = 4f;

        //Name -> where its room ends, from the left edge of its tab button, before the slanted edge (pixels at 1920x1080,
        //measured on the pawn shop at the bottom of the letters, where the edge is furthest left; Weapon a little less)
        private static readonly System.Collections.Generic.Dictionary<string, float> _RoomRight = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Weapon", 154f }, { "Armor", 149f }, { "Accessories", 156f }, { "Medicine", 153f }, { "Gift", 149f },
        };

        //Name -> (font sizes added after fitting, pixels moved to the left). The bag and bottle icons are drawn narrower than
        //their box, so these two names can come a little closer to them and be a little bigger.
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> _Adjust = new System.Collections.Generic.Dictionary<string, Vector2>
        {
            { "Accessories", new Vector2(2f, 4f) }, { "Medicine", new Vector2(2f, 4f) },
        };

        //Size, position and alignment each name has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Text, TextAnchor> _Alignment = new System.Collections.Generic.Dictionary<Text, TextAnchor>();

        public static void Fit(Text text)
        {
            if (text == null || text.text == null || !_RoomRight.TryGetValue(text.text.Trim(), out float roomRight) || !(TextUtil.OwningForm(text) is UIPawnshop))
            {
                return;
            }
            RectTransform button = text.GetComponentInParent<WGTabBtn>()?.transform as RectTransform;
            Image icon = text.transform.parent != null ? text.transform.parent.GetComponent<Image>() : null;
            if (button == null || icon == null || icon.transform == button)
            {
                return;
            }

            //The icon first: the name sits inside it and moves with it
            IconSizer.Shrink(icon.rectTransform, IconScale);

            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
                _OriginalSize[text] = text.fontSize;
                _Alignment[text] = text.alignment;
            }
            rect.anchoredPosition = origin;
            text.fontSize = _OriginalSize[text];
            text.alignment = (TextAnchor)((int)_Alignment[text] / 3 * 3 + 1);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            float pixel = button.lossyScale.x;
            Vector3[] corners = new Vector3[4];
            button.GetWorldCorners(corners);
            float right = corners[0].x + roomRight * pixel;
            icon.rectTransform.GetWorldCorners(corners);
            float left = corners[2].x + Gap * pixel;
            if (right <= left)
            {
                return;
            }

            float room = (right - left) / rect.lossyScale.x;
            while (text.fontSize > MinSize && TextUtil.LinesWidth(text, text.text) > room)
            {
                text.fontSize--;
            }
            float move = 0f;
            if (_Adjust.TryGetValue(text.text.Trim(), out Vector2 adjust))
            {
                text.fontSize = Mathf.Min(_OriginalSize[text], text.fontSize + (int)adjust.x);
                move = adjust.y * pixel;
            }
            float center = rect.TransformPoint(rect.rect.center).x;
            rect.position += new Vector3((left + right) / 2f - move - center, 0f, 0f);
        }
    }

    /*
     * Keeps a picture smaller than the game makes it, with its left edge where it was. Its size is changed, not its scale, so
     * what sits inside it (a tab name) keeps its own size. Applied again right before each frame is drawn, so an animation
     * that sets the picture's size or position does not undo it: a value the animation set becomes the new base, a value we
     * set ourselves is left alone (so nothing shrinks twice).
     */
    internal static class IconSizer
    {
        private class Entry
        {
            public float Scale;
            public Vector2 BaseSize;
            public Vector2 BasePosition;
            public Vector2 SetSize;
            public Vector2 SetPosition;
        }

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Entry> _Entries = new System.Collections.Generic.Dictionary<RectTransform, Entry>();
        private static bool _Subscribed;

        public static void Shrink(RectTransform icon, float scale)
        {
            if (!_Entries.TryGetValue(icon, out Entry entry))
            {
                entry = new Entry { Scale = scale, BaseSize = icon.sizeDelta, BasePosition = icon.anchoredPosition, SetSize = new Vector2(float.NaN, float.NaN) };
                _Entries[icon] = entry;
            }
            Apply(icon, entry);
            if (!_Subscribed)
            {
                Canvas.willRenderCanvases += ApplyAll;
                _Subscribed = true;
            }
        }

        private static void Apply(RectTransform icon, Entry entry)
        {
            if (icon.sizeDelta != entry.SetSize)
            {
                entry.BaseSize = icon.sizeDelta;
            }
            if (icon.anchoredPosition != entry.SetPosition)
            {
                entry.BasePosition = icon.anchoredPosition;
            }
            entry.SetSize = entry.BaseSize * entry.Scale;
            //The size changes around the pivot, the left edge goes back to where it was
            entry.SetPosition = entry.BasePosition - new Vector2(icon.pivot.x * (entry.BaseSize.x - entry.SetSize.x), 0f);
            if (icon.sizeDelta != entry.SetSize)
            {
                icon.sizeDelta = entry.SetSize;
            }
            if (icon.anchoredPosition != entry.SetPosition)
            {
                icon.anchoredPosition = entry.SetPosition;
            }
        }

        private static readonly System.Collections.Generic.List<RectTransform> _Gone = new System.Collections.Generic.List<RectTransform>();

        private static void ApplyAll()
        {
            foreach (var pair in _Entries)
            {
                //An icon destroyed with its screen compares equal to null
                if (pair.Key == null)
                {
                    _Gone.Add(pair.Key);
                }
                else if (pair.Key.gameObject.activeInHierarchy)
                {
                    Apply(pair.Key, pair.Value);
                }
            }
            foreach (RectTransform gone in _Gone)
            {
                _Entries.Remove(gone);
            }
            _Gone.Clear();
        }
    }

    //Refine Medicine: the master's speech bubble broke in the middle of words ("It / 's a hassle", the game turns the spaces
    //into non-breaking spaces). It wraps at spaces again and shrinks if it would not fit the bubble.
    [HarmonyPatch(typeof(WGElixirIntroduction), "UpdateWidget", new[] { typeof(ElixirIntroductionInfo) })]
    public class WGElixirIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;

        public static void Postfix(WGText ___talk)
        {
            TextUtil.ShrinkToBox(TextUtil.TextOf(___talk), MinSize);
        }
    }

    /*
     * The tabs of the save/load screen (Local storage, Cloud Save, Auto Save, Combat Autosave): the names are centered on the
     * tab, so they ran over the icon on the left and into the slanted edge of the next tab. Each name moves right, next to its
     * icon, and is fitted to the room between the icon and the slanted edge: it goes on two lines if one is too wide, and only
     * shrinks if two lines are still too wide. Room and move are measured at 1920x1080, the first tab is shaped differently.
     */
    internal static class SaveLoadTab
    {
        private const int MinSize = 14;

        //Tab name id -> (move to the right, room between the icon and the slanted edge)
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> _Tabs = new System.Collections.Generic.Dictionary<string, Vector2>
        {
            { "SecondaryInterface2117", new Vector2(3f, 115f) },  //Local storage
            { "SecondaryInterface2103", new Vector2(35f, 145f) }, //Cloud Save
            { "SecondaryInterface2104", new Vector2(35f, 145f) }, //Auto Save
            { "SecondaryInterface2118", new Vector2(35f, 145f) }, //Combat Autosave
        };

        //Size and position each name has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Fit(Text text, string id)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || !_Tabs.TryGetValue(id, out Vector2 tab) || !(TextUtil.OwningForm(text) is UISaveLoad))
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(tab.x, 0f);

            if (!_OriginalSize.TryGetValue(text, out int size))
            {
                size = text.fontSize;
                _OriginalSize[text] = size;
            }
            text.fontSize = size;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.MiddleCenter;

            string oneLine = text.text.Replace('\n', ' ').Replace('\u00A0', ' ').Trim();
            text.text = TextUtil.LinesWidth(text, oneLine) <= tab.y ? oneLine : TextUtil.TwoLines(text, oneLine);
            while (text.fontSize > MinSize && TextUtil.LinesWidth(text, text.text) > tab.y)
            {
                text.fontSize--;
            }
        }
    }

    /*
     * The training tip (hovering a skill or inner art in the training menu) shows "Lv. 1 → Lv. 2" and "Damage 214→230".
     * Arial draws → small and low, next to the numbers it looked offset. A bigger arrow sits in the middle of the line.
     */
    internal static class TrainingTipArrow
    {
        private const float Scale = 1.4f;

        public static void Raise(Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || text.text.IndexOf('→') < 0 || text.text.Contains("→</size>")
                || !(TextUtil.OwningForm(text) is UINurturance))
            {
                return;
            }

            //"Lv. 1 → Lv. 2" has a space on both sides of the arrow, "Damage 214→230" had none, both get one
            text.supportRichText = true;
            text.text = text.text.Replace(" → ", "→").Replace("→", " <size=" + Mathf.RoundToInt(text.fontSize * Scale) + ">→</size> ");
        }
    }

    [HarmonyPatch(typeof(WGTitleValue), "ValueFromat")]
    public class WGTitleValue_ValueFromat_Hook
    {
        public static void Postfix(ref Text ___Value)
        {
            //Debug.Log("WGTitleValue_ValueFromat_Hook");
            ___Value.font = TextUtil.UIFont;
            ___Value.fontSize = 20;
            TrainingTipArrow.Raise(___Value);
        }
    }

    [HarmonyPatch(typeof(UIRelationship), "UpdateRelationship")]
    public class UIRelationship_UpdateRelationship_Hook
    {
        public static void Postfix(UIRelationship __instance, ref Text ___community_name, ref Text ___community_introduction, WGFavorability[] ___community_fav_level)
        {
            //Debug.Log("UIRelationship_UpdateRelationship_Hook");
            //Before fitting the abilities, so they are measured in the font they are shown in
            TextUtil.ReplaceGameFont(__instance, TextUtil.UIFont);
            RelationshipAbilities.Fit(___community_fav_level);

            ___community_name.font = TextUtil.UIFont;
            ___community_introduction.font = TextUtil.UIFont;

            //The name box is sized for 2-4 Chinese characters, "Nan Feihuang" wrapped at the space and only "Nan" was left. We keep it on one line and shrink it to fit.
            TextUtil.ShrinkToWidth(___community_name, 16);

            //The game turns every space into a non-breaking space (StringTool.Replace), so English lines broke in the middle of words. Turning them back allows wrapping at spaces.
            ___community_introduction.text = ___community_introduction.text.Replace(' ', ' ');

            //Longer bios got cut off at the bottom of the Info box, so we let the text shrink until it fits with a little room
            //above and below, then justify it. It sits in the middle of the box, so the room is the same at the top and bottom.
            TextUtil.ShrinkToBox(___community_introduction, 12, BioPadding);
            ___community_introduction.alignment = TextAnchor.MiddleLeft;
            TextUtil.Justify(___community_introduction);

            //The text sits closer to the right edge of the Info box than to the left one, moved left so both gaps are about the same
            RectTransform rect = ___community_introduction.rectTransform;
            if (!_BioOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _BioOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(-BioShift, 0f);
        }

        private const float BioShift = 8f;
        private const float BioPadding = 4f;

        //Where the bio sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _BioOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
    }

    //Hobby labels on the relationship screen (Calligraphy, Alcohol...), the pills are sized for 2 Chinese characters
    [HarmonyPatch(typeof(WGFavItem), "SetFavItem")]
    public class WGFavItem_SetFavItem_Hook
    {
        public static void Postfix(Text ___item_name)
        {
            if (___item_name == null || !___item_name.gameObject.activeSelf || string.IsNullOrEmpty(___item_name.text))
            {
                return;
            }

            TextUtil.ShrinkToWidth(___item_name, 12);
        }
    }

    /*
     * Status tab traits: the description shrinks (down to 15) to fit its box, and is justified. It sat near the top of the
     * dark box behind it, with much less room below than above, so it is also centered in that box: the drawn letters of the
     * whole text, as Unity lays them out, have the same room above and below.
     */
    [HarmonyPatch(typeof(WGTalentItem), "UpdateWidget")]
    public class WGTalentItem_UpdateWidget_Hook
    {
        private const int MinSize = 15;
        //Room kept above and below the text inside the box
        private const float BoxPadding = 8f;

        //Where each description is in the prefab, the items are reused for every character
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly TextGenerator _Generator = new TextGenerator();

        public static void Postfix(WGTalentItem __instance)
        {
            System.Array talents = Traverse.Create(__instance).Field("talents").GetValue() as System.Array;
            if (talents == null)
            {
                return;
            }

            foreach (object talent in talents)
            {
                FitName(TextUtil.TextOf(Traverse.Create(talent).Field("name").GetValue<WGText>()));

                Text text = TextUtil.TextOf(Traverse.Create(talent).Field("description").GetValue<WGText>());
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                RectTransform rect = text.rectTransform;
                ResetPosition(text);
                RectTransform box = BoxOf(text);
                TextUtil.ShrinkToHeight(text, MinSize);
                float room = WorldHeight(box) - 2f * Mathf.Abs(box.TransformVector(new Vector3(0f, BoxPadding, 0f)).y);
                while (Mathf.Abs(rect.TransformVector(new Vector3(0f, text.preferredHeight, 0f)).y) > room && text.fontSize > MinSize)
                {
                    text.fontSize--;
                }

                //Middle of its own rect first, then the drawn letters are centered in the box
                text.alignment = TextAnchor.MiddleLeft;
                TextUtil.Justify(text);
            }

            Place(talents);
            //The tab slides in and its layout is only finished in the next frames, until then the places measured can be off
            Replacer.Run(__instance, () => Place(talents));
        }

        //Puts the names and descriptions where they belong, from where the game put them (can be done again at any time)
        internal static void Place(System.Array talents)
        {
            foreach (object talent in talents)
            {
                Text text = TextUtil.TextOf(Traverse.Create(talent).Field("description").GetValue<WGText>());
                Text name = TextUtil.TextOf(Traverse.Create(talent).Field("name").GetValue<WGText>());
                if (name != null && name.isActiveAndEnabled && !string.IsNullOrEmpty(name.text))
                {
                    ResetPosition(name);
                    PlaceName(name, text);
                }
                if (text != null && text.isActiveAndEnabled && !string.IsNullOrEmpty(text.text))
                {
                    ResetPosition(text);
                    RectTransform box = BoxOf(text);
                    LogOnce(text, box);
                    CenterInBox(text, box);
                }
            }
        }

        //Back where the prefab has it, the items are reused for every character
        private static void ResetPosition(Text text)
        {
            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
        }

        /*
         * The trait name ("Gifted") sits after the big "Traits :" title, in a smaller font, and was lower than it.
         * It stays where it starts across, and is moved up or down so its letters have the same middle as the title's
         * (middle of the capitals and lower case bands, see WGNurturanceBtn_UpdateButton_Hook).
         */
        private static readonly System.Collections.Generic.Dictionary<Text, TextAnchor> _Alignment = new System.Collections.Generic.Dictionary<Text, TextAnchor>();

        private static void FitName(Text name)
        {
            if (name == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            if (!_Alignment.TryGetValue(name, out TextAnchor alignment))
            {
                alignment = name.alignment;
                _Alignment[name] = alignment;
            }
            //Same side across as in the prefab, middle top to bottom
            switch ((int)alignment % 3)
            {
                case 0: name.alignment = TextAnchor.MiddleLeft; break;
                case 1: name.alignment = TextAnchor.MiddleCenter; break;
                default: name.alignment = TextAnchor.MiddleRight; break;
            }
            TextUtil.ShrinkToWidth(name, 12);
        }

        private static void PlaceName(Text name, Text description)
        {
            RectTransform rect = name.rectTransform;
            Text title = TitleOf(name, description);
            LogOnce(name, title == null ? null : title.rectTransform);
            //A group placing both would move the title along with the name
            if (title == null || (LayoutOf(name) != null && LayoutOf(name) == LayoutOf(title)))
            {
                return;
            }
            ResetLayout(name);
            float? nameMiddle = VisualMiddle(name);
            float? titleMiddle = VisualMiddle(title);
            if (nameMiddle == null || titleMiddle == null || rect.parent == null)
            {
                return;
            }
            Shift(name, rect.parent.InverseTransformVector(new Vector3(0f, titleMiddle.Value - nameMiddle.Value, 0f)).y);
        }

        //The "Traits" title on the same row as the name: the biggest other text nearby that overlaps it top to bottom
        private static Text TitleOf(Text name, Text description)
        {
            Vector3[] own = new Vector3[4];
            name.rectTransform.GetWorldCorners(own);
            Vector3[] corners = new Vector3[4];
            Text best = null;
            Transform root = name.transform.parent;
            if (root != null && root.parent != null && root.GetComponent<UIForm>() == null)
            {
                root = root.parent;
            }
            if (root == null)
            {
                return null;
            }
            foreach (Text other in root.GetComponentsInChildren<Text>())
            {
                if (other == name || other == description || string.IsNullOrEmpty(other.text) || other.text.Trim().Length < 2)
                {
                    continue;
                }
                other.rectTransform.GetWorldCorners(corners);
                if (corners[1].y <= own[0].y || corners[0].y >= own[1].y)
                {
                    continue;
                }
                if (best == null || other.fontSize > best.fontSize)
                {
                    best = other;
                }
            }
            return best;
        }

        //World height of the middle between the capitals band and the lower case band of a one line text, where it is drawn
        private static float? VisualMiddle(Text text)
        {
            RectTransform rect = text.rectTransform;
            TextGenerationSettings settings = text.GetGenerationSettings(rect.rect.size);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.richText = false;
            float? caps = LetterMiddle("H", settings, text.pixelsPerUnit);
            float? lower = LetterMiddle("x", settings, text.pixelsPerUnit);
            if (caps == null || lower == null)
            {
                return null;
            }
            return rect.TransformPoint(new Vector3(0f, (caps.Value + lower.Value) / 2f, 0f)).y;
        }

        private static float? LetterMiddle(string letter, TextGenerationSettings settings, float pixelsPerUnit)
        {
            _Generator.Populate(letter, settings);
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count < 4 || pixelsPerUnit <= 0f)
            {
                return null;
            }
            float bottom = float.MaxValue, top = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                bottom = Mathf.Min(bottom, verts[i].position.y);
                top = Mathf.Max(top, verts[i].position.y);
            }
            return (bottom + top) / 2f / pixelsPerUnit;
        }

        //Where the text and the boxes above it are, written once per text to the log (ModAPI console, F9)
        private static readonly System.Collections.Generic.HashSet<Text> _Logged = new System.Collections.Generic.HashSet<Text>();

        private static void LogOnce(Text text, RectTransform box)
        {
            if (!_Logged.Add(text))
            {
                return;
            }

            var line = new StringBuilder("[EnglishPatch] Trait text \"").Append(text.text.Length > 20 ? text.text.Substring(0, 20) : text.text).Append("\"");
            for (Transform t = text.transform; t != null && t.GetComponent<UIForm>() == null; t = t.parent)
            {
                RectTransform rect = t as RectTransform;
                line.Append(" | ").Append(t.name).Append(t == box ? " (box)" : "");
                if (rect != null)
                {
                    line.Append(" size ").Append(rect.rect.size).Append(" pos ").Append(rect.anchoredPosition);
                }
                if (t.GetComponent<Image>() != null) line.Append(" Image");
                LayoutGroup layout = t.GetComponent<LayoutGroup>();
                if (layout != null) line.Append(" ").Append(layout.GetType().Name).Append(" pad ").Append(layout.padding).Append(" align ").Append(layout.childAlignment);
                if (t.GetComponent<ContentSizeFitter>() != null) line.Append(" Fitter");
                LayoutElement element = t.GetComponent<LayoutElement>();
                if (element != null && element.ignoreLayout) line.Append(" ignoreLayout");
            }
            Debug.Log(line.ToString());
        }

        /*
         * The dark box behind a text: the smallest image that spans the text across and holds its middle, among its parents
         * and their other children (the box can be an image drawn behind the text, not only one of its parents).
         * The text's own rect when there is none.
         */
        private static RectTransform BoxOf(Text text)
        {
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            float middle = (own[0].y + own[1].y) / 2f;
            Vector3[] corners = new Vector3[4];
            RectTransform best = null;
            float bestArea = float.MaxValue;
            int levels = 0;
            for (Transform t = text.transform.parent; t != null && levels < 4 && t.GetComponent<UIForm>() == null; t = t.parent, levels++)
            {
                Consider(t);
                foreach (Transform child in t)
                {
                    if (child != text.transform && child.gameObject.activeInHierarchy)
                    {
                        Consider(child);
                    }
                }
            }
            return best ?? text.rectTransform;

            void Consider(Transform t)
            {
                RectTransform rect = t as RectTransform;
                Image image = t.GetComponent<Image>();
                if (rect == null || image == null || !image.enabled)
                {
                    return;
                }
                rect.GetWorldCorners(corners);
                float area = (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y);
                if (corners[0].x <= own[0].x + 1f && corners[2].x >= own[2].x - 1f && corners[0].y < middle && corners[1].y > middle && area < bestArea)
                {
                    best = rect;
                    bestArea = area;
                }
            }
        }

        private static float WorldHeight(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[1].y - corners[0].y;
        }

        /*
         * Moves the text so the top of its first line and the bottom of its last line are as far from the box edges.
         * When a layout group places the text, a moved text is put back by the next layout pass, so the shift goes into the
         * group's padding instead (more at the top and less at the bottom moves it down by the same amount, whatever the
         * child alignment). The padding of each group is always taken from the prefab, the items are reused.
         */
        private static void CenterInBox(Text text, RectTransform box)
        {
            ResetLayout(text);
            float? shift = ShiftToCenter(text, box);
            if (shift != null)
            {
                Shift(text, shift.Value);
            }
        }

        //The layout group that places the text, if any
        private static LayoutGroup LayoutOf(Text text)
        {
            RectTransform rect = text.rectTransform;
            LayoutElement element = text.GetComponent<LayoutElement>();
            if (rect.parent == null || (element != null && element.ignoreLayout))
            {
                return null;
            }
            return rect.parent.GetComponent<LayoutGroup>();
        }

        //Puts back the prefab padding of the group placing the text, so it is measured where the game puts it
        private static void ResetLayout(Text text)
        {
            LayoutGroup layout = LayoutOf(text);
            if (layout == null)
            {
                return;
            }
            if (!_Padding.TryGetValue(layout, out RectOffset original))
            {
                original = new RectOffset(layout.padding.left, layout.padding.right, layout.padding.top, layout.padding.bottom);
                _Padding[layout] = original;
            }
            layout.padding = new RectOffset(original.left, original.right, original.top, original.bottom);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)text.rectTransform.parent);
        }

        //Moves the text up by this much (units of its parent), through the padding of its layout group if it has one
        private static void Shift(Text text, float shift)
        {
            LayoutGroup layout = LayoutOf(text);
            if (layout != null)
            {
                ResetLayout(text);
                int units = Mathf.RoundToInt(shift);
                RectOffset original = _Padding[layout];
                layout.padding = new RectOffset(original.left, original.right, original.top - units, original.bottom + units);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)text.rectTransform.parent);
            }
            else
            {
                text.rectTransform.anchoredPosition += new Vector2(0f, shift);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<LayoutGroup, RectOffset> _Padding = new System.Collections.Generic.Dictionary<LayoutGroup, RectOffset>();

        //How far up the text has to move (in the units of its parent) so its drawn letters are in the middle of the box
        private static float? ShiftToCenter(Text text, RectTransform box)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, text.GetGenerationSettings(rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f || rect.parent == null)
            {
                return null;
            }

            float bottom = float.MaxValue, top = float.MinValue;
            foreach (UIVertex vert in verts)
            {
                bottom = Mathf.Min(bottom, vert.position.y);
                top = Mathf.Max(top, vert.position.y);
            }
            float inkMiddle = rect.TransformPoint(new Vector3(0f, (bottom + top) / 2f / text.pixelsPerUnit, 0f)).y;

            Vector3[] corners = new Vector3[4];
            box.GetWorldCorners(corners);
            float boxMiddle = (corners[0].y + corners[1].y) / 2f;

            return rect.parent.InverseTransformVector(new Vector3(0f, boxMiddle - inkMiddle, 0f)).y;
        }
    }

    /*
     * Does a placing again every frame for a moment after a widget is updated: screens slide in and finish their layout in
     * the next frames, until then the places measured can be off. The placing must give the same result when done again.
     */
    public class Replacer : MonoBehaviour
    {
        private const float Duration = 1f;

        private System.Action _Place;
        private float _Until;

        public static void Run(Component widget, System.Action place)
        {
            Replacer replacer = widget.GetComponent<Replacer>();
            if (replacer == null)
            {
                replacer = widget.gameObject.AddComponent<Replacer>();
            }
            replacer._Place = place;
            replacer._Until = Time.unscaledTime + Duration;
            replacer.enabled = true;
        }

        private void LateUpdate()
        {
            if (_Place == null || Time.unscaledTime > _Until)
            {
                enabled = false;
                return;
            }
            try
            {
                _Place();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[EnglishPatch] Could not place " + name + ": " + e.Message);
                enabled = false;
            }
        }
    }

    /*
     * Combat skill bar: the key numbers (1 to 9) under the buttons sat off the middle of the dark badge behind them.
     * Each one is put in the middle of its badge (its drawn digit, both ways), and the badge is lighter (more see-through), it was a heavy black spot.
     */
    /*
     * Tooltip texts (the skill Effect in combat, buffs, traits): the row is made exactly as high as the text, and a line
     * of text has room for tall letters above it but little below, so the text sat low: a big gap under the heading
     * above it and almost none at the bottom of the tooltip. The row now keeps a set gap above the first line's letters
     * and a bigger one below the last line's.
     */
    [HarmonyPatch(typeof(WGTip), "SetTipType")]
    public class WGTip_SetTipType_Hook
    {
        private const float TopGap = 4f;
        private const float BottomGap = 14f;

        private static readonly TextGenerator _Generator = new TextGenerator();

        public static void Postfix(WGTitleValue titlevalue, WGTip.TipType type)
        {
            if (titlevalue == null || (type != WGTip.TipType.Context && type != WGTip.TipType.TraitContext))
            {
                return;
            }
            Text text = titlevalue.Title;
            if (text == null || string.IsNullOrEmpty(text.text) || text.pixelsPerUnit <= 0f)
            {
                return;
            }

            //Drawn from the top of a tall enough area, where its letters start and end below that top
            RectTransform rect = text.rectTransform;
            float width = rect.rect.width;
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(width, text.preferredHeight + 200f));
            settings.textAnchor = TextAnchor.UpperLeft;
            _Generator.Populate(text.text, settings);
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0)
            {
                return;
            }
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (UIVertex vert in verts)
            {
                bottom = Mathf.Min(bottom, vert.position.y);
                top = Mathf.Max(top, vert.position.y);
            }
            float areaTop = settings.generationExtents.y * (1f - rect.pivot.y) * text.pixelsPerUnit;
            float inkTop = (areaTop - top) / text.pixelsPerUnit;
            float inkHeight = (top - bottom) / text.pixelsPerUnit;

            //Row as high as the letters and the gaps, the text from the top of the row with its letters TopGap below it
            titlevalue.SetHihgt(Mathf.CeilToInt(TopGap + inkHeight + BottomGap));
            text.alignment = (TextAnchor)((int)text.alignment % 3);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)titlevalue.transform);

            Vector3[] row = new Vector3[4];
            ((RectTransform)titlevalue.transform).GetWorldCorners(row);
            Vector3[] own = new Vector3[4];
            rect.GetWorldCorners(own);
            //Where the text's top should be, in world units: TopGap below the row top, raised by the room above its letters
            float wanted = row[1].y - Mathf.Abs(rect.TransformVector(new Vector3(0f, TopGap - inkTop, 0f)).y) * Mathf.Sign(TopGap - inkTop);
            if (rect.parent != null)
            {
                rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, wanted - own[1].y, 0f)).y);
            }
        }
    }

    [HarmonyPatch]
    public class UIBattle_SkillHotKeys_Hook
    {
        //Part of the prefab opacity the badges keep
        private const float BadgeAlpha = 0.5f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Image, float> _Alpha = new System.Collections.Generic.Dictionary<Image, float>();
        //Made when first used, not when the hook is set up
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        //The numbers are shown again when the skill bar opens or changes, and when the keys are refreshed
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIBattle), "OpenMenu");
            yield return AccessTools.Method(typeof(UIBattle), "UpdateMenu");
            yield return AccessTools.Method(typeof(UIBattle), "RefreshKeyHint");
            yield return AccessTools.Method(typeof(UIBattle), "KeyHintShow");
        }

        public static void Postfix(UIBattle __instance, System.Collections.Generic.List<WGText> ___SkillHotKeys)
        {
            if (___SkillHotKeys == null)
            {
                return;
            }
            Text[] texts = ___SkillHotKeys.ConvertAll(TextUtil.TextOf).ToArray();
            Replacer.Run(__instance, () => Place(texts));
        }

        private static void Place(Text[] texts)
        {
            foreach (Text text in texts)
            {
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                RectTransform rect = text.rectTransform;
                if (!_Origin.TryGetValue(rect, out Vector2 origin))
                {
                    origin = rect.anchoredPosition;
                    _Origin[rect] = origin;
                }
                rect.anchoredPosition = origin;

                Image badge = BadgeOf(text);
                if (badge == null || rect.parent == null)
                {
                    continue;
                }
                if (!_Alpha.TryGetValue(badge, out float alpha))
                {
                    alpha = badge.color.a;
                    _Alpha[badge] = alpha;
                }
                Color color = badge.color;
                color.a = alpha * BadgeAlpha;
                badge.color = color;

                Vector3? ink = InkCenter(text);
                if (ink == null)
                {
                    continue;
                }
                Vector3[] corners = new Vector3[4];
                badge.rectTransform.GetWorldCorners(corners);
                Vector3 middle = (corners[0] + corners[2]) / 2f;
                Vector3 shift = rect.parent.InverseTransformVector(middle - ink.Value);
                rect.anchoredPosition = origin + new Vector2(shift.x, shift.y);
            }
        }

        //The smallest image around the number, the number itself or one next to it, no more than a few times its size
        private static Image BadgeOf(Text text)
        {
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            Vector3 middle = (own[0] + own[2]) / 2f;
            float size = Mathf.Abs(text.rectTransform.TransformVector(new Vector3(0f, text.fontSize, 0f)).y);
            Vector3[] corners = new Vector3[4];
            Image best = null;
            float bestArea = float.MaxValue;
            Transform parent = text.transform.parent;
            if (parent == null)
            {
                return null;
            }
            var candidates = new System.Collections.Generic.List<Transform> { parent };
            foreach (Transform child in parent)
            {
                candidates.Add(child);
            }
            foreach (Transform t in candidates)
            {
                Image image = t.GetComponent<Image>();
                if (image == null || !image.isActiveAndEnabled)
                {
                    continue;
                }
                image.rectTransform.GetWorldCorners(corners);
                float width = corners[2].x - corners[0].x, height = corners[1].y - corners[0].y;
                if (middle.x < corners[0].x || middle.x > corners[2].x || middle.y < corners[0].y || middle.y > corners[1].y
                    || width > size * 4f || height > size * 4f)
                {
                    continue;
                }
                if (width * height < bestArea)
                {
                    best = image;
                    bestArea = width * height;
                }
            }
            return best;
        }

        //World middle of the drawn letters of a text
        private static Vector3? InkCenter(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, text.GetGenerationSettings(rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (UIVertex vert in verts)
            {
                min = Vector2.Min(min, vert.position);
                max = Vector2.Max(max, vert.position);
            }
            return rect.TransformPoint((min + max) / 2f / text.pixelsPerUnit);
        }
    }

    /*
     * Relationship screen, the ability text of every level: the text area was wider than the gray bar behind it,
     * so long texts ran past the bar. The text area now ends a little before the right end of the bar, and all
     * levels get the same font size, the largest at which every unlocked text fits on its lines.
     */
    public static class RelationshipAbilities
    {
        private const float Margin = 12f;
        private const int MinSize = 14;

        private static readonly AccessTools.FieldRef<WGFavorability, Text> _Content = AccessTools.FieldRefAccess<WGFavorability, Text>("content");

        //Width, position and font size each text has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, float[]> _Original = new System.Collections.Generic.Dictionary<Text, float[]>();

        public static void Fit(WGFavorability[] levels)
        {
            if (levels == null)
            {
                return;
            }

            var texts = new System.Collections.Generic.List<Text>();
            foreach (WGFavorability level in levels)
            {
                Text content = level != null ? _Content(level) : null;
                RectTransform bar = content != null ? content.transform.parent as RectTransform : null;
                if (bar == null)
                {
                    continue;
                }

                RectTransform rect = content.rectTransform;
                if (!_Original.TryGetValue(content, out float[] original))
                {
                    original = new[] { rect.sizeDelta.x, rect.anchoredPosition.x, content.fontSize };
                    _Original[content] = original;
                }
                rect.sizeDelta = new Vector2(original[0], rect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(original[1], rect.anchoredPosition.y);
                content.fontSize = (int)original[2];

                //Keeping the left end, the right end comes Margin before the end of the bar
                float left = rect.rect.xMin;
                float barRight = rect.InverseTransformPoint(bar.TransformPoint(new Vector3(bar.rect.xMax, 0f, 0f))).x;
                float width = barRight - Margin - left;
                if (width > 0f && width < rect.rect.width)
                {
                    float shrink = rect.rect.width - width;
                    rect.sizeDelta -= new Vector2(shrink, 0f);
                    rect.anchoredPosition -= new Vector2(shrink * (1f - rect.pivot.x), 0f);
                }

                content.text = content.text.Replace('\u00A0', ' ');
                content.horizontalOverflow = HorizontalWrapMode.Wrap;
                texts.Add(content);
            }

            int size = int.MaxValue;
            foreach (Text text in texts)
            {
                size = Mathf.Min(size, text.fontSize);
            }
            while (size > MinSize && !AllFit(texts, size))
            {
                size--;
            }
            foreach (Text text in texts)
            {
                text.fontSize = size;
            }
        }

        //Whether every unlocked text fits its area at this size without breaking its lines
        private static bool AllFit(System.Collections.Generic.List<Text> texts, int size)
        {
            foreach (Text text in texts)
            {
                //Locked levels hide their text
                if (!text.gameObject.activeSelf || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                text.fontSize = size;
                float width = text.rectTransform.rect.width;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                bool fits = text.preferredWidth <= width;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (!fits || text.preferredHeight > text.rectTransform.rect.height)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /*
     * Showing the date the English way, "Year 1 Month 3" instead of "1. Year 3. Month".
     * The date box has separate texts for the unit (Chinese only), year, year unit, month and month unit.
     * SetYear and SetMonth are called on their own, so both lay out the whole line again.
    */
    public static class DateLayout
    {
        //Center of the line inside the date box and the gaps between label and number and between year and month
        private const float CenterX = 20f;
        private const float PosY = 41.5f;
        private const float LabelGap = 8f;
        private const float GroupGap = 16f;

        public static void Layout(UIDate ui)
        {
            //Child 0 of the UIDate is the UI_Date, its child 0 is the Date_BG holding Unit (0), Year (1), YearUnit (2), Month (3) and MonthUnit (4)
            Transform dateBG = ui.transform.GetChild(0).GetChild(0);
            Text unit = dateBG.GetChild(0).GetComponent<Text>();
            Text year = dateBG.GetChild(1).GetComponent<Text>();
            Text yearUnit = dateBG.GetChild(2).GetComponent<Text>();
            Text month = dateBG.GetChild(3).GetComponent<Text>();
            Text monthUnit = dateBG.GetChild(4).GetComponent<Text>();

            unit.text = "";
            unit.gameObject.SetActive(false);
            yearUnit.text = "Year";
            yearUnit.fontSize = 28;
            monthUnit.text = "Month";
            monthUnit.fontSize = 28;

            Text[] order = { yearUnit, year, monthUnit, month };
            float[] gaps = { LabelGap, GroupGap, LabelGap, 0f };
            float total = 0f;
            foreach (Text t in order)
            {
                //Left aligned on one line, keeping the vertical alignment, so each text starts where we put it
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.alignment = (TextAnchor)((int)t.alignment / 3 * 3);
                t.rectTransform.pivot = new Vector2(0f, t.rectTransform.pivot.y);
            }
            for (int i = 0; i < order.Length; i++)
            {
                total += order[i].preferredWidth + gaps[i];
            }

            float x = CenterX - total / 2f;
            for (int i = 0; i < order.Length; i++)
            {
                RectTransform rt = order[i].rectTransform;
                float width = order[i].preferredWidth;
                rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
                rt.localPosition = new Vector3(x, PosY, 0f);
                x += width + gaps[i];
            }
        }
    }

    [HarmonyPatch(typeof(UIDate), "SetYear")]
    public class UIDate_SetYear_Hook
    {
        public static void Postfix(UIDate __instance)
        {
            //Debug.Log("UIDate_SetYear_Hook");
            DateLayout.Layout(__instance);
        }
    }

    [HarmonyPatch(typeof(UIDate), "SetMonth")]
    public class UIDate_SetMonth_Hook
    {
        public static void Postfix(UIDate __instance)
        {
            //Debug.Log("UIDate_SetMonth_Hook");
            DateLayout.Layout(__instance);
        }
    }

    //The "精神" (Energy) label above the energy bar is written in the prefab, nothing translates it
    [HarmonyPatch(typeof(UINurturance), "UpdateValue")]
    public class UINurturance_UpdateValue_Hook
    {
        //The screen is created once, so its texts only need to be searched the first time
        private static UINurturance _Translated;

        public static void Postfix(UINurturance __instance)
        {
            if (_Translated == __instance)
            {
                return;
            }
            _Translated = __instance;

            foreach (Text text in __instance.GetComponentsInChildren<Text>(true))
            {
                if (text.text != null && text.text.Trim() == "精神")
                {
                    text.text = Heluo.Game.Data.Get<StringTable>("NurturanceProperty_Emotion")?.Text ?? "Energy";
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
        }
    }

    /*
    * Do all MainUI manipulation in this function.
    */

    [HarmonyPatch(typeof(UIMain), "Show")]
    public class UIMain_Show_Hook
    {
        public static void Postfix(ref UIMain __instance)
        {
            //Debug.Log("UIMain_Show_Hook");
            RectTransform dynamicCanvas = (RectTransform)__instance.transform.Find("DynamicCanvas");
            RectTransform menu = (RectTransform)dynamicCanvas.transform.Find("Menu");
            //Updating the Credits Button Text
            RectTransform btnMember = (RectTransform)menu.Find("BtnMember");
            RectTransform btn = (RectTransform)btnMember.Find("Btn");
            RectTransform btn_text = (RectTransform)btn.Find("Text");
            UnityEngine.UI.Text btn_text_text = (UnityEngine.UI.Text)btn_text.gameObject.GetComponentInChildren(typeof(UnityEngine.UI.Text));
            btn_text_text.text = "Credits";

            //Forcing the UI to update itself after all the changes we made. We just force every RectTransform to update itself
            for (int i = 0; i < __instance.transform.childCount; ++i)
            {
                Transform t = __instance.transform.GetChild(i);
                if (t != null && t.GetType() == typeof(RectTransform))
                {
                    RectTransform rt = t as RectTransform;
                    rt.ForceUpdateRectTransforms();
                }
            }
        }
    }

    //Quest screen reward rows ("Crimson Phoenix Dex Powder  1 pc"): kept on one line, shrunk until they fit their row
    [HarmonyPatch(typeof(WGCommissionIntroduction), "UpdateWidget")]
    public class WGCommissionIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;

        public static void Postfix(WGText[] ___EvaluationReward)
        {
            if (___EvaluationReward == null)
            {
                return;
            }

            foreach (WGText reward in ___EvaluationReward)
            {
                TextUtil.ShrinkToWidth(TextUtil.TextOf(reward), MinSize);
            }

            AlignRows(___EvaluationReward);
        }

        /*
         * The reward texts are spaced a little wider apart than the lines between the rows: the first one sits in the middle
         * of its row, the second about 2 pixels and the third about 7 pixels too low (measured at 1920x1080).
         * The rows below the first one are moved up so every text sits in the middle of its row. The lines are this much
         * closer together than the texts were (51.5 against 55 pixels), so it works the same at any resolution.
         */
        private const float RowToTextSpacing = 51.5f / 55f;

        //Where each reward text sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _RowOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void AlignRows(WGText[] rewards)
        {
            if (rewards.Length < 3)
            {
                return;
            }

            RectTransform[] rects = new RectTransform[rewards.Length];
            for (int i = 0; i < rewards.Length; i++)
            {
                Text text = TextUtil.TextOf(rewards[i]);
                if (text == null)
                {
                    return;
                }
                rects[i] = text.rectTransform;
                if (!_RowOrigin.TryGetValue(rects[i], out Vector2 origin))
                {
                    origin = rects[i].anchoredPosition;
                    _RowOrigin[rects[i]] = origin;
                }
                rects[i].anchoredPosition = origin;
            }

            float first = Center(rects[0]);
            float spacing = (first - Center(rects[2])) / 2f * RowToTextSpacing;
            for (int i = 1; i < rects.Length; i++)
            {
                rects[i].position += new Vector3(0f, first - i * spacing - Center(rects[i]), 0f);
            }
        }

        //Vertical world position of the middle of a rect
        private static float Center(RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center).y;
        }
    }

    //Quest rewards are built in code as "{name}  {count}個", 個 being the Chinese counter for items
    [HarmonyPatch(typeof(CtrlCommission), "get_reward")]
    public class CtrlCommission_GetReward_Hook
    {
        public static void Postfix(ref string __result)
        {
            if (__result == null || !__result.EndsWith("個"))
            {
                return;
            }

            string withoutCounter = __result.Substring(0, __result.Length - 1);
            string count = System.Text.RegularExpressions.Regex.Match(withoutCounter, @"\d+$").Value;
            __result = withoutCounter + (count == "1" ? " pc" : " pcs");
        }
    }

    /*
     * Reaching the intermediate level in one of the Four Arts adds the Intermediate (and later Expert) training of all four arts to the menu,
     * greyed out until their own art is high enough. We hide those tiers until the player qualifies for them.
     */
    [HarmonyPatch(typeof(NurturanceOrderTree), "CheckShow")]
    public class NurturanceOrderTree_CheckShow_Hook
    {
        //Intermediate (x2) and Expert (x3) tiers of Zither, Go, Calligraphy and Painting
        private static readonly System.Text.RegularExpressions.Regex _ArtTier = new System.Text.RegularExpressions.Regex(@"^Nurturance_04_[1-4][23]$");

        public static void Postfix(NurturanceOrderTree __instance, ref bool __result)
        {
            Nurturance nurturance = __instance.Value;
            if (!__result || nurturance == null || nurturance.OpenCondition == null || !_ArtTier.IsMatch(nurturance.Id))
            {
                return;
            }

            __result = nurturance.OpenCondition.GetValue<bool>();
        }
    }

    //The NPC speech bubble on the reading screen: wrap at spaces and shrink the quote until it fits the bubble
    [HarmonyPatch(typeof(WGNpcTalk), "UpdateWidget")]
    public class WGNpcTalk_UpdateWidget_Hook
    {
        public static void Postfix(WGText ___npcTalk)
        {
            Text text = TextUtil.TextOf(___npcTalk);
            if (text != null)
            {
                TextUtil.FitToBox(text, 12);
            }
        }
    }

    //The 新 ("new") badges on the inventory tabs, if they are a text, get the English "New" from StringTable (General_New)
    [HarmonyPatch(typeof(UIInventory), "UpdateTabNotice")]
    public class UIInventory_UpdateTabNotice_Hook
    {
        public static void Postfix(System.Collections.Generic.List<GameObject> ___TabNotices, int index, bool isOn)
        {
            if (!isOn || ___TabNotices == null || index < 0 || index >= ___TabNotices.Count || ___TabNotices[index] == null)
            {
                return;
            }

            foreach (Text text in ___TabNotices[index].GetComponentsInChildren<Text>(true))
            {
                if (text.text.Trim() == "新")
                {
                    text.text = Heluo.Game.Data.Get<StringTable>("General_New")?.Text ?? "New";
                    text.font = TextUtil.UIFont;
                    //The box is sized for one Chinese character. Wrapping or truncating would hide the word, so it may overflow
                    //and we shrink the font ourselves until the word is as wide as the box
                    text.resizeTextForBestFit = false;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    float width = text.rectTransform.rect.width;
                    if (width > 0 && text.preferredWidth > width)
                    {
                        text.fontSize = Mathf.Max(8, Mathf.FloorToInt(text.fontSize * width / text.preferredWidth));
                    }
                }
            }
        }
    }
}