using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace QualityBuilderRobots
{
    /// <summary>
    /// Soft access to Misc. Robots (Haplo.Miscellaneous.Robots, also used by Misc. Robots++).
    /// Robots are pawns whose def is an <c>AIRobot.X2_ThingDef_AIRobot</c>; their skills and work
    /// types live on that def (the pawn's skill tracker is reset to these values every tick).
    /// </summary>
    public static class RobotAccess
    {
        public static readonly bool Available;
        private static readonly Type robotDefType;
        private static readonly FieldInfo fiRobotSkills;    // List<RobotSkills { skillDef, level }>
        private static readonly FieldInfo fiRobotWorkTypes; // List<RobotWorkTypes { workTypeDef }>

        private static readonly Dictionary<ThingDef, Dictionary<SkillDef, int>> skillCache = new Dictionary<ThingDef, Dictionary<SkillDef, int>>();
        private static readonly Dictionary<ThingDef, HashSet<WorkTypeDef>> workCache = new Dictionary<ThingDef, HashSet<WorkTypeDef>>();

        static RobotAccess()
        {
            robotDefType = AccessTools.TypeByName("AIRobot.X2_ThingDef_AIRobot");
            if (robotDefType == null) return;
            fiRobotSkills = AccessTools.Field(robotDefType, "robotSkills");
            fiRobotWorkTypes = AccessTools.Field(robotDefType, "robotWorkTypes");
            Available = fiRobotSkills != null && fiRobotWorkTypes != null;
        }

        public static bool IsRobotDef(ThingDef def) => Available && def != null && robotDefType.IsInstanceOfType(def);

        public static bool IsRobot(Pawn p) => p != null && IsRobotDef(p.def);

        public static IEnumerable<ThingDef> AllRobotDefs()
        {
            if (!Available) yield break;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (robotDefType.IsInstanceOfType(def)) yield return def;
        }

        /// <summary>The robot's skill level for <paramref name="skill"/> as defined on its def, or -1 if the def has none.</summary>
        public static int SkillLevel(ThingDef def, SkillDef skill)
        {
            if (!IsRobotDef(def) || skill == null) return -1;
            if (!skillCache.TryGetValue(def, out var map))
            {
                map = new Dictionary<SkillDef, int>();
                if (fiRobotSkills.GetValue(def) is IEnumerable list)
                {
                    foreach (object entry in list)
                    {
                        if (entry == null) continue;
                        var sd = Traverse.Create(entry).Field("skillDef").GetValue<SkillDef>();
                        int lvl = Traverse.Create(entry).Field("level").GetValue<int>();
                        if (sd != null) map[sd] = lvl;
                    }
                }
                skillCache[def] = map;
            }
            return map.TryGetValue(skill, out int level) ? level : -1;
        }

        public static bool HasWorkType(ThingDef def, WorkTypeDef wt)
        {
            if (!IsRobotDef(def) || wt == null) return false;
            if (!workCache.TryGetValue(def, out var set))
            {
                set = new HashSet<WorkTypeDef>();
                if (fiRobotWorkTypes.GetValue(def) is IEnumerable list)
                {
                    foreach (object entry in list)
                    {
                        if (entry == null) continue;
                        var w = Traverse.Create(entry).Field("workTypeDef").GetValue<WorkTypeDef>();
                        if (w != null) set.Add(w);
                    }
                }
                workCache[def] = set;
            }
            return set.Contains(wt);
        }

        /// <summary>
        /// The skill level the game actually feeds into the quality roll for this pawn.
        /// Vanilla uses the race's <c>mechFixedSkillLevel</c> (10) for anything with mechanoid flesh —
        /// which includes these robots — unless our "use the robot's own skill" fix is on.
        /// </summary>
        public static int EffectiveQualitySkill(Pawn pawn, ThingDef def, SkillDef skill)
        {
            RaceProperties race = pawn?.RaceProps ?? def?.race;
            if (race != null && race.IsMechanoid)
            {
                if (QBRMod.Settings.useRobotSkillForQuality)
                {
                    int own = SkillLevel(pawn?.def ?? def, skill);
                    if (own >= 0) return Clamp(own);
                }
                return Clamp(race.mechFixedSkillLevel);
            }
            if (pawn?.skills != null) return pawn.skills.GetSkill(skill).Level;
            int defLevel = SkillLevel(def, skill);
            return defLevel >= 0 ? Clamp(defLevel) : 0;
        }

        private static int Clamp(int v) => v < 0 ? 0 : (v > 20 ? 20 : v);
    }

    /// <summary>Soft access to QualityBuilder (hatti.qualitybuilder). Its settings class is internal.</summary>
    public static class QBAccess
    {
        public static readonly bool Available;
        public static readonly Type QbType;               // QualityBuilder.QualityBuilder
        public static readonly Type WorkGiverPatchType;   // QualityBuilder._WorkGiver_ConstructFinishFrames
        private static readonly MethodInfo miGetSettings; // QualityBuilderModSettings.getSettings(Map)
        private static readonly MethodInfo miGetComp;     // QualityBuilder.getCompQualityBuilder(Thing)
        private static readonly MethodInfo miHasDesignation;
        private static readonly MethodInfo miPawnCanConstruct;

        static QBAccess()
        {
            QbType = AccessTools.TypeByName("QualityBuilder.QualityBuilder");
            WorkGiverPatchType = AccessTools.TypeByName("QualityBuilder._WorkGiver_ConstructFinishFrames");
            Type settingsType = AccessTools.TypeByName("QualityBuilder.QualityBuilderModSettings");
            if (QbType == null || WorkGiverPatchType == null || settingsType == null) return;
            miGetSettings = AccessTools.Method(settingsType, "getSettings", new[] { typeof(Map) });
            miGetComp = AccessTools.Method(QbType, "getCompQualityBuilder", new[] { typeof(Thing) });
            miHasDesignation = AccessTools.Method(QbType, "hasDesignation", new[] { typeof(Thing) });
            miPawnCanConstruct = AccessTools.Method(QbType, "pawnCanConstruct", new[] { typeof(Pawn) });
            Available = miGetSettings != null && miGetComp != null && miHasDesignation != null && miPawnCanConstruct != null;
        }

        public static object Settings(Map map) => miGetSettings.Invoke(null, new object[] { map });

        public static T Setting<T>(object settings, string property, T fallback)
        {
            if (settings == null) return fallback;
            try { return Traverse.Create(settings).Property(property).GetValue<T>(); }
            catch { return fallback; }
        }

        public static bool HasDesignation(Thing t) => t != null && (bool)miHasDesignation.Invoke(null, new object[] { t });

        public static bool PawnCanConstruct(Pawn p) => p != null && (bool)miPawnCanConstruct.Invoke(null, new object[] { p });

        /// <summary>The minimum quality the player asked for on this frame/blueprint, or null if it has no QB comp.</summary>
        public static QualityCategory? DesiredMinQuality(Thing t)
        {
            if (t == null) return null;
            object comp = miGetComp.Invoke(null, new object[] { t });
            if (comp == null) return null;
            return Traverse.Create(comp).Property("desiredMinQuality").GetValue<QualityCategory>();
        }
    }

    /// <summary>
    /// Soft access to Static Quality (vis.StaticQuality). All of its settings are public static fields
    /// on <c>StaticQualityPlus.Settings</c>, so we always read the live values the player chose.
    /// </summary>
    public static class StaticQualityAccess
    {
        public static readonly bool Available;
        private static readonly Type settingsType;

        static StaticQualityAccess()
        {
            settingsType = AccessTools.TypeByName("StaticQualityPlus.Settings");
            Available = settingsType != null && AccessTools.Field(settingsType, "QualitySwitch") != null;
        }

        private static T Get<T>(string field, T fallback)
        {
            if (!Available) return fallback;
            try { return (T)AccessTools.Field(settingsType, field).GetValue(null); }
            catch { return fallback; }
        }

        /// <summary>0 = vanilla, 1 = exact, 2 = ±1, 3 = ±2, 4 = cheat.</summary>
        public static int Mode => Get("QualitySwitch", 0);
        public static bool LegendaryRequiresInspiration => Get("LegendaryRequiresInspiration", true);

        /// <summary>Skill thresholds for Poor, Normal, Good, Excellent, Masterwork, Legendary.</summary>
        public static int[] Thresholds => new[]
        {
            Get("PoorThreshold", 3),
            Get("NormalThreshold", 7),
            Get("GoodThreshold", 11),
            Get("ExcellentThreshold", 15),
            Get("MasterworkThreshold", 18),
            Get("LegendaryThreshold", 20),
        };
    }
}
