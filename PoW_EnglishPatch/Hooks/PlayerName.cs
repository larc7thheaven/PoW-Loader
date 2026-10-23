using HarmonyLib;

using Heluo;
using Heluo.Data;
using Heluo.UI;
using Heluo.Utility;

namespace EnglishPatch.Hooks
{
    /*
     * The game shows names as SurName + Name without a space (fine for Chinese).
     * The translated NPC surnames already end with a space ("Shi " + "Kun"), but the player types their own name,
     * so it ended up as "ZhaoZilong". We give the player surname the same trailing space.
     * Dialogue uses the surname on its own, so it gets the trimmed version (see StringTool_ParseName_Hook).
     */

    public static class PlayerName
    {
        public static void AddSpaceToSurName(GameData gameData)
        {
            if (gameData == null || gameData.Exterior == null || !gameData.Exterior.ContainsKey(GameConfig.Player))
            {
                return;
            }

            CharacterExteriorData player = gameData.Exterior[GameConfig.Player];
            string surName = player.SurName;
            if (string.IsNullOrEmpty(surName))
            {
                return;
            }

            //Already spaced, or a Chinese name that should stay without a space
            char last = surName[surName.Length - 1];
            if (char.IsWhiteSpace(last) || last >= '⺀')
            {
                return;
            }

            player.SurName = surName + " ";
        }
    }

    //Loading a save (also fixes saves made before this patch)
    [HarmonyPatch(typeof(GameData), "Initialize")]
    public class GameData_Initialize_Hook
    {
        public static void Postfix(GameData __instance)
        {
            PlayerName.AddSpaceToSurName(__instance);
        }
    }

    //Dialogue inserts the surname alone with {name_1} and the translations expect it without the space ("{name_1}, ...", "{name_1} {name_2}"),
    //so we fill in {name_1} with the trimmed surname before the game does.
    [HarmonyPatch(typeof(StringTool), "ParseName")]
    public class StringTool_ParseName_Hook
    {
        public static void Prefix(ref string text)
        {
            if (text == null || !text.Contains("{name_1}"))
            {
                return;
            }

            string surName = Game.GameData.Exterior[GameConfig.Player].SurName;
            text = text.Replace("{name_1}", surName?.TrimEnd());
        }
    }

    //Finishing the new game registration. Not done while typing, since the name is set on every keystroke.
    [HarmonyPatch(typeof(CtrlRegistration), "InitialRewards")]
    public class CtrlRegistration_InitialRewards_Hook
    {
        public static void Postfix()
        {
            PlayerName.AddSpaceToSurName(Game.GameData);
        }
    }
}
