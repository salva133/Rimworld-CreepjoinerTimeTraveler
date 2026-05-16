using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Reagiert auf das vanilla Creep-Joiner-Incident.
    ///
    /// Pipeline:
    ///   1. IncidentWorker_CreepJoinerJoin.TryExecuteWorker startet.
    ///   2. PawnGenerator.GeneratePawn erzeugt einen rohen CreepJoiner-Pawn -
    ///      die Form auf CompCreepJoiner ist hier noch nicht gesetzt.
    ///   3. Der Worker waehlt die Form gewichtet aus und schreibt sie auf den
    ///      Comp.
    ///   4. Der Worker spawnt den Pawn auf der Zielkarte und feuert die Letter.
    ///   5. Unser Postfix laeuft - jetzt ist alles initialisiert und wir
    ///      finden den frischen CTT_TimeTraveler auf der Karte.
    ///
    /// Patch ist dynamisch installiert, weil RimWorld.IncidentWorker_CreepJoinerJoin
    /// im Krafs-Ref-Assembly nicht garantiert oeffentlich ist. AccessTools.TypeByName
    /// haengt sich gegen den realen Anomaly-Assembly-Typ.
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

            // Finde den juengsten, noch-nicht-transformierten CTT-Joiner auf
            // der Zielkarte. Marker "schon transformiert" = Visit-Hediff bereits da.
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
