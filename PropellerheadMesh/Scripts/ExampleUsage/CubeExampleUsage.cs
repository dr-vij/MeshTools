using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

namespace PropellerHead
{
    public class CubeExampleUsage : MonoBehaviour
    {
        private Detail m_CubeDetail;
        private GameObject m_DebugMeshObject;

        // Store original positions to keep wiggling around the original shape
        private Dictionary<long, float3> m_OriginalPositions = new();

        [Header("Debug Settings")] public bool ShowVertexSpheres = true;
        public bool ShowPrimitiveWireframes = true;
        public float SphereRadius = 0.05f;

        private void Start()
        {
            CreateCube();
            CalculateNormals();
            CreateDebugVisualization();
        }

        private void CalculateNormals()
        {
            if (m_CubeDetail == null)
                return;
            NormalsOperators.CalculateNormals(m_CubeDetail);
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
            Debug.Log($"Cube created with {stats.PointCount} points, {stats.VertexCount} vertices, {stats.PrimCount} primitives");

            var detailedStats = m_CubeDetail.GetDetailedStats();
            Debug.Log($"Detailed stats: {detailedStats}");

            // Validate the cube structure
            var isValid = m_CubeDetail.ValidateIntegrity();
            Debug.Log($"Cube structure is valid: {isValid}");
        }

        private void ShakePoints()
        {
            if (m_CubeDetail == null)
                return;

            // Get the position attribute
            var positionAttrib = m_CubeDetail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            // Store original positions if not already stored
            if (!m_OriginalPositions.Any())
            {
                StoreOriginalPositions();
            }

            // Apply noise-based wiggling to all points
            var time = Time.time;
            var noiseScale = 1f; // Controls noise frequency
            var wiggleAmplitude = 0.3f; // Controls wiggle intensity

            foreach (var pointOffset in m_CubeDetail.Points.GetAllOffsets())
            {
                if (m_OriginalPositions.TryGetValue(pointOffset, out var originalPos))
                {
                    // Generate 3D noise for each axis
                    var noiseX = Mathf.PerlinNoise(originalPos.x * noiseScale + time * 0.8f, originalPos.y * noiseScale + time * 0.3f) * 2f - 1f;
                    var noiseY = Mathf.PerlinNoise(originalPos.y * noiseScale + time * 0.6f, originalPos.z * noiseScale + time * 0.9f) * 2f - 1f;
                    var noiseZ = Mathf.PerlinNoise(originalPos.z * noiseScale + time * 0.4f, originalPos.x * noiseScale + time * 0.7f) * 2f - 1f;

                    // Apply wiggle offset
                    var wiggleOffset = new float3(noiseX, noiseY, noiseZ) * wiggleAmplitude;
                    var newPosition = originalPos + wiggleOffset;

                    // Update the position
                    positionAttrib.Set(pointOffset, newPosition, m_CubeDetail.Points);
                }
            }
        }
        
        private void Update()
        {
            ShakePoints();
            CalculateNormals();

            if (m_DebugMeshObject != null)
            {
                UpdateDebugVisualization();
            }

            CreateDebugVisualization();
        }

        private void CreateDebugVisualization()
        {
            if (m_CubeDetail == null)
                return;

            // Create debug mesh object with vertex colors (will use point colors)
            if (m_DebugMeshObject != null)
                Destroy(m_DebugMeshObject);

            m_DebugMeshObject = m_CubeDetail.CreateDebugObject(Color.black);

            if (m_DebugMeshObject != null)
            {
                // Position it relative to this GameObject
                m_DebugMeshObject.transform.SetParent(transform);
                m_DebugMeshObject.transform.localPosition = Vector3.zero;
            }
        }

        private void OnDrawGizmos()
        {
            if (m_CubeDetail == null)
                return;

            // Draw all vertices as colored spheres (if enabled)
            if (ShowVertexSpheres)
                m_CubeDetail.DrawDebugSpheres(SphereRadius, Color.black);

            // Draw primitive wireframes with their colors (if enabled)
            if (ShowPrimitiveWireframes)
                m_CubeDetail.DrawDebugPrimitivesWithFaceColor(Color.black);
        }

        private void StoreOriginalPositions()
        {
            var positionAttrib = m_CubeDetail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            foreach (var pointOffset in m_CubeDetail.Points.GetAllOffsets())
            {
                if (!m_OriginalPositions.ContainsKey(pointOffset))
                {
                    var originalPos = positionAttrib.Get(pointOffset, m_CubeDetail.Points);
                    m_OriginalPositions[pointOffset] = originalPos;
                }
            }
        }

        private void UpdateDebugVisualization()
        {
            if (m_DebugMeshObject == null)
                return;

            var meshFilter = m_DebugMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;

            var mesh = meshFilter.mesh;
            if (mesh == null)
                return;

            // Get the position attribute
            var positionAttrib = m_CubeDetail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            // Update vertex positions
            var vertices = mesh.vertices;
            var vertexCount = Mathf.Min(vertices.Length, m_CubeDetail.Points.Count); // Ensure we don't go out of bounds

            var pointOffsets = m_CubeDetail.Points.GetAllOffsets().ToArray(); // Get all allocated offsets

            for (int i = 0; i < vertexCount; i++)
            {
                if (i < pointOffsets.Length)
                {
                    var pointOffset = pointOffsets[i];
                    var position = positionAttrib.Get(pointOffset, m_CubeDetail.Points);
                    vertices[i] = position;
                }
                else
                {
                    Debug.LogWarning($"Vertex index {i} is out of range for point offsets.");
                }
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
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