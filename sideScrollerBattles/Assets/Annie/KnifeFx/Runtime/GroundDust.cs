using UnityEngine;

namespace Annie.KnifeFx
{
    /// <summary>A small kick of dust puffs at a ground point. Takes an origin, a scale and a sort order, nothing else.</summary>
    public class GroundDust : MonoBehaviour
    {
        public ParticleSystem ps;
        float baseSize, baseSpeed, baseRadius;
        bool cached;

        void Cache()
        {
            if (cached) return;
            var m = ps.main; var sh = ps.shape;
            baseSize = m.startSizeMultiplier; baseSpeed = m.startSpeedMultiplier; baseRadius = sh.radius;
            cached = true;
        }

        public void Play(Vector3 origin, float scale, int sortOrder)
        {
            Cache();
            transform.position = origin;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.startSizeMultiplier = baseSize * scale; m.startSpeedMultiplier = baseSpeed * scale;
            var sh = ps.shape; sh.radius = baseRadius * scale;
            ps.GetComponent<ParticleSystemRenderer>().sortingOrder = sortOrder;
            ps.Play(true);
        }

        public void Hide() { if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    }
}
