using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace PropellerheadMesh
{
    public struct NativeDetail : IDisposable
    {
        private bool m_Disposed;
        private readonly Allocator m_Allocator;

        private AttributeMap m_PointAttribs;
        private AttributeMap m_VertexAttribs;
        private AttributeMap m_PrimAttribs;

        private NativeList<int> m_VertexToPoint;
        private NativeList<int> m_PointToVerticesData;
        private NativeList<int> m_PointToVerticesStarts;
        private NativeList<int> m_PointToVerticesCounts;
        
        private NativeList<int> m_VertexToPrimitivesData;
        private NativeList<int> m_VertexToPrimitivesStarts;
        private NativeList<int> m_VertexToPrimitivesCounts;

        private NativeList<int> m_PrimPointIndices;
        private NativeList<int> m_PrimStarts;
        private NativeList<int> m_PrimCounts;

        private NativeList<int> m_FreePointIndices;
        private NativeList<int> m_FreeVertexIndices;
        private NativeList<int> m_FreePrimIndices;

        private NativeBitArray m_ValidPoints;
        private NativeBitArray m_ValidVertices;
        private NativeBitArray m_ValidPrimitives;

        private int m_PointCount;
        private int m_VertexCount;
        private int m_PrimCount;

        public NativeDetail(int initialCapacity, Allocator allocator)
        {
            m_Disposed = false;
            m_Allocator = allocator;

            m_PointCount = m_VertexCount = m_PrimCount = 0;

            m_PointAttribs = new AttributeMap(8, allocator);
            m_VertexAttribs = new AttributeMap(8, allocator);
            m_PrimAttribs = new AttributeMap(8, allocator);

            m_VertexToPoint = new NativeList<int>(initialCapacity, allocator);
            
            m_PointToVerticesData = new NativeList<int>(initialCapacity * 2, allocator);
            m_PointToVerticesStarts = new NativeList<int>(initialCapacity, allocator);
            m_PointToVerticesCounts = new NativeList<int>(initialCapacity, allocator);

            m_VertexToPrimitivesData = new NativeList<int>(initialCapacity * 2, allocator);
            m_VertexToPrimitivesStarts = new NativeList<int>(initialCapacity, allocator);
            m_VertexToPrimitivesCounts = new NativeList<int>(initialCapacity, allocator);

            m_PrimPointIndices = new NativeList<int>(initialCapacity * 4, allocator);
            m_PrimStarts = new NativeList<int>(initialCapacity, allocator);
            m_PrimCounts = new NativeList<int>(initialCapacity, allocator);

            m_FreePointIndices = new NativeList<int>(allocator);
            m_FreeVertexIndices = new NativeList<int>(allocator);
            m_FreePrimIndices = new NativeList<int>(allocator);

            m_ValidPoints = new NativeBitArray(initialCapacity, allocator);
            m_ValidVertices = new NativeBitArray(initialCapacity, allocator);
            m_ValidPrimitives = new NativeBitArray(initialCapacity, allocator);

            m_PointAttribs.RegisterAttribute<float3>(AttributeID.Position, initialCapacity);
        }

        public bool PointExists(int pointIndex) => pointIndex < m_ValidPoints.Length && m_ValidPoints.IsSet(pointIndex);
        public bool VertexExists(int vertexIndex) => vertexIndex < m_ValidVertices.Length && m_ValidVertices.IsSet(vertexIndex);
        public bool PrimitiveExists(int primIndex) => primIndex < m_ValidPrimitives.Length && m_ValidPrimitives.IsSet(primIndex);

        public int PointCount => m_PointCount - m_FreePointIndices.Length;
        public int VertexCount => m_VertexCount - m_FreeVertexIndices.Length;
        public int PrimitiveCount => m_PrimCount - m_FreePrimIndices.Length;

        public AttributeMapResult AddPointAttrib<T>(int attributeId, int capacity) where T : unmanaged
        {
            return m_PointAttribs.RegisterAttribute<T>(attributeId, capacity);
        }

        public AttributeMapResult AddVertexAttrib<T>(int attributeId, int capacity) where T : unmanaged
        {
            return m_VertexAttribs.RegisterAttribute<T>(attributeId, capacity);
        }

        public AttributeMapResult AddPrimAttrib<T>(int attributeId, int capacity) where T : unmanaged
        {
            return m_PrimAttribs.RegisterAttribute<T>(attributeId, capacity);
        }

        public NativeAttributeAccessor<T> GetPointAccessor<T>(int attributeId) where T : unmanaged
        {
            return m_PointAttribs.GetAccessorUnchecked<T>(attributeId);
        }

        public NativeAttributeAccessor<T> GetVertexAccessor<T>(int attributeId) where T : unmanaged
        {
            return m_VertexAttribs.GetAccessorUnchecked<T>(attributeId);
        }

        public NativeAttributeAccessor<T> GetPrimAccessor<T>(int attributeId) where T : unmanaged
        {
            return m_PrimAttribs.GetAccessorUnchecked<T>(attributeId);
        }

        public AttributeMapResult RemovePointAttrib(int attributeId)
        {
            return m_PointAttribs.RemoveAttribute(attributeId);
        }

        public AttributeMapResult RemoveVertexAttrib(int attributeId)
        {
            return m_VertexAttribs.RemoveAttribute(attributeId);
        }

        public AttributeMapResult RemovePrimAttrib(int attributeId)
        {
            return m_PrimAttribs.RemoveAttribute(attributeId);
        }

        public int AddPoint(float3 pos)
        {
            int index = AllocatePointIndex();
            m_ValidPoints.Set(index, true);

            var posAccessor = m_PointAttribs.GetAccessorUnchecked<float3>(AttributeID.Position);
            posAccessor[index] = pos;

            EnsurePointToVerticesCapacity(index);
            m_PointToVerticesStarts[index] = m_PointToVerticesData.Length;
            m_PointToVerticesCounts[index] = 0;

            return index;
        }

        public bool RemovePoint(int pointIndex)
        {
            if (!PointExists(pointIndex))
                return false;

            if (m_PointToVerticesCounts[pointIndex] > 0)
            {
                int start = m_PointToVerticesStarts[pointIndex];
                int count = m_PointToVerticesCounts[pointIndex];
                
                for (int i = 0; i < count; i++)
                {
                    int vertexIndex = m_PointToVerticesData[start + i];
                    RemoveVertex(vertexIndex);
                }
            }

            m_ValidPoints.Set(pointIndex, false);
            m_FreePointIndices.Add(pointIndex);

            return true;
        }

        public int AddVertex(int pointIndex)
        {
            int vertexIndex = AllocateVertexIndex();
            m_ValidVertices.Set(vertexIndex, true);
            
            EnsureVertexToPointCapacity(vertexIndex);
            m_VertexToPoint[vertexIndex] = pointIndex;

            AddVertexToPoint(pointIndex, vertexIndex);

            EnsureVertexToPrimitivesCapacity(vertexIndex);
            m_VertexToPrimitivesStarts[vertexIndex] = m_VertexToPrimitivesData.Length;
            m_VertexToPrimitivesCounts[vertexIndex] = 0;

            return vertexIndex;
        }

        public bool RemoveVertex(int vertexIndex)
        {
            if (!VertexExists(vertexIndex))
                return false;

            if (m_VertexToPrimitivesCounts[vertexIndex] > 0)
            {
                int start = m_VertexToPrimitivesStarts[vertexIndex];
                int count = m_VertexToPrimitivesCounts[vertexIndex];
                
                for (int i = 0; i < count; i++)
                {
                    int primIndex = m_VertexToPrimitivesData[start + i];
                    RemoveVertexFromPrimitive(primIndex, vertexIndex);
                }
            }

            int pointIndex = m_VertexToPoint[vertexIndex];
            RemoveVertexFromPoint(pointIndex, vertexIndex);
            m_VertexToPoint[vertexIndex] = -1;

            m_ValidVertices.Set(vertexIndex, false);
            m_FreeVertexIndices.Add(vertexIndex);

            return true;
        }

        public int GetVertexPoint(int vertexIndex)
        {
            return vertexIndex < m_VertexToPoint.Length ? m_VertexToPoint[vertexIndex] : -1;
        }

        public NativeArray<int> GetVerticesForPoint(int pointIndex, Allocator allocator)
        {
            if (!PointExists(pointIndex) || pointIndex >= m_PointToVerticesCounts.Length)
                return new NativeArray<int>(0, allocator);

            int count = m_PointToVerticesCounts[pointIndex];
            if (count == 0)
                return new NativeArray<int>(0, allocator);

            var result = new NativeArray<int>(count, allocator);
            int start = m_PointToVerticesStarts[pointIndex];
            
            for (int i = 0; i < count; i++)
            {
                result[i] = m_PointToVerticesData[start + i];
            }

            return result;
        }

        public int AddPrim(NativeArray<int> pointIndices)
        {
            int primIndex = AllocatePrimIndex();
            m_ValidPrimitives.Set(primIndex, true);

            EnsurePrimCapacity(primIndex);
            m_PrimStarts[primIndex] = m_PrimPointIndices.Length;
            m_PrimCounts[primIndex] = pointIndices.Length;

            for (int i = 0; i < pointIndices.Length; i++)
            {
                int pointIndex = pointIndices[i];
                int vertexIndex = AddVertex(pointIndex);
                m_PrimPointIndices.Add(pointIndex);
                AddPrimitiveToVertex(vertexIndex, primIndex);
            }

            return primIndex;
        }

        public bool RemovePrim(int primIndex)
        {
            if (!PrimitiveExists(primIndex))
                return false;

            int start = m_PrimStarts[primIndex];
            int count = m_PrimCounts[primIndex];

            for (int i = 0; i < count; i++)
            {
                int pointIndex = m_PrimPointIndices[start + i];
                var vertices = GetVerticesForPoint(pointIndex, Allocator.Temp);
                
                foreach (var vertexIndex in vertices)
                {
                    RemovePrimitiveFromVertex(vertexIndex, primIndex);
                    RemoveVertex(vertexIndex);
                }
                
                vertices.Dispose();
            }

            for (int i = 0; i < count; i++)
            {
                m_PrimPointIndices[start + i] = -1;
            }

            m_ValidPrimitives.Set(primIndex, false);
            m_FreePrimIndices.Add(primIndex);

            return true;
        }

        public NativeArray<int> GetPrimPoints(int primIndex, Allocator allocator)
        {
            if (!PrimitiveExists(primIndex))
                return new NativeArray<int>(0, allocator);

            int start = m_PrimStarts[primIndex];
            int count = m_PrimCounts[primIndex];
            var result = new NativeArray<int>(count, allocator);
            
            for (int i = 0; i < count; i++)
            {
                result[i] = m_PrimPointIndices[start + i];
            }

            return result;
        }

        private int AllocatePointIndex()
        {
            if (m_FreePointIndices.Length > 0)
            {
                int index = m_FreePointIndices[^1];
                m_FreePointIndices.Length--;
                return index;
            }

            int newIndex = m_PointCount++;
            if (newIndex >= m_ValidPoints.Length)
            {
                int newCapacity = math.max(m_ValidPoints.Length * 2, newIndex + 1);
                m_ValidPoints.Resize(newCapacity);
                m_PointAttribs.ResizeAllAttributes(newCapacity);
            }

            return newIndex;
        }

        private int AllocateVertexIndex()
        {
            if (m_FreeVertexIndices.Length > 0)
            {
                int index = m_FreeVertexIndices[^1];
                m_FreeVertexIndices.Length--;
                return index;
            }

            int newIndex = m_VertexCount++;
            if (newIndex >= m_ValidVertices.Length)
            {
                int newCapacity = math.max(m_ValidVertices.Length * 2, newIndex + 1);
                m_ValidVertices.Resize(newCapacity, NativeArrayOptions.ClearMemory);
                m_VertexAttribs.ResizeAllAttributes(newCapacity);
            }

            return newIndex;
        }

        private int AllocatePrimIndex()
        {
            if (m_FreePrimIndices.Length > 0)
            {
                int index = m_FreePrimIndices[^1];
                m_FreePrimIndices.Length--;
                return index;
            }

            int newIndex = m_PrimCount++;
            if (newIndex >= m_ValidPrimitives.Length)
            {
                int newCapacity = math.max(m_ValidPrimitives.Length * 2, newIndex + 1);
                m_ValidPrimitives.Resize(newCapacity, NativeArrayOptions.ClearMemory);
                m_PrimAttribs.ResizeAllAttributes(newCapacity);
            }

            return newIndex;
        }

        private void EnsurePointToVerticesCapacity(int pointIndex)
        {
            while (m_PointToVerticesStarts.Length <= pointIndex)
            {
                m_PointToVerticesStarts.Add(0);
                m_PointToVerticesCounts.Add(0);
            }
        }

        private void EnsureVertexToPointCapacity(int vertexIndex)
        {
            while (m_VertexToPoint.Length <= vertexIndex)
            {
                m_VertexToPoint.Add(-1);
            }
        }

        private void EnsureVertexToPrimitivesCapacity(int vertexIndex)
        {
            while (m_VertexToPrimitivesStarts.Length <= vertexIndex)
            {
                m_VertexToPrimitivesStarts.Add(0);
                m_VertexToPrimitivesCounts.Add(0);
            }
        }

        private void EnsurePrimCapacity(int primIndex)
        {
            while (m_PrimStarts.Length <= primIndex)
            {
                m_PrimStarts.Add(0);
                m_PrimCounts.Add(0);
            }
        }

        private void AddVertexToPoint(int pointIndex, int vertexIndex)
        {
            m_PointToVerticesData.Add(vertexIndex);
            m_PointToVerticesCounts[pointIndex]++;
        }

        private void RemoveVertexFromPoint(int pointIndex, int vertexIndex)
        {
            int start = m_PointToVerticesStarts[pointIndex];
            int count = m_PointToVerticesCounts[pointIndex];
            
            for (int i = 0; i < count; i++)
            {
                if (m_PointToVerticesData[start + i] == vertexIndex)
                {
                    m_PointToVerticesData[start + i] = -1;
                    break;
                }
            }
        }

        private void AddPrimitiveToVertex(int vertexIndex, int primIndex)
        {
            m_VertexToPrimitivesData.Add(primIndex);
            m_VertexToPrimitivesCounts[vertexIndex]++;
        }

        private void RemovePrimitiveFromVertex(int vertexIndex, int primIndex)
        {
            int start = m_VertexToPrimitivesStarts[vertexIndex];
            int count = m_VertexToPrimitivesCounts[vertexIndex];
            
            for (int i = 0; i < count; i++)
            {
                if (m_VertexToPrimitivesData[start + i] == primIndex)
                {
                    m_VertexToPrimitivesData[start + i] = -1;
                    break;
                }
            }
        }

        private void RemoveVertexFromPrimitive(int primIndex, int vertexIndex)
        {
            int start = m_PrimStarts[primIndex];
            int count = m_PrimCounts[primIndex];
            
            for (int i = 0; i < count; i++)
            {
                if (m_PrimPointIndices[start + i] == vertexIndex)
                {
                    m_PrimPointIndices[start + i] = -1;
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_PointAttribs.Dispose();
            m_VertexAttribs.Dispose();
            m_PrimAttribs.Dispose();

            m_VertexToPoint.Dispose();
            m_PointToVerticesData.Dispose();
            m_PointToVerticesStarts.Dispose();
            m_PointToVerticesCounts.Dispose();

            m_VertexToPrimitivesData.Dispose();
            m_VertexToPrimitivesStarts.Dispose();
            m_VertexToPrimitivesCounts.Dispose();

            m_PrimPointIndices.Dispose();
            m_PrimStarts.Dispose();
            m_PrimCounts.Dispose();

            m_FreePointIndices.Dispose();
            m_FreeVertexIndices.Dispose();
            m_FreePrimIndices.Dispose();

            m_ValidPoints.Dispose();
            m_ValidVertices.Dispose();
            m_ValidPrimitives.Dispose();

            m_Disposed = true;
        }
    }
}