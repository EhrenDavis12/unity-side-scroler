using System;
using UnityEngine;

namespace Annie.ArcherVsArcher
{
    [Serializable]
    public class AvaSheets
    {
        public Sprite[] idleBody, idleBow, attackBody, attackBow, hurtBody, hurtBow;
    }

    /// <summary>One Archer: installed body + bow sprites, played frame by frame at 16 fps from a 30 fps tick. Never moves the body.</summary>
    public class AvaFighter : MonoBehaviour
    {
        public int side, slot, row, col;
        public float s;
        public SpriteRenderer body, bow;
        public AvaSheets sh;
        public enum St { Idle, Attack, Hurt }
        public St state = St.Idle;
        public int startTick, idleStart, idleOff;
        public int lastFrame = -1;

        public void Init(AvaSheets sheets, int side_, int slot_, int idleOffset)
        {
            sh = sheets; side = side_; slot = slot_;
            AvaGeo.SlotRC(slot, out row, out col);
            s = AvaGeo.Scale(row);
            var feet = AvaGeo.FeetWorld(side, slot);
            transform.position = AvaGeo.SpriteRoot(feet, s, side);
            transform.localScale = new Vector3((side == 0 ? 1f : -1f) * 5.12f * s, 5.12f * s, 1f);
            var bg = new GameObject("body"); bg.transform.SetParent(transform, false);
            body = bg.AddComponent<SpriteRenderer>();
            var wg = new GameObject("bow"); wg.transform.SetParent(transform, false);
            bow = wg.AddComponent<SpriteRenderer>();
            body.sortingOrder = 100 - row * 10; bow.sortingOrder = body.sortingOrder + 1;
            idleStart = 0; idleOff = idleOffset;
            Show(0, Idle(0));
        }

        public int IdleFrameAt(int tick) { return (((int)((tick - idleStart) * 16f / 30f) + idleOff) % 64 + 64) % 64; }
        Sprite[] Idle(int dummy) { return sh.idleBody; }

        public void StartAttack(int t) { state = St.Attack; startTick = t; }
        public void StartHurt(int t) { state = St.Hurt; startTick = t; }
        void ToIdle(int t) { state = St.Idle; idleStart = t; idleOff = 0; }

        void Show(int f, Sprite[] b)
        {
            // set both layers
            if (state == St.Idle) { body.sprite = sh.idleBody[f]; bow.sprite = sh.idleBow[f]; }
            else if (state == St.Attack) { body.sprite = sh.attackBody[f]; bow.sprite = sh.attackBow[f]; }
            else { body.sprite = sh.hurtBody[f]; bow.sprite = sh.hurtBow[f]; }
            lastFrame = f;
        }

        public void Tick(int t)
        {
            if (state == St.Attack || state == St.Hurt)
            {
                int f = (int)((t - startTick) * 16f / 30f);
                if (f >= 20) ToIdle(t); else { Show(f, null); return; }
            }
            Show(IdleFrameAt(t), null);
        }

        public string FrameLabel() { return state + " " + lastFrame; }
    }
}
