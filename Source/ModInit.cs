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
            Patch_IncidentWorker_CreepJoinerJoin.Install(harmony);
            Log.Message("[CreepjoinerTimeTraveler] initialized");
        }
    }
}
