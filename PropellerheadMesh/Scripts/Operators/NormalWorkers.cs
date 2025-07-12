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
    /// Ultra-fast direct access normals calculation job
    /// </summary>
    [BurstCompile]
    public struct FastNormalsCalculationJob : IJob
    {
        [ReadOnly] public NativeArray<int> ValidPrimitives;
        [ReadOnly] public NativeArray<int> ValidVertices;
        [ReadOnly] public float SmoothAngle;
        
        // Direct unsafe accessors to NativeDetail data
        [ReadOnly] [NativeDisableUnsafePtrRestriction] public unsafe void* DetailPtr;
        [ReadOnly] [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> PositionAccessor;
        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> VertexNormalAccessor;
        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> PrimitiveNormalAccessor;
        
        // Pre-allocated working arrays
        [NativeDisableParallelForRestriction] public NativeArray<float3> FaceNormals;
        [NativeDisableParallelForRestriction] public NativeArray<float> FaceAreas;
        
        // Vertex adjacency data - preallocated with max possible size
        [NativeDisableParallelForRestriction] public NativeArray<int> VertexPrimitiveData; // Packed adjacency data
        [NativeDisableParallelForRestriction] public NativeArray<int> VertexPrimitiveOffsets; // Offsets for each vertex
        [NativeDisableParallelForRestriction] public NativeArray<int> VertexPrimitiveCounts; // Count for each vertex
        
        public unsafe void Execute()
        {
            var detail = (NativeDetail*)DetailPtr;
            
            // Step 1: Calculate face normals and build adjacency in single pass
            BuildFaceNormalsAndAdjacency(detail);
            
            // Step 2: Calculate vertex normals using prebuilt adjacency
            CalculateVertexNormals();
        }
        
        private unsafe void BuildFaceNormalsAndAdjacency(NativeDetail* detail)
        {
            // Clear adjacency counters
            UnsafeUtility.MemClear(VertexPrimitiveCounts.GetUnsafePtr(), VertexPrimitiveCounts.Length * sizeof(int));
            
            // First pass: calculate face normals and count adjacencies
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primIndex = ValidPrimitives[i];
                var buffer = detail->GetPrimitiveVertices(primIndex);
                
                if (!buffer.IsCreated || buffer.Length < 3)
                {
                    FaceNormals[primIndex] = float3.zero;
                    FaceAreas[primIndex] = 0f;
                    PrimitiveNormalAccessor[primIndex] = float3.zero;
                    continue;
                }
                
                // Get first three vertices for normal calculation
                var v0 = buffer[0].VertexIndex;
                var v1 = buffer[1].VertexIndex;
                var v2 = buffer[2].VertexIndex;
                
                // Get positions
                var p0 = detail->GetVertexPoint(v0);
                var p1 = detail->GetVertexPoint(v1);
                var p2 = detail->GetVertexPoint(v2);
                
                var pos0 = PositionAccessor[p0];
                var pos1 = PositionAccessor[p1];
                var pos2 = PositionAccessor[p2];
                
                // Calculate face normal and area
                var edge1 = pos1 - pos0;
                var edge2 = pos2 - pos0;
                var cross = math.cross(edge1, edge2);
                var area = math.length(cross) * 0.5f;
                var normal = math.select(math.normalize(cross), float3.zero, area < 1e-7f);
                
                FaceNormals[primIndex] = normal;
                FaceAreas[primIndex] = area;
                PrimitiveNormalAccessor[primIndex] = normal;
                
                // Count adjacencies for each vertex in this primitive
                for (int j = 0; j < buffer.Length; j++)
                {
                    var vertexIndex = buffer[j].VertexIndex;
                    if (vertexIndex < VertexPrimitiveCounts.Length)
                    {
                        VertexPrimitiveCounts[vertexIndex]++;
                    }
                }
            }
            
            // Calculate offsets for packed adjacency data
            int currentOffset = 0;
            for (int i = 0; i < VertexPrimitiveOffsets.Length; i++)
            {
                VertexPrimitiveOffsets[i] = currentOffset;
                currentOffset += VertexPrimitiveCounts[i];
                VertexPrimitiveCounts[i] = 0; // Reset for second pass
            }
            
            // Second pass: fill adjacency data
            for (int i = 0; i < ValidPrimitives.Length; i++)
            {
                var primIndex = ValidPrimitives[i];
                var buffer = detail->GetPrimitiveVertices(primIndex);
                
                if (!buffer.IsCreated || buffer.Length < 3)
                    continue;
                
                for (int j = 0; j < buffer.Length; j++)
                {
                    var vertexIndex = buffer[j].VertexIndex;
                    if (vertexIndex < VertexPrimitiveOffsets.Length)
                    {
                        var offset = VertexPrimitiveOffsets[vertexIndex];
                        var count = VertexPrimitiveCounts[vertexIndex];
                        
                        if (offset + count < VertexPrimitiveData.Length)
                        {
                            VertexPrimitiveData[offset + count] = primIndex;
                            VertexPrimitiveCounts[vertexIndex]++;
                        }
                    }
                }
            }
        }
        
        private void CalculateVertexNormals()
        {
            for (int i = 0; i < ValidVertices.Length; i++)
            {
                var vertexIndex = ValidVertices[i];
                
                if (vertexIndex >= VertexPrimitiveOffsets.Length || vertexIndex >= VertexPrimitiveCounts.Length)
                {
                    VertexNormalAccessor[vertexIndex] = new float3(0, 1, 0);
                    continue;
                }
                
                var offset = VertexPrimitiveOffsets[vertexIndex];
                var count = VertexPrimitiveCounts[vertexIndex];
                
                if (count == 0)
                {
                    VertexNormalAccessor[vertexIndex] = new float3(0, 1, 0);
                    continue;
                }
                
                if (count == 1)
                {
                    // Single face - use face normal
                    var primIndex = VertexPrimitiveData[offset];
                    VertexNormalAccessor[vertexIndex] = FaceNormals[primIndex];
                    continue;
                }
                
                // Multiple faces - calculate smooth normal with angle consideration
                var vertexNormal = CalculateSmoothedVertexNormal(offset, count);
                VertexNormalAccessor[vertexIndex] = vertexNormal;
            }
        }
        
        private float3 CalculateSmoothedVertexNormal(int offset, int count)
        {
            var smoothNormal = float3.zero;
            var totalWeight = 0f;
            var hasHardEdges = false;
            
            // Check for hard edges first (optimized)
            for (int i = 0; i < count && !hasHardEdges; i++)
            {
                var primIndex1 = VertexPrimitiveData[offset + i];
                var normal1 = FaceNormals[primIndex1];
                
                for (int j = i + 1; j < count; j++)
                {
                    var primIndex2 = VertexPrimitiveData[offset + j];
                    var normal2 = FaceNormals[primIndex2];
                    
                    var dot = math.dot(normal1, normal2);
                    var angle = math.acos(math.clamp(dot, -1f, 1f));
                    
                    if (angle > SmoothAngle)
                    {
                        hasHardEdges = true;
                        break;
                    }
                }
            }
            
            if (hasHardEdges)
            {
                // Find the face with the largest area as the dominant normal
                var maxArea = 0f;
                var dominantNormal = new float3(0, 1, 0);
                
                for (int i = 0; i < count; i++)
                {
                    var primIndex = VertexPrimitiveData[offset + i];
                    var area = FaceAreas[primIndex];
                    
                    if (area > maxArea)
                    {
                        maxArea = area;
                        dominantNormal = FaceNormals[primIndex];
                    }
                }
                
                return dominantNormal;
            }
            
            // Smooth all faces together - simplified weighting
            for (int i = 0; i < count; i++)
            {
                var primIndex = VertexPrimitiveData[offset + i];
                var faceNormal = FaceNormals[primIndex];
                var weight = FaceAreas[primIndex]; // Use area as weight for simplicity
                
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
    }
    
    /// <summary>
    /// Optimized native normals calculation operator
    /// </summary>
    public static class NativeNormalsOperators
    {
        /// <summary>
        /// Ultra-fast normals calculation using direct memory access
        /// </summary>
        public static unsafe JobHandle CalculateNormals(ref NativeDetail detail, float smoothAngle = math.PI / 3f, JobHandle dependency = default)
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
            var validPrimitives = new NativeArray<int>(detail.PrimitiveCount, Allocator.TempJob);
            var validVertices = new NativeArray<int>(detail.VertexCount, Allocator.TempJob);
            
            int primCount = 0;
            int vertCount = 0;
            
            // Fill valid arrays directly
            for (int i = 0; i < detail.PrimitiveCount; i++)
            {
                if (detail.IsPrimitiveValid(i))
                {
                    validPrimitives[primCount++] = i;
                }
            }
            
            for (int i = 0; i < detail.VertexCount; i++)
            {
                if (detail.IsVertexValid(i))
                {
                    validVertices[vertCount++] = i;
                }
            }
            
            // Resize arrays to actual count
            var actualValidPrimitives = new NativeArray<int>(primCount, Allocator.TempJob);
            var actualValidVertices = new NativeArray<int>(vertCount, Allocator.TempJob);
            
            UnsafeUtility.MemCpy(actualValidPrimitives.GetUnsafePtr(), validPrimitives.GetUnsafePtr(), primCount * sizeof(int));
            UnsafeUtility.MemCpy(actualValidVertices.GetUnsafePtr(), validVertices.GetUnsafePtr(), vertCount * sizeof(int));
            
            validPrimitives.Dispose();
            validVertices.Dispose();
            
            // Estimate maximum adjacency data size (conservative estimate)
            var maxAdjacencySize = math.max(primCount * 6, 1024); // Assume max 6 primitives per vertex on average
            
            // Create working arrays
            var faceNormals = new NativeArray<float3>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            var faceAreas = new NativeArray<float>(math.max(detail.PrimitiveCount, 1), Allocator.TempJob);
            var vertexPrimitiveData = new NativeArray<int>(maxAdjacencySize, Allocator.TempJob);
            var vertexPrimitiveOffsets = new NativeArray<int>(math.max(detail.VertexCount, 1), Allocator.TempJob);
            var vertexPrimitiveCounts = new NativeArray<int>(math.max(detail.VertexCount, 1), Allocator.TempJob);
            
            // Get unsafe pointer to NativeDetail
            var detailPtr = UnsafeUtility.AddressOf(ref detail);
            
            // Create and schedule the job
            var job = new FastNormalsCalculationJob
            {
                ValidPrimitives = actualValidPrimitives,
                ValidVertices = actualValidVertices,
                SmoothAngle = smoothAngle,
                DetailPtr = detailPtr,
                PositionAccessor = positionAccessor,
                VertexNormalAccessor = vertexNormalAccessor,
                PrimitiveNormalAccessor = primitiveNormalAccessor,
                FaceNormals = faceNormals,
                FaceAreas = faceAreas,
                VertexPrimitiveData = vertexPrimitiveData,
                VertexPrimitiveOffsets = vertexPrimitiveOffsets,
                VertexPrimitiveCounts = vertexPrimitiveCounts
            };
            
            var handle = job.Schedule(dependency);
            
            // Cleanup resources after job completion
            handle.Complete();
            
            actualValidPrimitives.Dispose();
            actualValidVertices.Dispose();
            faceNormals.Dispose();
            faceAreas.Dispose();
            vertexPrimitiveData.Dispose();
            vertexPrimitiveOffsets.Dispose();
            vertexPrimitiveCounts.Dispose();
            
            return handle;
        }
        
        /// <summary>
        /// Converts degrees to radians for smooth angle parameter
        /// </summary>
        public static float DegreesToRadians(float degrees)
        {
            return degrees * math.PI / 180f;
        }
        
        /// <summary>
        /// Gets the current normal for a vertex
        /// </summary>
        public static float3 GetVertexNormal(ref NativeDetail detail, int vertexIndex)
        {
            return detail.GetVertexAttribute<float3>(vertexIndex, AttributeID.Normal);
        }
        
        /// <summary>
        /// Gets the current normal for a primitive
        /// </summary>
        public static float3 GetPrimitiveNormal(ref NativeDetail detail, int primitiveIndex)
        {
            return detail.GetPrimitiveAttribute<float3>(primitiveIndex, AttributeID.Normal);
        }
    }
}