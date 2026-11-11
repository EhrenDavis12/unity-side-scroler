using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Annie
{
    /// <summary>Test-scene frame capture: PNG frames at 30 fps plus capture.json, then leaves play mode.</summary>
    public class AnnieCapture : MonoBehaviour
    {
        public Camera cam;
        public string outDir = "";
        public string caption = "";
        public int frameCount = 90;
        public int fps = 30;
        public bool replayLayersOnStart = true;
        public bool autoStart = true;
        /// <summary>Srgb: sRGB render texture, blends in linear light (default). Gamma: blends on the sRGB bytes, like PIL; needs sRGB-off sprite textures.</summary>
        public BlendSpace blendSpace = BlendSpace.Srgb;

        public enum BlendSpace { Srgb, Gamma }

        [Serializable] class Toggle { public string name; public bool on; }
        [Serializable] class LayerSet { public string owner; public List<Toggle> toggles = new List<Toggle>(); }
        [Serializable] class Seed { public string path; public uint seed; }
        [Serializable]
        class Report
        {
            public string caption;
            public int fps;
            public int frames;
            public int width;
            public int height;
            public string blendSpace;
            public List<Seed> seeds = new List<Seed>();
            public List<LayerSet> layers = new List<LayerSet>();
        }

        bool stopRequested;

        void Start()
        {
            if (autoStart) StartCapture();
        }

        public void SetOutDir(string dir) { outDir = dir; }

        public void StartCapture() { StartCoroutine(Run()); }

        public void StopCapture() { stopRequested = true; }

        IEnumerator Run()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null || string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("AnnieCapture: needs a camera and outDir");
                yield break;
            }
            const int W = 1920, H = 1080;
            string framesDir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(framesDir);

            Time.captureFramerate = fps;
            var rt = blendSpace == BlendSpace.Gamma
                ? new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                : new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;

            var sets = FindObjectsByType<AnnieLayers>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (replayLayersOnStart) foreach (var s in sets) s.Replay();

            int n = 0;
            stopRequested = false;
            while (n < frameCount && !stopRequested)
            {
                yield return new WaitForEndOfFrame();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(framesDir, "f" + n.ToString("D4") + ".png"), tex.EncodeToPNG());
                n++;
            }

            cam.targetTexture = prevTarget;
            Time.captureFramerate = 0;

            var report = new Report { caption = caption, fps = fps, frames = n, width = W, height = H, blendSpace = blendSpace.ToString() };
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!ps.useAutoRandomSeed) report.seeds.Add(new Seed { path = PathOf(ps.transform), seed = ps.randomSeed });
            foreach (var s in sets)
            {
                var set = new LayerSet { owner = PathOf(s.transform) };
                foreach (var kv in s.GetStates()) set.toggles.Add(new Toggle { name = kv.Key, on = kv.Value });
                report.layers.Add(set);
            }
            File.WriteAllText(Path.Combine(outDir, "capture.json"), JsonUtility.ToJson(report, true));
            Debug.Log("AnnieCapture: wrote " + n + " frames to " + outDir);

            rt.Release();
            Destroy(rt);
            Destroy(tex);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
