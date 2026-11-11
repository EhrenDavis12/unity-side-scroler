using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.StarSageSpell
{
    public static class SsUtil
    {
        public static readonly Color Cyan = new Color(0f, 0.973f, 0.957f, 1f);
        public static readonly Color Gold = new Color(0.839f, 0.682f, 0.212f, 1f);
        public static readonly Color Violet = new Color(0.396f, 0.122f, 0.541f, 1f);

        public static Sprite Spr(Texture2D t, string name)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); s.name = name; return s;
        }

        /// <summary>Restart a particle system at a place, aimed along dir (its +z), with a fixed seed. Does nothing when its layer is off.</summary>
        public static bool Fire(ParticleSystem ps, Vector3 pos, Vector3 dir, uint seed)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) return false;
            ps.transform.position = pos;
            ps.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.forward);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = seed;
            ps.Play(true);
            return true;
        }

        public static void Show(SpriteRenderer sr, Vector2 px, float sizePx, float texPx, float alpha, Color c, float rotDeg)
        {
            sr.enabled = true;
            sr.transform.position = SsGeo.PxToWorld(px);
            sr.transform.localScale = Vector3.one * (sizePx / texPx);
            sr.transform.rotation = Quaternion.Euler(0, 0, rotDeg);
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }

        public static void Light(Light2D l, Vector2 px, float intensity)
        {
            if (l == null) return;
            l.enabled = intensity > 0.001f; l.intensity = intensity; l.transform.position = SsGeo.PxToWorld(px);
        }
    }
}
