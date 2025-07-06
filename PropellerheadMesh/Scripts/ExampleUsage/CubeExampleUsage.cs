using UnityEngine;
using PropellerHead;
using Unity.Mathematics;
using System.Linq;

namespace PropellerHead
{
    public class CubeExampleUsage : MonoBehaviour
    {
        private Detail m_CubeDetail;
        private GameObject m_DebugMeshObject;

        [Header("Debug Settings")]
        public bool showVertexSpheres = true;
        public float sphereRadius = 0.05f;

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
                new float3(-half, -half, -half), // 0: bottom-left-back
                new float3(half, -half, -half), // 1: bottom-right-back
                new float3(half, half, -half), // 2: top-right-back
                new float3(-half, half, -half), // 3: top-left-back
                new float3(-half, -half, half), // 4: bottom-left-front
                new float3(half, -half, half), // 5: bottom-right-front
                new float3(half, half, half), // 6: top-right-front
                new float3(-half, half, half) // 7: top-left-front
            };

            // Add points to detail
            var pointOffsets = new long[8];
            for (var i = 0; i < cubeVertices.Length; i++)
            {
                pointOffsets[i] = m_CubeDetail.AddPoint(cubeVertices[i]);
            }

            // Create 6 faces (quads) of the cube
            var cubeFaces = new int[][]
            {
                new int[] { 0, 1, 2, 3 }, // Back face
                new int[] { 4, 7, 6, 5 }, // Front face
                new int[] { 0, 4, 5, 1 }, // Bottom face
                new int[] { 3, 2, 6, 7 }, // Top face
                new int[] { 0, 3, 7, 4 }, // Left face
                new int[] { 1, 5, 6, 2 } // Right face
            };

            // Add primitives (faces) to detail
            foreach (var face in cubeFaces)
            {
                var facePointOffsets = new long[face.Length];
                for (var i = 0; i < face.Length; i++)
                {
                    facePointOffsets[i] = pointOffsets[face[i]];
                }

                m_CubeDetail.AddPrim(facePointOffsets);
            }

            // Add a custom color attribute
            var colorAttrib = new Attribute<float3>(AttribID.Register("color"));
            m_CubeDetail.AddPointAttrib(colorAttrib);

            // Set different colors for each vertex
            var colors = new float3[]
            {
                new float3(1, 0, 0), // Red
                new float3(0, 1, 0), // Green
                new float3(0, 0, 1), // Blue
                new float3(1, 1, 0), // Yellow
                new float3(1, 0, 1), // Magenta
                new float3(0, 1, 1), // Cyan
                new float3(1, 1, 1), // White
                new float3(0, 0, 0) // Black
            };

            for (var i = 0; i < pointOffsets.Length; i++)
            {
                colorAttrib.Set(pointOffsets[i], colors[i], m_CubeDetail.Points);
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

            // Create debug mesh object with vertex colors
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
            if (m_CubeDetail == null || !showVertexSpheres)
                return;

            // Draw all vertices as colored spheres
            m_CubeDetail.DrawDebugSpheres(sphereRadius, Color.black);
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