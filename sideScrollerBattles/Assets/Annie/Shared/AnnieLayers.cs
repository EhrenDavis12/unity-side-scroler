using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Annie
{
    /// <summary>Layer toggles for an effect root. Each layer is a child object, switched on or off live.</summary>
    public class AnnieLayers : MonoBehaviour
    {
        [Serializable]
        public class Layer
        {
            public string name;
            public GameObject target;
            public bool on = true;
        }

        public List<Layer> layers = new List<Layer>();
        public bool loop;
        public float loopInterval = 2f;

        Coroutine loopRoutine;

        void OnValidate() { Apply(); }

        void OnEnable() { Apply(); }

        void Start()
        {
            Apply();
            if (loop) loopRoutine = StartCoroutine(LoopRoutine());
        }

        void Update()
        {
            Apply();
            if (loop && loopRoutine == null && Application.isPlaying) loopRoutine = StartCoroutine(LoopRoutine());
            if (!loop && loopRoutine != null) { StopCoroutine(loopRoutine); loopRoutine = null; }
        }

        IEnumerator LoopRoutine()
        {
            while (loop)
            {
                Replay();
                yield return new WaitForSeconds(Mathf.Max(0.05f, loopInterval));
            }
            loopRoutine = null;
        }

        public void Apply()
        {
            foreach (var l in layers)
                if (l.target != null && l.target.activeSelf != l.on) l.target.SetActive(l.on);
        }

        public void SetLayer(string layerName, bool on)
        {
            foreach (var l in layers) if (l.name == layerName) l.on = on;
            Apply();
        }

        /// <summary>Restart every enabled layer.</summary>
        public void Replay()
        {
            Apply();
            foreach (var l in layers)
            {
                if (l.target == null || !l.on) continue;
                var go = l.target;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Clear(true);
                    ps.Play(true);
                }
                foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();
                foreach (var an in go.GetComponentsInChildren<Animator>(true))
                {
                    an.Rebind();
                    an.Update(0f);
                }
                // Objects without particles or animators (flash, light) restart by a quick off/on.
                if (go.GetComponentInChildren<ParticleSystem>(true) == null &&
                    go.GetComponentInChildren<Animator>(true) == null)
                {
                    go.SetActive(false);
                    go.SetActive(true);
                }
            }
        }

        /// <summary>Layer name to current on/off state.</summary>
        public Dictionary<string, bool> GetStates()
        {
            var d = new Dictionary<string, bool>();
            foreach (var l in layers) d[l.name] = l.on;
            return d;
        }
    }
}
