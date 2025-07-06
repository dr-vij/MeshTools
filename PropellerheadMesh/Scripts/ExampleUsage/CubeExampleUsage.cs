using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using PropellerHead.Generators;
using PropellerHead.Operators;

namespace PropellerHead
{
    public class CubeExampleUsage : MonoBehaviour
    {
        private Detail m_CubeDetail;
        private GameObject m_DebugMeshObject;
        private PositionWiggler m_Wiggler;

        [Header("Debug Settings")] public bool ShowVertexSpheres = true;
        public bool ShowPrimitiveWireframes = true;
        public float SphereRadius = 0.05f;

        [Header("Wiggle Settings")] public float NoiseScale = 1f;
        public float WiggleAmplitude = 0.3f;
        public Vector3 TimeMultiplier = new(0.8f, 0.6f, 0.4f);
        public Vector3 NoiseOffset = new(0.3f, 0.9f, 0.7f);

        private void Start()
        {
            CreateCube();
            SetupWiggler();
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
            var size = new float3(2, 2, 2);
            m_CubeDetail.GenerateCube(size);

            var pointColorAttrib = new Attribute<float3>(AttribID.Color);
            m_CubeDetail.AddPointAttrib(pointColorAttrib);

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

            var pointOffsets = m_CubeDetail.Points.GetAllOffsets().ToArray();
            for (var i = 0; i < pointOffsets.Length && i < vertexColors.Length; i++)
            {
                pointColorAttrib.Set(pointOffsets[i], vertexColors[i], m_CubeDetail.Points);
            }

            var faceColors = new float3[]
            {
                new(1, 0, 0), // Back face - Red
                new(0, 1, 0), // Front face - Green
                new(0, 0, 1), // Bottom face - Blue
                new(1, 1, 0), // Top face - Yellow
                new(1, 0, 1), // Left face - Magenta
                new(0, 1, 1) // Right face - Cyan
            };

            var primitiveColorAttrib = new Attribute<float3>(AttribID.Register("face_color"));
            m_CubeDetail.AddPrimAttrib(primitiveColorAttrib);

            var primOffsets = m_CubeDetail.Prims.GetAllOffsets().ToArray();
            for (var faceIndex = 0; faceIndex < primOffsets.Length && faceIndex < faceColors.Length; faceIndex++)
            {
                var primOffset = primOffsets[faceIndex];
                primitiveColorAttrib.Set(primOffset, faceColors[faceIndex], m_CubeDetail.Prims);

                Debug.Log($"Face {faceIndex} assigned color {faceColors[faceIndex]} at primitive offset {primOffset}");
            }

            var stats = m_CubeDetail.GetStats();
            Debug.Log($"Cube created with {stats.PointCount} points, {stats.VertexCount} vertices, {stats.PrimCount} primitives");

            var detailedStats = m_CubeDetail.GetDetailedStats();
            Debug.Log($"Detailed stats: {detailedStats}");

            var isValid = m_CubeDetail.ValidateIntegrity();
            Debug.Log($"Cube structure is valid: {isValid}");
        }

        private void SetupWiggler()
        {
            m_Wiggler = new PositionWiggler();
            m_Wiggler.Initialize(m_CubeDetail);
            UpdateWigglerSettings();
        }

        private void UpdateWigglerSettings()
        {
            if (m_Wiggler == null)
                return;

            m_Wiggler.NoiseScale = NoiseScale;
            m_Wiggler.WiggleAmplitude = WiggleAmplitude;
            m_Wiggler.TimeMultiplier = TimeMultiplier;
            m_Wiggler.NoiseOffset = NoiseOffset;
        }

        private void Update()
        {
            if (m_Wiggler != null && m_CubeDetail != null)
            {
                UpdateWigglerSettings();
                m_Wiggler.Apply(m_CubeDetail, Time.time);
            }

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

            if (m_DebugMeshObject != null)
                Destroy(m_DebugMeshObject);

            m_DebugMeshObject = m_CubeDetail.CreateDebugObject(Color.black);

            if (m_DebugMeshObject != null)
            {
                m_DebugMeshObject.transform.SetParent(transform);
                m_DebugMeshObject.transform.localPosition = Vector3.zero;
            }
        }

        private void OnDrawGizmos()
        {
            if (m_CubeDetail == null)
                return;

            if (ShowVertexSpheres)
                m_CubeDetail.DrawDebugSpheres(SphereRadius, Color.black);

            if (ShowPrimitiveWireframes)
                m_CubeDetail.DrawDebugPrimitivesWithFaceColor(Color.black);
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

            var positionAttrib = m_CubeDetail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            var vertices = mesh.vertices;
            var vertexCount = Mathf.Min(vertices.Length, m_CubeDetail.Points.Count);
            var pointOffsets = m_CubeDetail.Points.GetAllOffsets().ToArray();

            for (int i = 0; i < vertexCount; i++)
            {
                if (i < pointOffsets.Length)
                {
                    var pointOffset = pointOffsets[i];
                    var position = positionAttrib.Get(pointOffset, m_CubeDetail.Points);
                    vertices[i] = position;
                }
                else
                    Debug.LogWarning($"Vertex index {i} is out of range for point offsets.");
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
        }

        private void OnDestroy()
        {
            if (m_DebugMeshObject != null)
            {
                if (Application.isPlaying)
                    Destroy(m_DebugMeshObject);
                else
                    DestroyImmediate(m_DebugMeshObject);
            }

            m_CubeDetail?.Dispose();
            m_Wiggler = null;
        }

        [ContextMenu("Reset Wiggle")]
        public void ResetWiggle()
        {
            m_Wiggler?.Reset(m_CubeDetail);
        }

        [ContextMenu("Reinitialize Wiggler")]
        public void ReinitializeWiggler()
        {
            m_Wiggler?.Initialize(m_CubeDetail);
        }
    }
}