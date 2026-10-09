using System.Collections.Generic;
using UnityEngine;

namespace Bombaclat
{
    /// <summary>Procedural flat-shaded meshes for the low-poly look. No imported models needed.</summary>
    public static class LowPolyMesh
    {
        /// <summary>A prism / frustum / cone (topRadius 0) standing on y = 0.</summary>
        public static Mesh Prism(int sides, float bottomRadius, float topRadius, float height, float jitter = 0f, int seed = 0)
        {
            var rng = new System.Random(seed);
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                float j = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * j;
                bottom[i] = dir * bottomRadius;
                top[i] = dir * topRadius + Vector3.up * height;
            }

            bool cone = topRadius < 0.0001f;
            var tris = new List<Vector3>();
            var bottomCenter = Vector3.zero;
            var topCenter = Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                if (cone)
                {
                    tris.Add(topCenter); tris.Add(bottom[n]); tris.Add(bottom[i]);
                }
                else
                {
                    tris.Add(top[i]); tris.Add(top[n]); tris.Add(bottom[n]);
                    tris.Add(top[i]); tris.Add(bottom[n]); tris.Add(bottom[i]);
                    tris.Add(topCenter); tris.Add(top[n]); tris.Add(top[i]);
                }
                tris.Add(bottomCenter); tris.Add(bottom[i]); tris.Add(bottom[n]);
            }
            return Build(tris, Vector3.up * height * 0.5f, "Prism");
        }

        /// <summary>A faceted sphere centered on the origin. Jitter makes rocks and bushes.</summary>
        public static Mesh IcoSphere(float radius, int subdivisions = 1, float jitter = 0f, int seed = 0)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>();
                for (int f = 0; f < faces.Count; f += 3)
                {
                    int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                    int ab = Midpoint(verts, cache, a, b);
                    int bc = Midpoint(verts, cache, b, c);
                    int ca = Midpoint(verts, cache, c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }

            var rng = new System.Random(seed);
            for (int i = 0; i < verts.Count; i++)
                verts[i] *= radius * (1f + ((float)rng.NextDouble() * 2f - 1f) * jitter);

            var tris = new List<Vector3>(faces.Count);
            foreach (int index in faces) tris.Add(verts[index]);
            return Build(tris, Vector3.zero, "IcoSphere");
        }

        /// <summary>A cut gem: used for the crown marker and chaos tiles.</summary>
        public static Mesh Gem(float radius, float height, int sides = 6)
        {
            var tris = new List<Vector3>();
            var top = Vector3.up * height * 0.35f;
            var bottom = Vector3.down * height * 0.65f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                tris.Add(top); tris.Add(p1); tris.Add(p0);
                tris.Add(bottom); tris.Add(p0); tris.Add(p1);
            }
            return Build(tris, Vector3.zero, "Gem");
        }

        static int Midpoint(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index)) return index;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        /// <summary>
        /// Turns a triangle soup into a flat-shaded mesh. Every triangle is re-oriented to face
        /// away from <paramref name="center"/>, so callers never have to think about winding order
        /// (all shapes here are convex).
        /// </summary>
        static Mesh Build(List<Vector3> tris, Vector3 center, string name)
        {
            var indices = new int[tris.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                var a = tris[i]; var b = tris[i + 1]; var c = tris[i + 2];
                var normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, (a + b + c) / 3f - center) < 0f)
                {
                    tris[i + 1] = c;
                    tris[i + 2] = b;
                }
                indices[i] = i; indices[i + 1] = i + 1; indices[i + 2] = i + 2;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(tris);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
