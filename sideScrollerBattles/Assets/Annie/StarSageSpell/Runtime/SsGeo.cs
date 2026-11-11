using UnityEngine;

namespace Annie.StarSageSpell
{
    /// <summary>Screen-pixel maths for the Star Sage spell. Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre.</summary>
    public static class SsGeo
    {
        public const float S = 240f / 404f;                       // Archer front-row scale (cell px to screen px)
        public const float FeetCellX = 247f, FeetCellY = 431f;
        public const float CasterScale = 218f / 804f;             // Star Sage opaque height 804 px in the still -> the Archer's 218 px
        public const float StillFeetX = 424f, StillFeetY = 862f;  // feet of the 1024 still (bottom opaque row + 1, x mean of the hem rows)
        public const float OrbStillX = 781.6f, OrbStillY = 169.6f; // orb_centre (488.5, 106) x 1.6
        public static readonly Vector2 CasterFeet = new Vector2(430f, 1040f);
        public static readonly Vector2 TargetFeet = new Vector2(1490f, 1040f);
        public const float TargetEdgeX = 1570f;                    // just inside the target's far (right) edge; the back shards start here, behind the body

        public static Vector3 PxToWorld(double x, double y) { return new Vector3((float)((x - 960.0) / 100.0), (float)((540.0 - y) / 100.0), 0f); }
        public static Vector3 PxToWorld(Vector2 p) { return PxToWorld(p.x, p.y); }

        public static Vector2 Orb { get { return new Vector2(CasterFeet.x + (OrbStillX - StillFeetX) * CasterScale, CasterFeet.y - (StillFeetY - OrbStillY) * CasterScale); } }
        public static Vector2 CasterCentre { get { return new Vector2(CasterFeet.x + (512f - StillFeetX) * CasterScale, CasterFeet.y - (StillFeetY - 512f) * CasterScale); } }
        public static Vector2 Chest { get { return new Vector2(TargetFeet.x - (300f - FeetCellX) * S, TargetFeet.y - (FeetCellY - 238f) * S); } }

        /// <summary>Sprite root (pivot bottom-centre of the 512 cell) so the feet point lands on feet; mirrored (side 1).</summary>
        public static Vector3 TargetRoot()
        {
            var feet = PxToWorld(TargetFeet);
            return feet - new Vector3((FeetCellX - 256f) * 0.01f * S * -1f, (512f - FeetCellY) * 0.01f * S, 0f);
        }

        public const int C1 = 15, CastFrames = 12, FlightFrames = 12, HitFrames = 20;
        public static int Hurt(int tick, int start) { return (int)((tick - start) * 16f / 30f); }

        public class Plan { public int C1, I1, C2, I2, hurtEnd1, frames; public int idleOff; }

        /// <summary>Two spells. The target's idle phase is chosen so it is on frame 0 at I1; the second impact waits for frame 63, 0 or 1 after the hit returns to idle.</summary>
        public static Plan MakePlan()
        {
            var p = new Plan { C1 = C1 };
            p.I1 = C1 + CastFrames + FlightFrames;
            p.idleOff = (64 - (int)(p.I1 * 16f / 30f) % 64) % 64;
            int t = p.I1 + 1; while (Hurt(t, p.I1) < HitFrames) t++;
            p.hurtEnd1 = t;
            int i2 = p.I1 + 50;
            for (; ; i2++)
            {
                int fr = (((int)((i2 - p.hurtEnd1) * 16f / 30f)) % 64 + 64) % 64;
                if (fr == 63 || fr == 0 || fr == 1) break;
            }
            p.I2 = i2; p.C2 = i2 - CastFrames - FlightFrames;
            p.frames = p.I2 + 42;
            return p;
        }
    }
}
