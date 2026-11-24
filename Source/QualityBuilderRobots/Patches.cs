using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace QualityBuilderRobots
{
    /// <summary>
    /// Remembers which frame QualityBuilder is currently judging. QB's own gate
    /// (<c>_WorkGiver_ConstructFinishFrames.isPawnGoodEnoughToBuild(Pawn)</c>) only gets the pawn, but it is
    /// always called from QB's postfix on <see cref="WorkGiver_ConstructFinishFrames.JobOnThing"/> —
    /// for the builder itself and for builders it might kick off the same frame.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), nameof(WorkGiver_ConstructFinishFrames.JobOnThing))]
    public static class Patch_JobOnThing_FrameContext
    {
        public static Thing CurrentFrame;

        static bool Prepare() => QBAccess.Available && RobotAccess.Available;

        [HarmonyPriority(Priority.First)]
        static void Prefix(Thing t, out Thing __state)
        {
            __state = CurrentFrame;
            CurrentFrame = t;
        }

        static Exception Finalizer(Exception __exception, Thing __state)
        {
            CurrentFrame = __state;
            return __exception;
        }
    }

    /// <summary>
    /// The actual compatibility gate. For robots, QB's "best builder only" rule is replaced by:
    /// "may build if its quality roll can reach the minimum quality set on this frame".
    /// Colonists and Biotech mechs keep QB's original rule untouched.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_QB_IsPawnGoodEnoughToBuild
    {
        static bool Prepare() => QBAccess.Available && RobotAccess.Available;

        static MethodBase TargetMethod() => AccessTools.Method(QBAccess.WorkGiverPatchType, "isPawnGoodEnoughToBuild", new[] { typeof(Pawn) });

        static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (!QBRMod.Settings.enabled || !RobotAccess.IsRobot(pawn)) return true;
            try
            {
                __result = RobotGate.MayBuild(pawn, Patch_JobOnThing_FrameContext.CurrentFrame, out string why);
                RobotGate.DebugLog(pawn, Patch_JobOnThing_FrameContext.CurrentFrame, __result, why);
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[QualityBuilder Robots] gate failed, falling back to QualityBuilder's own rule: " + e, 0x5EB0B01);
                return true;
            }
        }
    }

    public static class RobotGate
    {
        public static bool MayBuild(Pawn robot, Thing frame, out string why)
        {
            if (!RobotAccess.HasWorkType(robot.def, WorkTypeDefOf.Construction))
            {
                why = "robot has no Construction work type";
                return false;
            }

            Map map = robot.Map ?? frame?.Map;
            object qb = QBAccess.Settings(map);

            if (QBRMod.Settings.respectExcludeMechs && QBAccess.Setting(qb, "excludeMechsFromSkilledBuilds", false))
            {
                why = "QualityBuilder excludes mechanoids from quality builds";
                return false;
            }

            if (QBRMod.Settings.respectBestConstructorOverride)
            {
                Pawn chosen = QBAccess.Setting<Pawn>(qb, "bestConstructorOverride", null);
                if (chosen != null && chosen.Spawned && chosen.Map == map && !chosen.Dead && QBAccess.PawnCanConstruct(chosen))
                {
                    why = chosen == robot ? "robot is QualityBuilder's chosen builder" : "QualityBuilder reserves quality builds for " + chosen.LabelShort;
                    return chosen == robot;
                }
            }

            int skill = RobotAccess.EffectiveQualitySkill(robot, robot.def, SkillDefOf.Construction);

            int ignoreAt = QBAccess.Setting(qb, "ignoreQualityBuilderAtSkill", 20);
            if (skill >= ignoreAt)
            {
                why = $"skill {skill} >= QualityBuilder's 'ignore at skill' {ignoreAt}";
                return true;
            }

            QualityCategory desired = QBAccess.DesiredMinQuality(frame)
                ?? QBAccess.Setting(qb, "defaultMinQualitySetting", QualityCategory.Awful);
            bool ok = QualityPredictor.CanReach(skill, desired);
            why = $"skill {skill}, wants >= {desired}, best possible {QualityPredictor.BestReachable(skill)} ({QualityPredictor.ActiveSource})";
            return ok;
        }

        private static readonly Dictionary<long, int> lastLog = new Dictionary<long, int>();

        public static void DebugLog(Pawn robot, Thing frame, bool result, string why)
        {
            if (!QBRMod.Settings.debugLog) return;
            long key = ((long)robot.thingIDNumber << 32) ^ (uint)(frame?.thingIDNumber ?? 0) ^ (result ? 1L << 62 : 0);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastLog.TryGetValue(key, out int last) && now - last < 2500) return;
            if (lastLog.Count > 512) lastLog.Clear();
            lastLog[key] = now;
            Log.Message($"[QualityBuilder Robots] {robot.LabelShort} -> {frame?.LabelShort ?? "?"}: {(result ? "ALLOWED" : "blocked")} ({why})");
        }
    }

    /// <summary>
    /// Optional fix: vanilla rolls the quality of anything built/crafted by a pawn with mechanoid flesh
    /// with the race's fixed mech skill (10). Misc. Robots' robots have mechanoid flesh, so every robot —
    /// from the basic to the top tier — built like a skill-10 pawn. With this on, the roll uses the robot's
    /// own skill from its def. Implemented by temporarily setting the race's fixed level for the duration
    /// of the call, so the original method (and every other mod's patch on it, e.g. Static Quality) runs unchanged.
    /// </summary>
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class Patch_RobotQualityUsesOwnSkill
    {
        static bool Prepare() => RobotAccess.Available;

        static void Prefix(Pawn pawn, SkillDef relevantSkill, out int __state)
        {
            __state = int.MinValue;
            if (!QBRMod.Settings.useRobotSkillForQuality || !RobotAccess.IsRobot(pawn) || !pawn.RaceProps.IsMechanoid) return;
            int own = RobotAccess.SkillLevel(pawn.def, relevantSkill);
            if (own < 0) return;
            __state = pawn.RaceProps.mechFixedSkillLevel;
            pawn.RaceProps.mechFixedSkillLevel = Math.Max(0, Math.Min(20, own));
        }

        static Exception Finalizer(Exception __exception, Pawn pawn, int __state)
        {
            if (__state != int.MinValue && pawn?.RaceProps != null)
                pawn.RaceProps.mechFixedSkillLevel = __state;
            return __exception;
        }
    }
}
