using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Turns a freshly generated creep-joiner pawn into a 20-30 year older
    /// copy of a random colonist: same gender/appearance, same scars and
    /// augmentations, different name, high-tech outfit.
    /// </summary>
    public static class TimeTravelerTransformer
    {
        public static void Apply(Pawn pawn, DefModExtension_TimeTraveler ext)
        {
            var template = PickTemplateColonist(pawn);
            if (template == null)
            {
                // No colonist found - leave the pawn unchanged so the event
                // doesn't run into an error.
                Log.Message("[CreepjoinerTimeTraveler] no colonist template available - skipping transform.");
                return;
            }

            CopyAppearance(pawn, template);
            CopyGenes(pawn, template);
            BumpAge(pawn, template, ext);
            CopyMarkerHediffs(pawn, template);
            RandomizeName(pawn);
            ReplaceApparelHightech(pawn, ext.minTechLevel);
            ReplaceWeapon(pawn, ext.minTechLevel);
            AttachVisitTimer(pawn, ext);
        }

        // ---------- Template ----------

        private static Pawn PickTemplateColonist(Pawn pawn)
        {
            var map = pawn.MapHeld ?? Find.AnyPlayerHomeMap;
            IEnumerable<Pawn> pool = null;

            if (map != null)
            {
                pool = map.mapPawns.FreeColonistsSpawned
                    .Where(p => p != null && !p.Dead && p.RaceProps.Humanlike);
            }
            if (pool == null || !pool.Any())
            {
                pool = PawnsFinder.AllMaps_FreeColonists
                    .Where(p => p != null && !p.Dead && p.RaceProps.Humanlike);
            }

            return pool.RandomElementWithFallback();
        }

        // ---------- Appearance ----------

        private static void CopyAppearance(Pawn pawn, Pawn t)
        {
            pawn.gender = t.gender;

            if (pawn.story != null && t.story != null)
            {
                pawn.story.bodyType          = t.story.bodyType;
                pawn.story.headType          = t.story.headType;
                pawn.story.hairDef           = t.story.hairDef;
                pawn.story.HairColor         = t.story.HairColor;
                pawn.story.skinColorOverride = t.story.skinColorOverride;
                pawn.story.SkinColorBase     = t.story.SkinColorBase;
            }

            if (pawn.style != null && t.style != null)
            {
                pawn.style.beardDef   = t.style.beardDef;
                pawn.style.FaceTattoo = t.style.FaceTattoo;
                pawn.style.BodyTattoo = t.style.BodyTattoo;
            }

            // Renderer refresh - defensive, because the renderer has shifted
            // between RW versions. If it fails it's only a cosmetic detail.
            try { pawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
            try { PortraitsCache.SetDirty(pawn); } catch { }
        }

        // ---------- Genes (Biotech only) ----------

        private static void CopyGenes(Pawn pawn, Pawn t)
        {
            if (!ModsConfig.BiotechActive) return;
            if (pawn.genes == null || t.genes == null) return;

            // Clear existing genes.
            foreach (var g in pawn.genes.GenesListForReading.ToList())
                pawn.genes.RemoveGene(g);

            // Copy xenotype label.
            pawn.genes.SetXenotypeDirect(t.genes.Xenotype);

            foreach (var g in t.genes.Endogenes.ToList())
                pawn.genes.AddGene(g.def, false);
            foreach (var g in t.genes.Xenogenes.ToList())
                pawn.genes.AddGene(g.def, true);
        }

        // ---------- Age ----------

        private static void BumpAge(Pawn pawn, Pawn t, DefModExtension_TimeTraveler ext)
        {
            int yearsOffset = Rand.RangeInclusive(ext.minAgeOffsetYears, ext.maxAgeOffsetYears);
            long offsetTicks = (long)yearsOffset * 3600000L;

            pawn.ageTracker.AgeBiologicalTicks    = t.ageTracker.AgeBiologicalTicks    + offsetTicks;
            pawn.ageTracker.AgeChronologicalTicks = t.ageTracker.AgeChronologicalTicks + offsetTicks;
        }

        // ---------- Scars + augmentations ----------

        private static void CopyMarkerHediffs(Pawn pawn, Pawn t)
        {
            // Copy everything that counts as a "marker"; we don't strip anything
            // from the target pawn - the generated hediffs of a creep-joiner
            // are often desirable.
            foreach (var h in t.health.hediffSet.hediffs.ToList())
            {
                bool isScar    = h is Hediff_Injury inj && inj.IsPermanent();
                bool isAugment = h.def.countsAsAddedPartOrImplant
                                 || h is Hediff_AddedPart
                                 || h is Hediff_Implant;

                if (!isScar && !isAugment) continue;

                // Find the matching body part on the target pawn (same def + same label index).
                BodyPartRecord targetPart = null;
                if (h.Part != null)
                {
                    var candidates = pawn.RaceProps.body.AllParts
                        .Where(p => p.def == h.Part.def).ToList();
                    targetPart = candidates.FirstOrDefault(p => p.Label == h.Part.Label)
                                 ?? candidates.FirstOrDefault();
                }

                try
                {
                    var copy = HediffMaker.MakeHediff(h.def, pawn, targetPart);
                    copy.Severity = h.Severity;

                    if (copy is Hediff_Injury copyInj && isScar)
                    {
                        var permComp = copyInj.TryGetComp<HediffComp_GetsPermanent>();
                        if (permComp != null) permComp.IsPermanent = true;
                    }

                    pawn.health.AddHediff(copy, targetPart);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[CreepjoinerTimeTraveler] failed to copy {h.def.defName}: {ex.Message}");
                }
            }
        }

        // ---------- Name ----------

        private static void RandomizeName(Pawn pawn)
        {
            try
            {
                pawn.Name = PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full, null);
            }
            catch
            {
                // Fallback - in case name generation complains about the xenotype.
            }
        }

        // ---------- Apparel ----------

        private static void ReplaceApparelHightech(Pawn pawn, TechLevel minTech)
        {
            if (pawn.apparel == null) return;
            pawn.apparel.DestroyAll();

            var pool = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.IsApparel
                            && d.techLevel >= minTech
                            && !d.destroyOnDrop
                            && d.apparel != null
                            && (d.apparel.developmentalStageFilter & DevelopmentalStage.Adult) != 0)
                .ToList();

            BodyPartGroupDef[] slots =
            {
                BodyPartGroupDefOf.Torso,
                BodyPartGroupDefOf.Legs,
                BodyPartGroupDefOf.UpperHead,
            };

            foreach (var slot in slots)
            {
                var fit = pool.Where(a => a.apparel.bodyPartGroups.Contains(slot)).ToList();
                if (fit.Count == 0) continue;

                var def = fit.RandomElement();
                var stuff = def.MadeFromStuff ? GenStuff.RandomStuffByCommonalityFor(def) : null;
                var apparel = (Apparel)ThingMaker.MakeThing(def, stuff);
                if (apparel.def.colorGenerator != null)
                    apparel.SetColor(apparel.def.colorGenerator.NewRandomizedColor());
                pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            }
        }

        // ---------- Weapon ----------

        private static void ReplaceWeapon(Pawn pawn, TechLevel minTech)
        {
            if (pawn.equipment == null) return;
            if (pawn.equipment.Primary != null)
                pawn.equipment.DestroyEquipment(pawn.equipment.Primary);

            // 40% chance: no weapon - he reads as less threatening.
            if (Rand.Chance(0.4f)) return;

            var pool = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.IsWeapon
                            && (d.IsMeleeWeapon || d.IsRangedWeapon)
                            && d.techLevel >= minTech
                            && !d.destroyOnDrop)
                .ToList();
            if (pool.Count == 0) return;

            var def = pool.RandomElement();
            var stuff = def.MadeFromStuff ? GenStuff.RandomStuffByCommonalityFor(def) : null;
            var weapon = (ThingWithComps)ThingMaker.MakeThing(def, stuff);
            pawn.equipment.AddEquipment(weapon);
        }

        // ---------- Timer ----------

        private static void AttachVisitTimer(Pawn pawn, DefModExtension_TimeTraveler ext)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("CTT_TimeTravelerVisit");
            if (def == null) return;

            var hediff = HediffMaker.MakeHediff(def, pawn);
            pawn.health.AddHediff(hediff);

            // If the def-defined duration should be overridden, adjust here.
            var comp = hediff.TryGetComp<HediffComp_LeaveAfterTicks>();
            if (comp != null && ext.visitDurationDays > 0)
            {
                Traverse.Create(comp).Field("ticksLeft").SetValue(ext.visitDurationDays * 60000);
            }
        }
    }
}
