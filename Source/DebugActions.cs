using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Dev-mode entries for the time-traveler creep-joiner.
    ///
    /// Entries appear in dev mode under
    ///   "Debug Actions Menu" -> category "Creep Joiner: Time Traveler".
    /// </summary>
    public static class DebugActions
    {
        // ----------------------------------------------------------------------
        // PRODUCTION: Forces the vanilla creep-joiner incident with form
        // "CTT_TimeTraveler" guaranteed. Works by temporarily setting all other
        // CreepJoinerFormKindDef weights to 0 and restoring them in the finally
        // block. The actual spawn runs through the unmodified vanilla path
        // (CreepJoinerJoin -> PawnGenerator -> our postfix), so the result is
        // identical to a live event.
        // ----------------------------------------------------------------------
        [DebugAction("Creep Joiner: Time Traveler",
            "Force CTT_TimeTraveler join",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceTimeTravelerJoin()
        {
            var def = DefDatabase<CreepJoinerFormKindDef>.GetNamedSilentFail("CTT_TimeTraveler");
            if (def == null)
            {
                Log.Error("[CreepjoinerTimeTraveler] CreepJoinerFormKindDef 'CTT_TimeTraveler' not found.");
                return;
            }

            var incident = DefDatabase<IncidentDef>.GetNamedSilentFail("CreepJoinerJoin");
            if (incident == null)
            {
                Log.Error("[CreepjoinerTimeTraveler] IncidentDef 'CreepJoinerJoin' not found - Anomaly DLC missing?");
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[CreepjoinerTimeTraveler] No current map.");
                return;
            }

            // The 'weight' field exists on CreepJoinerFormKindDef at runtime
            // (vanilla uses it in Anomaly/Defs/CreepJoinerFormKindDefs/Forms.xml),
            // but it isn't public in the Krafs ref assembly. Access therefore
            // runs reflectively through Harmony's Traverse.
            var backup = new Dictionary<CreepJoinerFormKindDef, float>();
            foreach (var f in DefDatabase<CreepJoinerFormKindDef>.AllDefs)
            {
                var t = Traverse.Create(f).Field("weight");
                backup[f] = t.GetValue<float>();
                t.SetValue(0f);
            }
            Traverse.Create(def).Field("weight").SetValue(1000f);

            try
            {
                var parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
                if (!incident.Worker.TryExecute(parms))
                    Log.Warning("[CreepjoinerTimeTraveler] CreepJoinerJoin worker refused to fire (check storyteller / map state).");
            }
            finally
            {
                foreach (var kv in backup)
                    Traverse.Create(kv.Key).Field("weight").SetValue(kv.Value);
            }
        }

        // ======================================================================
        // REMOVE BEFORE RELEASE - START
        // ----------------------------------------------------------------------
        // Pure developer tool: applies the time-traveler transformation
        // directly to any selected pawn. Bypasses the vanilla creep-joiner
        // path entirely and is therefore ideal for iterating on
        // CopyAppearance / CopyGenes / CopyMarkerHediffs / ReplaceApparel /
        // visit timer without constantly triggering the incident.
        //
        // After clicking, the cursor turns into a pawn picker (ToolMapForPawns).
        //
        // Remove this entire block (including the markers) before the
        // workshop release.
        // ======================================================================
        [DebugAction("Creep Joiner: Time Traveler",
            "Apply transform to selected pawn",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApplyTransformToSelectedPawn(Pawn p)
        {
            if (p == null) return;

            var form = DefDatabase<CreepJoinerFormKindDef>.GetNamedSilentFail("CTT_TimeTraveler");
            if (form == null)
            {
                Log.Error("[CreepjoinerTimeTraveler] CreepJoinerFormKindDef 'CTT_TimeTraveler' not found.");
                return;
            }

            var ext = form.GetModExtension<DefModExtension_TimeTraveler>();
            if (ext == null)
            {
                Log.Error("[CreepjoinerTimeTraveler] DefModExtension_TimeTraveler missing on CTT_TimeTraveler.");
                return;
            }

            TimeTravelerTransformer.Apply(p, ext);
            Messages.Message($"[CTT] transform applied to {p.LabelShort}",
                MessageTypeDefOf.NeutralEvent, historical: false);
        }
        // ----------------------------------------------------------------------
        // REMOVE BEFORE RELEASE - END
        // ======================================================================
    }
}
