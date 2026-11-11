using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.DamageText
{
    [Serializable] public class DtSheet { public string name; public float[] v; }
    [Serializable] public class DtHeadData { public DtSheet[] sheets; }

    /// <summary>Head-top table: per body sheet and cell, the topmost opaque row and the x of the head there (cell px, y down, unmirrored). Measured from the sheets by scripts/headtops.py.</summary>
    public class DtHeadTops
    {
        readonly Dictionary<string, float[]> d = new Dictionary<string, float[]>();
        public DtHeadTops(string json) { foreach (var s in JsonUtility.FromJson<DtHeadData>(json).sheets) d[s.name] = s.v; }
        /// <summary>(x, top) in cell px.</summary>
        public Vector2 Get(string sheet, int cell) { var v = d[sheet]; return new Vector2(v[cell * 2 + 1], v[cell * 2]); }
    }

    [Serializable] public class DtGlyphM { public string name; public float advance, bearing_x, bearing_top, w, h; }
    [Serializable] public class DtMetricsData { public float dcap, wcap; public DtGlyphM[] glyphs; }

    /// <summary>One sprite set. chibi sets lay glyphs side by side by sprite width; the game set by advance and bearing (metrics json, display px).</summary>
    [Serializable]
    public class DtSet
    {
        public string name;                       // chibi, chibi_half, game
        public Sprite[] red, green;               // chibi: 10 each; game: 30 each, index digit*3+variant
        public Sprite[] plus;                     // chibi: 1; game: 3 variants
        public Sprite miss, blocked;
        public TextAsset metrics;                 // game only
        public float plusRaise = 6f;              // display px the '+' sits above the digit centre (chibi)
        public float baseline = 8f;               // game: display px from the head top to the baseline
        public bool ByAdvance { get { return metrics != null; } }
    }

    /// <summary>One pooled pop: a root with up to 5 glyph sprites. State lives here, so a newer pop that claims the slot silently ends the stale one.</summary>
    public class DtPopSlot : MonoBehaviour
    {
        public SpriteRenderer[] glyphs;
        [NonSerialized] public int popId = -1;
        [NonSerialized] public int spawnTick;
        [NonSerialized] public Vector3 start, dir;     // world
        [NonSerialized] public float dist, tilt, k;    // dist in world units, signed final tilt deg, world scale per display px *100
        [NonSerialized] public int used;
    }

    /// <summary>Floating combat text. Four layers (Damage, Heal, Miss, Blocked), a pool of 32 slots each. Every glyph is a baked PNG sprite.</summary>
    public class DtPops : MonoBehaviour
    {
        public Transform[] layerRoots;                 // 0 Damage, 1 Heal, 2 Miss, 3 Blocked
        public DtSet[] sets;                           // chibi, chibi_half, game
        public string fontSet = "chibi_half";
        public float unitsPerPx = 0.01f;               // world units per screen px (A: 0.01, B: 1)
        public int fps = 30;
        public int seed = 1;
        public int orderBase = 31000;                  // sortingOrder is a short: 31000 + kind*400 + slot*10 + glyph tops out at 32514 (kind*1000 overflowed past 32767 and drew Miss and Blocked behind the scene)
        public string logPath = "";

        public const float Life = 0.90f, TiltMin = 6f, TiltMax = 12f, DistMin = 110f, DistMax = 150f, ConeDeg = 35f, MinTurnDeg = 12f;
        public const float GlyphPxPerSprite = 0.5f;    // baked at 2x

        [Serializable] public class Rec { public int id, kind, tick, slot; public string text, glyphNames; public float angleDeg, distPx, tiltDeg; public Vector2 headTopWorld, startWorld; public string target; public bool slotWasBusy; }
        [Serializable] class Log { public int seed, fps; public string fontSet; public float unitsPerPx; public List<Rec> pops = new List<Rec>(); }
        readonly Log log = new Log();

        DtPopSlot[][] slots; int[] claimed; System.Random rng, vrng; int popCounter;
        DtSet set; readonly Dictionary<string, DtGlyphM> gm = new Dictionary<string, DtGlyphM>();
        readonly Dictionary<string, float> lastDir = new Dictionary<string, float>();
        bool ready;

        void Init()
        {
            if (ready) return;
            ready = true;
            slots = new DtPopSlot[layerRoots.Length][]; claimed = new int[layerRoots.Length];
            for (int i = 0; i < layerRoots.Length; i++)
            {
                slots[i] = layerRoots[i].GetComponentsInChildren<DtPopSlot>(true);
                foreach (var s in slots[i]) Hide(s);
            }
            rng = new System.Random(seed); vrng = new System.Random(seed + 7919);   // variants draw from their own stream: the drift stays the same in every set
            set = null; foreach (var st in sets) if (st.name == fontSet) set = st;
            if (set == null) throw new System.Exception("no font set " + fontSet);
            gm.Clear();
            if (set.ByAdvance) foreach (var g in JsonUtility.FromJson<DtMetricsData>(set.metrics.text).glyphs) gm[g.name] = g;
            log.fontSet = fontSet; log.seed = seed; log.fps = fps; log.unitsPerPx = unitsPerPx;
        }

        public void Configure(int fps_, int seed_, float unitsPerPx_, string fontSet_ = "chibi_half") { fps = fps_; seed = seed_; unitsPerPx = unitsPerPx_; fontSet = fontSet_; ready = false; Init(); }

        static void Hide(DtPopSlot s) { foreach (var g in s.glyphs) g.enabled = false; s.popId = -1; }

        Sprite[] Glyphs(int kind, string text)
        {
            if (kind == 2) return new[] { set.miss };
            if (kind == 3) return new[] { set.blocked };
            var list = new List<Sprite>();
            int vbase = set.ByAdvance ? vrng.Next(3) : 0;
            var seen = new Dictionary<char, int>();
            foreach (char c in text)
            {
                int k; seen.TryGetValue(c, out k); seen[c] = k + 1;
                int v = (vbase + k) % 3;                           // a repeated digit cycles its variants, so "22" is not twins
                if (c == '+') list.Add(set.ByAdvance ? set.plus[v] : set.plus[0]);
                else
                {
                    var arr = kind == 1 ? set.green : set.red;
                    list.Add(set.ByAdvance ? arr[(c - '0') * 3 + v] : arr[c - '0']);
                }
            }
            return list.ToArray();
        }

        /// <summary>Starts a pop at 'tick'. headTop is the world point of the target's head top. Returns the pop id, or -1 when the layer is switched off.</summary>
        public int Spawn(int kind, string text, Vector3 headTop, string target, int tick)
        {
            Init();
            if (!layerRoots[kind].gameObject.activeInHierarchy) return -1;
            int slotIdx = claimed[kind] % slots[kind].Length; claimed[kind]++;
            var slot = slots[kind][slotIdx];
            bool busy = slot.popId >= 0;
            var sp = Glyphs(kind, text);
            // direction: random within the cone, at least MinTurnDeg from this target's last one
            float a; int tries = 0;
            do { a = (float)((rng.NextDouble() * 2.0 - 1.0) * ConeDeg); tries++; }
            while (lastDir.ContainsKey(target) && Mathf.Abs(a - lastDir[target]) < MinTurnDeg && tries < 200);
            lastDir[target] = a;
            float distPx = (float)(DistMin + rng.NextDouble() * (DistMax - DistMin));
            float tiltMag = (float)(TiltMin + rng.NextDouble() * (TiltMax - TiltMin));
            float h; float startUp;
            if (!set.ByAdvance)
            {
                // chibi: glyph canvases side by side; '+' sits plusRaise display px higher than the digit centre
                float total = 0; foreach (var s0 in sp) total += s0.rect.width * GlyphPxPerSprite;
                float x = -total / 2f; h = sp[0].rect.height * GlyphPxPerSprite; startUp = h / 2f + 2f;
                for (int i = 0; i < slot.glyphs.Length; i++)
                {
                    var g = slot.glyphs[i];
                    if (i >= sp.Length) { g.enabled = false; continue; }
                    float w = sp[i].rect.width * GlyphPxPerSprite;
                    g.sprite = sp[i]; g.color = Color.white;
                    float y = (kind <= 1 && sp[i] == set.plus[0]) ? set.plusRaise : 0f;
                    g.transform.localPosition = new Vector3((x + w / 2f) * 0.01f, y * 0.01f, 0f);
                    g.sortingOrder = orderBase + kind * 400 + slotIdx * 10 + i;
                    g.enabled = true;
                    x += w;
                }
            }
            else
            {
                // game: pen advance + bearing, one shared baseline; the pivot sits half a cap above it
                float cap = kind <= 1 ? 14f : 9f; startUp = set.baseline + cap; h = cap * 2f;
                float total = 0; var ms = new DtGlyphM[sp.Length];
                for (int i = 0; i < sp.Length; i++) { ms[i] = gm[sp[i].name]; total += ms[i].advance; }
                float pen = -total / 2f;
                for (int i = 0; i < slot.glyphs.Length; i++)
                {
                    var g = slot.glyphs[i];
                    if (i >= sp.Length) { g.enabled = false; continue; }
                    var m = ms[i];
                    g.sprite = sp[i]; g.color = Color.white;
                    float cx = pen + m.bearing_x + m.w / 2f, cy = -cap + m.bearing_top - m.h / 2f;
                    g.transform.localPosition = new Vector3(cx * 0.01f, cy * 0.01f, 0f);
                    g.sortingOrder = orderBase + kind * 400 + slotIdx * 10 + i;
                    g.enabled = true;
                    pen += m.advance;
                }
            }
            var names = new StringBuilder(); foreach (var s1 in sp) names.Append(s1.name).Append(' ');
            slot.used = sp.Length;
            slot.popId = popCounter;
            slot.spawnTick = tick;
            float sgn = a >= 0 ? 1f : -1f;
            slot.dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad), 0f);
            slot.dist = distPx * unitsPerPx;
            slot.tilt = -sgn * tiltMag;                         // leans the way it drifts (right drift = clockwise)
            slot.k = unitsPerPx * 100f;
            slot.start = headTop + new Vector3(0f, startUp * unitsPerPx, 0f);
            log.pops.Add(new Rec { id = popCounter, kind = kind, tick = tick, slot = slotIdx, text = text, glyphNames = names.ToString().Trim(), angleDeg = a, distPx = distPx, tiltDeg = slot.tilt, headTopWorld = headTop, startWorld = slot.start, target = target, slotWasBusy = busy });
            return popCounter++;
        }

        static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1f - (1f - u) * (1f - u); }
        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        public static float ScaleAt(float age)
        {
            if (age < 0.10f) return Mathf.Lerp(0.35f, 1.20f, EaseOut(age / 0.10f));
            if (age < 0.21f) return Mathf.Lerp(1.20f, 1.00f, Smooth((age - 0.10f) / 0.11f));
            return 1f;
        }
        public static float AlphaAt(float age) { return age <= 0.50f ? 1f : Mathf.Clamp01(1f - (age - 0.50f) / 0.40f); }

        /// <summary>Poses every live pop for this tick.</summary>
        public void Tick(int tick)
        {
            Init();
            foreach (var layer in slots)
                foreach (var s in layer)
                {
                    if (s.popId < 0) continue;
                    float age = (tick - s.spawnTick) / (float)fps;
                    if (age < 0f || age >= Life - 1e-4f) { Hide(s); continue; }
                    float e = EaseOut(age / Life);
                    s.transform.position = s.start + s.dir * (s.dist * e);
                    s.transform.rotation = Quaternion.Euler(0f, 0f, s.tilt * e);
                    s.transform.localScale = Vector3.one * (ScaleAt(age) * s.k);
                    var c = new Color(1f, 1f, 1f, AlphaAt(age));
                    for (int i = 0; i < s.used; i++) { s.glyphs[i].color = c; s.glyphs[i].enabled = true; }
                }
        }

        void OnDestroy()
        {
            if (!string.IsNullOrEmpty(logPath) && log.pops.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.WriteAllText(logPath, JsonUtility.ToJson(log, true));
            }
        }
    }
}
