# Creep Joiner: Time Traveler

A RimWorld mod that adds a new form to the Anomaly creep-joiner system:
a **time traveler** who is secretly a 20-30 year older copy of one of your
current colonists.

> *"A drifter in strange, ageing gear. There is something unsettlingly familiar about
> them - old scars and augmentations that you swear you have seen before. They claim
> to come from far away. They are quiet, withdrawn, and ask to stay a while."*

---

## What happens in-game

When the game triggers a creep-joiner event and rolls the `CTT_TimeTraveler`
form, the freshly generated pawn is rebuilt in a postfix on
`PawnGenerator.GeneratePawn`:

- **Template:** a random living colonist from your home map is picked as the
  template (fallback: any free colonist on any player map).
- **Appearance, 1:1:** gender, body type, head type, hair def, hair color,
  skin color, beard and tattoos (face + body) are copied over.
- **Genes (Biotech only):** endogenes, xenogenes and the xenotype label are
  copied from the template; the joiner's existing genes are cleared first.
- **Age:** biological and chronological age are both shifted forward by a
  random **20-30 years** relative to the template.
- **Scars + augmentations:** every permanent injury (`Hediff_Injury` with
  `IsPermanent`) and every added part / implant (`Hediff_AddedPart`,
  `Hediff_Implant`, `countsAsAddedPartOrImplant`) is mirrored onto the matching
  body part on the joiner. The template's hediffs are left untouched.
- **New name:** the pawn gets a freshly generated first / nick / last name -
  *not* the template's name.
- **Equipment:**
  - Apparel is wiped and replaced with random apparel defs at or above the
    configured tech level (default: Industrial), filling torso, legs and upper
    head slots.
  - Weapon: 60% chance of a random melee or ranged weapon at or above the
    tech level, 40% chance of **no weapon** at all (he reads as less
    threatening).
- **Visit timer:** the pawn gets the invisible `CTT_TimeTravelerVisit` hediff.
  When it expires (default: **30 days**, 1,800,000 ticks) the pawn is dropped
  from the player faction and pathed to the nearest reachable map-edge cell to
  leave the map on his own. A message
  `"<Name> leaves the colony - as quietly as he arrived."` notifies
  the player.

The time traveler is **mechanically harmless** - he doesn't attack and carries
no hidden Anomaly effects. The only hint at his identity is the set of shared
scars, implants and genes with the original colonist.

---

## Requirements

| Component | Version |
| --- | --- |
| RimWorld | **1.6** |
| Anomaly DLC | required (creep-joiner system) |
| Biotech DLC | optional - enables the gene copy |
| [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) | required (runtime patch) |

Load order (from `About.xml`): after Core, Royalty, Ideology, Biotech, Anomaly,
Harmony.

---

## Directory layout

```
CreepjoinerTimeTraveler/
+- About/
|  +- About.xml                       # mod manifest, dependencies, description
+- LoadFolders.xml                    # loads both the root and 1.6/
+- 1.6/
|  +- Assemblies/
|  |  +- CreepjoinerTimeTraveler.dll  # produced from Source/
|  +- About/
|  |  +- About.xml                    # empty placeholder (real manifest is in /About)
|  +- Defs/
|     +- CreepJoinerFormKindDefs/
|     |  +- CTT_TimeTraveler.xml      # new creep-joiner form
|     +- HediffDefs/
|        +- CTT_Hediffs.xml           # invisible visit-timer hediff
+- Source/
|  +- CreepjoinerTimeTraveler.csproj  # net472, OutputPath -> ../1.6/Assemblies/
|  +- ModInit.cs                      # Harmony.PatchAll() on game start
|  +- DefModExtension_TimeTraveler.cs # configuration attached to the form def
|  +- Patch_PawnGenerator.cs          # Harmony postfix on PawnGenerator.GeneratePawn
|  +- TimeTravelerTransformer.cs      # the actual transformation
|  +- HediffComp_LeaveAfterTicks.cs   # timer + "leave the map" logic
+- README.md
+- .gitattributes
+- .gitignore
```

---

## Configuration

The `DefModExtension_TimeTraveler` on the `CreepJoinerFormKindDef` drives the
behavior without code changes:

```xml
<modExtensions>
  <li Class="CreepjoinerTimeTraveler.DefModExtension_TimeTraveler">
    <minAgeOffsetYears>20</minAgeOffsetYears>
    <maxAgeOffsetYears>30</maxAgeOffsetYears>
    <visitDurationDays>30</visitDurationDays>
    <minTechLevel>Industrial</minTechLevel>
  </li>
</modExtensions>
```

| Field | Default | Effect |
| --- | --- | --- |
| `minAgeOffsetYears` | 20 | lower bound for the age offset relative to the template |
| `maxAgeOffsetYears` | 30 | upper bound for the age offset |
| `visitDurationDays` | 30 | days until the pawn leaves the map |
| `minTechLevel` | Industrial | minimum tech level for apparel and weapons |

The tick value in `CTT_Hediffs.xml` (`<leaveAfterTicks>`) is overwritten at
runtime by `visitDurationDays * 60000` as soon as the hediff is attached, so
in practice only the DefModExtension value matters.

---

## Implementation notes

- **Version-tolerant form lookup:** `Patch_PawnGenerator` grabs the
  `CompCreepJoiner` via reflection and scans its fields / properties for a
  `CreepJoinerFormKindDef`. This keeps the mod alive across small Anomaly API
  shifts.
- **Defensive renderer refreshes:** `SetAllGraphicsDirty()` and
  `PortraitsCache.SetDirty()` are wrapped in `try/catch` because the renderer
  has shifted between RimWorld versions - if it fails the worst case is a
  briefly stale portrait.
- **Scar copy:** body parts are matched by `def + Label`, falling back to the
  first part with a matching def. The permanent flag is set via
  `HediffComp_GetsPermanent`.
- **Departure logic:** when the timer fires the pawn is dropped from the
  player faction (`SetFaction(null)`), given a `JobDefOf.Goto` job to an edge
  cell with `exitMapOnArrival = true`, and the marker hediff is removed so the
  code can't fire twice.

---

## Build

Requires the .NET SDK and NuGet.

```cmd
cd Source
dotnet build -c Release
```

References come in via NuGet:

- `Krafs.Rimworld.Ref` 1.6.* (RimWorld + Unity refs, all DLCs included)
- `Lib.Harmony` 2.3.* (`ExcludeAssets=runtime`, since Harmony is loaded as a
  separate mod)

Build output drops straight into `..\1.6\Assemblies\CreepjoinerTimeTraveler.dll`,
exactly where RimWorld expects it.

---

## License / Author

Author: **DonSantana**
PackageId: `donsantana.creepjoiners.timetraveler`
