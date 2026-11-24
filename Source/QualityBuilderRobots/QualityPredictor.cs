using System;
using RimWorld;

namespace QualityBuilderRobots
{
    /// <summary>
    /// Answers "which qualities can a builder with skill N produce at all?" — using exactly the same
    /// rules as the quality roll that will happen later. Robots are never inspired and have no
    /// ideoligion role, so neither inspiration (+2) nor production-specialist bonuses apply.
    /// </summary>
    public static class QualityPredictor
    {
        public enum Source { Vanilla, StaticExact, StaticPlusMinus1, StaticPlusMinus2, StaticCheat }

        public static Source ActiveSource
        {
            get
            {
                if (!StaticQualityAccess.Available) return Source.Vanilla;
                switch (StaticQualityAccess.Mode)
                {
                    case 1: return Source.StaticExact;
                    case 2: return Source.StaticPlusMinus1;
                    case 3: return Source.StaticPlusMinus2;
                    case 4: return Source.StaticCheat;
                    default: return Source.Vanilla;
                }
            }
        }

        /// <summary>True if a robot with this effective skill can roll at least <paramref name="desired"/>.</summary>
        public static bool CanReach(int skill, QualityCategory desired)
        {
            if (ActiveSource == Source.Vanilla)
                return ChanceAtLeast(skill, desired) * 100f >= QBRMod.Settings.vanillaMinChancePercent - 0.0001f;
            return (int)desired <= (int)BestReachable(skill);
        }

        /// <summary>Highest quality the roll can produce for this skill (Static Quality modes; vanilla: highest with chance &gt; 0).</summary>
        public static QualityCategory BestReachable(int skill)
        {
            Source src = ActiveSource;
            if (src == Source.Vanilla) return QualityCategory.Masterwork; // vanilla roll is clamped to 0..5 without inspiration

            bool legendaryNeedsInspiration = StaticQualityAccess.LegendaryRequiresInspiration;
            int cap = legendaryNeedsInspiration ? 5 : 6;
            if (src == Source.StaticCheat) return (QualityCategory)cap;

            int baseQ = StaticBase(skill);
            int spread = src == Source.StaticPlusMinus2 ? 2 : (src == Source.StaticPlusMinus1 ? 1 : 0);
            return (QualityCategory)Math.Min(baseQ + spread, cap);
        }

        /// <summary>Lowest quality the roll can produce (shown in the settings table only).</summary>
        public static QualityCategory WorstReachable(int skill)
        {
            Source src = ActiveSource;
            if (src == Source.Vanilla) return QualityCategory.Awful;
            bool legendaryNeedsInspiration = StaticQualityAccess.LegendaryRequiresInspiration;
            if (src == Source.StaticCheat) return (QualityCategory)(legendaryNeedsInspiration ? 5 : 6);
            int spread = src == Source.StaticPlusMinus2 ? 2 : (src == Source.StaticPlusMinus1 ? 1 : 0);
            int q = Math.Max(0, StaticBase(skill) - spread);
            return (QualityCategory)Math.Min(q, legendaryNeedsInspiration ? 5 : 6);
        }

        // Static Quality: the quality a skill maps to before the ± spread (uninspired).
        private static int StaticBase(int skill)
        {
            int[] t = StaticQualityAccess.Thresholds;
            int q = 0;
            for (int i = 0; i < t.Length; i++)
                if (skill >= t[i]) q = i + 1;
            if (q >= 6 && StaticQualityAccess.LegendaryRequiresInspiration) q = 5;
            return q;
        }

        // ---------------------------------------------------------------------------------------
        // Vanilla: QualityUtility.GenerateQualityCreatedByPawn(int, bool)
        //   value = (int)GaussianAsymmetric(center, 0.6, 0.8), clamped 0..5;
        //   if value == 5, 50 % chance to re-roll with upper width 0.95.
        // ---------------------------------------------------------------------------------------
        private static readonly float[] VanillaCenter =
        {
            0.7f, 1.1f, 1.5f, 1.8f, 2f, 2.2f, 2.4f, 2.6f, 2.8f, 2.95f, 3.1f,
            3.25f, 3.4f, 3.5f, 3.6f, 3.7f, 3.8f, 3.9f, 4f, 4.1f, 4.2f,
        };

        /// <summary>Probability (0..1) that an uninspired vanilla roll at this skill is at least <paramref name="desired"/>.</summary>
        public static float ChanceAtLeast(int skill, QualityCategory desired)
        {
            int k = (int)desired;
            if (k <= 0) return 1f;
            if (k >= 6) return 0f;
            float c = VanillaCenter[Math.Max(0, Math.Min(20, skill))];
            double first5 = Tail(5, c, 0.8);       // first roll lands on Masterwork
            double firstK = Tail(k, c, 0.8);       // first roll at least k
            double reroll = Tail(k, c, 0.95);      // re-roll at least k
            double p = (firstK - first5) + first5 * (0.5 + 0.5 * reroll);
            return (float)Math.Max(0, Math.Min(1, p));
        }

        // P(X >= x) for an asymmetric normal with lower width 0.6 and the given upper width.
        private static double Tail(int x, float center, double upperWidth)
        {
            if (x >= center) return 0.5 * Erfc((x - center) / (upperWidth * Math.Sqrt(2)));
            return 1 - 0.5 * Erfc((center - x) / (0.6 * Math.Sqrt(2)));
        }

        // Complementary error function (Numerical Recipes erfcc, |error| < 1.2e-7).
        private static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1 / (1 + 0.5 * z);
            double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                       t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                       t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? r : 2 - r;
        }
    }
}
