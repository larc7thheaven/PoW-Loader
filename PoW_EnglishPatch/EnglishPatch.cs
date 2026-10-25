using System.IO;

using BepInEx;

using HarmonyLib;

using ModAPI;

namespace EnglishPatch
{
    [BepInPlugin("plugins.englishpatch", "English Patch", "0.9.0")]
    [BepInDependency("plugins.modapi", BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("PathOfWuxia.exe")]
    public class EnglishPatch : BaseUnityPlugin, IPoWMod
    {
        private static readonly string _VERSION = "0.9.0";

        public string GetVersion()
        {
            return _VERSION;
        }

        public string GetName()
        {
            return "English Patch";
        }

        private Harmony _HM;

        //Arial is the font the English texts always had, so updating the mod does not change how the game looks
        private const string DefaultFont = "Arial";

        //Font for the English texts, from BepInEx/config/plugins.englishpatch.cfg
        public static string FontName { get; private set; } = DefaultFont;

        public void Load()
        {
            ModAPI.ModAPI.GetInstance().ResourceRedirector.AddRessourceFolder("ModResources" + Path.DirectorySeparatorChar + "EnglishTranslate");

            _HM = new Harmony("EnglishPatch");
            //Patching hook by hook, so one hook broken by a game update does not stop all the others
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(EnglishPatch).Assembly))
            {
                try
                {
                    _HM.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError("[EnglishPatch] Could not apply hook " + type.FullName + ": " + e.Message);
                }
            }
        }

        public void Unload()
        {
            _HM.UnpatchAll("EnglishPatch");
        }

        private void Awake()
        {
            this.name = GetName();
            FontName = Config.Bind("Text", "Font", DefaultFont,
                "Font for the English texts. Any font installed in Windows works, by its name as shown in the Windows font list " +
                "(for example Palatino Linotype, Book Antiqua, Constantia, Georgia, Cambria, Arial). " +
                "A font that is not installed falls back to Arial. Restart the game after changing it.").Value;
            //Adding it to ModAPI list
            ModAPI.ModAPI.GetInstance().AddMod(this);
        }
    }
}