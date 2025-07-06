using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace PropellerHead
{
    /// <summary>
    /// Represents a geometric primitive with vertex references
    /// Features:
    /// - Thread-safe operations with minimal locking
    /// - Optimized for frequent Contains operations using HashSet
    /// - Comprehensive validation and error recovery
    /// - Performance monitoring and metrics
    /// </summary>
    public class Primitive : IDisposable
    {
        private readonly HashSet<long> m_VertexOffsets = new();
        private readonly ReaderWriterLockSlim m_Lock = new();
        private IReadOnlyList<long> m_CachedReadOnlyVertices;
        private volatile bool m_CacheInvalid = true;
        private volatile bool m_Disposed = false;
        
        // Performance counters
        private long m_AddCount;
        private long m_RemoveCount;
        private long m_ContainsCount;

        /// <summary>
        /// Gets a read-only view of the vertex offsets
        /// Performance: O(1) when cached, O(n) when cache invalid
        /// </summary>
        public IReadOnlyList<long> VertexOffsets
        {
            get
            {
                ThrowIfDisposed();
                
                // Fast path: check cache without lock
                if (!m_CacheInvalid && m_CachedReadOnlyVertices != null)
                    return m_CachedReadOnlyVertices;
                
                m_Lock.EnterWriteLock();
                try
                {
                    // Double-check after acquiring lock
                    if (m_CacheInvalid || m_CachedReadOnlyVertices == null)
                    {
                        m_CachedReadOnlyVertices = m_VertexOffsets.ToList().AsReadOnly();
                        m_CacheInvalid = false;
                    }
                    return m_CachedReadOnlyVertices;
                }
                finally
                {
                    m_Lock.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Gets the number of vertices in this primitive
        /// Performance: O(1)
        /// </summary>
        public int VertexCount
        {
            get
            {
                ThrowIfDisposed();
                
                m_Lock.EnterReadLock();
                try
                {
                    return m_VertexOffsets.Count;
                }
                finally
                {
                    m_Lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public PrimitiveMetrics GetMetrics()
        {
            m_Lock.EnterReadLock();
            try
            {
                return new PrimitiveMetrics(
                    m_VertexOffsets.Count,
                    Interlocked.Read(ref m_AddCount),
                    Interlocked.Read(ref m_RemoveCount),
                    Interlocked.Read(ref m_ContainsCount),
                    m_CacheInvalid
                );
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Adds a vertex offset to this primitive
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to add</param>
        /// <exception cref="ArgumentException">Thrown if the vertex offset is already present</exception>
        public void AddVertex(long vertexOffset)
        {
            ThrowIfDisposed();
            
            m_Lock.EnterWriteLock();
            try
            {
                if (!m_VertexOffsets.Add(vertexOffset))
                {
                    throw new ArgumentException($"Vertex offset {vertexOffset} is already present in this primitive", nameof(vertexOffset));
                }

                m_CacheInvalid = true;
                Interlocked.Increment(ref m_AddCount);
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds multiple vertex offsets efficiently
        /// Performance: O(n) where n is number of vertices to add
        /// </summary>
        /// <param name="vertexOffsets">The vertex offsets to add</param>
        /// <exception cref="ArgumentException">Thrown if any vertex offset is already present</exception>
        public void AddVertices(IEnumerable<long> vertexOffsets)
        {
            if (vertexOffsets == null)
                throw new ArgumentNullException(nameof(vertexOffsets));

            ThrowIfDisposed();
            
            var offsetsToAdd = vertexOffsets.ToList();
            
            m_Lock.EnterWriteLock();
            try
            {
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
                Interlocked.Add(ref m_AddCount, offsetsToAdd.Count);
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a vertex offset from this primitive
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to remove</param>
        /// <returns>True if the vertex was found and removed, false otherwise</returns>
        public bool RemoveVertex(long vertexOffset)
        {
            ThrowIfDisposed();
            
            m_Lock.EnterWriteLock();
            try
            {
                bool removed = m_VertexOffsets.Remove(vertexOffset);
                if (removed)
                {
                    m_CacheInvalid = true;
                    Interlocked.Increment(ref m_RemoveCount);
                }
                return removed;
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Checks if this primitive contains the specified vertex offset
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to check</param>
        /// <returns>True if the vertex is present, false otherwise</returns>
        public bool ContainsVertex(long vertexOffset)
        {
            ThrowIfDisposed();
            
            m_Lock.EnterReadLock();
            try
            {
                Interlocked.Increment(ref m_ContainsCount);
                return m_VertexOffsets.Contains(vertexOffset);
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Clears all vertex offsets from this primitive
        /// Performance: O(n)
        /// </summary>
        public void ClearVertices()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterWriteLock();
            try
            {
                m_VertexOffsets.Clear();
                m_CacheInvalid = true;
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Validates that this primitive has a valid configuration
        /// </summary>
        /// <returns>True if the primitive is valid, false otherwise</returns>
        public bool IsValid()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterReadLock();
            try
            {
                // A primitive needs at least 3 vertices to be valid
                return m_VertexOffsets.Count >= 3;
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Validates the internal consistency of the primitive
        /// </summary>
        /// <returns>True if internal state is consistent</returns>
        public bool ValidateIntegrity()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterReadLock();
            try
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
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Compacts the internal storage (HashSet is already compact)
        /// </summary>
        public void Compact()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterWriteLock();
            try
            {
                // Force cache rebuild
                m_CacheInvalid = true;
                m_CachedReadOnlyVertices = null;
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Primitive));
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            
            m_Lock.EnterWriteLock();
            try
            {
                m_VertexOffsets.Clear();
                m_CachedReadOnlyVertices = null;
                m_Disposed = true;
            }
            finally
            {
                m_Lock.ExitWriteLock();
                m_Lock.Dispose();
            }
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