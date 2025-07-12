using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PropellerheadMesh
{
    /// <summary>
    /// Burst-compiled job for calculating face normals and vertex normals with smooth angle support
    /// </summary>
    [BurstCompile]
    public struct NativeNormalsCalculationJob : IJob
    {
        // Input data
        [ReadOnly] public NativeArray<int> ValidPrimitives;
        [ReadOnly] public NativeArray<int> ValidVertices;
        [ReadOnly] public NativeArray<int> VertexToPoint;
        [ReadOnly] public NativeArray<int> PrimitiveVertexIndices;
        [ReadOnly] public NativeArray<int> PrimitiveOffsets;
        [ReadOnly] public NativeArray<int> PrimitiveCounts;
        [ReadOnly] public float SmoothAngle;
        
        // Accessors
        [ReadOnly] [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> PositionAccessor;
        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> VertexNormalAccessor;
        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> PrimitiveNormalAccessor;
        
        // Temporary arrays for calculations
        [NativeDisableParallelForRestriction] public NativeArray<float3> FaceNormals;
        [NativeDisableParallelForRestriction] public NativeArray<float> FaceAreas;
        [NativeDisableParallelForRestriction] public NativeHashMap<int, UnsafeList<int>> VertexToPrimitives;
        [NativeDisableParallelForRestriction] public NativeHashMap<int, UnsafeList<int>> PrimitiveToVertices;
        
        public void Execute()
        {
            // Step 1: Build vertex-to-primitives and primitive-to-vertices mappings
            BuildTopologyMappings();
            
            // Step 2: Calculate face normals and areas
            CalculateFaceNormals();
            
            // Step 3: Calculate vertex normals with smooth angle support
            CalculateVertexNormals();
        }
        
        private void BuildTopologyMappings()
        {
            // Clear existing mappings
            VertexToPrimitives.Clear();
            PrimitiveToVertices.Clear();
            
            // Build mappings for each valid primitive
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primitiveIndex = ValidPrimitives[i];
                
                if (primitiveIndex >= PrimitiveOffsets.Length || primitiveIndex >= PrimitiveCounts.Length)
                    continue;
                
                var offset = PrimitiveOffsets[primitiveIndex];
                var count = PrimitiveCounts[primitiveIndex];
                
                if (count < 3 || offset + count > PrimitiveVertexIndices.Length)
                    continue;
                
                // Create primitive-to-vertices mapping
                if (!PrimitiveToVertices.TryGetValue(primitiveIndex, out var primitiveVertices))
                {
                    primitiveVertices = new UnsafeList<int>(count, Allocator.Temp);
                    PrimitiveToVertices[primitiveIndex] = primitiveVertices;
                }
                
                // Add vertices to primitive mapping and build reverse mapping
                for (int j = 0; j < count; j++)
                {
                    var vertexIndex = PrimitiveVertexIndices[offset + j];
                    primitiveVertices.Add(vertexIndex);
                    
                    // Add to vertex-to-primitives mapping
                    if (!VertexToPrimitives.TryGetValue(vertexIndex, out var vertexPrimitives))
                    {
                        vertexPrimitives = new UnsafeList<int>(4, Allocator.Temp);
                        VertexToPrimitives[vertexIndex] = vertexPrimitives;
                    }
                    
                    vertexPrimitives.Add(primitiveIndex);
                }
            }
        }
        
        private void CalculateFaceNormals()
        {
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primitiveIndex = ValidPrimitives[i];
                
                if (!PrimitiveToVertices.TryGetValue(primitiveIndex, out var vertices) || vertices.Length < 3)
                {
                    FaceNormals[primitiveIndex] = float3.zero;
                    FaceAreas[primitiveIndex] = 0f;
                    continue;
                }
                
                // Get first three vertices for normal calculation
                var vertex0 = vertices[0];
                var vertex1 = vertices[1];
                var vertex2 = vertices[2];
                
                // Get point indices
                var point0 = VertexToPoint[vertex0];
                var point1 = VertexToPoint[vertex1];
                var point2 = VertexToPoint[vertex2];
                
                // Get positions
                var pos0 = PositionAccessor[point0];
                var pos1 = PositionAccessor[point1];
                var pos2 = PositionAccessor[point2];
                
                // Calculate face normal and area
                var edge1 = pos1 - pos0;
                var edge2 = pos2 - pos0;
                var cross = math.cross(edge1, edge2);
                var area = math.length(cross) * 0.5f;
                var normal = math.select(math.normalize(cross), float3.zero, area < 1e-7f);
                
                FaceNormals[primitiveIndex] = normal;
                FaceAreas[primitiveIndex] = area;
                
                // Set primitive normal attribute
                PrimitiveNormalAccessor[primitiveIndex] = normal;
            }
        }
        
        private void CalculateVertexNormals()
        {
            for (int i = 0; i < ValidVertices.Length; i++)
            {
                var vertexIndex = ValidVertices[i];
                
                if (!VertexToPrimitives.TryGetValue(vertexIndex, out var adjacentPrimitives))
                {
                    VertexNormalAccessor[vertexIndex] = new float3(0, 1, 0);
                    continue;
                }
                
                var primitiveCount = adjacentPrimitives.Length;
                
                if (primitiveCount == 0)
                {
                    VertexNormalAccessor[vertexIndex] = new float3(0, 1, 0);
                    continue;
                }
                
                if (primitiveCount == 1)
                {
                    // Single face - use face normal
                    var primIndex = adjacentPrimitives[0];
                    VertexNormalAccessor[vertexIndex] = FaceNormals[primIndex];
                    continue;
                }
                
                // Multiple faces - calculate smooth normal with angle consideration
                var vertexNormal = CalculateSmoothedVertexNormal(vertexIndex, adjacentPrimitives);
                VertexNormalAccessor[vertexIndex] = vertexNormal;
            }
        }
        
        private float3 CalculateSmoothedVertexNormal(int vertexIndex, UnsafeList<int> adjacentPrimitives)
        {
            var smoothNormal = float3.zero;
            var totalWeight = 0f;
            var hasHardEdges = false;
            
            // Check for hard edges first
            for (int i = 0; i < adjacentPrimitives.Length; i++)
            {
                var primIndex1 = adjacentPrimitives[i];
                var normal1 = FaceNormals[primIndex1];
                
                for (int j = i + 1; j < adjacentPrimitives.Length; j++)
                {
                    var primIndex2 = adjacentPrimitives[j];
                    var normal2 = FaceNormals[primIndex2];
                    
                    var angle = math.acos(math.clamp(math.dot(normal1, normal2), -1f, 1f));
                    
                    if (angle > SmoothAngle)
                    {
                        hasHardEdges = true;
                        break;
                    }
                }
                
                if (hasHardEdges)
                    break;
            }
            
            if (hasHardEdges)
            {
                // Find the face with the largest area as the dominant normal
                var maxArea = 0f;
                var dominantNormal = new float3(0, 1, 0);
                
                for (int i = 0; i < adjacentPrimitives.Length; i++)
                {
                    var primIndex = adjacentPrimitives[i];
                    var area = FaceAreas[primIndex];
                    
                    if (area > maxArea)
                    {
                        maxArea = area;
                        dominantNormal = FaceNormals[primIndex];
                    }
                }
                
                return dominantNormal;
            }
            
            // Smooth all faces together
            for (int i = 0; i < adjacentPrimitives.Length; i++)
            {
                var primIndex = adjacentPrimitives[i];
                var faceNormal = FaceNormals[primIndex];
                var weight = CalculateVertexWeightInFace(vertexIndex, primIndex);
                
                smoothNormal += faceNormal * weight;
                totalWeight += weight;
            }
            
            if (totalWeight > 0f)
            {
                smoothNormal /= totalWeight;
                return math.normalize(smoothNormal);
            }
            
            return new float3(0, 1, 0);
        }
        
        private float CalculateVertexWeightInFace(int vertexIndex, int primitiveIndex)
        {
            if (!PrimitiveToVertices.TryGetValue(primitiveIndex, out var vertices))
                return 1f;
            
            var vertexCount = vertices.Length;
            if (vertexCount < 3)
                return 1f;
            
            // Find vertex index in the primitive
            var localVertexIndex = -1;
            for (int i = 0; i < vertexCount; i++)
            {
                if (vertices[i] == vertexIndex)
                {
                    localVertexIndex = i;
                    break;
                }
            }
            
            if (localVertexIndex == -1)
                return 1f;
            
            // Calculate angle at this vertex
            var prevIndex = (localVertexIndex - 1 + vertexCount) % vertexCount;
            var nextIndex = (localVertexIndex + 1) % vertexCount;
            
            var currentVertex = vertices[localVertexIndex];
            var prevVertex = vertices[prevIndex];
            var nextVertex = vertices[nextIndex];
            
            var pointCurrent = VertexToPoint[currentVertex];
            var pointPrev = VertexToPoint[prevVertex];
            var pointNext = VertexToPoint[nextVertex];
            
            var posCurrent = PositionAccessor[pointCurrent];
            var posPrev = PositionAccessor[pointPrev];
            var posNext = PositionAccessor[pointNext];
            
            var edge1 = math.normalize(posPrev - posCurrent);
            var edge2 = math.normalize(posNext - posCurrent);
            
            var angle = math.acos(math.clamp(math.dot(edge1, edge2), -1f, 1f));
            
            // Use angle as weight - larger angles contribute more
            return math.max(angle, 0.1f);
        }
    }
    
    /// <summary>
    /// Native normals calculation operator for NativeDetail
    /// </summary>
    public static class NativeNormalsOperators
    {
        /// <summary>
        /// Calculates and sets vertex and primitive normals based on face angles
        /// </summary>
        /// <param name="detail">The NativeDetail containing the geometry</param>
        /// <param name="smoothAngle">The maximum angle (in radians) between faces to consider them smooth</param>
        /// <param name="dependency">Job dependency</param>
        /// <returns>Job handle for the normals calculation operation</returns>
        public static JobHandle CalculateNormals(ref NativeDetail detail, float smoothAngle = math.PI / 3f, JobHandle dependency = default)
        {
            // Ensure normal attributes exist
            if (!detail.HasVertexAttribute(AttributeID.Normal))
                detail.AddVertexAttribute<float3>(AttributeID.Normal);
            
            if (!detail.HasPrimitiveAttribute(AttributeID.Normal))
                detail.AddPrimitiveAttribute<float3>(AttributeID.Normal);
            
            // Get required accessors
            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) != AttributeMapResult.Success)
                return dependency;
            
            if (detail.GetVertexAttributeAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor) != AttributeMapResult.Success)
                return dependency;
            
            if (detail.GetPrimitiveAttributeAccessor<float3>(AttributeID.Normal, out var primitiveNormalAccessor) != AttributeMapResult.Success)
                return dependency;
            
            // Get valid elements
            var validPrimitives = new NativeList<int>(detail.PrimitiveCount, Allocator.TempJob);
            var validVertices = new NativeList<int>(detail.VertexCount, Allocator.TempJob);
            
            detail.GetAllValidPrimitives(validPrimitives);
            detail.GetAllValidVertices(validVertices);
            
            // Create temporary arrays for calculations
            var faceNormals = new NativeArray<float3>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            var faceAreas = new NativeArray<float>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            var vertexToPrimitives = new NativeHashMap<int, UnsafeList<int>>(detail.VertexCount, Allocator.TempJob);
            var primitiveToVertices = new NativeHashMap<int, UnsafeList<int>>(detail.PrimitiveCount, Allocator.TempJob);
            
            // Get vertex to point mapping
            var vertexToPoint = new NativeArray<int>(math.max(detail.VertexCount, 1), Allocator.TempJob);
            for (int i = 0; i < validVertices.Length; i++)
            {
                var vertexIndex = validVertices[i];
                var pointIndex = detail.GetVertexPoint(vertexIndex);
                if (pointIndex >= 0 && vertexIndex < vertexToPoint.Length)
                {
                    vertexToPoint[vertexIndex] = pointIndex;
                }
            }
            
            // Flatten primitive vertex data
            var (primitiveVertexIndices, primitiveOffsets, primitiveCounts) = FlattenPrimitiveData(ref detail, validPrimitives);
            
            // Create and schedule the job
            var job = new NativeNormalsCalculationJob
            {
                ValidPrimitives = validPrimitives.AsArray(),
                ValidVertices = validVertices.AsArray(),
                VertexToPoint = vertexToPoint,
                PrimitiveVertexIndices = primitiveVertexIndices,
                PrimitiveOffsets = primitiveOffsets,
                PrimitiveCounts = primitiveCounts,
                SmoothAngle = smoothAngle,
                PositionAccessor = positionAccessor,
                VertexNormalAccessor = vertexNormalAccessor,
                PrimitiveNormalAccessor = primitiveNormalAccessor,
                FaceNormals = faceNormals,
                FaceAreas = faceAreas,
                VertexToPrimitives = vertexToPrimitives,
                PrimitiveToVertices = primitiveToVertices
            };
            
            var handle = job.Schedule(dependency);
            
            // Cleanup в main thread после завершения job
            handle.Complete();
            
            // Dispose UnsafeLists in hash maps
            foreach (var kvp in vertexToPrimitives)
            {
                kvp.Value.Dispose();
            }
            vertexToPrimitives.Dispose();
            
            foreach (var kvp in primitiveToVertices)
            {
                kvp.Value.Dispose();
            }
            primitiveToVertices.Dispose();
            
            // Dispose other arrays
            faceNormals.Dispose();
            faceAreas.Dispose();
            primitiveVertexIndices.Dispose();
            primitiveOffsets.Dispose();
            primitiveCounts.Dispose();
            validPrimitives.Dispose();
            validVertices.Dispose();
            vertexToPoint.Dispose();
            
            return handle;
        }
        
        /// <summary>
        /// Flattens primitive vertex data into simple arrays for job processing
        /// </summary>
        private static (NativeArray<int> vertexIndices, NativeArray<int> offsets, NativeArray<int> counts) 
            FlattenPrimitiveData(ref NativeDetail detail, NativeList<int> validPrimitives)
        {
            // First pass: calculate total size needed
            var totalVertexCount = 0;
            for (int i = 0; i < validPrimitives.Length; i++)
            {
                var primIndex = validPrimitives[i];
                var buffer = detail.GetPrimitiveVertices(primIndex);
                if (buffer.IsCreated)
                {
                    totalVertexCount += buffer.Length;
                }
            }
            
            // Create arrays
            var vertexIndices = new NativeArray<int>(math.max(totalVertexCount, 1), Allocator.TempJob);
            var offsets = new NativeArray<int>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            var counts = new NativeArray<int>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            
            // Second pass: fill arrays
            var currentOffset = 0;
            for (int i = 0; i < validPrimitives.Length; i++)
            {
                var primIndex = validPrimitives[i];
                var buffer = detail.GetPrimitiveVertices(primIndex);
                
                if (buffer.IsCreated && primIndex < offsets.Length && primIndex < counts.Length)
                {
                    offsets[primIndex] = currentOffset;
                    counts[primIndex] = buffer.Length;
                    
                    // Copy vertex indices
                    for (int j = 0; j < buffer.Length && currentOffset + j < vertexIndices.Length; j++)
                    {
                        vertexIndices[currentOffset + j] = buffer[j].VertexIndex;
                    }
                    
                    currentOffset += buffer.Length;
                }
            }
            
            return (vertexIndices, offsets, counts);
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
        /// <param name="detail">The NativeDetail</param>
        /// <param name="vertexIndex">The vertex index</param>
        /// <returns>The current normal, or zero if not found</returns>
        public static float3 GetVertexNormal(ref NativeDetail detail, int vertexIndex)
        {
            return detail.GetVertexAttribute<float3>(vertexIndex, AttributeID.Normal);
        }
        
        /// <summary>
        /// Gets the current normal for a primitive
        /// </summary>
        /// <param name="detail">The NativeDetail</param>
        /// <param name="primitiveIndex">The primitive index</param>
        /// <returns>The current normal, or zero if not found</returns>
        public static float3 GetPrimitiveNormal(ref NativeDetail detail, int primitiveIndex)
        {
            return detail.GetPrimitiveAttribute<float3>(primitiveIndex, AttributeID.Normal);
        }
    }
}