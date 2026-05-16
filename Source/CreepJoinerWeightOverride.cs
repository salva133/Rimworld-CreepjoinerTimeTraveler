using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Pins every CreepJoinerFormKindDef's spawn weight at startup:
    /// CTT_TimeTraveler -> 1, everything else -> 0.
    ///
    /// Effect: the vanilla CreepJoinerJoin incident still rolls and fires as
    /// usual, but the form picker only ever has one candidate with non-zero
    /// weight, so every creep-joiner that arrives is a time traveler.
    ///
    /// Implementation notes:
    /// - 'weight' on CreepJoinerFormKindDef exists at runtime (vanilla XML
    ///   writes to it) but is hidden in the Krafs ref assembly. Access goes
    ///   through Harmony's Traverse, same pattern the rest of the mod uses.
    /// - Runs once at game start via StaticConstructorOnStartup, after all
    ///   Defs are loaded. The override survives save/load because it touches
    ///   the def itself, not any per-game state.
    /// - Forms from other mods are also zeroed - that is intentional, the
    ///   mod's premise is that the time traveler is the only creep joiner.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CreepJoinerWeightOverride
    {
        private const string TimeTravelerDefName = "CTT_TimeTraveler";

        static CreepJoinerWeightOverride()
        {
            int zeroed = 0;
            bool foundSelf = false;

            foreach (var def in DefDatabase<CreepJoinerFormKindDef>.AllDefsListForReading)
            {
                var t = Traverse.Create(def).Field("weight");
                if (def.defName == TimeTravelerDefName)
                {
                    t.SetValue(1f);
                    foundSelf = true;
                }
                else
                {
                    t.SetValue(0f);
                    zeroed++;
                }
            }

            if (!foundSelf)
            {
                Log.Error($"[CreepjoinerTimeTraveler] {TimeTravelerDefName} not found in CreepJoinerFormKindDef database - weight override left other forms at 0, no creep joiner will ever spawn.");
                return;
            }

            Log.Message($"[CreepjoinerTimeTraveler] weight override applied: {TimeTravelerDefName}=1, {zeroed} other form(s) zeroed.");
        }
    }
}
