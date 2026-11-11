using UnityEngine;

namespace Annie.ArcherVsArcher
{
    /// <summary>Formation B3fix2 and the cell-pixel maths. Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre.</summary>
    public static class AvaGeo
    {
        public static readonly float[] RowH = { 240, 226, 212, 198, 184 };
        public static readonly float[] FeetY = { 1040, 901, 770, 647, 532 };
        public static readonly float[] ColX = { 170, 430, 690 };
        public const float FeetCellX = 247, FeetCellY = 431, SrcH = 404;
        public static readonly Vector2 ChestCell = new Vector2(300, 238);
        public static readonly Vector2 GripCell = new Vector2(389, 210);
        public const float ArrowLen = 205f; // cell px, nock to tip
        public const float ArrowSpeed = 2000f / 30f / 100f; // world units per tick

        public static float Scale(int row) { return RowH[row] / SrcH; }
        public static float K(int row) { return RowH[row] / RowH[0]; }

        /// <summary>Slot index 0..14: rows front to back, columns nearest the centre first.</summary>
        public static void SlotRC(int slot, out int row, out int col) { row = slot / 3; col = 2 - slot % 3; }

        public static Vector2 FeetPx(int side, int row, int col)
        {
            float x = ColX[col];
            if (row == 1 || row == 3) x += 30;
            if (side == 1) x = 1920f - x;
            return new Vector2(x, FeetY[row]);
        }

        public static Vector3 PxToWorld(Vector2 p) { return new Vector3((p.x - 960f) / 100f, (540f - p.y) / 100f, 0); }

        public static Vector3 FeetWorld(int side, int slot)
        {
            int r, c; SlotRC(slot, out r, out c);
            return PxToWorld(FeetPx(side, r, c));
        }

        public static Vector3 CellToWorld(Vector2 cell, Vector3 feet, float s, int side)
        {
            float sx = side == 0 ? 1f : -1f;
            return feet + new Vector3((cell.x - FeetCellX) * 0.01f * s * sx, (FeetCellY - cell.y) * 0.01f * s, 0);
        }

        /// <summary>Position of the sprite object (pivot bottom-centre of the 512 cell) so the feet point lands on feet.</summary>
        public static Vector3 SpriteRoot(Vector3 feet, float s, int side)
        {
            float sx = side == 0 ? 1f : -1f;
            return feet - new Vector3((FeetCellX - 256f) * 0.01f * s * sx, (512f - FeetCellY) * 0.01f * s, 0);
        }

        public static void ShotPoints(int sSide, int sSlot, int tSide, int tSlot, out Vector3 grip, out Vector3 chest, out float sScale, out float tK, out int flightTicks)
        {
            int sr, sc, tr, tc; SlotRC(sSlot, out sr, out sc); SlotRC(tSlot, out tr, out tc);
            sScale = Scale(sr);
            float tS = Scale(tr); tK = K(tr);
            grip = CellToWorld(GripCell, FeetWorld(sSide, sSlot), sScale, sSide);
            chest = CellToWorld(ChestCell, FeetWorld(tSide, tSlot), tS, tSide);
            float len = ArrowLen * 0.01f * sScale;
            float d = Vector2.Distance(grip, chest);
            flightTicks = Mathf.Max(1, Mathf.CeilToInt((d - len * 0.5f) / ArrowSpeed));
        }
    }
}
