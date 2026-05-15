using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// An die CreepJoinerFormKindDef gehaengte Konfiguration des Zeitreisenden.
    /// </summary>
    public class DefModExtension_TimeTraveler : DefModExtension
    {
        public int minAgeOffsetYears = 20;
        public int maxAgeOffsetYears = 30;
        public int visitDurationDays  = 30;
        public TechLevel minTechLevel = TechLevel.Industrial;
    }
}
