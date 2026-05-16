using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Drives the situational social thought CTT_FamiliarSomehow.
    ///
    /// Returns Active for the (traveler -> source) pair: traveler is the
    /// pawn carrying the CTT_TimeTravelerVisit hediff, source is the
    /// colonist the traveler was generated from. Asymmetric by construction
    /// - the source has no hediff, so the reverse check is always inactive.
    /// </summary>
    public class ThoughtWorker_TimeTravelerSource : ThoughtWorker
    {
        private static HediffDef _visitDef;

        private static HediffDef VisitDef
        {
            get
            {
                if (_visitDef == null)
                    _visitDef = DefDatabase<HediffDef>.GetNamedSilentFail("CTT_TimeTravelerVisit");
                return _visitDef;
            }
        }

        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null) return ThoughtState.Inactive;
            if (p == other) return ThoughtState.Inactive;

            var def = VisitDef;
            if (def == null) return ThoughtState.Inactive;

            var hediff = p.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (hediff == null) return ThoughtState.Inactive;

            var bound = hediff.TryGetComp<HediffComp_BoundToSource>();
            if (bound?.Source == other) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
