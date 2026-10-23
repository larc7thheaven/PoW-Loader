using HarmonyLib;

using Heluo;
using Heluo.Data;
using Heluo.UI;

namespace EnglishPatch.Hooks
{
    /*
     * The game shows names as SurName + Name without a space (fine for Chinese).
     * The translated NPC surnames already end with a space ("Shi " + "Kun"), but the player types their own name,
     * so it ended up as "ZhaoZilong". We give the player surname the same trailing space.
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
