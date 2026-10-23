using System.Reflection;

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
        //Title labels whose English text is wider than the brush stroke behind them, shrunk to the width of their box
        private static readonly System.Collections.Generic.HashSet<string> _FitWidth = new System.Collections.Generic.HashSet<string> { "Medicine_Browse" };
        private const int MinTitleSize = 14;

        public static void Postfix(WGStringTable __instance, string ___id)
        {
            //Debug.Log("WGStringTable_UpdateString_Hook");
            Text text = __instance.GetComponent<Text>();
            text.font = TextUtil.Arial;
            TextUtil.FitWrapping(text);

            if (_FitWidth.Contains(___id))
            {
                float width = text.rectTransform.rect.width;
                while (text.preferredWidth > width && text.fontSize > MinTitleSize)
                {
                    text.fontSize--;
                }
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
        }
    }

    //Gear tab of the character screen: the item Info and Effect texts shrink (down to 14) to fit their boxes.
    //WGPropsIntroduction is also used by the shop, forge, elixirs and battle status, those stay as they are.
    [HarmonyPatch(typeof(WGPropsIntroduction), "UpdateWidget")]
    public class WGPropsIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;

        public static void Postfix(WGPropsIntroduction __instance, WGText ___description, WGText ___effectDescription)
        {
            if (!TextUtil.HasParent<UIEquip>(__instance))
            {
                return;
            }

            TextUtil.ShrinkToHeight(TextUtil.TextOf(___description), MinSize);
            TextUtil.ShrinkToHeight(TextUtil.TextOf(___effectDescription), MinSize);
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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }
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
        public static void Postfix(ref Text ___community_name, ref Text ___community_introduction)
        {
            //Debug.Log("UIRelationship_UpdateRelationship_Hook");
            ___community_name.font = TextUtil.Arial;
            ___community_introduction.font = TextUtil.Arial;

            //The game turns every space into a non-breaking space (StringTool.Replace), so English lines broke in the middle of words. Turning them back allows wrapping at spaces.
            ___community_introduction.text = ___community_introduction.text.Replace(' ', ' ');

            //Longer bios got cut off at the bottom of the Info box, so we let the text shrink until it fits.
            if (!___community_introduction.resizeTextForBestFit)
            {
                ___community_introduction.resizeTextMaxSize = ___community_introduction.fontSize;
                ___community_introduction.resizeTextMinSize = 12;
                ___community_introduction.resizeTextForBestFit = true;
            }
            ___community_introduction.horizontalOverflow = HorizontalWrapMode.Wrap;
            ___community_introduction.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }

    /*
     * Updating Date to display Year and Month in English
     * Updating the Units in SetMonth and adding a . after the number in both SetYear and SetMonth
    */

    [HarmonyPatch(typeof(UIDate), "SetYear")]
    public class UIDate_SetYear_Hook
    {
        public static void Prefix(ref string yearStr)
        {
            //Debug.Log("UIDate_SetYear_Hook");
            yearStr += ".";
        }
    }

    [HarmonyPatch(typeof(UIDate), "SetMonth")]
    public class UIDate_SetMonth_Hook
    {
        public static void Prefix(ref UIDate __instance, ref string monthStr)
        {
            //Debug.Log("UIDate_SetMonth_Hook");
            monthStr += ".";

            //Getting child 0, which is the RectTransform
            RectTransform UI_Date_rectTransform = __instance.gameObject.transform.GetChild(0) as RectTransform;

            //Getting the Date_BG which is the first child again (Index 0)
            RectTransform dateBG_rectTransform = UI_Date_rectTransform.gameObject.transform.GetChild(0) as RectTransform;

            //Here now we can get the Unit(child 0),Year (child 1), YearUnit(child 2) Month(child 3) and monthUnit(child 4)
            RectTransform unit = dateBG_rectTransform.gameObject.transform.GetChild(0) as RectTransform;
            RectTransform year = dateBG_rectTransform.gameObject.transform.GetChild(1) as RectTransform;
            RectTransform yearUnit = dateBG_rectTransform.gameObject.transform.GetChild(2) as RectTransform;
            RectTransform month = dateBG_rectTransform.gameObject.transform.GetChild(3) as RectTransform;
            RectTransform monthUnit = dateBG_rectTransform.gameObject.transform.GetChild(4) as RectTransform;

            //Disabling "Unit" since we do not need it in English
            unit.localPosition = new Vector3(0, 0, 0);
            unit.gameObject.SetActive(false);
            //Moving the Unit/Year/YearUnit/Month/monthUnit to hardcoded positions that look good
            year.localPosition = new Vector3(-60f, 41.5f, 0f);
            yearUnit.localPosition = new Vector3(-20f, 41.5f, 0);
            yearUnit.sizeDelta = new Vector2(70f, 50f);
            month.localPosition = new Vector3(35f, 41.5f, 0f);
            monthUnit.localPosition = new Vector3(86f, 41.5f, 0);
            monthUnit.sizeDelta = new Vector2(70f, 50f);

            //Lastly we overwrite the text they contain
            unit.gameObject.GetComponent<UnityEngine.UI.Text>().text = "";
            UnityEngine.UI.Text YearUnitText = yearUnit.gameObject.GetComponent<UnityEngine.UI.Text>();
            YearUnitText.text = "Year";
            YearUnitText.fontSize = 28;
            UnityEngine.UI.Text monthUnitText = monthUnit.gameObject.GetComponent<UnityEngine.UI.Text>();
            monthUnitText.text = "Month";
            monthUnitText.fontSize = 28;
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