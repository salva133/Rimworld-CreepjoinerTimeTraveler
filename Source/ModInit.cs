using HarmonyLib;
using Verse;

namespace CreepjoinerTimeTraveler
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        public const string HarmonyId = "steve.creepjoinertimetraveler";

        static ModInit()
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            Log.Message("[CreepjoinerTimeTraveler] initialized");
        }
    }
}
