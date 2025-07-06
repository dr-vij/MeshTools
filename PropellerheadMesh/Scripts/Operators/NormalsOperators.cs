using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace PropellerHead.Operators
{
    public static class NormalsOperators
    {
        /// <summary>
        /// Calculates and sets vertex normals based on face angles
        /// </summary>
        /// <param name="detail">The detail object containing the geometry</param>
        /// <param name="smoothAngle">The maximum angle (in radians) between faces to consider them smooth</param>
        public static void CalculateNormals(Detail detail, float smoothAngle = math.PI / 3f)
        {
            if (detail == null)
                throw new ArgumentNullException(nameof(detail));

            // Get or create a normal attribute using the existing system ID
            var normalAttrib = detail.GetVertexAttrib<float3>(AttribID.Normal);
            if (normalAttrib == null)
            {
                normalAttrib = new Attribute<float3>(AttribID.Normal, float3.zero);
                detail.AddVertexAttrib(normalAttrib);
            }

            // Cache for face normals
            var faceNormals = new Dictionary<long, float3>();

            // Calculate face normals for all primitives
            foreach (var primPair in detail.Primitives)
            {
                var primitive = primPair.Value;
                var primOffset = primPair.Key;

                if (primitive.VertexCount < 3)
                    continue;

                var faceNormal = CalculateFaceNormal(detail, primitive);
                faceNormals[primOffset] = faceNormal;
            }

            // Calculate vertex normals
            foreach (var vertexPair in detail.VertexToPoint)
            {
                var vertexOffset = vertexPair.Key;
                var pointOffset = vertexPair.Value;

                var vertexNormal = CalculateVertexNormal(detail, vertexOffset, pointOffset,
                    faceNormals, smoothAngle);

                normalAttrib.Set(vertexOffset, vertexNormal, detail.Vertices);
            }
        }

        /// <summary>
        /// Calculates the normal for a single face/primitive
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="primitive">The primitive to calculate normal for</param>
        /// <returns>The calculated face normal</returns>
        private static float3 CalculateFaceNormal(Detail detail, Primitive primitive)
        {
            var vertices = primitive.VertexOffsets.ToList();
            if (vertices.Count < 3)
                return float3.zero;

            // Get positions of the first three vertices
            var pointOffset0 = detail.GetVertexPoint(vertices[0]);
            var pointOffset1 = detail.GetVertexPoint(vertices[1]);
            var pointOffset2 = detail.GetVertexPoint(vertices[2]);

            var pos0 = detail.GetPointPos(pointOffset0);
            var pos1 = detail.GetPointPos(pointOffset1);
            var pos2 = detail.GetPointPos(pointOffset2);

            // Calculate normal using cross-product
            var edge1 = pos1 - pos0;
            var edge2 = pos2 - pos0;
            var normal = math.normalize(math.cross(edge1, edge2));

            return math.any(math.isfinite(normal)) ? normal : float3.zero;
        }

        /// <summary>
        /// Calculates the normal for a vertex based on adjacent faces and smooth angle
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="vertexOffset">The vertex to calculate normal for</param>
        /// <param name="pointOffset">The point offset for the vertex</param>
        /// <param name="faceNormals">Cache of face normals</param>
        /// <param name="smoothAngle">Maximum angle for smoothing</param>
        /// <returns>The calculated vertex normal</returns>
        private static float3 CalculateVertexNormal(Detail detail, long vertexOffset, long pointOffset,
            Dictionary<long, float3> faceNormals, float smoothAngle)
        {
            var adjacentPrimitives = detail.GetPrimitivesForVertex(vertexOffset).ToList();
            if (adjacentPrimitives.Count == 0)
                return new float3(0, 1, 0); // Default up normal

            if (adjacentPrimitives.Count == 1)
            {
                // Single face - use face normal
                return faceNormals.TryGetValue(adjacentPrimitives[0], out var normal) ? normal : new float3(0, 1, 0);
            }

            // Multiple faces - smooth based on angle
            var smoothNormal = float3.zero;
            var totalWeight = 0f;

            foreach (var primOffset in adjacentPrimitives)
            {
                if (!faceNormals.TryGetValue(primOffset, out var faceNormal))
                    continue;

                var weight = CalculateVertexWeightInFace(detail, vertexOffset, primOffset);

                // Check if this face should be smoothed with others
                var shouldSmooth = ShouldSmoothWithAdjacentFaces(detail, primOffset, adjacentPrimitives,
                    faceNormals, smoothAngle);

                if (shouldSmooth)
                {
                    smoothNormal += faceNormal * weight;
                    totalWeight += weight;
                }
                else
                {
                    // Hard edge - use face normal directly
                    return faceNormal;
                }
            }

            if (totalWeight > 0)
            {
                smoothNormal /= totalWeight;
                return math.normalize(smoothNormal);
            }

            return new float3(0, 1, 0); // Fallback
        }

        /// <summary>
        /// Calculates the weight of a vertex contribution to a face normal
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <param name="primOffset">The primitive offset</param>
        /// <returns>Weight value for normal calculation</returns>
        private static float CalculateVertexWeightInFace(Detail detail, long vertexOffset, long primOffset)
        {
            var primitive = detail.GetPrimitive(primOffset);
            if (primitive == null)
                return 1f;

            var vertices = primitive.VertexOffsets.ToList();
            var vertexIndex = vertices.IndexOf(vertexOffset);

            if (vertexIndex == -1 || vertices.Count < 3)
                return 1f;

            // Calculate angle at this vertex
            var prevIndex = (vertexIndex - 1 + vertices.Count) % vertices.Count;
            var nextIndex = (vertexIndex + 1) % vertices.Count;

            var pointCurrent = detail.GetVertexPoint(vertices[vertexIndex]);
            var pointPrev = detail.GetVertexPoint(vertices[prevIndex]);
            var pointNext = detail.GetVertexPoint(vertices[nextIndex]);

            var posCurrent = detail.GetPointPos(pointCurrent);
            var posPrev = detail.GetPointPos(pointPrev);
            var posNext = detail.GetPointPos(pointNext);

            var edge1 = math.normalize(posPrev - posCurrent);
            var edge2 = math.normalize(posNext - posCurrent);

            var angle = math.acos(math.clamp(math.dot(edge1, edge2), -1f, 1f));

            // Use an angle as a weight-larger angles contribute more
            return math.max(angle, 0.1f);
        }

        /// <summary>
        /// Determines if a face should be smoothed with its adjacent faces
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="primOffset">The primitive offset</param>
        /// <param name="adjacentPrimitives">List of adjacent primitives</param>
        /// <param name="faceNormals">Cache of face normals</param>
        /// <param name="smoothAngle">Maximum angle for smoothing</param>
        /// <returns>True if smoothing should be applied</returns>
        private static bool ShouldSmoothWithAdjacentFaces(Detail detail, long primOffset,
            List<long> adjacentPrimitives, Dictionary<long, float3> faceNormals, float smoothAngle)
        {
            if (!faceNormals.TryGetValue(primOffset, out var currentNormal))
                return false;

            foreach (var adjacentPrimOffset in adjacentPrimitives)
            {
                if (adjacentPrimOffset == primOffset)
                    continue;

                if (!faceNormals.TryGetValue(adjacentPrimOffset, out var adjacentNormal))
                    continue;

                var angle = math.acos(math.clamp(math.dot(currentNormal, adjacentNormal), -1f, 1f));

                if (angle > smoothAngle)
                    return false; // Hard edge detected
            }

            return true;
        }

        /// <summary>
        /// Recalculates normals for specific vertices only
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="vertexOffsets">Specific vertices to recalculate</param>
        /// <param name="smoothAngle">The maximum angle for smoothing</param>
        public static void RecalculateNormalsForVertices(Detail detail, IEnumerable<long> vertexOffsets, float smoothAngle = math.PI / 3f)
        {
            if (detail == null)
                throw new ArgumentNullException(nameof(detail));

            var normalAttrib = detail.GetVertexAttrib<float3>(AttribID.Normal);
            if (normalAttrib == null)
                return;

            var faceNormals = new Dictionary<long, float3>();
            var affectedPrimitives = new HashSet<long>();

            // Find all primitives that need face normal recalculation
            foreach (var vertexOffset in vertexOffsets)
            {
                var primitives = detail.GetPrimitivesForVertex(vertexOffset);
                foreach (var primOffset in primitives)
                {
                    affectedPrimitives.Add(primOffset);
                }
            }

            // Recalculate face normals for affected primitives
            foreach (var primOffset in affectedPrimitives)
            {
                var primitive = detail.GetPrimitive(primOffset);
                if (primitive != null)
                {
                    faceNormals[primOffset] = CalculateFaceNormal(detail, primitive);
                }
            }

            // Recalculate vertex normals
            foreach (var vertexOffset in vertexOffsets)
            {
                var pointOffset = detail.GetVertexPoint(vertexOffset);
                if (pointOffset != -1)
                {
                    var vertexNormal = CalculateVertexNormal(detail, vertexOffset, pointOffset,
                        faceNormals, smoothAngle);

                    normalAttrib.Set(vertexOffset, vertexNormal, detail.Vertices);
                }
            }
        }

        /// <summary>
        /// Converts degrees to radians for smooth angle parameter
        /// </summary>
        /// <param name="degrees">Angle in degrees</param>
        /// <returns>Angle in radians</returns>
        public static float DegreesToRadians(float degrees)
        {
            return degrees * math.PI / 180f;
        }

        /// <summary>
        /// Gets the current normal for a vertex
        /// </summary>
        /// <param name="detail">The detail object</param>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <returns>The current normal, or zero if not found</returns>
        public static float3 GetVertexNormal(Detail detail, long vertexOffset)
        {
            if (detail == null)
                return float3.zero;

            var normalAttrib = detail.GetVertexAttrib<float3>(AttribID.Normal);
            return normalAttrib?.Get(vertexOffset, detail.Vertices) ?? float3.zero;
        }
    }
}