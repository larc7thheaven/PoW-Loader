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
        private static Font _Arial;

        //The built in Arial font is the same object every time, so we look it up once instead of on every text change
        public static Font Arial
        {
            get
            {
                if (_Arial == null)
                {
                    _Arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _Arial;
            }
        }

        private static readonly AccessTools.FieldRef<WGText, Text> _WGTextText = AccessTools.FieldRefAccess<WGText, Text>("text");

        //The Text a WGText shows
        public static Text TextOf(WGText wgText)
        {
            return wgText == null ? null : _WGTextText(wgText);
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
        public static void ShrinkToBox(Text text, int minSize)
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
            while (text.fontSize > minSize && (text.preferredHeight > rect.height || WidestWord(text, words) > rect.width))
            {
                text.fontSize--;
            }
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
         * Texts with rich text tags or not aligned to the left are left alone.
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

        //Whether the object or one of its parents has the component, also when they are inactive
        public static bool HasParent<T>(Component component) where T : Component
        {
            for (Transform t = component.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<T>() != null)
                {
                    return true;
                }
            }
            return false;
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
            ___text.font = TextUtil.Arial;
            ___text.fontSize = 20;
            TextUtil.FitWrapping(___text);
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
        //Labels whose English text is wider than the box behind them, shrunk to the width of their box
        //Label id and the room to keep free at both ends together, for labels on rounded backgrounds
        private static readonly System.Collections.Generic.Dictionary<string, float> _FitWidth = new System.Collections.Generic.Dictionary<string, float>
        {
            //Relationship screen: Friendship bar label, Send Gifts and Outings buttons
            { "General_Favorability", 0f }, { "SecondaryInterface0808", 0f }, { "SecondaryInterface0803", 0f }, { "SecondaryInterface0804", 0f },
            //Relationship screen: the small "Ability" tag in front of every level
            { "SecondaryInterface0809", 10f },
        };
        private const int MinTitleSize = 14;

        public static void Postfix(WGStringTable __instance, string ___id)
        {
            //Debug.Log("WGStringTable_UpdateString_Hook");
            Text text = __instance.GetComponent<Text>();
            text.font = TextUtil.Arial;
            TextUtil.FitWrapping(text);

            if (_FitWidth.TryGetValue(___id, out float margin))
            {
                TextUtil.ShrinkToWidth(text, MinTitleSize, margin);
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
            ___Name.font = TextUtil.Arial;
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
            //The original measures the text right after setting it, so the font has to be Arial before that
            ___pmdText.font = TextUtil.Arial;

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

    //Gear tab of the character screen: the item Info and Effect texts shrink (down to 14) to fit their boxes.
    //Items tab (Gift, Books, Quest): nothing is below the Info box, so the box grows downwards to fit the text instead.
    //WGPropsIntroduction is also used by the shop, forge, elixirs and battle status, those keep their size.
    [HarmonyPatch(typeof(WGPropsIntroduction), "UpdateWidget")]
    public class WGPropsIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;
        //Four more lines of size 20 than the two the box was made for
        private const float ItemsExtraHeight = 92f;

        public static void Postfix(WGPropsIntroduction __instance, WGText ___description, WGText ___effectDescription)
        {
            if (TextUtil.HasParent<UIEquip>(__instance))
            {
                //The Info box always reaches down to the bottom of the Effect box next to it
                InfoBox.Grow(TextUtil.TextOf(___description), float.MaxValue, MinSize, null);
                TextUtil.ShrinkToHeight(TextUtil.TextOf(___effectDescription), MinSize);
            }
            else if (TextUtil.HasParent<UIFormInventory>(__instance))
            {
                Text effect = TextUtil.TextOf(___effectDescription);
                if (effect == null)
                {
                    //Items tab: every item gets the same box, with room for six lines, only longer texts make it bigger
                    InfoBox.Grow(TextUtil.TextOf(___description), ItemsExtraHeight, MinSize, null);
                }
                else
                {
                    //Medicine tab: the Effect part below moves down (and loses its empty space) to make room
                    InfoBox.Grow(TextUtil.TextOf(___description), 0f, MinSize, effect);
                }
            }

            //The item Info is justified everywhere
            TextUtil.Justify(TextUtil.TextOf(___description));
        }
    }

    /*
     * The item Info box is a background image with the text stretched inside it, sized for two lines of Chinese.
     * The box keeps its top and grows down: by at least minGrow (so it does not change size from item to item),
     * and more when the text needs it, but never past the bottom of the panel it sits in.
     * With a part below it (the Effect box on the Medicine tab), that part moves down into the free room at the
     * bottom of the panel and gives up its empty space, so the Info box can take that room.
     * A text that still does not fit shrinks (never below minSize). The text always starts at the top of the box.
     */
    public static class InfoBox
    {
        //Room kept between a moved part and the text inside it
        private const float Padding = 6f;

        //Size and position each box has in the prefab, so we always change it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Grow(Text text, float minGrow, int minSize, Text below)
        {
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            RectTransform panel = box != null ? box.parent as RectTransform : null;
            if (panel == null || box.GetComponent<Image>() == null)
            {
                return;
            }
            RectTransform belowBox = below != null ? below.transform.parent as RectTransform : null;
            if (belowBox != null && belowBox.parent != panel)
            {
                belowBox = null;
            }

            Restore(box);
            Restore(belowBox);
            if (string.IsNullOrEmpty(text.text))
            {
                return;
            }

            text.alignment = (TextAnchor)((int)text.alignment % 3);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            float missing = text.preferredHeight - text.rectTransform.rect.height;
            float wanted = Mathf.Max(missing, minGrow);
            if (wanted <= 0f)
            {
                return;
            }

            float room;
            float drop = 0f;
            float slack = 0f;
            if (belowBox == null)
            {
                room = Mathf.Max(0f, Bottom(box, panel) - panel.rect.yMin);
            }
            else
            {
                //The part below can go down to the bottom of the panel, and lose what its own text does not use
                drop = Mathf.Max(0f, Bottom(belowBox, panel) - panel.rect.yMin);
                below.horizontalOverflow = HorizontalWrapMode.Wrap;
                slack = Mathf.Max(0f, below.rectTransform.rect.height - below.preferredHeight - Padding);
                room = drop + slack;
            }

            float grow = Mathf.Min(wanted, room);
            MoveEdges(box, 0f, grow);
            if (belowBox != null)
            {
                float moved = Mathf.Min(grow, drop);
                MoveEdges(belowBox, grow, moved);
            }

            if (missing > grow)
            {
                TextUtil.ShrinkToHeight(text, minSize);
            }
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
        public static void Prefix(WGPaoMaDengText ___effectDescription)
        {
            if (___effectDescription != null)
            {
                WGPaoMaDengText_AddMessage_Hook.FitToBox.Add(___effectDescription);
            }
        }

        //The book description under the title is justified
        public static void Postfix(WGText ___description)
        {
            TextUtil.Justify(TextUtil.TextOf(___description));
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

        //Where each "Qi Usage" label sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector3> _LabelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector3>();

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
        public static void Postfix(GameObject[] ___skillGo, WGText[] ___skills, WGText[] ___mps)
        {
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

        private static void FitRow(Text name, Text label, Text mp)
        {
            TextUtil.VisibleX(mp, out float mpLeft, out _);
            float rightLimit = mpLeft;

            if (label != null)
            {
                RectTransform labelRect = label.rectTransform;
                if (!_LabelOrigin.TryGetValue(labelRect, out Vector3 origin))
                {
                    origin = labelRect.position;
                    _LabelOrigin[labelRect] = origin;
                }
                labelRect.position = origin;
                label.fontSize = Mathf.Min(label.fontSize, LabelSize);

                TextUtil.VisibleX(label, out float labelLeft, out float labelRight);
                float shift = mpLeft - TextUtil.WorldWidth(labelRect, Gap) - labelRight;
                if (shift > 0)
                {
                    labelRect.position = origin + new Vector3(shift, 0f, 0f);
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
            text.font = TextUtil.Arial;

            //Only Return, Socialize and the character names inside Socialize are fitted to their box, the other buttons keep the default size
            Nurturance nurturance = __instance.tree?.Value;
            bool fit = nurturance != null
                && (nurturance.Fuction == NurturanceFunction.Back || nurturance.Fuction == NurturanceFunction.Firend || nurturance.Id == "Nurturance_05");
            if (!fit)
            {
                TextUtil.RestoreBoxFit(text);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                return;
            }

            //Return has a tall narrow box made for Chinese written top to bottom, English is stacked letter by letter there
            Rect rect = text.rectTransform.rect;
            if (rect.height > rect.width * 1.5f && !string.IsNullOrEmpty(text.text))
            {
                text.text = string.Join("\n", System.Array.ConvertAll(text.text.Replace(" ", "").ToCharArray(), c => c.ToString()));
            }

            TextUtil.ShrinkToBox(text, MinSize);
        }

        private const int MinSize = 10;
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

    [HarmonyPatch(typeof(WGBookBtn), "UpdateWidget")]
    public class WGBookBtn_UpdateWidget_Hook
    {
        public static void Postfix(ref WGText ___itemName)
        {
            //Debug.Log("WGBookBtn_UpdateWidget_Hook");
            TextUtil.FitWrapping(___itemName.GetComponent<Text>());
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
            ___Title.font = TextUtil.Arial;
            ___Title.fontSize = 24;
        }
    }

    [HarmonyPatch(typeof(WGTitleValue), "ValueFromat")]
    public class WGTitleValue_ValueFromat_Hook
    {
        public static void Postfix(ref Text ___Value)
        {
            //Debug.Log("WGTitleValue_ValueFromat_Hook");
            ___Value.font = TextUtil.Arial;
            ___Value.fontSize = 20;
        }
    }

    [HarmonyPatch(typeof(UIRelationship), "UpdateRelationship")]
    public class UIRelationship_UpdateRelationship_Hook
    {
        public static void Postfix(ref Text ___community_name, ref Text ___community_introduction, WGFavorability[] ___community_fav_level)
        {
            RelationshipAbilities.Fit(___community_fav_level);

            //Debug.Log("UIRelationship_UpdateRelationship_Hook");
            ___community_name.font = TextUtil.Arial;
            ___community_introduction.font = TextUtil.Arial;

            //The name box is sized for 2-4 Chinese characters, "Nan Feihuang" wrapped at the space and only "Nan" was left. We keep it on one line and shrink it to fit.
            TextUtil.ShrinkToWidth(___community_name, 16);

            //The game turns every space into a non-breaking space (StringTool.Replace), so English lines broke in the middle of words. Turning them back allows wrapping at spaces.
            ___community_introduction.text = ___community_introduction.text.Replace(' ', ' ');

            //Longer bios got cut off at the bottom of the Info box, so we let the text shrink until it fits, then justify it.
            TextUtil.ShrinkToBox(___community_introduction, 12);
            TextUtil.Justify(___community_introduction);
        }
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

    //Status tab traits: the description shrinks (down to 15) to fit its box, and is justified
    [HarmonyPatch(typeof(WGTalentItem), "UpdateWidget")]
    public class WGTalentItem_UpdateWidget_Hook
    {
        private const int MinSize = 15;

        public static void Postfix(WGTalentItem __instance)
        {
            System.Array talents = Traverse.Create(__instance).Field("talents").GetValue() as System.Array;
            if (talents == null)
            {
                return;
            }

            foreach (object talent in talents)
            {
                Text text = TextUtil.TextOf(Traverse.Create(talent).Field("description").GetValue<WGText>());
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                TextUtil.ShrinkToHeight(text, MinSize);
                TextUtil.Justify(text);
            }
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
                Text content = level != null ? Traverse.Create(level).Field("content").GetValue<Text>() : null;
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
                if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text))
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
                    text.font = TextUtil.Arial;
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