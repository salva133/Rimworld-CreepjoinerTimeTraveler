using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Hooks in after PawnGenerator.GeneratePawn completes.
    /// If the freshly generated pawn is a creep-joiner with our
    /// "CTT_TimeTraveler" form, we transform him.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                  new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        static void Postfix(Pawn __result)
        {
            if (__result == null) return;
            try
            {
                var form = TryGetCreepJoinerForm(__result);
                if (form == null || form.defName != "CTT_TimeTraveler") return;

                var ext = form.GetModExtension<DefModExtension_TimeTraveler>();
                if (ext == null) return;

                TimeTravelerTransformer.Apply(__result, ext);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[CreepjoinerTimeTraveler] postfix transform failed: {ex}");
            }
        }

        /// <summary>
        /// Grabs the CreepJoinerFormKindDef from the pawn as version-tolerantly
        /// as possible. Anomaly has a ThingComp "CompCreepJoiner" with a field
        /// or property of type CreepJoinerFormKindDef. We search reflectively
        /// so small API shifts between 1.5/1.6 don't break everything.
        /// </summary>
        private static CreepJoinerFormKindDef TryGetCreepJoinerForm(Pawn pawn)
        {
            var comp = pawn.AllComps?.FirstOrDefault(c => c.GetType().Name == "CompCreepJoiner");
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
