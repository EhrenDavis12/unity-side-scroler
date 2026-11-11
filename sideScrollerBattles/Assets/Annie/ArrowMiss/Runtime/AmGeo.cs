using UnityEngine;

namespace Annie.ArrowMiss
{
    /// <summary>Cell-pixel maths for the arrow-miss job (the Archer-vs-Archer formation, front row only). Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre.</summary>
    public static class AmGeo
    {
        public const float S = 240f / 404f;                 // front row scale (cell px to screen px)
        public const float FeetCellX = 247f, FeetCellY = 431f;
        public const float GripCellX = 389f, GripCellY = 210f;
        public const float ArrowLenCell = 205f;             // cell px, nock to tip
        public const float Speed = 2000f / 30f;             // screen px per tick, the approved arrow speed
        public static readonly Vector2 ShooterFeet = new Vector2(690f, 1040f);
        public static readonly Vector2 TargetFeet = new Vector2(1230f, 1040f);

        public static Vector3 PxToWorld(double x, double y) { return new Vector3((float)((x - 960.0) / 100.0), (float)((540.0 - y) / 100.0), 0f); }
        public static Vector3 PxToWorld(Vector2 p) { return PxToWorld(p.x, p.y); }

        public static Vector2 GripPx { get { return new Vector2(ShooterFeet.x + (GripCellX - FeetCellX) * S, ShooterFeet.y - (FeetCellY - GripCellY) * S); } }

        /// <summary>Sprite root (pivot bottom-centre of the 512 cell) so the feet point lands on feet.</summary>
        public static Vector3 SpriteRoot(Vector2 feetPx, int side)
        {
            float sx = side == 0 ? 1f : -1f;
            var feet = PxToWorld(feetPx);
            return feet - new Vector3((FeetCellX - 256f) * 0.01f * S * sx, (512f - FeetCellY) * 0.01f * S, 0f);
        }
    }

    /// <summary>A parabola through the grip, travelled at constant arc speed. Image px, y down.</summary>
    public class AmPath
    {
        public double x0, y0, tan, k;
        public AmPath(double x0_, double y0_, double thetaDeg, double k_) { x0 = x0_; y0 = y0_; tan = System.Math.Tan(thetaDeg * System.Math.PI / 180.0); k = k_; }

        double Slope(double X) { return -tan + 2.0 * k * X; }

        /// <summary>Centre of the arrow after arc length s: position and unit direction of travel.</summary>
        public void Pose(double s, out double cx, out double cy, out double ux, out double uy)
        {
            double X = 0, acc = 0; const double dx = 0.05;
            while (true)
            {
                double sl = Slope(X + dx * 0.5); double ds = dx * System.Math.Sqrt(1 + sl * sl);
                if (acc + ds >= s) { X += dx * (s - acc) / ds; break; }
                acc += ds; X += dx;
            }
            double sl2 = Slope(X); double h = System.Math.Sqrt(1 + sl2 * sl2);
            ux = 1.0 / h; uy = sl2 / h; cx = x0 + X; cy = y0 - tan * X + k * X * X;
        }
    }
}
