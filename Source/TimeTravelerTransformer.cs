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
    /// copy of a random colonist: same gender, appearance, genes, scars,
    /// augmentations, traits, backstories, and skills (+5 each to model
    /// extra years of experience). Name, apparel, and weapon are re-rolled.
    /// Adds the Transcendence xenogene if Vanilla Races Expanded - Archons
    /// is loaded.
    ///
    /// Pipeline order matters:
    ///   1. Gender first (downstream systems branch on it).
    ///   2. Genes BEFORE story colors. Adding a color-tone gene writes into
    ///      story.hairColor / story.skinColorOverride during AddGene, so we
    ///      must finish all gene operations before the explicit color writes.
    ///   3. Story body/head/hair defs (plain fields with safe setters).
    ///   4. Hair + skin colors via Traverse field writes - the vanilla
    ///      property setters have side effects (SkinColorBase clears
    ///      skinColorOverride) that silently undo any ordering we pick.
    ///   5. Style (beard, tattoos).
    ///   6. Transcendence xenogene (optional, gated on the def existing).
    ///   7. Backstories first among the personality batch - they define
    ///      disabled work tags that constrain skill writes.
    ///   8. Traits next - can also disable skills.
    ///   9. Skills last in the personality batch, so disables are in place.
    ///  10. Scars + augmentations (hediffs).
    ///  11. Age bump.
    ///  12. Name, apparel, weapon.
    ///  13. Visit timer hediff (also acts as the dedupe marker).
    ///  14. Renderer refresh at the very end, after everything has settled.
    ///  15. Flavor message into the message log.
    /// </summary>
    public static class TimeTravelerTransformer
    {
        private const string TranscendenceGeneDefName = "VRE_Transcendent";

        // Years of experience the time traveler has on his younger self -
        // mechanically modelled as a flat skill bonus per skill, capped at
        // the vanilla maximum (20).
        private const int SkillBonus = 5;
        private const int SkillMax = 20;

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

            Log.Message($"[CreepjoinerTimeTraveler] transforming {pawn.LabelShort} from template {template.LabelShort}");

            // 1) Gender (must precede everything else - body type / hair / genes branch on it).
            pawn.gender = template.gender;

            // 2) Genes first - color-tone genes write into story during AddGene.
            CopyGenes(pawn, template);

            // 3) Story body/head/hair defs (no color side effects on these).
            CopyBodyAndHairDefs(pawn, template);

            // 4) Explicit color writes via Traverse - bypass property setters
            //    so SkinColorBase's setter does not wipe skinColorOverride.
            CopyColors(pawn, template);

            // 5) Style (beard, tattoos) - guarded for gender mismatch.
            CopyStyle(pawn, template);

            // 6) Optional Transcendence xenogene (Vanilla Races Expanded - Archons).
            AddTranscendenceXenogene(pawn);

            // 7) Backstories first - they define disabled work tags that
            //    constrain which skills the SkillRecord setter accepts.
            CopyBackstories(pawn, template);

            // 8) Traits next - can also disable skills, and may be required
            //    by backstories.
            CopyTraits(pawn, template);

            // 9) Skills last so backstory- and trait-derived disables are
            //    already in place. Each skill gets +SkillBonus (clamped to
            //    SkillMax) to represent extra years of experience.
            CopySkillsWithBonus(pawn, template);

            // 10) Scars and augmentations.
            CopyMarkerHediffs(pawn, template);

            // 11) Age bump.
            BumpAge(pawn, template, ext);

            // 12) New name, gear, weapon.
            RandomizeName(pawn);
            ReplaceApparelHightech(pawn, ext.minTechLevel);
            ReplaceWeapon(pawn, ext.minTechLevel);

            // 13) Visit timer + source binding + familiar relation.
            AttachVisitTimer(pawn, template, ext);
            AddFamiliarRelation(pawn, template);

            // 14) Refresh visuals after everything has been written.
            RefreshVisuals(pawn);

            // 15) Flavor message attached to the pawn so it shows in the
            //     message log together with the vanilla arrival letter.
            ShowArrivalMessage(pawn, template);
        }

        // ---------- Template ----------
        //
        // Template pool excludes colonists younger than MinTemplateAgeYears.
        // Below that age the source pawn has no Adulthood backstory and a
        // child-shaped body/head; copying any of that onto an adult time
        // traveler would be inconsistent, so we filter them out at the
        // selection step.

        private const int MinTemplateAgeYears = 12;

        private static Pawn PickTemplateColonist(Pawn pawn)
        {
            var map = pawn.MapHeld ?? Find.AnyPlayerHomeMap;
            IEnumerable<Pawn> pool = null;

            if (map != null)
            {
                pool = map.mapPawns.FreeColonistsSpawned.Where(IsEligibleTemplate);
            }
            if (pool == null || !pool.Any())
            {
                pool = PawnsFinder.AllMaps_FreeColonists.Where(IsEligibleTemplate);
            }

            return pool.RandomElementWithFallback();
        }

        private static bool IsEligibleTemplate(Pawn p)
        {
            if (p == null || p.Dead) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.ageTracker == null) return false;
            return p.ageTracker.AgeBiologicalYears >= MinTemplateAgeYears;
        }

        // ---------- Body / head / hair defs ----------

        private static void CopyBodyAndHairDefs(Pawn pawn, Pawn t)
        {
            if (pawn.story == null || t.story == null) return;

            if (t.story.bodyType != null) pawn.story.bodyType = t.story.bodyType;
            if (t.story.headType != null) pawn.story.headType = t.story.headType;
            if (t.story.hairDef  != null) pawn.story.hairDef  = t.story.hairDef;
        }

        // ---------- Colors ----------
        //
        // Direct field writes via Traverse. The property setters in vanilla
        // Pawn_StoryTracker have side effects (e.g. SkinColorBase resets
        // skinColorOverride) which silently undo correct assignments
        // depending on call order. Field writes bypass all of that; the
        // final renderer refresh in RefreshVisuals() picks the new values up.

        private static void CopyColors(Pawn pawn, Pawn t)
        {
            if (pawn.story == null || t.story == null) return;

            try
            {
                var srcHair = Traverse.Create(t.story).Field("hairColor").GetValue<Color>();
                Traverse.Create(pawn.story).Field("hairColor").SetValue(srcHair);
            }
            catch { /* defensive - if the field moved we just keep gene-based color */ }

            try
            {
                var srcBase = Traverse.Create(t.story).Field("skinColorBase").GetValue<Color>();
                Traverse.Create(pawn.story).Field("skinColorBase").SetValue(srcBase);
            }
            catch { }

            try
            {
                var srcOverride = Traverse.Create(t.story).Field("skinColorOverride").GetValue<Color?>();
                Traverse.Create(pawn.story).Field("skinColorOverride").SetValue(srcOverride);
            }
            catch { }

            // Genes cache color tones. Tell the gene system to pick up the
            // new values; otherwise the cached color from a previously
            // resolved gene wins on the next render. NotifyColorsChanged
            // exists at runtime but is hidden in the Krafs ref assembly, so
            // we call it reflectively.
            try
            {
                if (pawn.genes != null)
                    Traverse.Create(pawn.genes).Method("NotifyColorsChanged").GetValue();
            }
            catch { }
        }

        // ---------- Style (beard, tattoos) ----------

        private static void CopyStyle(Pawn pawn, Pawn t)
        {
            if (pawn.style == null || t.style == null) return;

            // Beard only makes sense for matching gender; we already aligned
            // gender, so this branch is mostly defensive against modded styles.
            if (pawn.gender == t.gender && t.style.beardDef != null)
            {
                pawn.style.beardDef = t.style.beardDef;
            }

            if (t.style.FaceTattoo != null) pawn.style.FaceTattoo = t.style.FaceTattoo;
            if (t.style.BodyTattoo != null) pawn.style.BodyTattoo = t.style.BodyTattoo;
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

            // Order matters - vanilla resolves color tones in the order
            // genes were added, with later genes winning. Preserve template
            // order for both groups.
            foreach (var g in t.genes.Endogenes.ToList())
                pawn.genes.AddGene(g.def, false);
            foreach (var g in t.genes.Xenogenes.ToList())
                pawn.genes.AddGene(g.def, true);
        }

        // ---------- Transcendence xenogene ----------
        //
        // VRE_Transcendent is the gene def from Vanilla Races Expanded - Archons.
        // GetNamedSilentFail keeps the code safe when that mod is uninstalled.

        private static void AddTranscendenceXenogene(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive) return;
            if (pawn.genes == null) return;

            var def = DefDatabase<GeneDef>.GetNamedSilentFail(TranscendenceGeneDefName);
            if (def == null) return;

            if (pawn.genes.GenesListForReading.Any(g => g.def == def))
                return;

            try
            {
                pawn.genes.AddGene(def, xenogene: true);
                Log.Message($"[CreepjoinerTimeTraveler] added xenogene {def.defName} to {pawn.LabelShort}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CreepjoinerTimeTraveler] failed to add Transcendence gene: {ex.Message}");
            }
        }

        // ---------- Backstories ----------

        private static void CopyBackstories(Pawn pawn, Pawn t)
        {
            if (pawn.story == null || t.story == null) return;

            try
            {
                pawn.story.Childhood = t.story.Childhood;
                pawn.story.Adulthood = t.story.Adulthood;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CreepjoinerTimeTraveler] backstory copy failed: {ex.Message}");
            }
        }

        // ---------- Traits ----------
        //
        // Clear everything first, then re-add from the template. Some traits
        // on the template might originate from genes; since we copy the same
        // genes the gene system will re-apply those during AddGene, so going
        // through TraitSet here and then deduping via def is safe.

        private static void CopyTraits(Pawn pawn, Pawn t)
        {
            if (pawn.story?.traits == null || t.story?.traits == null) return;

            foreach (var trait in pawn.story.traits.allTraits.ToList())
            {
                try { pawn.story.traits.RemoveTrait(trait); } catch { }
            }

            foreach (var srcTrait in t.story.traits.allTraits)
            {
                if (srcTrait?.def == null) continue;
                if (pawn.story.traits.HasTrait(srcTrait.def)) continue;

                try
                {
                    pawn.story.traits.GainTrait(new Trait(srcTrait.def, srcTrait.Degree, forced: false));
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[CreepjoinerTimeTraveler] failed to add trait {srcTrait.def.defName}: {ex.Message}");
                }
            }
        }

        // ---------- Skills ----------
        //
        // Copy levels and passions, with a flat +SkillBonus on top. Skills
        // disabled by backstory or trait silently stay at zero - SkillRecord
        // accepts the assignment but TotallyDisabled blocks the use.

        private static void CopySkillsWithBonus(Pawn pawn, Pawn t)
        {
            if (pawn.skills == null || t.skills == null) return;

            foreach (var src in t.skills.skills)
            {
                if (src?.def == null) continue;
                var dst = pawn.skills.GetSkill(src.def);
                if (dst == null) continue;

                int newLevel = Mathf.Clamp(src.Level + SkillBonus, 0, SkillMax);

                try
                {
                    dst.Level = newLevel;
                    dst.passion = src.passion;
                    dst.xpSinceLastLevel = src.xpSinceLastLevel;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[CreepjoinerTimeTraveler] failed to set skill {src.def.defName}: {ex.Message}");
                }
            }
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

        private static void AttachVisitTimer(Pawn pawn, Pawn template, DefModExtension_TimeTraveler ext)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("CTT_TimeTravelerVisit");
            if (def == null) return;

            var hediff = HediffMaker.MakeHediff(def, pawn);
            pawn.health.AddHediff(hediff);

            // Override the def-defined leave timer from the mod extension.
            var timer = hediff.TryGetComp<HediffComp_LeaveAfterTicks>();
            if (timer != null && ext.visitDurationDays > 0)
            {
                Traverse.Create(timer).Field("ticksLeft").SetValue(ext.visitDurationDays * 60000);
            }

            // Bind the source pawn so the comp can collapse the traveler
            // when the source dies. Both lifetime mechanics (leave timer +
            // source binding) live on the same hediff for save/load and
            // dedupe simplicity.
            var bound = hediff.TryGetComp<HediffComp_BoundToSource>();
            bound?.Bind(template);
        }

        // ---------- Familiar relation ----------
        //
        // Symmetric "familiar" relation visible on the social tab of both
        // pawns. Intentionally vague label - the player should notice the
        // resemblance themselves, not be told.

        private static void AddFamiliarRelation(Pawn pawn, Pawn template)
        {
            if (pawn?.relations == null || template == null) return;

            var def = DefDatabase<PawnRelationDef>.GetNamedSilentFail("CTT_Familiar");
            if (def == null) return;

            try
            {
                if (!pawn.relations.DirectRelationExists(def, template))
                    pawn.relations.AddDirectRelation(def, template);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CreepjoinerTimeTraveler] failed to add familiar relation: {ex.Message}");
            }
        }

        // ---------- Flavor message ----------
        //
        // A small pool of plain observations - things the colony might
        // plausibly notice about a new arrival, without drawing any
        // conclusion. The aim is texture, not explanation; players who
        // catch the resemblance to one of their own colonists should
        // arrive at it themselves.

        private static readonly string[] ArrivalFlavorTemplates =
        {
            "{0} settles in without asking which bunk is free.",
            "{0} pauses at the kitchen, as if looking for something that isn't there.",
            "{0} takes the long way around the workbench, then doubles back.",
            "{0} watches the colony with the quiet of someone with nowhere else to be.",
            "{0} stops sometimes, as if listening for something the others can't hear.",
            "{0} hums something tuneless while they work - none of you know the song.",
        };

        private static void ShowArrivalMessage(Pawn pawn, Pawn template)
        {
            try
            {
                var line = ArrivalFlavorTemplates.RandomElement();
                var text = string.Format(line, pawn.LabelShort);
                Messages.Message(text, pawn, MessageTypeDefOf.NeutralEvent, historical: true);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CreepjoinerTimeTraveler] arrival flavor message failed: {ex.Message}");
            }
        }

        // ---------- Visual refresh ----------
        //
        // Renderer internals shifted between 1.4 / 1.5 / 1.6; keep each call
        // in its own try/catch so a missing API does not cancel the others.

        private static void RefreshVisuals(Pawn pawn)
        {
            try { pawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
            try { PortraitsCache.SetDirty(pawn); } catch { }
        }
    }
}
