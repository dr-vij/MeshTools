using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace PropellerheadMesh
{
    public static class DetailVisualizer
    {
        public static void DrawPointGizmos(this NativeDetail detail, float pointSize, Color pointColor)
        {
            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);
            var initialColor = Gizmos.color;
            Gizmos.color = pointColor;
            for (int i = 0; i < detail.PointCount; i++)
            {
                float3 pos = positionAccessor[i];
                Gizmos.DrawWireCube(pos, new float3(pointSize, pointSize, pointSize));
            }

            Gizmos.color = initialColor;
        }

        public static void DrawVertexNormalsGizmos(this NativeDetail detail, float normalLength, Color normalColor)
        {
            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);
            detail.GetVertexAttributeAccessor<float3>(AttributeID.Normal, out var normalAccessor);

            var initialColor = Gizmos.color;
            Gizmos.color = normalColor;

            for (int i = 0; i < detail.VertexCount; i++)
            {
                int pointIndex = detail.GetVertexPoint(i);
                float3 pos = positionAccessor[pointIndex];
                float3 normal = normalAccessor[i];
                float3 endPoint = pos + normal * normalLength;

                Gizmos.DrawLine(pos, endPoint);
            }

            Gizmos.color = initialColor;
        }

        public static void DrawPointNumbers(this NativeDetail detail, Color textColor, float offset = 0.1f)
        {
#if UNITY_EDITOR

            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);

            var style = new GUIStyle
            {
                normal =
                {
                    textColor = textColor
                },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            for (int i = 0; i < detail.PointCount; i++)
            {
                float3 pos = positionAccessor[i];
                Vector3 labelPos = pos + new float3(offset, offset, offset);
                Handles.Label(labelPos, i.ToString(), style);
            }
#endif
        }
    }
}