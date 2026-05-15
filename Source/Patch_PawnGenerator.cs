using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Greift nach Abschluss von PawnGenerator.GeneratePawn ein.
    /// Wenn der gerade erzeugte Pawn ein Creep-Joiner mit unserer
    /// Form "CTT_TimeTraveler" ist, transformieren wir ihn.
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
        /// Holt - moeglichst version-tolerant - die CreepJoinerFormKindDef vom Pawn.
        /// Anomaly hat einen ThingComp "CompCreepJoiner" mit einem Feld/Property
        /// vom Typ CreepJoinerFormKindDef. Wir suchen reflektiv, damit kleinere
        /// API-Aenderungen zwischen 1.5/1.6 nicht alles brechen.
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
