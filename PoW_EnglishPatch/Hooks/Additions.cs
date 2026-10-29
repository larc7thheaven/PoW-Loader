using System.IO;

using BepInEx;

using HarmonyLib;

using Heluo.Resource;

namespace EnglishPatch.Hooks
{
    /*
     * Our redirects only replace files the game bundles already have: a path missing from the bundle sheet is never asked from a bundle.
     * The game itself reads loose files first from the folder of its own mod system (ExternalResourceProvider, images and bytes),
     * which is empty unless such a mod is chosen. Pointing it to ModResources/Additions lets new files (icons, cutscenes) load.
     */
    internal static class Additions
    {
        private static readonly string _Folder = Path.Combine(Path.Combine(Paths.GameRootPath, "ModResources"), "Additions");

        private static readonly AccessTools.FieldRef<ExternalResourceProvider, string> _Directory =
            AccessTools.FieldRefAccess<ExternalResourceProvider, string>("ExternalDirectory");

        public static void Use(ExternalResourceProvider provider)
        {
            if (string.IsNullOrEmpty(_Directory(provider)) && Directory.Exists(_Folder))
            {
                _Directory(provider) = _Folder;
            }
        }
    }

    [HarmonyPatch(typeof(ExternalResourceProvider), MethodType.Constructor, typeof(Heluo.ICoroutineRunner), typeof(Heluo.Mod.IModManager))]
    public class ExternalResourceProvider_Ctor_Hook
    {
        public static void Postfix(ExternalResourceProvider __instance)
        {
            Additions.Use(__instance);
        }
    }

    //In case the provider was made before our hooks: the data tables are read through LoadBytes before any icon is loaded
    [HarmonyPatch(typeof(ExternalResourceProvider), "LoadBytes")]
    public class ExternalResourceProvider_LoadBytes_Hook
    {
        public static void Prefix(ExternalResourceProvider __instance)
        {
            Additions.Use(__instance);
        }
    }
}
