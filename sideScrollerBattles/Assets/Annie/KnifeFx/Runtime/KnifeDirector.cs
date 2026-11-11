using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.KnifeFx
{
    /// <summary>
    /// The knife test scene: one Assassin, one Archer, one effect. The tick is the number of frames the capture has already written, so capture frame n shows tick n.
    /// hit = the Archer plays its hurt sheet from contact; miss = the Archer stays in idle, further away.
    /// </summary>
    public class KnifeDirector : MonoBehaviour
    {
        public Sprite[] asIdle, asAttack, arIdleBody, arIdleBow, arHurtBody, arHurtBow;
        public KnifeEffect fx;
        public bool hit = true;
        public float assassinFeetPx = 780f, archerFeetPx = 932f;
        public int attackStart = 4;
        public int assassinOrder = 103, archerOrder = 100;

        public int tick = -1;
        KnifeActor assassin, archer;
        int contactTick = -1;
        readonly StringBuilder tl = new StringBuilder();

        void Start()
        {
            var asFeet = KnifeGeo.PxToWorld(new Vector2(assassinFeetPx, KnifeGeo.FeetPxY));
            var arFeet = KnifeGeo.PxToWorld(new Vector2(archerFeetPx, KnifeGeo.FeetPxY));

            var ag = new GameObject("Assassin"); ag.transform.SetParent(transform, false);
            assassin = ag.AddComponent<KnifeActor>();
            assassin.idleBody = asIdle; assassin.attackBody = asAttack; assassin.feetCellX = KnifeGeo.AsFeetCellX; assassin.order = assassinOrder;
            assassin.mirror = false; assassin.returnCell = 1;
            // idle shows cell 0 on the tick the attack starts (and cell 47 the tick before)
            assassin.idleStartCell = ((48 - (int)(attackStart * 16f / 30f)) % 48 + 48) % 48;
            assassin.Init(asFeet);

            // the Archer's idle is on frame 0 at contact (C = attackStart + 10, the first tick showing f5)
            int C = attackStart + Mathf.CeilToInt(5 * 30f / 16f);
            var rg = new GameObject("Archer"); rg.transform.SetParent(transform, false);
            archer = rg.AddComponent<KnifeActor>();
            archer.idleBody = arIdleBody; archer.idleWeapon = arIdleBow; archer.hurtBody = arHurtBody; archer.hurtWeapon = arHurtBow;
            archer.mirror = true; archer.feetCellX = KnifeGeo.ArFeetCellX; archer.order = archerOrder; archer.returnCell = 0;
            archer.idleStartCell = ((64 - (int)(C * 16f / 30f) % 64) % 64);
            archer.Init(arFeet);

            // far edge of the Archer's silhouette at the tip row (cell x 170 at y 219, idle 0), mirrored
            float farX = KnifeGeo.ArcherCell(new Vector2(170f, 219f), arFeet).x;
            fx.Setup(asFeet, arFeet, farX, archerOrder, assassinOrder);
        }

        void Update()
        {
            if (Time.captureFramerate == 0) return;
            var cap = GetComponent<AnnieCapture>();
            string fd = Path.Combine(cap.outDir, "frames");
            if (!Directory.Exists(fd)) return;
            tick = Directory.GetFiles(fd, "f*.png").Length;
            Step();
        }

        void Step()
        {
            int t = tick;
            if (t == attackStart) assassin.StartAttack(t);
            assassin.Tick(t);
            if (contactTick < 0 && assassin.AttackFrame == 5)
            {
                contactTick = t;
                if (hit) archer.StartHurt(t);
            }
            archer.Tick(t);
            fx.Tick(t, assassin.AttackFrame);
            tl.Append(t).Append(": assassin ").Append(assassin.Label()).Append(" | archer ").Append(archer.Label()).Append('\n');
        }

        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (cap != null && !string.IsNullOrEmpty(cap.outDir) && tick >= 0)
                File.WriteAllText(Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", fx.log) + "\n\n" + tl);
        }
    }
}
