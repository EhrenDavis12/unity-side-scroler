using UnityEngine;

namespace Annie.SkeletonClawFx
{
    /// <summary>One figure played frame by frame at 16 fps from a 30 fps tick. Idle loops, attack and hurt are one-shots that return to idle. Never moves the body.</summary>
    public class ClawActor : MonoBehaviour
    {
        public Sprite[] idleBody, idleWeapon, attackBody, hurtBody, hurtWeapon;
        public bool mirror;
        public int idleStartCell;          // idle cell showing at tick 0
        public int returnCell = 0;         // idle cell shown on the first tick after a one-shot
        public int order = 100;

        public enum St { Idle, Attack, Hurt }
        public St state = St.Idle;
        int startTick, idleStart;
        int lastFrame = -1;
        public int Frame { get { return lastFrame; } }
        public int AttackFrame { get { return state == St.Attack ? lastFrame : -1; } }
        SpriteRenderer body, weapon;

        public void Init(Vector3 rootWorld)
        {
            transform.position = rootWorld;
            transform.localScale = new Vector3((mirror ? -1f : 1f) * 5.12f * ClawGeo.S, 5.12f * ClawGeo.S, 1f);
            var bg = new GameObject("body"); bg.transform.SetParent(transform, false);
            body = bg.AddComponent<SpriteRenderer>(); body.sortingOrder = order;
            if (idleWeapon != null && idleWeapon.Length > 0)
            {
                var wg = new GameObject("weapon"); wg.transform.SetParent(transform, false);
                weapon = wg.AddComponent<SpriteRenderer>(); weapon.sortingOrder = order + 1;
            }
            idleStart = 0;
            Show(CurrentIdle(0));
        }

        public void StartAttack(int t) { state = St.Attack; startTick = t; }
        public void StartHurt(int t) { state = St.Hurt; startTick = t; }

        int CurrentIdle(int t) { return (((int)((t - idleStart) * 16f / 30f) + idleStartCell) % idleBody.Length + idleBody.Length) % idleBody.Length; }

        void Show(int f)
        {
            Sprite[] b, w;
            if (state == St.Idle) { b = idleBody; w = idleWeapon; }
            else if (state == St.Attack) { b = attackBody; w = null; }
            else { b = hurtBody; w = hurtWeapon; }
            body.sprite = b[f];
            if (weapon != null) weapon.sprite = (w != null && w.Length > f) ? w[f] : null;
            lastFrame = f;
        }

        public void Tick(int t)
        {
            if (state != St.Idle)
            {
                int n = (state == St.Attack ? attackBody.Length : hurtBody.Length);
                int f = (int)((t - startTick) * 16f / 30f);
                if (f >= n) { state = St.Idle; idleStart = t; idleStartCell = returnCell; }
                else { Show(f); return; }
            }
            Show(CurrentIdle(t));
        }

        public string Label() { return state + " " + lastFrame; }
    }
}
