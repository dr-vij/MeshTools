using Unity.Mathematics;
using System.Collections.Generic;

namespace PropellerHead.Generators
{
    public static class SphereGenerator
    {
        /// <summary>
        /// Generates a sphere with a specified subdivision level
        /// </summary>
        /// <param name="detail">The detail object to add the sphere to</param>
        /// <param name="radius">Radius of the sphere</param>
        /// <param name="subdivisionLevel">Subdivision level (0 and higher)</param>
        public static void GenerateSphere(this Detail detail, float radius, int subdivisionLevel = 2)
        {
            subdivisionLevel = math.max(0, subdivisionLevel);

            // Golden ratio
            var t = (1.0f + math.sqrt(5.0f)) / 2.0f;

            // Initial 12 vertices of icosahedron
            var vertices = new List<float3>
            {
                math.normalize(new float3(-1, t, 0)) * radius,
                math.normalize(new float3(1, t, 0)) * radius,
                math.normalize(new float3(-1, -t, 0)) * radius,
                math.normalize(new float3(1, -t, 0)) * radius,
                math.normalize(new float3(0, -1, t)) * radius,
                math.normalize(new float3(0, 1, t)) * radius,
                math.normalize(new float3(0, -1, -t)) * radius,
                math.normalize(new float3(0, 1, -t)) * radius,
                math.normalize(new float3(t, 0, -1)) * radius,
                math.normalize(new float3(t, 0, 1)) * radius,
                math.normalize(new float3(-t, 0, -1)) * radius,
                math.normalize(new float3(-t, 0, 1)) * radius
            };

            // Initial 20 faces
            var faces = new List<int[]>
            {
                new[] {0, 11, 5}, new[] {0, 5, 1}, new[] {0, 1, 7}, new[] {0, 7, 10}, new[] {0, 10, 11},
                new[] {1, 5, 9}, new[] {5, 11, 4}, new[] {11, 10, 2}, new[] {10, 7, 6}, new[] {7, 1, 8},
                new[] {3, 9, 4}, new[] {3, 4, 2}, new[] {3, 2, 6}, new[] {3, 6, 8}, new[] {3, 8, 9},
                new[] {4, 9, 5}, new[] {2, 4, 11}, new[] {6, 2, 10}, new[] {8, 6, 7}, new[] {9, 8, 1}
            };

            // Subdivide
            for (var i = 0; i < subdivisionLevel; i++)
            {
                var newFaces = new List<int[]>();
                var midpointCache = new Dictionary<long, int>();

                foreach (var face in faces)
                {
                    var a = GetMidpointIndex(face[0], face[1], vertices, midpointCache, radius);
                    var b = GetMidpointIndex(face[1], face[2], vertices, midpointCache, radius);
                    var c = GetMidpointIndex(face[2], face[0], vertices, midpointCache, radius);

                    newFaces.Add(new[] { face[0], a, c });
                    newFaces.Add(new[] { face[1], b, a });
                    newFaces.Add(new[] { face[2], c, b });
                    newFaces.Add(new[] { a, b, c });
                }

                faces = newFaces;
            }

            // Add vertices to detail
            var pointOffsets = new long[vertices.Count];
            for (var i = 0; i < vertices.Count; i++)
            {
                pointOffsets[i] = detail.AddPoint(vertices[i]);
            }

            // Add faces to detail
            foreach (var face in faces)
            {
                var faceOffsets = new long[3];
                for (var i = 0; i < 3; i++)
                {
                    faceOffsets[i] = pointOffsets[face[i]];
                }
                detail.AddPrim(faceOffsets);
            }
        }

        private static int GetMidpointIndex(int p1, int p2, List<float3> vertices, Dictionary<long, int> cache, float radius)
        {
            // Create unique key for edge
            var key = ((long)math.min(p1, p2) << 32) | (long)math.max(p1, p2);
            
            if (cache.TryGetValue(key, out var index))
                return index;

            // Calculate midpoint and project to sphere
            var midpoint = (vertices[p1] + vertices[p2]) * 0.5f;
            midpoint = math.normalize(midpoint) * radius;

            // Add to a vertices list
            vertices.Add(midpoint);
            index = vertices.Count - 1;
            cache[key] = index;

            return index;
        }
    }
}