using System.Reflection;

using HarmonyLib;

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
        public static void Postfix(ref WGStringTable __instance)
        {
            //Debug.Log("WGStringTable_UpdateString_Hook");
            Text text = __instance.GetComponent<Text>();
            text.font = TextUtil.Arial;
            TextUtil.FitWrapping(text);
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
        public static void Postfix(ref WGBattleInfo __instance, ref WGText ___Name)
        {
            //Debug.Log("WGBattleInfo_UpdateInfo_Hook");
            Text text = ___Name.GetComponent<Text>();
            text.font = TextUtil.Arial;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }

    [HarmonyPatch(typeof(WGPaoMaDengText), "AddMessage")]
    public class WGPaoMaDengText_AddMessage_Hook
    {
        public static void Postfix(ref WGPaoMaDengText __instance, ref WGText ___pmdText)
        {
            //Debug.Log("WGPaoMaDengText_AddMessage_Hook");
            ___pmdText.GetComponent<Text>().font = TextUtil.Arial;
        }
    }

    [HarmonyPatch(typeof(WGNurturanceBtn), "UpdateButton")]
    public class WGNurturanceBtn_UpdateButton_Hook
    {
        public static void Postfix(ref WGNurturanceBtn __instance, ref WGText ___Btn_Name)
        {
            //Debug.Log("WGNurturanceBtn_UpdateButton_Hook");
            Text text = __instance.Btn_Name.GetComponent<Text>();
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
}