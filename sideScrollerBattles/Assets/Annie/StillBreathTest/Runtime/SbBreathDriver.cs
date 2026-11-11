using System;
using System.Collections.Generic;
using UnityEngine;

namespace Annie.StillBreathTest
{
    /// <summary>Drives the breath: push(t) = amp * (0.5 - 0.5 cos(2 pi t / T)) written to each figure's renderer, and the real idle's sheet cell.</summary>
    public class SbBreathDriver : MonoBehaviour
    {
        [Serializable]
        public class Figure
        {
            public string name;
            public Renderer renderer;
            public bool useMaps;
            public float peakPx = 11f;      // peak rise in px at the 1024 texture
            public Texture mapA;
            public Texture mapB;
            public bool sheet;              // the real idle: advance the cell at sheetFps
            public int sheetFrames = 64;
            public float sheetFps = 16f;
        }

        public List<Figure> figures = new List<Figure>();
        public Texture pTex;
        public char take = 'A';
        public float period = 4f;
        public float fps = 30f;
        public float tickOffset = 0f;   // capture shows tick n+1 on frame n; calibrated on the first capture
        public float texSize = 1024f;
        public int sheetStartCell;      // where the real idle's sheet starts, so its breath is in step with the fakes
        public bool freezeRest;         // push 0 on every frame (the push-0 reference render)

        int counter;
        MaterialPropertyBlock mpb;

        void Update()
        {
            float tick = counter + tickOffset;
            counter++;
            ApplyTick(tick);
        }

        public void ApplyTick(float tick)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float t = tick / fps;
            float k = freezeRest ? 0f : 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / period);
            foreach (var f in figures)
            {
                if (f.renderer == null) continue;
                f.renderer.GetPropertyBlock(mpb);
                if (f.sheet)
                {
                    int cell = (((Mathf.FloorToInt(t * f.sheetFps) + sheetStartCell) % f.sheetFrames) + f.sheetFrames) % f.sheetFrames;
                    mpb.SetFloat("_Cell", cell);
                }
                else if (f.useMaps)
                {
                    mpb.SetTexture("_PTex", pTex);
                    mpb.SetTexture("_MTex", take == 'B' ? f.mapB : f.mapA);
                    mpb.SetFloat("_UseMaps", 1f);
                    mpb.SetFloat("_Push", k * f.peakPx / texSize);
                }
                f.renderer.SetPropertyBlock(mpb);
            }
        }
    }
}
