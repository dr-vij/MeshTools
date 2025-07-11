using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

namespace Legacy
{
    public static class PrimitiveDebugExtensions
    {
        /// <summary>
        /// Creates a Unity Mesh from Detail with proper triangulation and primitive colors
        /// </summary>
        /// <param name="detail">The detail to convert to mesh</param>
        /// <param name="fallbackColor">Fallback color if no color attribute is found</param>
        /// <returns>Generated Unity Mesh</returns>
        public static Mesh CreateMesh(this Detail detail, Color fallbackColor = default)
        {
            if (detail == null)
                return null;

            if (fallbackColor == default)
                fallbackColor = Color.black;

            var mesh = new Mesh();
            var allVertices = new List<Vector3>();
            var allColors = new List<Color>();
            var allTriangles = new List<int>();

            // Get primitive color attribute (from primitives, not points!)
            var faceColorAttrib = GetFaceColorAttribute(detail);

            int vertexOffset = 0;

            // Process each primitive
            foreach (var primKvp in detail.Primitives)
            {
                var primOffset = primKvp.Key;
                var primitive = primKvp.Value;
                var vertices = GetPrimitiveVertices(primitive, detail);

                if (vertices.Count < 3)
                    continue;

                // Get color for this primitive (face)
                var primitiveColor = GetFaceColor(primOffset, detail, faceColorAttrib, fallbackColor);

                // Fan triangulation from first vertex
                for (int i = 1; i < vertices.Count - 1; i++)
                {
                    // Add triangle vertices
                    allVertices.Add(vertices[0]);
                    allVertices.Add(vertices[i + 1]);
                    allVertices.Add(vertices[i]);

                    // Add the SAME primitive color to all triangle vertices
                    allColors.Add(primitiveColor);
                    allColors.Add(primitiveColor);
                    allColors.Add(primitiveColor);

                    // Add triangle indices
                    allTriangles.Add(vertexOffset);
                    allTriangles.Add(vertexOffset + 1);
                    allTriangles.Add(vertexOffset + 2);

                    vertexOffset += 3;
                }
            }

            // Set mesh data
            mesh.vertices = allVertices.ToArray();
            mesh.colors = allColors.ToArray();
            mesh.triangles = allTriangles.ToArray();

            // Calculate normals
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Creates a debug GameObject with MeshRenderer showing the Detail
        /// </summary>
        /// <param name="detail">The detail to visualize</param>
        /// <param name="fallbackColor">Fallback color if no color attribute</param>
        /// <param name="material">Material to use (if null, creates default vertex color material)</param>
        /// <returns>Created GameObject</returns>
        public static GameObject CreateDebugObject(this Detail detail, Color fallbackColor = default, Material material = null)
        {
            if (detail == null)
                return null;

            var mesh = detail.CreateMesh(fallbackColor);
            if (mesh == null)
                return null;

            var go = new GameObject("Detail Debug Mesh");
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            meshFilter.mesh = mesh;

            if (material == null)
            {
                // Create material with your custom vertex color shader
                material = new Material(Shader.Find("Shader Graphs/VertexColorLitDebug"));
                material.name = "Vertex Color Debug Material";
            }

            meshRenderer.material = material;

            return go;
        }

        /// <summary>
        /// Draws all primitives as colored wireframes using face colors
        /// </summary>
        /// <param name="detail">The detail to visualize primitives from</param>
        /// <param name="fallbackColor">Fallback color if no color attribute</param>
        public static void DrawDebugPrimitivesWithFaceColor(this Detail detail, Color fallbackColor = default)
        {
            if (detail == null)
                return;

            if (fallbackColor == default)
                fallbackColor = Color.black;

            var colorAttrib = GetFaceColorAttribute(detail);

            // Iterate through all primitives
            foreach (var primKvp in detail.Primitives)
            {
                var primOffset = primKvp.Key;
                var primitive = primKvp.Value;
                var vertices = GetPrimitiveVertices(primitive, detail);

                if (vertices.Count < 3)
                    continue;

                // Get color for this primitive
                var primitiveColor = GetFaceColor(primOffset, detail, colorAttrib, fallbackColor);
                Gizmos.color = primitiveColor;

                // Draw wireframe of primitive
                for (int i = 0; i < vertices.Count; i++)
                {
                    var start = vertices[i];
                    var end = vertices[(i + 1) % vertices.Count];
                    Gizmos.DrawLine(start, end);
                }
            }
        }

        /// <summary>
        /// Draws all unique points as colored spheres using Gizmos
        /// </summary>
        /// <param name="detail">The detail to visualize points from</param>
        /// <param name="sphereRadius">Radius of debug spheres</param>
        /// <param name="fallbackColor">Fallback color if no color attribute</param>
        public static void DrawDebugSpheres(this Detail detail, float sphereRadius = 0.05f, Color fallbackColor = default)
        {
            if (detail == null)
                return;

            if (fallbackColor == default)
                fallbackColor = Color.black;

            var colorAttrib = GetPointColorAttribute(detail);
            var drawnPoints = new HashSet<long>();

            // Iterate through all points in detail using the new API
            foreach (var pointOffset in detail.GetAllPointOffsets())
            {
                if (!drawnPoints.Add(pointOffset))
                    continue;

                var position = detail.GetPointPos(pointOffset);
                var color = GetPointColor(pointOffset, detail, colorAttrib, fallbackColor);

                // Set gizmo color and draw sphere
                Gizmos.color = color;
                Gizmos.DrawSphere(new Vector3(position.x, position.y, position.z), sphereRadius);
            }
        }

        private static Color GetVertexColor(long vertexOffset, Detail detail, Attribute<float3> colorAttrib, Color fallbackColor)
        {
            if (colorAttrib == null)
                return fallbackColor;

            var pointOffset = detail.GetVertexPoint(vertexOffset);
            return GetPointColor(pointOffset, detail, colorAttrib, fallbackColor);
        }

        private static Color GetPointColor(long pointOffset, Detail detail, Attribute<float3> colorAttrib, Color fallbackColor)
        {
            if (colorAttrib == null)
                return fallbackColor;

            // Use the new API - check if attribute has value at this offset
            if (colorAttrib.HasValue(pointOffset))
            {
                var color = colorAttrib.Get(pointOffset);
                return new Color(color.x, color.y, color.z, 1f);
            }

            return fallbackColor;
        }

        private static Color GetFaceColor(long primOffset, Detail detail, Attribute<float3> colorAttrib, Color fallbackColor)
        {
            if (colorAttrib == null)
                return fallbackColor;

            // Use the new API - check if attribute has value at this offset
            if (colorAttrib.HasValue(primOffset))
            {
                var color = colorAttrib.Get(primOffset);
                return new Color(color.x, color.y, color.z, 1f);
            }

            return fallbackColor;
        }

        private static Attribute<float3> GetPointColorAttribute(Detail detail)
        {
            var colorId = AttribID.Color;
            if (colorId != -1 && detail.PointAttribs.ContainsKey(colorId))
            {
                return detail.GetPointAttrib<float3>(colorId);
            }

            return null;
        }

        private static Attribute<float3> GetFaceColorAttribute(Detail detail)
        {
            var colorId = AttribID.GetId("face_color");
            if (colorId != -1 && detail.PrimAttribs.ContainsKey(colorId))
            {
                return detail.GetPrimAttrib<float3>(colorId);
            }

            return null;
        }

        private static List<Vector3> GetPrimitiveVertices(Primitive primitive, Detail detail)
        {
            var vertices = new List<Vector3>();

            foreach (var vertexOffset in primitive.VertexOffsets)
            {
                var pointOffset = detail.GetVertexPoint(vertexOffset);
                var position = detail.GetPointPos(pointOffset);
                vertices.Add(new Vector3(position.x, position.y, position.z));
            }

            return vertices;
        }
    }
}