using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Transform hook. Patches the six-arg overload of
    /// RimWorld.CreepJoinerUtility.GenerateAndSpawn, which carries the form
    /// as an explicit parameter and returns the spawned Pawn.
    ///
    /// Patch is installed reflectively (AccessTools.TypeByName + parameter
    /// count match) because CreepJoinerUtility and the BenefitDef /
    /// DownsideDef / AggressiveDef / RejectionDef types in argument
    /// positions 1..4 are not guaranteed public in the Krafs ref assembly.
    /// The six-parameter filter is unambiguous - the only other overload
    /// takes (Map, float).
    /// </summary>
    public static class Patch_CreepJoinerUtility
    {
        public static void Install(Harmony harmony)
        {
            var utilType = AccessTools.TypeByName("RimWorld.CreepJoinerUtility");
            if (utilType == null)
            {
                Log.Warning("[CreepjoinerTimeTraveler] RimWorld.CreepJoinerUtility not found - Anomaly missing?");
                return;
            }

            var target = utilType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GenerateAndSpawn" && m.GetParameters().Length == 6);

            if (target == null)
            {
                Log.Warning("[CreepjoinerTimeTraveler] CreepJoinerUtility.GenerateAndSpawn(form,...) (6-arg overload) not found.");
                return;
            }

            var firstParamType = target.GetParameters()[0].ParameterType;
            if (!typeof(CreepJoinerFormKindDef).IsAssignableFrom(firstParamType))
            {
                Log.Warning($"[CreepjoinerTimeTraveler] 6-arg GenerateAndSpawn first param is {firstParamType.FullName}, expected CreepJoinerFormKindDef - aborting patch.");
                return;
            }

            var postfix = AccessTools.Method(typeof(Patch_CreepJoinerUtility), nameof(Postfix));
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Log.Message("[CreepjoinerTimeTraveler] hooked CreepJoinerUtility.GenerateAndSpawn (6-arg form overload)");
        }

        /// <summary>
        /// __0 is the form (first positional argument), __result the spawned
        /// Pawn. If __result is ever null (e.g. a future RW version drops
        /// the return value) we skip silently rather than crashing.
        /// </summary>
        public static void Postfix(Pawn __result, CreepJoinerFormKindDef __0)
        {
            try
            {
                if (__result == null || __0 == null) return;
                if (__0.defName != "CTT_TimeTraveler") return;

                var ext = __0.GetModExtension<DefModExtension_TimeTraveler>();
                if (ext == null)
                {
                    Log.Warning("[CreepjoinerTimeTraveler] CTT_TimeTraveler is missing DefModExtension_TimeTraveler.");
                    return;
                }

                var visitDef = DefDatabase<HediffDef>.GetNamedSilentFail("CTT_TimeTravelerVisit");
                if (visitDef != null && __result.health?.hediffSet?.HasHediff(visitDef) == true)
                {
                    // Visit hediff is attached as the last step of Apply(),
                    // so its presence means this pawn was already transformed.
                    return;
                }

                Log.Message($"[CreepjoinerTimeTraveler] GenerateAndSpawn postfix transforming {__result.LabelShort}");
                TimeTravelerTransformer.Apply(__result, ext);
            }
            catch (Exception ex)
            {
                Log.Error($"[CreepjoinerTimeTraveler] GenerateAndSpawn postfix failed: {ex}");
            }
        }
    }
}
