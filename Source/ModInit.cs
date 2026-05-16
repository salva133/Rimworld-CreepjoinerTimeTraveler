using HarmonyLib;
using Verse;

namespace CreepjoinerTimeTraveler
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        public const string HarmonyId = "donsantana.creepjoiners.timetraveler";

        static ModInit()
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            Patch_CreepJoinerUtility.Install(harmony);
            Log.Message("[CreepjoinerTimeTraveler] initialized");
        }
    }
}
