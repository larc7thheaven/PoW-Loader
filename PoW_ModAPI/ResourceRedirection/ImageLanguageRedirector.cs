using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;

using Heluo;
using Heluo.UI;

using ModAPI;

using UnityEngine;

namespace PoW_ModAPI.ResourceRedirection
{
    /*
    ImageLanguage (and WgBattleGameObject, used for the battle popups like Parry) only loads its language sprite if the Image has no sprite yet.
    If the prefab already ships with the Chinese sprite set, the load never happens and our redirect is skipped.
    So after the original ran, we load the sprite ourselves whenever we have a redirect for it.
    */

    [HarmonyPatch]
    public class ImageLanguage_LoadSprite_Hook
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ImageLanguage), "Awake");
            yield return AccessTools.Method(typeof(ImageLanguage), "LoadSprite");
        }

        public static void Postfix(ImageLanguage __instance)
        {
            if (__instance.Image == null)
            {
                return;
            }

            string path = $"{__instance.path}/{__instance.spritename}_{GameConfig.Language}.png";
            var resourceRedirector = ResourceRedirectManager.GetInstance();
            if (!resourceRedirector.ExistsRedirect(path))
            {
                return;
            }

            Sprite sprite = resourceRedirector.GetRedirectedSprite(path);
            if (sprite != null)
            {
                __instance.Image.sprite = sprite;
            }
        }
    }
}
