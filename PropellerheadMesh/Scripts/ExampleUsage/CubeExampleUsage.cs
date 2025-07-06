using UnityEngine;
using Unity.Mathematics;

namespace PropellerHead
{
    public class CubeExampleUsage : MonoBehaviour
    {
        private Detail m_CubeDetail;
        private GameObject m_DebugMeshObject;

        [Header("Debug Settings")] public bool ShowVertexSpheres = true;
        public bool ShowPrimitiveWireframes = true;
        public float SphereRadius = 0.05f;

        private void Start()
        {
            CreateCube();
            CreateDebugVisualization();
        }

        private void CreateCube()
        {
            m_CubeDetail = new Detail();
            var size = 2f;
            var half = size * 0.5f;

            // Create 8 vertices of a cube
            var cubeVertices = new float3[]
            {
                new(-half, -half, -half), // 0: bottom-left-back
                new(half, -half, -half), // 1: bottom-right-back
                new(half, half, -half), // 2: top-right-back
                new(-half, half, -half), // 3: top-left-back
                new(-half, -half, half), // 4: bottom-left-front
                new(half, -half, half), // 5: bottom-right-front
                new(half, half, half), // 6: top-right-front
                new(-half, half, half) // 7: top-left-front
            };

            // Add points to detail
            var pointOffsets = new long[8];
            for (var i = 0; i < cubeVertices.Length; i++)
            {
                pointOffsets[i] = m_CubeDetail.AddPoint(cubeVertices[i]);
            }

            // Add point color attribute for vertex colors
            var pointColorAttrib = new Attribute<float3>(AttribID.Register("color"));
            m_CubeDetail.AddPointAttrib(pointColorAttrib);

            // Set different colors for each vertex
            var vertexColors = new float3[]
            {
                new(1, 0, 0), // Red
                new(0, 1, 0), // Green
                new(0, 0, 1), // Blue
                new(1, 1, 0), // Yellow
                new(1, 0, 1), // Magenta
                new(0, 1, 1), // Cyan
                new(1, 1, 1), // White
                new(0, 0, 0) // Black
            };

            for (var i = 0; i < pointOffsets.Length; i++)
            {
                pointColorAttrib.Set(pointOffsets[i], vertexColors[i], m_CubeDetail.Points);
            }

            // Create 6 faces (quads) of the cube
            var cubeFaces = new int[][]
            {
                new[] { 0, 1, 2, 3 }, // Back face
                new[] { 4, 7, 6, 5 }, // Front face
                new[] { 0, 4, 5, 1 }, // Bottom face
                new[] { 3, 2, 6, 7 }, // Top face
                new[] { 0, 3, 7, 4 }, // Left face
                new[] { 1, 5, 6, 2 } // Right face
            };

            // Define colors for each face
            var faceColors = new float3[]
            {
                new(1, 0, 0), // Back face - Red
                new(0, 1, 0), // Front face - Green
                new(0, 0, 1), // Bottom face - Blue
                new(1, 1, 0), // Top face - Yellow
                new(1, 0, 1), // Left face - Magenta
                new(0, 1, 1) // Right face - Cyan
            };

            // Add primitive color attribute (using a different name to avoid conflicts)
            var primitiveColorAttrib = new Attribute<float3>(AttribID.Register("face_color"));
            m_CubeDetail.AddPrimAttrib(primitiveColorAttrib);

            // Add primitives (faces) to detail and set their colors
            for (var faceIndex = 0; faceIndex < cubeFaces.Length; faceIndex++)
            {
                var face = cubeFaces[faceIndex];
                var facePointOffsets = new long[face.Length];
                for (var i = 0; i < face.Length; i++)
                {
                    facePointOffsets[i] = pointOffsets[face[i]];
                }

                // Add the primitive and get its offset
                var primOffset = m_CubeDetail.AddPrim(facePointOffsets);

                // Set the color for this primitive
                primitiveColorAttrib.Set(primOffset, faceColors[faceIndex], m_CubeDetail.Prims);

                Debug.Log($"Face {faceIndex} created with color {faceColors[faceIndex]} at primitive offset {primOffset}");
            }

            // Log cube statistics
            var stats = m_CubeDetail.GetStats();
            Debug.Log($"Cube created with {stats.PointCount} points, " +
                      $"{stats.VertexCount} vertices, {stats.PrimCount} primitives");

            var detailedStats = m_CubeDetail.GetDetailedStats();
            Debug.Log($"Detailed stats: {detailedStats}");

            // Validate the cube structure
            var isValid = m_CubeDetail.ValidateIntegrity();
            Debug.Log($"Cube structure is valid: {isValid}");
        }

        private void CreateDebugVisualization()
        {
            if (m_CubeDetail == null)
                return;

            // Create debug mesh object with vertex colors (will use point colors)
            m_DebugMeshObject = m_CubeDetail.CreateDebugObject(Color.black);

            if (m_DebugMeshObject != null)
            {
                // Position it relative to this GameObject
                m_DebugMeshObject.transform.SetParent(transform);
                m_DebugMeshObject.transform.localPosition = Vector3.zero;

                Debug.Log("Debug mesh created with vertex colors!");
            }
        }

        private void OnDrawGizmos()
        {
            if (m_CubeDetail == null)
                return;

            // Draw all vertices as colored spheres (if enabled)
            if (ShowVertexSpheres)
            {
                m_CubeDetail.DrawDebugSpheres(SphereRadius, Color.gray);
            }

            // Draw primitive wireframes with their colors (if enabled)
            if (ShowPrimitiveWireframes)
            {
                m_CubeDetail.DrawDebugPrimitivesWithFaceColor(Color.white);
            }
        }

        private void OnDestroy()
        {
            // Clean up resources
            if (m_DebugMeshObject != null)
            {
                if (Application.isPlaying)
                    Destroy(m_DebugMeshObject);
                else
                    DestroyImmediate(m_DebugMeshObject);
            }

            m_CubeDetail?.Dispose();
        }
    }
}