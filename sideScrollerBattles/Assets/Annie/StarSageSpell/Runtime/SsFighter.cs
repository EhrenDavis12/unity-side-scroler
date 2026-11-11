using System;
using UnityEngine;

namespace Annie.StarSageSpell
{
    [Serializable] public class SsSheets { public Sprite[] idleBody, idleBow, hurtBody, hurtBow; }

    /// <summary>The Archer target, mirrored to face left: installed idle and hit sprites at 16 fps from a 30 fps tick. Never moves the body.</summary>
    public class SsFighter : MonoBehaviour
    {
        public SpriteRenderer body, bow;
        public SsSheets sh;
        public bool hurt;
        public int startTick, idleStart, idleOff, lastFrame = -1;

        public void Init(SsSheets sheets, int idleOffset)
        {
            sh = sheets;
            transform.position = SsGeo.TargetRoot();
            transform.localScale = new Vector3(-5.12f * SsGeo.S, 5.12f * SsGeo.S, 1f);
            var bg = new GameObject("body"); bg.transform.SetParent(transform, false); body = bg.AddComponent<SpriteRenderer>();
            var wg = new GameObject("bow"); wg.transform.SetParent(transform, false); bow = wg.AddComponent<SpriteRenderer>();
            body.sortingOrder = 100; bow.sortingOrder = 101;
            idleStart = 0; idleOff = idleOffset;
            Show(IdleFrameAt(0));
        }

        public int IdleFrameAt(int tick) { return (((int)((tick - idleStart) * 16f / 30f) + idleOff) % 64 + 64) % 64; }
        public void StartHurt(int t) { hurt = true; startTick = t; }

        void Show(int f)
        {
            if (!hurt) { body.sprite = sh.idleBody[f]; bow.sprite = sh.idleBow[f]; }
            else { body.sprite = sh.hurtBody[f]; bow.sprite = sh.hurtBow[f]; }
            lastFrame = f;
        }

        public void Tick(int t)
        {
            if (hurt)
            {
                int f = SsGeo.Hurt(t, startTick);
                if (f >= SsGeo.HitFrames) { hurt = false; idleStart = t; idleOff = 0; }
                else { Show(f); return; }
            }
            Show(IdleFrameAt(t));
        }

        public string FrameLabel() { return (hurt ? "Hurt " : "Idle ") + lastFrame; }
    }
}
