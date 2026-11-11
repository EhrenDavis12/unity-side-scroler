using UnityEngine;

namespace Annie.KnifeFx
{
    /// <summary>Cell-pixel maths for the knife job. Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre. Front row, H = 2.4 units.</summary>
    public static class KnifeGeo
    {
        public const float S = 240f / 404f;            // both figures, front row
        public const float H = 2.4f;                   // front-row figure height in world units
        public const float FeetPxY = 1040f;
        public const float AsFeetCellX = 258f, ArFeetCellX = 247f, FeetCellY = 431f;

        // Assassin attack sheet, upper-arm blade, cell px (y down). Measured from the sheet's own pixels.
        public static readonly float[] TipX = { 268, 268, 394, 449, 471, 473, 473, 480, 442, 442, 438, 423, 419, 417, 413, 411, 409, 407, 407, 407 };
        public static readonly float[] TipY = { 308, 308, 234, 213, 212, 219, 219, 225, 211, 209, 212, 211, 211, 211, 211, 212, 212, 213, 213, 213 };
        public static readonly float[] BaseX = { 201.5f, 201.5f, 327f, 382.3f, 405.1f, 408.7f, 408.7f, 415.1f, 387f, 388f, 384.2f, 370.1f, 366.1f, 364.1f, 360.1f, 359f, 356.1f, 354.3f, 353.8f, 354.1f };
        public static readonly float[] BaseY = { 285.3f, 285.3f, 243.1f, 224.8f, 220.1f, 221.9f, 221.9f, 227.1f, 216.5f, 214.6f, 216.4f, 215.4f, 215.8f, 216f, 216.4f, 216.7f, 216.4f, 217.5f, 218f, 217.8f };
        public static readonly Vector2 RearBootCell = new Vector2(84f, 426f);   // rear boot on f2, where it slides back

        public static Vector3 PxToWorld(Vector2 p) { return new Vector3((p.x - 960f) / 100f, (540f - p.y) / 100f, 0f); }

        public static Vector3 AssassinCell(Vector2 cell, Vector3 feet)
        {
            return feet + new Vector3((cell.x - AsFeetCellX) * 0.01f * S, (FeetCellY - cell.y) * 0.01f * S, 0f);
        }

        public static Vector3 ArcherCell(Vector2 cell, Vector3 feet)
        {
            return feet + new Vector3(-(cell.x - ArFeetCellX) * 0.01f * S, (FeetCellY - cell.y) * 0.01f * S, 0f);
        }

        public static int Fi(int f) { return Mathf.Clamp(f, 0, 19); }
        public static Vector2 TipCell(int f) { f = Fi(f); return new Vector2(TipX[f], TipY[f]); }
        /// <summary>Blade direction, unit vector in world axes (y up).</summary>
        public static Vector3 BladeDir(int f)
        {
            f = Fi(f);
            var d = new Vector3(TipX[f] - BaseX[f], -(TipY[f] - BaseY[f]), 0f);
            return d.normalized;
        }
        public static float BladeAngle(int f) { var d = BladeDir(f); return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; }

        /// <summary>Sprite root (bottom centre of the 512 cell) for a figure whose feet point sits at 'feet'.</summary>
        public static Vector3 SpriteRoot(Vector3 feet, float feetCellX, bool mirror)
        {
            float sx = mirror ? -1f : 1f;
            return feet - new Vector3((feetCellX - 256f) * 0.01f * S * sx, (512f - FeetCellY) * 0.01f * S, 0f);
        }
    }
}
