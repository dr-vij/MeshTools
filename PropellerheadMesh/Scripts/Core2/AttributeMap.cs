using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using System;

namespace PropellerheadMesh
{
    public enum AttributeMapResult : byte
    {
        Success = 0,
        AttributeAlreadyExists = 1,
        AttributeNotFound = 2,
        TypeMismatch = 3,
        IndexOutOfRange = 4
    }

    public unsafe struct AttributeMap : IDisposable
    {
        public struct AttributeMetadata
        {
            public int Stride;
            public int ElementCount;
            public int TypeHash;
        }

        private struct AttributeEntry
        {
            public UnsafeList<byte> Buffer;
            public int Stride;
            public int TypeHash;
        }

        private UnsafeParallelHashMap<int, AttributeEntry> m_Attributes;
        private readonly Allocator m_Allocator;

        public AttributeMap(int estimatedAttributeCount, Allocator allocator)
        {
            m_Attributes = new UnsafeParallelHashMap<int, AttributeEntry>(estimatedAttributeCount, allocator);
            m_Allocator = allocator;
        }

        public AttributeMapResult RegisterAttribute<T>(int attributeId, int elementCount) where T : unmanaged
        {
            if (m_Attributes.TryGetValue(attributeId, out _))
                return AttributeMapResult.AttributeAlreadyExists;

            var stride = UnsafeUtility.SizeOf<T>();
            var buffer = new UnsafeList<byte>(stride * elementCount, m_Allocator);
            buffer.Length = stride * elementCount;

            m_Attributes[attributeId] = new AttributeEntry
            {
                Buffer = buffer,
                Stride = stride,
                TypeHash = BurstRuntime.GetHashCode32<T>()
            };

            return AttributeMapResult.Success;
        }

        public AttributeMapResult ResizeAttribute<T>(int attributeId, int newElementCount) where T : unmanaged
        {
            if (!m_Attributes.TryGetValue(attributeId, out var entry))
                return AttributeMapResult.AttributeNotFound;

            if (entry.TypeHash != BurstRuntime.GetHashCode32<T>())
                return AttributeMapResult.TypeMismatch;

            int newSize = newElementCount * entry.Stride;
            entry.Buffer.Resize(newSize);
            m_Attributes[attributeId] = entry;

            return AttributeMapResult.Success;
        }

        public AttributeMapResult RemoveAttribute(int attributeId)
        {
            if (m_Attributes.TryGetValue(attributeId, out var entry))
            {
                if (entry.Buffer.IsCreated)
                    entry.Buffer.Dispose();

                m_Attributes.Remove(attributeId);
                return AttributeMapResult.Success;
            }

            return AttributeMapResult.AttributeNotFound;
        }

        public AttributeMapResult TryGetPointer<T>(int attributeId, int index, out T* ptr) where T : unmanaged
        {
            ptr = null;

            if (!m_Attributes.TryGetValue(attributeId, out var entry))
                return AttributeMapResult.AttributeNotFound;

            if (entry.TypeHash != BurstRuntime.GetHashCode32<T>())
                return AttributeMapResult.TypeMismatch;

            int count = entry.Buffer.Length / entry.Stride;
            if ((uint)index >= (uint)count)
                return AttributeMapResult.IndexOutOfRange;

            ptr = (T*)(entry.Buffer.Ptr + index * entry.Stride);
            return AttributeMapResult.Success;
        }

        public T* GetPointerUnchecked<T>(int attributeId, int index) where T : unmanaged
        {
            var entry = m_Attributes[attributeId];
            return (T*)(entry.Buffer.Ptr + index * entry.Stride);
        }

        public T* GetBasePointerUnchecked<T>(int attributeId) where T : unmanaged
        {
            var entry = m_Attributes[attributeId];
            return (T*)entry.Buffer.Ptr;
        }

        public AttributeMapResult TryGetBasePointer<T>(int attributeId, out T* ptr) where T : unmanaged
        {
            ptr = null;

            if (!m_Attributes.TryGetValue(attributeId, out var entry))
                return AttributeMapResult.AttributeNotFound;

            if (entry.TypeHash != BurstRuntime.GetHashCode32<T>())
                return AttributeMapResult.TypeMismatch;

            ptr = (T*)entry.Buffer.Ptr;
            return AttributeMapResult.Success;
        }

        public bool TryGetMetadata(int attributeId, out AttributeMetadata meta)
        {
            if (m_Attributes.TryGetValue(attributeId, out var entry))
            {
                meta = new AttributeMetadata
                {
                    Stride = entry.Stride,
                    ElementCount = entry.Buffer.Length / entry.Stride,
                    TypeHash = entry.TypeHash
                };
                return true;
            }

            meta = default;
            return false;
        }

        public AttributeMapResult TryGetAccessor<T>(int attributeId, out AttributeAccessor<T> accessor) where T : unmanaged
        {
            accessor = default;

            if (!m_Attributes.TryGetValue(attributeId, out var entry))
                return AttributeMapResult.AttributeNotFound;

            if (entry.TypeHash != BurstRuntime.GetHashCode32<T>())
                return AttributeMapResult.TypeMismatch;

            int elementCount = entry.Buffer.Length / entry.Stride;
            accessor = new AttributeAccessor<T>(entry.Buffer, entry.Stride, elementCount);
            return AttributeMapResult.Success;
        }

        public AttributeAccessor<T> GetAccessorUnchecked<T>(int attributeId) where T : unmanaged
        {
            var entry = m_Attributes[attributeId];
            int elementCount = entry.Buffer.Length / entry.Stride;
            return new AttributeAccessor<T>(entry.Buffer, entry.Stride, elementCount);
        }

        public void Dispose()
        {
            var keys = m_Attributes.GetKeyArray(Allocator.Temp);
            foreach (var id in keys)
            {
                var entry = m_Attributes[id];
                if (entry.Buffer.IsCreated)
                    entry.Buffer.Dispose();
            }

            keys.Dispose();

            if (m_Attributes.IsCreated)
                m_Attributes.Dispose();
        }
    }
}