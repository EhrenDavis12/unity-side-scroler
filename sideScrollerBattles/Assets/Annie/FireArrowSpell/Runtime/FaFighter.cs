using UnityEngine;

namespace Annie.FireArrowSpell
{
    /// <summary>The Archer target (installed idle and hit sprites, mirrored to face left). Never moves the body.</summary>
    public class FaFighter : MonoBehaviour
    {
        public SpriteRenderer body, bow;
        public FaSheets sh;
        public bool hurt;
        public int startTick, idleStart, idleOff, lastFrame = -1;

        public void Init(FaSheets sheets, int idleOffset)
        {
            sh = sheets;
            transform.position = FaGeo.TargetRoot();
            transform.localScale = new Vector3(-5.12f * FaGeo.S, 5.12f * FaGeo.S, 1f);
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
                int f = FaGeo.Hurt(t, startTick);
                if (f >= FaGeo.HitFrames) { hurt = false; idleStart = t; idleOff = 0; }
                else { Show(f); return; }
            }
            Show(IdleFrameAt(t));
        }
        public string FrameLabel() { return (hurt ? "Hurt " : "Idle ") + lastFrame; }
    }
}
