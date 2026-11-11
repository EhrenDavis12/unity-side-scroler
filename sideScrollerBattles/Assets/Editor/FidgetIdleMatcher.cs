#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// For one fidget sheet, finds which idle v9 frames look closest to the fidget's own first frame
/// — both ends of every fidget are the reference rest pose, but idle v9 was never pinned to it, so
/// its frames drift; IdleRotationPlayer needs to know which idle frames it's safe to cut away from
/// (and back into) without a visible pop. Runs once, at import time (see
/// Assets/Editor/AnimationVersionsImporter.cs), so the runtime crossfade does no image work at all
/// — only ever reads the <see cref="AnimationVersionEntry.matchedIdleFrames"/> this produces.
///
/// Each frame is downsampled to a small (<see cref="ThumbnailSize"/>) thumbnail — one average
/// colour per output pixel, alpha-weighted so the transparent padding around a tight-cropped
/// character never drags the average toward black — then two frames' "pixel difference" is the
/// alpha-weighted mean absolute RGB difference between their two thumbnails. This is a real
/// (if coarse) spatial comparison, not a single whole-frame average colour — two different poses
/// with the same overall palette would otherwise score as identical.
/// </summary>
public static class FidgetIdleMatcher
{
    private const int ThumbnailSize = 64;

    /// <summary>One thumbnail pixel: the alpha-weighted mean RGB of its source block, and that
    /// block's own average alpha (used to weight this pixel's contribution to a diff).</summary>
    private struct ThumbPixel
    {
        public float r, g, b, a;
    }

    /// <summary>The <paramref name="k"/> idle v9 frame indices whose thumbnails are closest to
    /// <paramref name="fidgetFirstFrame"/>'s, ascending by distance (index 0 is the single best
    /// match), with their distances in the same order.</summary>
    public static int[] FindBestMatches(
        IReadOnlyList<Sprite> idleFrames, Sprite fidgetFirstFrame, int k, out float[] distances)
    {
        ThumbPixel[] fidgetThumb = Downsample(fidgetFirstFrame, null);

        var textureCache = new Dictionary<Texture2D, Color32[]>();
        var allDistances = new float[idleFrames.Count];
        for (int i = 0; i < idleFrames.Count; i++)
        {
            ThumbPixel[] idleThumb = Downsample(idleFrames[i], textureCache);
            allDistances[i] = Difference(idleThumb, fidgetThumb);
        }

        int[] order = Enumerable.Range(0, idleFrames.Count)
            .OrderBy(i => allDistances[i])
            .Take(k)
            .ToArray();
        distances = order.Select(i => allDistances[i]).ToArray();
        return order;
    }

    /// <summary>The single pixel difference between one idle frame and one fidget frame —
    /// used to report a crossfade's entry/exit seam after the fact, with the exact same metric
    /// <see cref="FindBestMatches"/> used to pick it.</summary>
    public static float Difference(Sprite idleFrame, Sprite fidgetFrame)
    {
        return Difference(Downsample(idleFrame, null), Downsample(fidgetFrame, null));
    }

    private static ThumbPixel[] Downsample(Sprite sprite, Dictionary<Texture2D, Color32[]> cache)
    {
        Texture2D texture = sprite.texture;
        if (cache == null || !cache.TryGetValue(texture, out Color32[] pixels))
        {
            pixels = texture.GetPixels32();
            cache?.Add(texture, pixels);
        }

        Rect cellRect = sprite.textureRect; // pixel space, origin bottom-left — same as the postprocessor's own slicing rects
        int cellSize = Mathf.RoundToInt(cellRect.width);
        int block = Mathf.Max(1, cellSize / ThumbnailSize);
        int textureWidth = texture.width;

        var thumb = new ThumbPixel[ThumbnailSize * ThumbnailSize];
        for (int oy = 0; oy < ThumbnailSize; oy++)
        {
            for (int ox = 0; ox < ThumbnailSize; ox++)
            {
                float rSum = 0f, gSum = 0f, bSum = 0f, aSum = 0f;
                int baseX = (int)cellRect.x + ox * block;
                int baseY = (int)cellRect.y + oy * block;
                for (int by = 0; by < block; by++)
                {
                    int rowStart = (baseY + by) * textureWidth + baseX;
                    for (int bx = 0; bx < block; bx++)
                    {
                        Color32 p = pixels[rowStart + bx];
                        float a = p.a / 255f;
                        rSum += (p.r / 255f) * a;
                        gSum += (p.g / 255f) * a;
                        bSum += (p.b / 255f) * a;
                        aSum += a;
                    }
                }
                int sampleCount = block * block;
                thumb[oy * ThumbnailSize + ox] = new ThumbPixel
                {
                    r = aSum > 0f ? rSum / aSum : 0f,
                    g = aSum > 0f ? gSum / aSum : 0f,
                    b = aSum > 0f ? bSum / aSum : 0f,
                    a = aSum / sampleCount,
                };
            }
        }
        return thumb;
    }

    /// <summary>Alpha-weighted mean absolute RGB difference (0..1) between two thumbnails of the
    /// same size — a fully-transparent region in either contributes ~0 regardless of colour.
    /// </summary>
    private static float Difference(ThumbPixel[] a, ThumbPixel[] b)
    {
        double weightedSum = 0;
        double weightTotal = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float weight = a[i].a * b[i].a;
            float channelDiff = (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b)) / 3f;
            weightedSum += weight * channelDiff;
            weightTotal += weight;
        }
        return weightTotal > 0 ? (float)(weightedSum / weightTotal) : 0f;
    }
}
#endif
