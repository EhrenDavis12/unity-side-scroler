using System;
using UnityEngine;

namespace Annie.ArrowMiss
{
    [Serializable]
    public class AmSheets { public Sprite[] idleBody, idleBow, attackBody, attackBow; }

    /// <summary>One Archer: installed body + bow sprites, played frame by frame at 16 fps from a 30 fps tick. Never moves the body. Same timing as the Archer-vs-Archer fighter.</summary>
    public class AmFighter : MonoBehaviour
    {
        public int side;
        public SpriteRenderer body, bow;
        public AmSheets sh;
        public enum St { Idle, Attack }
        public St state = St.Idle;
        public int startTick, idleStart, idleOff;
        public int lastFrame = -1;

        public void Init(AmSheets sheets, int side_, int idleOffset)
        {
            sh = sheets; side = side_;
            var feet = side == 0 ? AmGeo.ShooterFeet : AmGeo.TargetFeet;
            transform.position = AmGeo.SpriteRoot(feet, side);
            transform.localScale = new Vector3((side == 0 ? 1f : -1f) * 5.12f * AmGeo.S, 5.12f * AmGeo.S, 1f);
            var bg = new GameObject("body"); bg.transform.SetParent(transform, false); body = bg.AddComponent<SpriteRenderer>();
            var wg = new GameObject("bow"); wg.transform.SetParent(transform, false); bow = wg.AddComponent<SpriteRenderer>();
            body.sortingOrder = 100; bow.sortingOrder = 101;
            idleStart = 0; idleOff = idleOffset;
            Show(0);
        }

        public int IdleFrameAt(int tick) { return (((int)((tick - idleStart) * 16f / 30f) + idleOff) % 64 + 64) % 64; }
        public void StartAttack(int t) { state = St.Attack; startTick = t; }
        void ToIdle(int t) { state = St.Idle; idleStart = t; idleOff = 0; }

        void Show(int f)
        {
            if (state == St.Idle) { body.sprite = sh.idleBody[f]; bow.sprite = sh.idleBow[f]; }
            else { body.sprite = sh.attackBody[f]; bow.sprite = sh.attackBow[f]; }
            lastFrame = f;
        }

        public void Tick(int t)
        {
            if (state == St.Attack)
            {
                int f = (int)((t - startTick) * 16f / 30f);
                if (f >= 20) ToIdle(t); else { Show(f); return; }
            }
            Show(IdleFrameAt(t));
        }

        public string FrameLabel() { return state + " " + lastFrame; }
    }
}
