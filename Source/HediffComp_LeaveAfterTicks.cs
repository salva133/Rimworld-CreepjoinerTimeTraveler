using RimWorld;
using Verse;
using Verse.AI;

namespace CreepjoinerTimeTraveler
{
    public class HediffCompProperties_LeaveAfterTicks : HediffCompProperties
    {
        public int leaveAfterTicks = 1800000; // 30 RimWorld days

        public HediffCompProperties_LeaveAfterTicks()
        {
            compClass = typeof(HediffComp_LeaveAfterTicks);
        }
    }

    /// <summary>
    /// When the timer expires, the pawn leaves the colony and the map.
    /// </summary>
    public class HediffComp_LeaveAfterTicks : HediffComp
    {
        private int ticksLeft = -1;

        public HediffCompProperties_LeaveAfterTicks Props
            => (HediffCompProperties_LeaveAfterTicks)props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            if (ticksLeft < 0) ticksLeft = Props.leaveAfterTicks;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksLeft, "CTT_ticksLeft", -1);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (ticksLeft < 0) return;
            ticksLeft--;
            if (ticksLeft <= 0) TriggerLeave();
        }

        private void TriggerLeave()
        {
            var pawn = Pawn;
            if (pawn == null || pawn.Destroyed) return;

            try
            {
                if (pawn.MapHeld != null && pawn.Spawned)
                {
                    Messages.Message(
                        $"{pawn.LabelShort} leaves the colony - as quietly as he arrived.",
                        pawn, MessageTypeDefOf.NeutralEvent, historical: false);

                    // Drop out of the player faction so the pawn wanders off freely.
                    if (pawn.Faction != null && pawn.Faction.IsPlayer)
                    {
                        pawn.SetFaction(null);
                    }

                    if (CellFinder.TryFindRandomEdgeCellWith(
                            c => pawn.MapHeld.reachability.CanReach(
                                pawn.Position, c, PathEndMode.OnCell, TraverseMode.PassDoors,
                                Danger.Deadly),
                            pawn.MapHeld, CellFinder.EdgeRoadChance_Ignore, out var exit))
                    {
                        var job = JobMaker.MakeJob(JobDefOf.Goto, exit);
                        job.exitMapOnArrival = true;
                        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CreepjoinerTimeTraveler] leave failed: {ex}");
            }

            // Remove the marker hediff so this can't fire twice.
            pawn.health.RemoveHediff(parent);
        }
    }
}
