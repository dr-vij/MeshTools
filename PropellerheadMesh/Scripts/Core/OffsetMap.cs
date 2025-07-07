using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PropellerHead
{
    /// <summary>
    /// Manages offset-to-index mapping for sparse data structures with efficient removal
    /// Performance: O(1) allocate, O(1) remove, O(1) lookup
    /// Memory: Compact storage with minimal overhead
    /// Single-threaded: Designed for Unity's single-threaded main loop
    /// </summary>
    public class OffsetMap : IDisposable
    {
        private readonly List<long> m_IndexToOffset = new List<long>();
        private readonly Dictionary<long, int> m_OffsetToIndex = new Dictionary<long, int>();
        private readonly List<OffsetRemovedEventData> m_EventBuffer = new List<OffsetRemovedEventData>();
        private long m_NextOffset;
        private bool m_Disposed = false;
        
        // Performance counters
        private long m_AllocateCount;
        private long m_RemoveCount;
        private long m_LookupCount;

        /// <summary>
        /// Event fired when an offset is removed, providing both the offset and its old index
        /// </summary>
        public event Action<long, int> OnOffsetRemoved;

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public OffsetMapMetrics GetMetrics()
        {
            return new OffsetMapMetrics(
                m_IndexToOffset.Count,
                m_AllocateCount,
                m_RemoveCount,
                m_LookupCount,
                m_EventBuffer.Count
            );
        }

        /// <summary>
        /// Allocates a new offset and returns it
        /// Performance: O(1)
        /// </summary>
        /// <returns>The allocated offset</returns>
        public long Allocate()
        {
            ThrowIfDisposed();
            
            long offset = m_NextOffset++;
            int index = m_IndexToOffset.Count;
            m_IndexToOffset.Add(offset);
            m_OffsetToIndex[offset] = index;
            
            m_AllocateCount++;
            return offset;
        }

        /// <summary>
        /// Removes an offset and compacts the index mapping
        /// Performance: O(1)
        /// </summary>
        /// <param name="offset">The offset to remove</param>
        /// <returns>True if the offset was found and removed, false otherwise</returns>
        public bool Remove(long offset)
        {
            ThrowIfDisposed();
            
            if (!m_OffsetToIndex.TryGetValue(offset, out int removedIndex))
                return false;

            int lastIndex = m_IndexToOffset.Count - 1;
            
            // Store event data
            m_EventBuffer.Add(new OffsetRemovedEventData(offset, removedIndex));

            // Compact the arrays
            if (removedIndex != lastIndex)
            {
                long lastOffset = m_IndexToOffset[lastIndex];
                m_IndexToOffset[removedIndex] = lastOffset;
                m_OffsetToIndex[lastOffset] = removedIndex;
            }

            m_IndexToOffset.RemoveAt(lastIndex);
            m_OffsetToIndex.Remove(offset);
            
            m_RemoveCount++;
            
            // Fire events immediately
            ProcessPendingEvents();
            
            return true;
        }

        /// <summary>
        /// Gets the index for a given offset
        /// Performance: O(1)
        /// </summary>
        /// <param name="offset">The offset to look up</param>
        /// <returns>The index, or -1 if not found</returns>
        public int GetIndex(long offset)
        {
            ThrowIfDisposed();
            
            m_LookupCount++;
            return m_OffsetToIndex.GetValueOrDefault(offset, -1);
        }

        /// <summary>
        /// Checks if an offset exists in the map
        /// Performance: O(1)
        /// </summary>
        /// <param name="offset">The offset to check</param>
        /// <returns>True if the offset exists, false otherwise</returns>
        public bool Contains(long offset)
        {
            ThrowIfDisposed();
            return m_OffsetToIndex.ContainsKey(offset);
        }

        /// <summary>
        /// Gets the number of allocated offsets
        /// Performance: O(1)
        /// </summary>
        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return m_IndexToOffset.Count;
            }
        }

        /// <summary>
        /// Gets all current offsets (for debugging/validation)
        /// Performance: O(n) - creates copy
        /// </summary>
        public IEnumerable<long> GetAllOffsets()
        {
            ThrowIfDisposed();
            return new List<long>(m_IndexToOffset);
        }

        /// <summary>
        /// Clears all offsets and resets the map
        /// Performance: O(n) due to event notifications
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            
            // Collect events for all existing offsets
            for (int i = 0; i < m_IndexToOffset.Count; i++)
            {
                m_EventBuffer.Add(new OffsetRemovedEventData(m_IndexToOffset[i], i));
            }

            m_IndexToOffset.Clear();
            m_OffsetToIndex.Clear();
            m_NextOffset = 0;
            
            // Process events
            ProcessPendingEvents();
        }

        /// <summary>
        /// Validates the internal consistency of the data structure
        /// </summary>
        public bool ValidateIntegrity()
        {
            ThrowIfDisposed();
            
            // Check that both mappings are consistent
            if (m_IndexToOffset.Count != m_OffsetToIndex.Count)
                return false;

            for (int i = 0; i < m_IndexToOffset.Count; i++)
            {
                long offset = m_IndexToOffset[i];
                if (!m_OffsetToIndex.TryGetValue(offset, out int mappedIndex) || mappedIndex != i)
                    return false;
            }

            return true;
        }

        private void ProcessPendingEvents()
        {
            while (m_EventBuffer.Count > 0)
            {
                var eventData = m_EventBuffer[0];
                m_EventBuffer.RemoveAt(0);
                
                try
                {
                    OnOffsetRemoved?.Invoke(eventData.Offset, eventData.Index);
                }
                catch (Exception ex)
                {
                    // Log error but don't propagate to prevent system failure
                    Debug.WriteLine($"Error processing OffsetMap event: {ex.Message}");
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(OffsetMap));
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            
            // Process any remaining events
            ProcessPendingEvents();
            
            m_IndexToOffset.Clear();
            m_OffsetToIndex.Clear();
            m_EventBuffer.Clear();
            OnOffsetRemoved = null;
            m_Disposed = true;
        }
    }

    /// <summary>
    /// Event data for offset removal notifications
    /// </summary>
    public struct OffsetRemovedEventData
    {
        public long Offset { get; private set; }
        public int Index { get; private set; }

        public OffsetRemovedEventData(long offset, int index)
        {
            Offset = offset;
            Index = index;
        }
    }

    /// <summary>
    /// Performance metrics for OffsetMap monitoring
    /// </summary>
    public struct OffsetMapMetrics
    {
        public int Count { get; private set; }
        public long AllocateCount { get; private set; }
        public long RemoveCount { get; private set; }
        public long LookupCount { get; private set; }
        public int EventQueueSize { get; private set; }

        public OffsetMapMetrics(int count, long allocateCount, long removeCount, long lookupCount, int eventQueueSize)
        {
            Count = count;
            AllocateCount = allocateCount;
            RemoveCount = removeCount;
            LookupCount = lookupCount;
            EventQueueSize = eventQueueSize;
        }
        
        public override string ToString()
        {
            return $"Count: {Count}, Allocates: {AllocateCount}, Removes: {RemoveCount}, " +
                   $"Lookups: {LookupCount}, EventQueue: {EventQueueSize}";
        }
    }
}