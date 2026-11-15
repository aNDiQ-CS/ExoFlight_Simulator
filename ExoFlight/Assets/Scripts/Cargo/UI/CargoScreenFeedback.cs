using UnityEngine;
using UnityEngine.UI;

namespace ExoFlight.Cargo.UI
{
    /// <summary>Soft screen-edge tint. No texture, post-processing volume or camera mutation is needed.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CargoScreenFeedback : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            const int columns = 32, rows = 20;
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = (float)x / columns, v = (float)y / rows;
                float edge = Mathf.Max(Mathf.Abs(u * 2f - 1f), Mathf.Abs(v * 2f - 1f));
                Color tint = color;
                tint.a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, 1f, edge));
                vh.AddVert(new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height), tint, new Vector2(u, v));
                if (x == columns || y == rows) continue;
                int i = y * (columns + 1) + x;
                vh.AddTriangle(i, i + columns + 1, i + 1);
                vh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
            }
        }
    }
}
