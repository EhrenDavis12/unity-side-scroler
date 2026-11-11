using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Annie.EnemyDeath
{
    /// <summary>
    /// One-shot death by disintegration for any enemy. Play(SpriteRenderer[] targets, int seed) reads the frame each renderer shows now,
    /// builds one seeded order map over their world bounds ("when does this cell go"), and from then on the body is cut by a shader that
    /// samples that map by world position while the flecks are born on exactly the pixels the cut just removed. No size is stored:
    /// everything scales from the measured body height. Alpha only: no pixel of the body ever moves.
    /// </summary>
    public class EnemyDeath : MonoBehaviour
    {
        [Header("Wiring")]
        public AnnieLayers layers;
        public GameObject coreLayer, edgeLayer, frontLayer, backLayer, lingerLayer;
        public ParticleSystem frontPs, backPs, lingerPs;
        public Material dissolveTemplate;
        public bool autoStep = true;   // false when a director steps it by ticks

        [Header("Reference numbers: front-row figure, body 218 px tall at 100 px per world unit")]
        public float refBodyHeight = 2.18f;
        public float holdSeconds = 0.2333f;       // from Play (the frame the hold pose first shows) to the first cut
        public float sweepSeconds = 1.4f;
        public float clearAfterSweep = 0.725f;    // last fleck and linger mote are gone this long after the sweep ends
        public float leanDegrees = 15f;
        // torn edge: four octaves of seeded noise, wavelength and amplitude as fractions of the body's height (long waves tilt, short ones bite)
        public float[] tearWave = { 0.55f, 0.22f, 0.09f, 0.035f };
        public float[] tearAmp = { 0.17f, 0.10f, 0.05f, 0.02f };
        public float feetCalmFrom = 0.62f, feetCalmTo = 0.95f, feetCalm = 0.75f;   // the noise fades toward the feet so they go last
        public float thinBias = 0.08f, thinNear = 0.012f, thinFar = 0.03f;        // thin outlying parts go sooner (bias and thickness as fractions of height)
        public float islandBlend = 0.6f;                                           // 0 = an island goes at the moment it would detach, 1 = its own noise time
        public float openRadius = 0.025f;                                           // slivers narrower than twice this go with the cut (fraction of height)
        public float feetBand = 0.12f;                                              // the bottom of the body is where the order is anchored
        public float rimPx = 3.5f;                                                  // rim width at k = 1, scaled by k like every other size
        public float easePower = 1.35f;           // slight ease-in
        public int mapLongSide = 128;

        public int frontCount = 350, backCount = 150, lingerCount = 25, maxPerDeath = 2000;
        public Vector2 frontSizePx = new Vector2(3f, 7f), backSizePx = new Vector2(2f, 4f), lingerSizePx = new Vector2(3f, 6f);
        public Vector2 lifeSeconds = new Vector2(0.7f, 1.3f);
        public Vector2 risePx = new Vector2(120f, 240f), driftPx = new Vector2(40f, 120f);
        public Vector2 lingerLifeSeconds = new Vector2(0.45f, 0.72f), lingerRisePx = new Vector2(50f, 120f), lingerDriftPx = new Vector2(15f, 50f);
        public float backTravelFraction = 0.6f;
        public float liftToPale = 0.35f;
        public float lightFloorV = 0.93f, lightMaxS = 0.45f, ashFloorV = 0.85f;   // after the lift, brightness is floored (hue kept) so flecks read lighter than the ground
        public float shardQuadPerVisible = 1.4f;  // the shard cell is not full: quad size over the visible shard
        public Color pale = new Color(1f, 0.95f, 0.8f, 1f);
        public Color rimColor = new Color(1f, 0.9f, 0.62f, 1f);
        public Color ashColor = new Color(0.78f, 0.75f, 0.70f, 1f);

        // ---- run state
        SpriteRenderer[] targets; Material[] origMats; Material mat;
        Vector2[] pxPos; Color32[] pxCol; float[] pxT; int N;
        float[] map; int nx, ny; float cell; Vector2 mapMin; Texture2D mapTex;
        float clock; bool playing; int facing; int seedUsed; System.Random rngF, rngB, rngL;
        float bodyH, k, sweepDur, clearAfter, sizeGrow;
        int cntFront, cntBack, cntLinger, emF, emB, emL, idxPrev;
        float xc, yTop;
        public float Progress { get; private set; }
        public int CutCount { get { return idxPrev; } }
        public int OpaqueCount { get { return N; } }
        public float BodyHeight { get { return bodyH; } }
        public float SweepSeconds { get { return sweepDur; } }
        public bool Playing { get { return playing; } }
        public int Facing { get { return facing; } }
        public int LogTag;
        public readonly StringBuilder EmitLog = new StringBuilder();
        public string Summary = "";

        void Update()
        {
            if (autoStep && playing) Step(Time.deltaTime);
        }

        void OnDisable() { Restore(); }

        /// <summary>Restore every target's material exactly.</summary>
        public void Restore()
        {
            if (targets != null && origMats != null)
                for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].sharedMaterial = origMats[i];
            if (mat != null) Kill(mat);
            if (mapTex != null) Kill(mapTex);
            mat = null; mapTex = null; playing = false;
        }

        /// <summary>Fire it again on the same targets with the same seed.</summary>
        public void Replay()
        {
            if (targets == null) return;
            var t = targets; int s = seedUsed; int f = facing;
            Play(t, s, f);
        }

        public void Play(SpriteRenderer[] t, int seed, int facingSign = 0)
        {
            Restore();
            targets = t; seedUsed = seed; rngF = new System.Random(seed * 7919 + 13); rngB = new System.Random(seed * 7919 + 977); rngL = new System.Random(seed * 7919 + 4421);   // one stream per layer: a layer off removes only itself
            facing = facingSign != 0 ? (facingSign > 0 ? 1 : -1) : (t != null && t.Length > 0 && t[0] != null && (t[0].transform.lossyScale.x < 0) != t[0].flipX ? -1 : 1);
            EmitLog.Length = 0; emF = emB = emL = 0; idxPrev = 0; clock = 0; Progress = -1f;

            // 1. read what each renderer shows now
            var pos = new List<Vector2>(); var col = new List<Color32>();
            bool any = false; Bounds ub = new Bounds(); float tallest = 0;
            origMats = new Material[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                origMats[i] = t[i] != null ? t[i].sharedMaterial : null;
                if (t[i] == null || t[i].sprite == null) continue;
                int before = pos.Count; float yMin, yMax;
                ReadSprite(t[i], pos, col, out yMin, out yMax);
                if (pos.Count > before) tallest = Mathf.Max(tallest, yMax - yMin);
                if (!any) { ub = t[i].bounds; any = true; } else ub.Encapsulate(t[i].bounds);
            }
            if (!any || pos.Count == 0) { Debug.LogWarning("EnemyDeath.Play: nothing opaque to disintegrate"); return; }
            bodyH = tallest;   // the tallest single target: bow plus body is one body, a bow held high does not make it taller
            float xmin = 1e9f, xmax = -1e9f, ymin = 1e9f, ymax = -1e9f;
            foreach (var p in pos) { xmin = Mathf.Min(xmin, p.x); xmax = Mathf.Max(xmax, p.x); ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            xc = 0.5f * (xmin + xmax); yTop = ymax;
            float unionH = ymax - ymin;
            k = Mathf.Sqrt(bodyH / refBodyHeight);
            sweepDur = sweepSeconds * k; clearAfter = clearAfterSweep * k;

            // 2. one seeded order map over the union of the targets' world bounds
            cell = Mathf.Max(ub.size.x, ub.size.y) / mapLongSide;
            nx = Mathf.CeilToInt(ub.size.x / cell) + 1; ny = Mathf.CeilToInt(ub.size.y / cell) + 1;
            mapMin = new Vector2(ub.min.x - 0.5f * cell, ub.min.y - 0.5f * cell);
            map = new float[nx * ny];
            BuildField(pos, unionH, seed);
            // normalise over the body's own pixels so the first pixel goes at 0.005 and the last at 0.995
            float rmin = 1e9f, rmax = -1e9f;
            for (int q = 0; q < pos.Count; q++) { float v = Sample(pos[q].x, pos[q].y); rmin = Mathf.Min(rmin, v); rmax = Mathf.Max(rmax, v); }
            float sc = 0.99f / Mathf.Max(1e-6f, rmax - rmin);
            for (int q = 0; q < map.Length; q++) map[q] = 0.005f + (map[q] - rmin) * sc;

            // 3. the body's pixels, sorted by when they go
            N = pos.Count; var key = new float[N]; var idx = new int[N];
            for (int q = 0; q < N; q++) { key[q] = Sample(pos[q].x, pos[q].y); idx[q] = q; }
            Array.Sort(key, idx);
            pxPos = new Vector2[N]; pxCol = new Color32[N]; pxT = key;
            for (int q = 0; q < N; q++) { pxPos[q] = pos[idx[q]]; pxCol[q] = col[idx[q]]; }

            // 4. counts and sizes from the measured height
            float cnt = bodyH / refBodyHeight;
            cntFront = Mathf.RoundToInt(frontCount * cnt); cntBack = Mathf.RoundToInt(backCount * cnt); cntLinger = Mathf.RoundToInt(lingerCount * cnt);
            int total = cntFront + cntBack + cntLinger; sizeGrow = 1f;
            if (total > maxPerDeath)
            {
                float f = maxPerDeath / (float)total;
                cntFront = Mathf.RoundToInt(cntFront * f); cntBack = Mathf.RoundToInt(cntBack * f); cntLinger = Mathf.RoundToInt(cntLinger * f);
                sizeGrow = Mathf.Sqrt(1f / f);
            }

            // 5. material, map texture, sort orders
            mapTex = new Texture2D(nx, ny, TextureFormat.RHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "EnemyDeathOrderMap" };
            var cols = new Color[nx * ny]; for (int q = 0; q < cols.Length; q++) cols[q] = new Color(map[q], 0, 0, 1);
            mapTex.SetPixels(cols); mapTex.Apply(false, false);
            mat = new Material(dissolveTemplate); mat.name = "EnemyDeathDissolve (run)";
            mat.SetTexture("_Map", mapTex); mat.SetVector("_MapMin", new Vector4(mapMin.x, mapMin.y, 0, 0));
            mat.SetVector("_MapInv", new Vector4(1f / (nx * cell), 1f / (ny * cell), 0, 0));
            mat.SetFloat("_MapCell", cell); mat.SetFloat("_Progress", -1f); mat.SetFloat("_Rim", 0f); mat.SetColor("_RimColor", rimColor);
            int oMin = int.MaxValue, oMax = int.MinValue;
            foreach (var r in t) { if (r == null) continue; oMin = Mathf.Min(oMin, r.sortingOrder); oMax = Mathf.Max(oMax, r.sortingOrder); r.sharedMaterial = mat; }
            SetOrder(backPs, oMin - 1); SetOrder(frontPs, oMax + 2); SetOrder(lingerPs, oMax + 3);
            foreach (var ps in new[] { frontPs, backPs, lingerPs })
                if (ps != null) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(); ps.Pause(); }
            playing = true;
            Summary = "N=" + N + " bodyH=" + bodyH.ToString("F3") + " unionH=" + unionH.ToString("F3") + " k=" + k.ToString("F4") + " sweep=" + sweepDur.ToString("F3") +
                      " clearAfter=" + clearAfter.ToString("F3") + " front=" + cntFront + " back=" + cntBack + " linger=" + cntLinger + " grow=" + sizeGrow.ToString("F3") +
                      " facing=" + facing + " map=" + nx + "x" + ny + " cell=" + cell.ToString("F4") +
                      " orders back/body/front/linger=" + (oMin - 1) + "/" + oMin + ".." + oMax + "/" + (oMax + 2) + "/" + (oMax + 3);
        }

        static void Kill(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        static void SetOrder(ParticleSystem ps, int o) { if (ps != null) ps.GetComponent<ParticleSystemRenderer>().sortingOrder = o; }

        /// <summary>Advance the effect by dt seconds (a director calls this once per tick).</summary>
        public void Step(float dt)
        {
            if (!playing) return;
            clock += dt;
            float s = (clock - holdSeconds) / sweepDur;
            float pEff = -1f;
            if (s > 0f)
            {
                float p = Mathf.Pow(Mathf.Min(s, 1f), easePower);
                pEff = s >= 1f ? 2f : p * 1.02f;
            }
            Progress = pEff;
            bool coreOn = coreLayer == null || coreLayer.activeInHierarchy;
            bool edgeOn = edgeLayer == null || edgeLayer.activeInHierarchy;
            float rim = (coreOn && edgeOn && s > 0f && s < 1f) ? rimPx * k / 100f * Mathf.Clamp01(s * 25f) : 0f;
            mat.SetFloat("_Progress", coreOn ? pEff : -1f);
            mat.SetFloat("_Rim", rim);

            int idxNow = UpperBound(pxT, pEff);
            if (idxNow > idxPrev && pEff < 1.5f)
            {
                bool fOn = frontLayer == null || frontLayer.activeInHierarchy, bOn = backLayer == null || backLayer.activeInHierarchy, lOn = lingerLayer == null || lingerLayer.activeInHierarchy;
                int dueF = (int)((long)cntFront * idxNow / N), dueB = (int)((long)cntBack * idxNow / N);
                int ls = (int)(0.55f * N);
                int dueL = idxNow > ls ? (int)((long)cntLinger * (idxNow - ls) / Math.Max(1, N - ls)) : 0;
                for (; emF < dueF; emF++) if (fOn) EmitOne(rngF, frontPs, 'F', frontSizePx, lifeSeconds, risePx, driftPx, 1f, 1f, false, idxPrev, idxNow);
                for (; emB < dueB; emB++) if (bOn) EmitOne(rngB, backPs, 'B', backSizePx, lifeSeconds, risePx, driftPx, backTravelFraction, 0.85f, false, idxPrev, idxNow);
                for (; emL < dueL; emL++) if (lOn) EmitOne(rngL, lingerPs, 'L', lingerSizePx, lingerLifeSeconds, lingerRisePx, lingerDriftPx, 1f, 0.6f, true, idxPrev, idxNow);
                if (!fOn) emF = dueF; if (!bOn) emB = dueB; if (!lOn) emL = dueL;
            }
            idxPrev = idxNow;
            foreach (var ps in new[] { frontPs, backPs, lingerPs })
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                if (!ps.isPaused) ps.Pause();
                ps.Simulate(dt, false, false, false);
            }
        }

        void EmitOne(System.Random rng, ParticleSystem ps, char group, Vector2 sizePx, Vector2 life, Vector2 rise, Vector2 drift, float travel, float alpha, bool ash, int lo, int hi)
        {
            if (ps == null) return;
            // born on the edge the cut is on now: of up to eight pixels the cut removed this tick, take the one nearest the edge (world distance from the order's isoline)
            int q = -1; float bd = 1e9f;
            for (int tr = 0; tr < 8; tr++)
            {
                int qq = Mathf.Clamp(rng.Next(lo, Math.Max(hi, lo + 1)), 0, N - 1);
                float dd = EdgeDistance(qq);
                if (dd < bd) { bd = dd; q = qq; }
                if (bd <= 0.8f * cell) break;
            }
            float u1 = (float)rng.NextDouble(), u2 = (float)rng.NextDouble(), u3 = (float)rng.NextDouble(), u4 = (float)rng.NextDouble(), u5 = (float)rng.NextDouble(), u6 = (float)rng.NextDouble();
            float born = clock, endAt = holdSeconds + sweepDur + clearAfter;
            float L = Mathf.Min(Mathf.Lerp(life.x, life.y, u1) * k, endAt - born);
            L = Mathf.Max(L, 0.12f);
            float rs = Mathf.Lerp(rise.x, rise.y, u2) * k / 100f * travel, dr = Mathf.Lerp(drift.x, drift.y, u3) * k / 100f * travel;
            Vector2 jitter = new Vector2((u4 - 0.5f), (u5 - 0.5f)) * (cell * 0.5f);
            Vector2 p0 = pxPos[q] + jitter;
            Color32 c32 = pxCol[q];
            Color c = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 1f);
            c = ash ? Color.Lerp(c, ashColor, 0.6f) : Color.Lerp(c, pale, liftToPale);
            float hh, ss, vv; Color.RGBToHSV(c, out hh, out ss, out vv);
            c = Color.HSVToRGB(hh, Mathf.Min(ss, lightMaxS), Mathf.Max(vv, ash ? ashFloorV : lightFloorV));
            c.a = alpha;
            var ep = new ParticleSystem.EmitParams();
            ep.position = new Vector3(p0.x, p0.y, 0f);
            ep.velocity = new Vector3(-facing * dr / L, rs / L, 0f);
            ep.startLifetime = L;
            ep.startSize = Mathf.Lerp(sizePx.x, sizePx.y, u6) * k / 100f * shardQuadPerVisible * sizeGrow;
            ep.startColor = (Color32)c;
            ep.rotation = u5 * 360f;
            ep.angularVelocity = (u4 - 0.5f) * 180f;
            ps.Emit(ep, 1);
            EmitLog.Append(LogTag).Append(' ').Append(group).Append(' ').Append(p0.x.ToString("F4")).Append(' ').Append(p0.y.ToString("F4")).Append(' ')
                   .Append(pxT[q].ToString("F5")).Append(' ').Append(Progress.ToString("F5")).Append(' ').Append(q).Append(' ').Append(L.ToString("F3")).Append(' ')
                   .Append(c32.r).Append(' ').Append(c32.g).Append(' ').Append(c32.b).Append(' ').Append(((Color32)c).r).Append(' ').Append(((Color32)c).g).Append(' ').Append(((Color32)c).b).Append(' ')
                   .Append((-facing * dr * 100f).ToString("F1")).Append(' ').Append((rs * 100f).ToString("F1")).Append(' ').Append((ep.startSize * 100f).ToString("F2")).Append('\n');
        }


        // ------------------------------------------------------------ the order field
        void BuildField(List<Vector2> pos, float H, int seed)
        {
            int n = nx * ny;
            float cs = Mathf.Cos(leanDegrees * Mathf.Deg2Rad), sn = Mathf.Sin(leanDegrees * Mathf.Deg2Rad);
            var occ = new bool[n];
            foreach (var p in pos)
            {
                int i = Mathf.Clamp((int)((p.x - mapMin.x) / cell), 0, nx - 1), j = Mathf.Clamp((int)((p.y - mapMin.y) / cell), 0, ny - 1);
                occ[j * nx + i] = true;
            }
            var occD = Dilate(occ, 2);
            // distance to the nearest empty cell, in world units (two-pass chamfer)
            var dist = new float[n];
            for (int q = 0; q < n; q++) dist[q] = occ[q] ? 1e6f : 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j0 = 0; j0 < ny; j0++) for (int i0 = 0; i0 < nx; i0++)
                {
                    int j = pass == 0 ? j0 : ny - 1 - j0, i = pass == 0 ? i0 : nx - 1 - i0, q = j * nx + i;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        if (pass == 0 ? (dj > 0 || (dj == 0 && di > 0)) : (dj < 0 || (dj == 0 && di < 0))) continue;
                        int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) { if (occ[q]) dist[q] = Mathf.Min(dist[q], 1f); continue; }
                        dist[q] = Mathf.Min(dist[q], dist[jj * nx + ii] + ((di != 0 && dj != 0) ? 1.4142f : 1f));
                    }
                }
            }
            // R: the lean plus torn noise (calmer toward the feet), thin outlying parts pulled earlier
            var R = new float[n];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                int q = j * nx + i;
                float wx = mapMin.x + (i + 0.5f) * cell - xc, wy = mapMin.y + (j + 0.5f) * cell;
                float v = Mathf.Clamp01((yTop - wy) / H);
                float calm = 1f - feetCalm * Smooth(feetCalmFrom, feetCalmTo, v);
                float nz = 0f;
                for (int o = 0; o < tearWave.Length; o++) nz += tearAmp[o] * H * Noise(wx / (tearWave[o] * H), wy / (tearWave[o] * H), seed * 31 + 1 + o);
                float u = (yTop - wy) * cs + wx * facing * sn;
                float thin = occ[q] ? 1f - Smooth(thinNear * H, thinFar * H, dist[q] * cell) : 0f;
                float awayFromFeet = 1f - Smooth(0.7f, 0.95f, v);
                R[q] = u + calm * nz - thin * thinBias * H * awayFromFeet;
            }
            // islands: every part is tied to the feet band. W = the best "weakest link" path from the feet; a part that would detach goes when its link goes
            var W = new float[n]; var done = new bool[n];
            var heap = new SortedSet<KeyValuePair<float, int>>(new MaxFirst());
            float feetY = yTop - (1f - feetBand) * H;
            for (int q = 0; q < n; q++) W[q] = occD[q] ? -1e9f : R[q];
            for (int q = 0; q < n; q++)
            {
                if (!occ[q]) continue;
                float wy = mapMin.y + ((q / nx) + 0.5f) * cell;
                if (wy <= feetY) { done[q] = false; heap.Add(new KeyValuePair<float, int>(R[q], q)); W[q] = R[q]; }
            }
            var reached = new bool[n]; for (int q = 0; q < n; q++) reached[q] = occD[q] && W[q] > -1e8f;
            while (heap.Count > 0)
            {
                var top = heap.Max; heap.Remove(top); int c = top.Value; if (done[c]) continue; done[c] = true;
                float b = W[c]; int ci = c % nx, cj = c / nx;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                {
                    int ii = ci + di, jj = cj + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                    int m = jj * nx + ii; if (!occD[m] || done[m]) continue;
                    float cand = Mathf.Min(b, R[m]);
                    if (cand > W[m]) { W[m] = cand; reached[m] = true; heap.Add(new KeyValuePair<float, int>(cand, m)); }
                }
            }
            var T1 = new float[n];
            for (int q = 0; q < n; q++) T1[q] = (occD[q] && reached[q]) ? W[q] + islandBlend * (R[q] - W[q]) : R[q];
            // opening on the body's own cells: a sliver the cut leaves narrower than twice the radius goes with it
            int rc = Mathf.Max(2, Mathf.RoundToInt(openRadius * H / cell));
            var E = new float[n];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                float mn = 1e9f;
                for (int dj = -rc; dj <= rc; dj++) for (int di = -rc; di <= rc; di++)
                {
                    if (di * di + dj * dj > rc * rc) continue;
                    int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                    int m = jj * nx + ii; if (occ[m]) mn = Mathf.Min(mn, T1[m]);
                }
                E[j * nx + i] = mn;
            }
            var T2 = new float[n]; var have = new bool[n];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                int q = j * nx + i; if (!occ[q]) { T2[q] = T1[q]; continue; }
                float mx = -1e9f;
                for (int dj = -rc; dj <= rc; dj++) for (int di = -rc; di <= rc; di++)
                {
                    if (di * di + dj * dj > rc * rc) continue;
                    int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                    float e = E[jj * nx + ii]; if (e < 1e8f) mx = Mathf.Max(mx, e);
                }
                T2[q] = mx > -1e8f ? Mathf.Min(mx, T1[q]) : T1[q]; have[q] = true;
            }
            // cells near the body but empty copy their occupied neighbours, so the filtered sample at the outline matches the inside
            for (int pass = 0; pass < 4; pass++)
            {
                var nh = (bool[])have.Clone(); var nt = (float[])T2.Clone();
                for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i; if (have[q]) continue;
                    float sum = 0; int cnt = 0;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                    {
                        int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                        int m = jj * nx + ii; if (have[m]) { sum += T2[m]; cnt++; }
                    }
                    if (cnt > 0) { nt[q] = sum / cnt; nh[q] = true; }
                }
                have = nh; T2 = nt;
            }
            map = T2;
        }

        class MaxFirst : IComparer<KeyValuePair<float, int>>
        {
            public int Compare(KeyValuePair<float, int> a, KeyValuePair<float, int> b)
            {
                int c = a.Key.CompareTo(b.Key); return c != 0 ? c : a.Value.CompareTo(b.Value);
            }
        }

        float EdgeDistance(int q)
        {
            Vector2 p = pxPos[q]; float h = 1.5f * cell;
            float gx = (Sample(p.x + h, p.y) - Sample(p.x - h, p.y)) / (2f * h), gy = (Sample(p.x, p.y + h) - Sample(p.x, p.y - h)) / (2f * h);
            return Mathf.Abs(Progress - pxT[q]) / Mathf.Max(Mathf.Sqrt(gx * gx + gy * gy), 0.02f);
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-6f, b - a)); return t * t * (3f - 2f * t); }

        bool[] Dilate(bool[] a, int r)
        {
            var o = new bool[a.Length];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
            {
                if (!a[j * nx + i]) continue;
                for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                {
                    int ii = i + di, jj = j + dj; if (ii >= 0 && jj >= 0 && ii < nx && jj < ny) o[jj * nx + ii] = true;
                }
            }
            return o;
        }

        // ------------------------------------------------------------ helpers
        void ReadSprite(SpriteRenderer sr, List<Vector2> pos, List<Color32> col, out float yMin, out float yMax)
        {
            var sp = sr.sprite; var r = sp.rect; var tex = sp.texture; int w = (int)r.width, h = (int)r.height;
            // GetPixels fails (isReadable is 0): blit the sprite's rect to a small RenderTexture and read that back
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var rd = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            rd.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = rd.GetPixels32(); Kill(rd);
            var tr = sr.transform; float ppu = sp.pixelsPerUnit; Vector2 pv = sp.pivot;
            yMin = 1e9f; yMax = -1e9f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    if (c.a < 128) continue;
                    float lx = (x + 0.5f - pv.x) / ppu; if (sr.flipX) lx = -lx;
                    float ly = (y + 0.5f - pv.y) / ppu; if (sr.flipY) ly = -ly;
                    var wp = tr.TransformPoint(new Vector3(lx, ly, 0f));
                    pos.Add(new Vector2(wp.x, wp.y)); col.Add(c);
                    yMin = Mathf.Min(yMin, wp.y); yMax = Mathf.Max(yMax, wp.y);
                }
        }

        float Sample(float wx, float wy)
        {
            float gx = Mathf.Clamp((wx - mapMin.x) / cell - 0.5f, 0f, nx - 1.001f), gy = Mathf.Clamp((wy - mapMin.y) / cell - 0.5f, 0f, ny - 1.001f);
            int i = (int)gx, j = (int)gy; float fx = gx - i, fy = gy - j;
            float a = Mathf.Lerp(map[j * nx + i], map[j * nx + i + 1], fx), b = Mathf.Lerp(map[(j + 1) * nx + i], map[(j + 1) * nx + i + 1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        static int UpperBound(float[] a, float v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (a[m] <= v) lo = m + 1; else hi = m; }
            return lo;
        }

        static float Hash(int xi, int yi, int s)
        {
            unchecked
            {
                uint h = (uint)(xi * 374761393 + yi * 668265263 + s * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
            }
        }

        static float Noise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), fx), b = Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>The order map as text for offline checks: nx ny minx miny cell, then rows.</summary>
        public string MapText()
        {
            var sb = new StringBuilder();
            sb.Append(nx).Append(' ').Append(ny).Append(' ').Append(mapMin.x.ToString("F5")).Append(' ').Append(mapMin.y.ToString("F5")).Append(' ').Append(cell.ToString("F6")).Append('\n');
            for (int q = 0; q < map.Length; q++) sb.Append(map[q].ToString("F4")).Append(q % nx == nx - 1 ? '\n' : ' ');
            return sb.ToString();
        }
    }
}
