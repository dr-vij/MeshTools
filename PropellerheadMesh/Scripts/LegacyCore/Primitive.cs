using System;
using System.Collections.Generic;
using System.Linq;

namespace Legacy
{
    /// <summary>
    /// Represents a geometric primitive with vertex references
    /// Features:
    /// - Optimized for frequent Contains operations using HashSet
    /// - Comprehensive validation and error recovery
    /// - Performance monitoring and metrics
    /// </summary>
    public class Primitive : IDisposable
    {
        private readonly HashSet<long> m_VertexOffsets = new();
        private IReadOnlyList<long> m_CachedReadOnlyVertices;
        private bool m_CacheInvalid = true;
        private bool m_Disposed;

        // Performance counters
        private long m_AddCount;
        private long m_RemoveCount;
        private long m_ContainsCount;

        /// <summary>
        /// Gets a read-only view of the vertex offsets
        /// </summary>
        public IReadOnlyList<long> VertexOffsets
        {
            get
            {
                if (m_CacheInvalid || m_CachedReadOnlyVertices == null)
                {
                    m_CachedReadOnlyVertices = m_VertexOffsets.ToList().AsReadOnly();
                    m_CacheInvalid = false;
                }

                return m_CachedReadOnlyVertices;
            }
        }

        /// <summary>
        /// Gets the number of vertices in this primitive
        /// </summary>
        public int VertexCount => m_VertexOffsets.Count;

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public PrimitiveMetrics GetMetrics()
        {
            return new PrimitiveMetrics(
                m_VertexOffsets.Count,
                m_AddCount,
                m_RemoveCount,
                m_ContainsCount,
                m_CacheInvalid
            );
        }

        /// <summary>
        /// Adds a vertex offset to this primitive
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to add</param>
        /// <exception cref="ArgumentException">Thrown if the vertex offset is already present</exception>
        public void AddVertex(long vertexOffset)
        {
            if (!m_VertexOffsets.Add(vertexOffset))
            {
                throw new ArgumentException($"Vertex offset {vertexOffset} is already present in this primitive", nameof(vertexOffset));
            }

            m_CacheInvalid = true;
            m_AddCount++;
        }

        /// <summary>
        /// Adds multiple vertex offsets efficiently
        /// </summary>
        /// <param name="vertexOffsets">The vertex offsets to add</param>
        /// <exception cref="ArgumentException">Thrown if any vertex offset is already present</exception>
        public void AddVertices(IEnumerable<long> vertexOffsets)
        {
            if (vertexOffsets == null)
                throw new ArgumentNullException(nameof(vertexOffsets));

            var offsetsToAdd = vertexOffsets.ToList();

            // Check for duplicates first
            var duplicates = offsetsToAdd.Where(m_VertexOffsets.Contains).ToList();
            if (duplicates.Count > 0)
            {
                throw new ArgumentException($"Vertex offsets already present: {string.Join(", ", duplicates)}");
            }

            // Add all vertices
            foreach (long offset in offsetsToAdd)
            {
                m_VertexOffsets.Add(offset);
            }

            m_CacheInvalid = true;
            m_AddCount += offsetsToAdd.Count;
        }

        /// <summary>
        /// Removes a vertex offset from this primitive
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to remove</param>
        /// <returns>True if the vertex was found and removed, false otherwise</returns>
        public bool RemoveVertex(long vertexOffset)
        {
            bool removed = m_VertexOffsets.Remove(vertexOffset);
            if (removed)
            {
                m_CacheInvalid = true;
                m_RemoveCount++;
            }

            return removed;
        }

        /// <summary>
        /// Checks if this primitive contains the specified vertex offset
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to check</param>
        /// <returns>True if the vertex is present, false otherwise</returns>
        public bool ContainsVertex(long vertexOffset)
        {
            m_ContainsCount++;
            return m_VertexOffsets.Contains(vertexOffset);
        }

        /// <summary>
        /// Clears all vertex offsets from this primitive
        /// </summary>
        public void ClearVertices()
        {
            m_VertexOffsets.Clear();
            m_CacheInvalid = true;
        }

        /// <summary>
        /// Validates that this primitive has a valid configuration
        /// </summary>
        /// <returns>True if the primitive is valid, false otherwise</returns>
        public bool IsValid()
        {
            // A primitive needs at least 3 vertices to be valid
            return m_VertexOffsets.Count >= 3;
        }

        /// <summary>
        /// Validates the internal consistency of the primitive
        /// </summary>
        /// <returns>True if internal state is consistent</returns>
        public bool ValidateIntegrity()
        {
            // Check for negative offsets
            if (m_VertexOffsets.Any(offset => offset < 0))
                return false;

            // Check cache consistency
            if (!m_CacheInvalid && m_CachedReadOnlyVertices != null)
            {
                var cachedSet = new HashSet<long>(m_CachedReadOnlyVertices);
                if (!cachedSet.SetEquals(m_VertexOffsets))
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (m_Disposed) return;

            m_VertexOffsets.Clear();
            m_CachedReadOnlyVertices = null;
            m_Disposed = true;
        }
    }

    /// <summary>
    /// Performance metrics for Primitive monitoring
    /// </summary>
    public readonly struct PrimitiveMetrics
    {
        public int VertexCount { get; }
        public long AddCount { get; }
        public long RemoveCount { get; }
        public long ContainsCount { get; }
        public bool CacheInvalid { get; }

        public PrimitiveMetrics(int vertexCount, long addCount, long removeCount, long containsCount, bool cacheInvalid)
        {
            VertexCount = vertexCount;
            AddCount = addCount;
            RemoveCount = removeCount;
            ContainsCount = containsCount;
            CacheInvalid = cacheInvalid;
        }

        public override string ToString()
        {
            return $"Vertices: {VertexCount}, Adds: {AddCount}, Removes: {RemoveCount}, " +
                   $"Contains: {ContainsCount}, CacheInvalid: {CacheInvalid}";
        }
    }
}