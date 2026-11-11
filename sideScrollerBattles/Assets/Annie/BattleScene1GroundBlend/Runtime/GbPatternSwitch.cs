using UnityEngine;

namespace Annie.BattleScene1GroundBlend
{
    public enum GbPattern { Off, M1, M2, M3, GrassOnly }

    /// <summary>Chooses the third layer of the ground blend: Off (approved soil) | M1 | M2 | M3 | Grass only.
    /// Change `pattern` in the inspector while playing. Works on a per-renderer material instance, never the asset.
    /// The matching "Pattern_*" child is the only one active, and its AnnieLayers toggle is the only one on, so capture.json records the choice.</summary>
    public class GbPatternSwitch : MonoBehaviour
    {
        public GbPattern pattern = GbPattern.Off;
        public Renderer groundRenderer;
        public Texture m1, m2, m3;
        public AnnieLayers layers;   // optional: the pattern toggles live here
        Material inst; GbPattern applied = (GbPattern)(-1);

        static readonly int MaskTex = Shader.PropertyToID("_MaskTex"), MaskMul = Shader.PropertyToID("_MaskMul"), MaskAdd = Shader.PropertyToID("_MaskAdd");

        void OnEnable() { Apply(true); }
        void Update() { if (pattern != applied) Apply(false); }
        void OnValidate() { if (isActiveAndEnabled) Apply(false); }

        public void Apply(bool force)
        {
            if (groundRenderer == null) return;
            if (!force && pattern == applied) return;
            applied = pattern;
            Material m = null;
            if (Application.isPlaying) { if (inst == null) inst = groundRenderer.material; m = inst; }   // edit mode never writes the material asset
            if (m != null) switch (pattern)
            {
                case GbPattern.Off: m.SetFloat(MaskMul, 0f); m.SetFloat(MaskAdd, 1f); break;
                case GbPattern.GrassOnly: m.SetFloat(MaskMul, 0f); m.SetFloat(MaskAdd, 0f); break;
                default:
                    m.SetTexture(MaskTex, pattern == GbPattern.M1 ? m1 : pattern == GbPattern.M2 ? m2 : m3);
                    m.SetFloat(MaskMul, 1f); m.SetFloat(MaskAdd, 0f); break;
            }
            if (layers != null)
                foreach (var l in layers.layers)
                    if (l.name.StartsWith("Pattern_")) l.on = (l.name == "Pattern_" + pattern);
        }
    }
}
