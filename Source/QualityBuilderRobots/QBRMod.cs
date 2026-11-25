using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace QualityBuilderRobots
{
    public class QBRSettings : ModSettings
    {
        public bool enabled = true;
        public bool useRobotSkillForQuality = true;
        public float vanillaMinChancePercent = 5f;
        public bool respectBestConstructorOverride = true;
        public bool respectExcludeMechs = false;
        public bool debugLog = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref useRobotSkillForQuality, "useRobotSkillForQuality", true);
            Scribe_Values.Look(ref vanillaMinChancePercent, "vanillaMinChancePercent", 5f);
            Scribe_Values.Look(ref respectBestConstructorOverride, "respectBestConstructorOverride", true);
            Scribe_Values.Look(ref respectExcludeMechs, "respectExcludeMechs", false);
            Scribe_Values.Look(ref debugLog, "debugLog", false);
        }
    }

    public class QBRMod : Mod
    {
        public static QBRSettings Settings;
        private Vector2 scroll;
        private float viewHeight = 900f;

        public QBRMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<QBRSettings>();
            new Harmony("juliusberger00.qualitybuilderrobots").PatchAll(typeof(QBRMod).Assembly);
            Log.Message($"[QualityBuilder Robots] loaded — QualityBuilder: {(QBAccess.Available ? "found" : "MISSING")}, " +
                        $"Misc. Robots: {(RobotAccess.Available ? "found" : "MISSING")}, " +
                        $"Static Quality: {(StaticQualityAccess.Available ? "found" : "not installed (vanilla quality rules)")}");
        }

        public override string SettingsCategory() => "QBR.SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect view = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            if (!QBAccess.Available || !RobotAccess.Available)
            {
                GUI.color = ColorLibrary.RedReadable;
                l.Label("QBR.MissingMods".Translate((QBAccess.Available ? "QBR.Found" : "QBR.Missing").Translate(), (RobotAccess.Available ? "QBR.Found" : "QBR.Missing").Translate()));
                GUI.color = Color.white;
                l.GapLine();
            }

            l.CheckboxLabeled("QBR.Enabled".Translate(), ref Settings.enabled, "QBR.Enabled.Tip".Translate());
            l.CheckboxLabeled("QBR.UseRobotSkill".Translate(), ref Settings.useRobotSkillForQuality, "QBR.UseRobotSkill.Tip".Translate());
            l.CheckboxLabeled("QBR.RespectOverride".Translate(), ref Settings.respectBestConstructorOverride, "QBR.RespectOverride.Tip".Translate());
            l.CheckboxLabeled("QBR.RespectExcludeMechs".Translate(), ref Settings.respectExcludeMechs, "QBR.RespectExcludeMechs.Tip".Translate());
            l.CheckboxLabeled("QBR.DebugLog".Translate(), ref Settings.debugLog, "QBR.DebugLog.Tip".Translate());

            l.GapLine();
            Text.Font = GameFont.Medium;
            l.Label("QBR.RulesHeader".Translate());
            Text.Font = GameFont.Small;
            l.Label(DescribeSource());

            if (QualityPredictor.ActiveSource == QualityPredictor.Source.Vanilla)
            {
                l.Gap(4f);
                l.Label("QBR.VanillaChance".Translate(Settings.vanillaMinChancePercent.ToString("0")), tooltip: "QBR.VanillaChance.Tip".Translate());
                Settings.vanillaMinChancePercent = Mathf.Round(l.Slider(Settings.vanillaMinChancePercent, 1f, 50f));
            }

            l.GapLine();
            Text.Font = GameFont.Medium;
            l.Label("QBR.TableHeader".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            l.Label("QBR.TableNote".Translate());
            GUI.color = Color.white;
            DrawRobotTable(l);

            viewHeight = l.CurHeight + 40f;
            l.End();
            Widgets.EndScrollView();
        }

        private static string DescribeSource()
        {
            switch (QualityPredictor.ActiveSource)
            {
                case QualityPredictor.Source.StaticExact: return "QBR.Source.Exact".Translate(Thresholds(), Legendary());
                case QualityPredictor.Source.StaticPlusMinus1: return "QBR.Source.PM1".Translate(Thresholds(), Legendary());
                case QualityPredictor.Source.StaticPlusMinus2: return "QBR.Source.PM2".Translate(Thresholds(), Legendary());
                case QualityPredictor.Source.StaticCheat: return "QBR.Source.Cheat".Translate(Legendary());
                default:
                    return StaticQualityAccess.Available ? "QBR.Source.VanillaViaSQ".Translate() : "QBR.Source.Vanilla".Translate();
            }
        }

        private static string Thresholds()
        {
            int[] t = StaticQualityAccess.Thresholds;
            string[] names = { "QualityCategory_Poor", "QualityCategory_Normal", "QualityCategory_Good", "QualityCategory_Excellent", "QualityCategory_Masterwork", "QualityCategory_Legendary" };
            return string.Join(", ", names.Select((n, i) => n.Translate() + " " + t[i]));
        }

        private static string Legendary()
            => StaticQualityAccess.LegendaryRequiresInspiration ? "QBR.LegendaryNeedsInspiration".Translate().ToString() : "";

        private static void DrawRobotTable(Listing_Standard l)
        {
            List<ThingDef> robots = RobotAccess.AllRobotDefs()
                .Where(d => RobotAccess.HasWorkType(d, WorkTypeDefOf.Construction))
                .OrderBy(d => RobotAccess.EffectiveQualitySkill(null, d, SkillDefOf.Construction))
                .ThenBy(d => d.label)
                .ToList();
            if (robots.Count == 0)
            {
                l.Label("QBR.NoRobots".Translate());
                return;
            }

            bool vanilla = QualityPredictor.ActiveSource == QualityPredictor.Source.Vanilla;
            var qualities = new[] { QualityCategory.Awful, QualityCategory.Poor, QualityCategory.Normal, QualityCategory.Good, QualityCategory.Excellent, QualityCategory.Masterwork, QualityCategory.Legendary };

            const float rowH = 24f;
            float nameW = 200f, skillW = 50f;
            Rect header = l.GetRect(rowH);
            float colW = (header.width - nameW - skillW) / qualities.Length;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(header.x, header.y, nameW, rowH), "QBR.Col.Robot".Translate());
            Widgets.Label(new Rect(header.x + nameW, header.y, skillW, rowH), "QBR.Col.Skill".Translate());
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < qualities.Length; i++)
                Widgets.Label(new Rect(header.x + nameW + skillW + i * colW, header.y, colW, rowH), qualities[i].GetLabelShort());
            Text.Font = GameFont.Small;

            foreach (ThingDef def in robots)
            {
                Rect row = l.GetRect(rowH);
                Widgets.DrawHighlightIfMouseover(row);
                int skill = RobotAccess.EffectiveQualitySkill(null, def, SkillDefOf.Construction);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x, row.y, nameW, rowH), def.LabelCap.Truncate(nameW - 4f));
                Widgets.Label(new Rect(row.x + nameW, row.y, skillW, rowH), skill.ToString());
                Text.Anchor = TextAnchor.MiddleCenter;
                for (int i = 0; i < qualities.Length; i++)
                {
                    bool ok = QualityPredictor.CanReach(skill, qualities[i]);
                    string cell = ok ? "OK" : "–";
                    if (vanilla) cell = (QualityPredictor.ChanceAtLeast(skill, qualities[i]) * 100f).ToString("0.#") + "%";
                    GUI.color = ok ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);
                    Widgets.Label(new Rect(row.x + nameW + skillW + i * colW, row.y, colW, rowH), cell);
                }
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
