using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExoFlight.EditorTools
{
    /// <summary>Original segmented racing gate geometry: hollow frame, inset lights, inward chevrons.</summary>
    public static class MarsCheckpointArt
    {
        public static Mesh Frame()
        {
            var b = new Builder();
            for (int i = 0; i < 4; i++) b.Arc(1.015f, 1.085f, .055f, 8f + 90f * i, 82f + 90f * i, true);
            return b.Save("Route gate frame");
        }
        public static Mesh Trim()
        {
            var b = new Builder();
            for (int i = 0; i < 4; i++) b.Arc(1.085f, 1.098f, .057f, 9f + 90f * i, 81f + 90f * i, false);
            return b.Save("Route gate edge");
        }
        public static Mesh Lights()
        {
            var b = new Builder();
            for (int i = 0; i < 4; i++)
            {
                b.Arc(.99f, 1.008f, .058f, 10f + 90f * i, 80f + 90f * i, false);
                Quaternion turn = Quaternion.Euler(0f, 0f, i * 90f);
                b.Stroke(turn * new Vector3(1.035f, -.065f), turn * new Vector3(.92f, 0f), .022f);
                b.Stroke(turn * new Vector3(.92f, 0f), turn * new Vector3(1.035f, .065f), .022f);
            }
            // Three short orientation bars distinguish the top without text or symbols.
            for (int i = -1; i <= 1; i++) b.Stroke(new Vector3(i * .045f, 1.115f), new Vector3(i * .045f, 1.17f), .013f);
            return b.Save("Route gate light inlays");
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
            }
            Vector3 Point(float r, float a, float z) => new Vector3(r * Mathf.Cos(a * Mathf.Deg2Rad), r * Mathf.Sin(a * Mathf.Deg2Rad), z);
            public void Arc(float inner, float outer, float depth, float from, float to, bool solid)
            {
                const int segments = 24;
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.Lerp(from, to, i / (float)segments), b = Mathf.Lerp(from, to, (i + 1f) / segments);
                    Quad(Point(inner, a, depth), Point(outer, a, depth), Point(outer, b, depth), Point(inner, b, depth));
                    Quad(Point(inner, b, -depth), Point(outer, b, -depth), Point(outer, a, -depth), Point(inner, a, -depth));
                    if (!solid) continue;
                    Quad(Point(outer, a, depth), Point(outer, a, -depth), Point(outer, b, -depth), Point(outer, b, depth));
                    Quad(Point(inner, b, depth), Point(inner, b, -depth), Point(inner, a, -depth), Point(inner, a, depth));
                }
                if (solid)
                {
                    Quad(Point(inner, from, -depth), Point(outer, from, -depth), Point(outer, from, depth), Point(inner, from, depth));
                    Quad(Point(inner, to, depth), Point(outer, to, depth), Point(outer, to, -depth), Point(inner, to, -depth));
                }
            }
            public void Stroke(Vector3 from, Vector3 to, float width)
            {
                Vector3 side = Vector3.Cross((to - from).normalized, Vector3.forward) * (width * .5f);
                foreach (float z in new[] { -.06f, .06f })
                    Quad(from - side + Vector3.forward * z, from + side + Vector3.forward * z,
                        to + side + Vector3.forward * z, to - side + Vector3.forward * z);
            }
            public Mesh Save(string name)
            {
                var generated = new Mesh { name = name, indexFormat = IndexFormat.UInt16 };
                generated.SetVertices(vertices); generated.SetTriangles(triangles, 0); generated.RecalculateNormals(); generated.RecalculateBounds();
                string path = "Assets/MarsMission/" + name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
                EditorUtility.CopySerialized(generated, existing);
                Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
        }
    }
}
