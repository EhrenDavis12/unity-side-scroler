using UnityEngine;

namespace Annie.SkeletonClawFx
{
    /// <summary>
    /// Cell-pixel maths for the skeleton cleaver job. Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre.
    /// The skeleton's attack cells are 768x591 with the sole of the foot at (172.5, 509.5) from the top-left; its idle cells are 512x512 with the sole at (172.5, 430.5).
    /// Head centroids are the cleaver head's opened blade mask, measured on the cells (geo.py head()).
    /// </summary>
    public static class ClawGeo
    {
        public const float S = 240f / 404f;              // both figures, front row
        public const float CellScale = 0.01f * S;         // world units per cell px
        public const float H = 2.37f;                     // the Archer's opaque height in world units (237 px)
        public const float FeetPxY = 1040f;
        public const float ArFeetCellX = 247f, ArFeetCellY = 431f;
        public const float SkPivotX = 172.5f, SkAttackPivotY = 509.5f;

        // Archer hurt frame 0 at the head row of contact, relative to the Archer's feet x (px): near (skeleton) side, far side, and the row
        public const float NearDX = -90f, FarDX = 47f, ContactPxY = 905f;

        public static readonly float[] HeadX = { 205.77f, 296.32f, 416.41f, 523.90f, 582.61f, 517.65f, 517.65f, 537.90f, 538.06f, 535.16f, 520.67f, 512.42f, 502.41f, 493.50f, 481.44f, 471.41f, 463.24f, 456.80f, 451.59f, 447.53f, 445.67f };
        public static readonly float[] HeadY = { 137.49f, 73.33f, 75.22f, 155.69f, 280.06f, 441.41f, 441.41f, 413.33f, 386.63f, 358.24f, 332.50f, 315.23f, 300.60f, 273.48f, 268.34f, 261.36f, 255.73f, 252.16f, 249.70f, 248.77f, 248.70f };

        public static Vector3 PxToWorld(float x, float y) { return new Vector3((x - 960f) / 100f, (540f - y) / 100f, 0f); }

        public static Vector3 SkCell(Vector2 cell, Vector3 feet)
        {
            return feet + new Vector3((cell.x - SkPivotX) * CellScale, (SkAttackPivotY - cell.y) * CellScale, 0f);
        }

        public static Vector3 Head(int f, Vector3 skFeet)
        {
            f = Mathf.Clamp(f, 0, 20);
            return SkCell(new Vector2(HeadX[f], HeadY[f]), skFeet);
        }

        /// <summary>The Archer's sprite root (bottom centre of the 512 cell) for feet at 'feet', mirrored to face left.</summary>
        public static Vector3 ArcherRoot(Vector3 feet)
        {
            return feet - new Vector3((ArFeetCellX - 256f) * CellScale * -1f, (512f - ArFeetCellY) * CellScale, 0f);
        }

        /// <summary>Catmull-Rom through three points, s in [0,2].</summary>
        public static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, float s)
        {
            s = Mathf.Clamp(s, 0f, 2f);
            int i = s >= 1f ? 1 : 0; float u = s - i;
            Vector3 a, b, c, d;
            if (i == 0) { a = 2f * p0 - p1; b = p0; c = p1; d = p2; }
            else { a = p0; b = p1; c = p2; d = 2f * p2 - p1; }
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * b) + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u2 + (-a + 3f * b - 3f * c + d) * u3);
        }
    }
}
