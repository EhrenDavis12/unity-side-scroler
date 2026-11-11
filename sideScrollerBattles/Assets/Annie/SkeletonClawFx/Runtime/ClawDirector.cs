using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.SkeletonClawFx
{
    /// <summary>
    /// The skeleton cleaver test scene: one skeleton (left, unflipped), one Archer (right, flipped), one effect. The tick is the number of frames the capture
    /// has already written, so capture frame n shows tick n. hit = the Archer plays its hurt sheet from contact (attack frame 4); miss = it stays in idle, further away.
    /// </summary>
    public class ClawDirector : MonoBehaviour
    {
        public Sprite[] skIdle, skAttack, arIdleBody, arIdleBow, arHurtBody, arHurtBow;
        public ClawEffect fx;
        public bool hit = true;
        public float skeletonFeetPx = 740f, archerFeetPx = 1042f;
        public int attackStart = 4;
        public int skeletonOrder = 103, archerOrder = 100;

        public int tick = -1;
        ClawActor skeleton, archer;
        int contactTick = -1, lastRun = -1;
        readonly StringBuilder tl = new StringBuilder();

        void Start()
        {
            var skFeet = ClawGeo.PxToWorld(skeletonFeetPx, ClawGeo.FeetPxY);
            var arFeet = ClawGeo.PxToWorld(archerFeetPx, ClawGeo.FeetPxY);

            var sg = new GameObject("Skeleton"); sg.transform.SetParent(transform, false);
            skeleton = sg.AddComponent<ClawActor>();
            skeleton.idleBody = skIdle; skeleton.attackBody = skAttack; skeleton.order = skeletonOrder; skeleton.mirror = false; skeleton.returnCell = 0;
            // idle shows cell 0 on the tick the attack starts (cell 46 on ticks 0-1, 47 on 2-3)
            skeleton.idleStartCell = ((skIdle.Length - (int)(attackStart * 16f / 30f)) % skIdle.Length + skIdle.Length) % skIdle.Length;
            skeleton.Init(skFeet);

            // the Archer's idle is on frame 0 at contact (C = attackStart + 8, the first tick showing attack frame 4)
            int C = attackStart + Mathf.CeilToInt(4 * 30f / 16f);
            var rg = new GameObject("Archer"); rg.transform.SetParent(transform, false);
            archer = rg.AddComponent<ClawActor>();
            archer.idleBody = arIdleBody; archer.idleWeapon = arIdleBow; archer.hurtBody = arHurtBody; archer.hurtWeapon = arHurtBow;
            archer.mirror = true; archer.order = archerOrder; archer.returnCell = 0;
            archer.idleStartCell = (arIdleBody.Length - (int)(C * 16f / 30f) % arIdleBody.Length) % arIdleBody.Length;
            archer.Init(ClawGeo.ArcherRoot(arFeet));

            fx.Setup(skFeet, arFeet, archerFeetPx, archerOrder, skeletonOrder);
        }

        void Update()
        {
            if (Time.captureFramerate == 0) return;
            var cap = GetComponent<AnnieCapture>();
            string fd = Path.Combine(cap.outDir, "frames");
            if (!Directory.Exists(fd)) return;
            tick = Directory.GetFiles(fd, "f*.png").Length;
            if (tick == lastRun) return;
            lastRun = tick;
            Step();
        }

        void Step()
        {
            int t = tick;
            if (t == attackStart) skeleton.StartAttack(t);
            skeleton.Tick(t);
            if (contactTick < 0 && skeleton.AttackFrame == 4)
            {
                contactTick = t;
                if (hit) archer.StartHurt(t);
            }
            archer.Tick(t);
            fx.Tick(t, skeleton.AttackFrame);
            tl.Append(t).Append(": skeleton ").Append(skeleton.Label()).Append(" | archer ").Append(archer.Label()).Append('\n');
        }

        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (cap != null && !string.IsNullOrEmpty(cap.outDir) && tick >= 0)
                File.WriteAllText(Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", fx.log) + "\n\n" + tl);
        }
    }
}
