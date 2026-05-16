using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Reacts to the vanilla creep-joiner incident.
    ///
    /// Pipeline:
    ///   1. IncidentWorker_CreepJoinerJoin.TryExecuteWorker starts.
    ///   2. PawnGenerator.GeneratePawn produces a raw creep-joiner pawn -
    ///      the form on CompCreepJoiner is not yet set at this point.
    ///   3. The worker picks the form by weight and writes it onto the
    ///      comp.
    ///   4. The worker spawns the pawn on the target map and fires the letter.
    ///   5. Our postfix runs - by now everything is initialized and we
    ///      find the fresh CTT_TimeTraveler on the map.
    ///
    /// The patch is installed dynamically because RimWorld.IncidentWorker_CreepJoinerJoin
    /// is not guaranteed to be public in the Krafs ref assembly. AccessTools.TypeByName
    /// binds against the real Anomaly assembly type.
    /// </summary>
    public static class Patch_IncidentWorker_CreepJoinerJoin
    {
        public static void Install(Harmony harmony)
        {
            var workerType = AccessTools.TypeByName("RimWorld.IncidentWorker_CreepJoinerJoin");
            if (workerType == null)
            {
                Log.Warning("[CreepjoinerTimeTraveler] IncidentWorker_CreepJoinerJoin type not found - is Anomaly active?");
                return;
            }

            var method = AccessTools.Method(workerType, "TryExecuteWorker", new[] { typeof(IncidentParms) });
            if (method == null)
            {
                Log.Warning("[CreepjoinerTimeTraveler] TryExecuteWorker(IncidentParms) not found on IncidentWorker_CreepJoinerJoin.");
                return;
            }

            var postfix = AccessTools.Method(typeof(Patch_IncidentWorker_CreepJoinerJoin), nameof(Postfix));
            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Message("[CreepjoinerTimeTraveler] hooked IncidentWorker_CreepJoinerJoin.TryExecuteWorker");
        }

        public static void Postfix(bool __result, IncidentParms parms)
        {
            if (!__result || parms == null) return;
            var map = parms.target as Map;
            if (map == null) return;

            var visitDef = DefDatabase<HediffDef>.GetNamedSilentFail("CTT_TimeTravelerVisit");

            // Find the youngest, not-yet-transformed CTT joiner on the
            // target map. Marker "already transformed" = visit hediff already present.
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                try
                {
                    var form = TryGetCreepJoinerForm(pawn);
                    if (form == null || form.defName != "CTT_TimeTraveler") continue;
                    if (visitDef != null && pawn.health.hediffSet.HasHediff(visitDef)) continue;

                    var ext = form.GetModExtension<DefModExtension_TimeTraveler>();
                    if (ext == null) continue;

                    TimeTravelerTransformer.Apply(pawn, ext);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error($"[CreepjoinerTimeTraveler] postfix transform failed for {pawn?.LabelShort}: {ex}");
                }
            }
        }

        private static CreepJoinerFormKindDef TryGetCreepJoinerForm(Pawn pawn)
        {
            var comp = pawn?.AllComps?.FirstOrDefault(c => c.GetType().Name == "CompCreepJoiner");
            if (comp == null) return null;

            var t = comp.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var f in t.GetFields(flags))
            {
                if (typeof(CreepJoinerFormKindDef).IsAssignableFrom(f.FieldType))
                    return f.GetValue(comp) as CreepJoinerFormKindDef;
            }
            foreach (var p in t.GetProperties(flags))
            {
                if (typeof(CreepJoinerFormKindDef).IsAssignableFrom(p.PropertyType))
                    return p.GetValue(comp) as CreepJoinerFormKindDef;
            }
            return null;
        }
    }
}
