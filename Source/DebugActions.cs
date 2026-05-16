using LudeonTK;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Dev-mode entries for the time-traveler creep-joiner.
    /// Entries appear in dev mode under
    ///   "Debug Actions Menu" -> category "Creep Joiner: Time Traveler".
    /// </summary>
    public static class DebugActions
    {
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
