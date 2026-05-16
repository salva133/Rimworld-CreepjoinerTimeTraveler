using System.Linq;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    public class HediffCompProperties_BoundToSource : HediffCompProperties
    {
        public HediffCompProperties_BoundToSource()
        {
            compClass = typeof(HediffComp_BoundToSource);
        }
    }

    /// <summary>
    /// Causality binding between a time traveler and the colonist he is a
    /// future copy of. Holds the source pawn reference, polls for source
    /// death, and on detection schedules a small randomized delay before
    /// collapsing the traveler.
    ///
    /// Collapse logic branches on the VRE_Transcendent xenogene:
    ///   - present  -> Pawn.Kill(null), so the gene's own death pipeline fires.
    ///   - absent   -> Pawn.Destroy(Vanish), no corpse, no death log.
    ///
    /// The +30 opinion the traveler holds toward the source lives in a
    /// separate situational social thought (CTT_FamiliarSomehow) whose
    /// ThoughtWorker queries this comp.
    /// </summary>
    public class HediffComp_BoundToSource : HediffComp
    {
        private const string TranscendenceGeneDefName = "VRE_Transcendent";

        // Source-death poll cadence in ticks. 60 ticks = 1 second of game
        // time at normal speed; one-second latency is invisible to the
        // player but a 60x cheaper than per-tick polling.
        private const int PollIntervalTicks = 60;

        // Collapse delay window in ticks. 0-300 ticks = 0-5 seconds at
        // normal speed, scheduled the instant source death is detected.
        // The brief gap lets the player register the source death before
        // the ripple arrives, which reads as mystical rather than mechanical.
        private const int CollapseDelayMinTicks = 0;
        private const int CollapseDelayMaxTicks = 300;

        private Pawn source;
        private bool collapsed;
        private int collapseAtTick = -1;

        public Pawn Source => source;

        public void Bind(Pawn s)
        {
            source = s;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref source, "CTT_source");
            Scribe_Values.Look(ref collapsed, "CTT_collapsed", false);
            Scribe_Values.Look(ref collapseAtTick, "CTT_collapseAtTick", -1);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (collapsed) return;

            // Delay has been scheduled - wait it out, then fire.
            if (collapseAtTick > 0)
            {
                if (Find.TickManager.TicksGame >= collapseAtTick)
                    TriggerCollapse();
                return;
            }

            // Throttled poll. Pawn.IsHashIntervalTick spreads the work out
            // across the tick window so multiple bound travelers do not all
            // check on the same tick.
            if (parent.pawn == null) return;
            if (!parent.pawn.IsHashIntervalTick(PollIntervalTicks)) return;

            if (source == null || source.Dead || source.Destroyed)
            {
                ScheduleCollapse();
            }
        }

        private void ScheduleCollapse()
        {
            int delay = Rand.RangeInclusive(CollapseDelayMinTicks, CollapseDelayMaxTicks);
            collapseAtTick = Find.TickManager.TicksGame + delay;
        }

        private void TriggerCollapse()
        {
            collapsed = true;
            var pawn = parent.pawn;
            if (pawn == null || pawn.Destroyed) return;

            bool hasTranscendent = HasActiveTranscendenceGene(pawn);

            try
            {
                if (hasTranscendent)
                {
                    // Trigger normal death pipeline so the Transcendent
                    // gene's own on-death hook fires.
                    Messages.Message(
                        $"{pawn.LabelShort} collapses without a sound.",
                        pawn, MessageTypeDefOf.NegativeEvent, historical: true);
                    pawn.Kill(null);
                }
                else
                {
                    // Vanilla causality break - no corpse, no death log.
                    var here = pawn.Spawned
                        ? new TargetInfo(pawn.Position, pawn.Map)
                        : TargetInfo.Invalid;

                    Messages.Message(
                        $"{pawn.LabelShort} is gone. Nobody quite remembers when.",
                        here, MessageTypeDefOf.NeutralEvent, historical: true);

                    if (pawn.Spawned) pawn.DeSpawn();
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[CreepjoinerTimeTraveler] collapse failed: {ex}");
            }
        }

        private static bool HasActiveTranscendenceGene(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive) return false;
            if (pawn.genes == null) return false;

            var def = DefDatabase<GeneDef>.GetNamedSilentFail(TranscendenceGeneDefName);
            if (def == null) return false;

            return pawn.genes.GenesListForReading.Any(g => g.def == def && g.Active);
        }
    }
}
