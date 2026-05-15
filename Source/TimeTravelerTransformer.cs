using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreepjoinerTimeTraveler
{
    /// <summary>
    /// Macht aus einem frisch generierten Creep-Joiner-Pawn eine 20-30 Jahre aeltere
    /// Kopie eines zufaelligen Kolonisten: gleiches Geschlecht/Aussehen,
    /// gleiche Narben und Augmentationen, anderer Name, Hightech-Outfit.
    /// </summary>
    public static class TimeTravelerTransformer
    {
        public static void Apply(Pawn pawn, DefModExtension_TimeTraveler ext)
        {
            var template = PickTemplateColonist(pawn);
            if (template == null)
            {
                // Kein Kolonist gefunden - dann bleibt der Pawn unveraendert,
                // damit das Event nicht in einen Fehler laeuft.
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

        // ---------- Aussehen ----------

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

            // Render-Refresh - defensiv, weil sich der Renderer zwischen RW-Versionen
            // veraendert hat. Failt das, ist es nur ein kosmetisches Detail.
            try { pawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
            try { PortraitsCache.SetDirty(pawn); } catch { }
        }

        // ---------- Gene (nur mit Biotech) ----------

        private static void CopyGenes(Pawn pawn, Pawn t)
        {
            if (!ModsConfig.BiotechActive) return;
            if (pawn.genes == null || t.genes == null) return;

            // Vorhandene Gene leeren.
            foreach (var g in pawn.genes.GenesListForReading.ToList())
                pawn.genes.RemoveGene(g);

            // Xenotyp-Label kopieren.
            pawn.genes.SetXenotypeDirect(t.genes.Xenotype);

            foreach (var g in t.genes.Endogenes.ToList())
                pawn.genes.AddGene(g.def, false);
            foreach (var g in t.genes.Xenogenes.ToList())
                pawn.genes.AddGene(g.def, true);
        }

        // ---------- Alter ----------

        private static void BumpAge(Pawn pawn, Pawn t, DefModExtension_TimeTraveler ext)
        {
            int yearsOffset = Rand.RangeInclusive(ext.minAgeOffsetYears, ext.maxAgeOffsetYears);
            long offsetTicks = (long)yearsOffset * 3600000L;

            pawn.ageTracker.AgeBiologicalTicks    = t.ageTracker.AgeBiologicalTicks    + offsetTicks;
            pawn.ageTracker.AgeChronologicalTicks = t.ageTracker.AgeChronologicalTicks + offsetTicks;
        }

        // ---------- Narben + Augmentationen ----------

        private static void CopyMarkerHediffs(Pawn pawn, Pawn t)
        {
            // Alles abnehmen, was als "Marker" gilt, vom Target Pawn entfernen wir nicht -
            // die generierten Hediffs eines Creep-Joiners sind oft erwuenscht.
            foreach (var h in t.health.hediffSet.hediffs.ToList())
            {
                bool isScar    = h is Hediff_Injury inj && inj.IsPermanent();
                bool isAugment = h.def.countsAsAddedPartOrImplant
                                 || h is Hediff_AddedPart
                                 || h is Hediff_Implant;

                if (!isScar && !isAugment) continue;

                // Passenden Body-Part am Ziel-Pawn finden (gleicher Def + gleicher Label-Index).
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
                // Fallback - falls die Namensgenerierung fuer den Xenotypen meckert.
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

        // ---------- Waffe ----------

        private static void ReplaceWeapon(Pawn pawn, TechLevel minTech)
        {
            if (pawn.equipment == null) return;
            if (pawn.equipment.Primary != null)
                pawn.equipment.DestroyEquipment(pawn.equipment.Primary);

            // 40% Chance: keine Waffe - er wirkt entwaffnender.
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

            // Falls die Def-Dauer ueberschrieben werden soll, hier anpassen.
            var comp = hediff.TryGetComp<HediffComp_LeaveAfterTicks>();
            if (comp != null && ext.visitDurationDays > 0)
            {
                Traverse.Create(comp).Field("ticksLeft").SetValue(ext.visitDurationDays * 60000);
            }
        }
    }
}
