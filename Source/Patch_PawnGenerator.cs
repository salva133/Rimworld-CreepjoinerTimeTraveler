// This file is intentionally empty.
//
// The original Harmony patch on PawnGenerator.GeneratePawn was removed because
// it fired before the IncidentWorker assigned the form to CompCreepJoiner,
// so the form lookup always returned null and the transformer never ran.
//
// The replacement hook lives in Patch_IncidentWorker_CreepJoinerJoin.cs and
// runs once the incident worker has fully spawned and configured the joiner.
//
// Safe to delete this file from disk and drop it from version control; it is
// currently picked up only by the default <Compile Include="**\*.cs"> glob.
