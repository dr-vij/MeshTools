using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PropellerHead
{
    /// <summary>
    /// Manages offset-to-index mapping for sparse data structures with efficient removal
    /// Performance: O(1) allocate, O(1) remove, O(1) lookup
    /// Thread-safe: All operations are thread-safe
    /// Memory: Compact storage with minimal overhead
    /// </summary>
    public class OffsetMap : IDisposable
    {
        private readonly List<long> m_IndexToOffset = new List<long>();
        private readonly Dictionary<long, int> m_OffsetToIndex = new Dictionary<long, int>();
        private readonly ConcurrentQueue<OffsetRemovedEventData> m_EventQueue = new ConcurrentQueue<OffsetRemovedEventData>();
        private readonly ReaderWriterLockSlim m_Lock = new ReaderWriterLockSlim();
        private long m_NextOffset;
        private bool m_Disposed = false;
        private volatile bool m_EventProcessingEnabled = true;
        
        // Performance counters
        private long m_AllocateCount;
        private long m_RemoveCount;
        private long m_LookupCount;

        /// <summary>
        /// Event fired when an offset is removed, providing both the offset and its old index
        /// Events are fired asynchronously to prevent deadlocks
        /// </summary>
        public event Action<long, int> OnOffsetRemoved;

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public OffsetMapMetrics GetMetrics()
        {
            m_Lock.EnterReadLock();
            try
            {
                return new OffsetMapMetrics(
                    m_IndexToOffset.Count,
                    Interlocked.Read(ref m_AllocateCount),
                    Interlocked.Read(ref m_RemoveCount),
                    Interlocked.Read(ref m_LookupCount),
                    m_EventQueue.Count
                );
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Allocates a new offset and returns it
        /// Performance: O(1)
        /// </summary>
        /// <returns>The allocated offset</returns>
        public long Allocate()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterWriteLock();
            try
            {
                long offset = m_NextOffset++;
                int index = m_IndexToOffset.Count;
                m_IndexToOffset.Add(offset);
                m_OffsetToIndex[offset] = index;
                
                Interlocked.Increment(ref m_AllocateCount);
                return offset;
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
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
            
            OffsetRemovedEventData eventData = default(OffsetRemovedEventData);
            bool hasEvent = false;
            
            m_Lock.EnterWriteLock();
            try
            {
                if (!m_OffsetToIndex.TryGetValue(offset, out int removedIndex))
                    return false;

                int lastIndex = m_IndexToOffset.Count - 1;
                
                // Prepare event data while holding lock
                eventData = new OffsetRemovedEventData(offset, removedIndex);
                hasEvent = true;

                // Compact the arrays
                if (removedIndex != lastIndex)
                {
                    long lastOffset = m_IndexToOffset[lastIndex];
                    m_IndexToOffset[removedIndex] = lastOffset;
                    m_OffsetToIndex[lastOffset] = removedIndex;
                }

                m_IndexToOffset.RemoveAt(lastIndex);
                m_OffsetToIndex.Remove(offset);
                
                Interlocked.Increment(ref m_RemoveCount);
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
            
            // Fire event outside of lock to prevent deadlocks
            if (hasEvent && m_EventProcessingEnabled)
            {
                m_EventQueue.Enqueue(eventData);
                ProcessEventsAsync();
            }

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
            
            m_Lock.EnterReadLock();
            try
            {
                Interlocked.Increment(ref m_LookupCount);
                if (m_OffsetToIndex.TryGetValue(offset, out int index))
                    return index;
                return -1;
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
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
            
            m_Lock.EnterReadLock();
            try
            {
                return m_OffsetToIndex.ContainsKey(offset);
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
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
                
                m_Lock.EnterReadLock();
                try
                {
                    return m_IndexToOffset.Count;
                }
                finally
                {
                    m_Lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets all current offsets (for debugging/validation)
        /// Performance: O(n) - creates copy
        /// </summary>
        public IEnumerable<long> GetAllOffsets()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterReadLock();
            try
            {
                return new List<long>(m_IndexToOffset);
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Clears all offsets and resets the map
        /// Performance: O(n) due to event notifications
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            
            List<OffsetRemovedEventData> eventsToProcess = new List<OffsetRemovedEventData>();
            
            m_Lock.EnterWriteLock();
            try
            {
                // Collect events while holding lock
                if (m_EventProcessingEnabled)
                {
                    for (int i = 0; i < m_IndexToOffset.Count; i++)
                    {
                        eventsToProcess.Add(new OffsetRemovedEventData(m_IndexToOffset[i], i));
                    }
                }

                m_IndexToOffset.Clear();
                m_OffsetToIndex.Clear();
                m_NextOffset = 0;
            }
            finally
            {
                m_Lock.ExitWriteLock();
            }
            
            // Process events outside of lock
            if (eventsToProcess.Count > 0)
            {
                foreach (var eventData in eventsToProcess)
                {
                    m_EventQueue.Enqueue(eventData);
                }
                ProcessEventsAsync();
            }
        }

        /// <summary>
        /// Validates the internal consistency of the data structure
        /// </summary>
        public bool ValidateIntegrity()
        {
            ThrowIfDisposed();
            
            m_Lock.EnterReadLock();
            try
            {
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
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

        private async void ProcessEventsAsync()
        {
            if (!m_EventProcessingEnabled) return;
            
            await Task.Run(() =>
            {
                while (m_EventQueue.TryDequeue(out OffsetRemovedEventData eventData))
                {
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
            });
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(OffsetMap));
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            
            m_EventProcessingEnabled = false;
            
            // Process any remaining events
            while (m_EventQueue.TryDequeue(out OffsetRemovedEventData eventData))
            {
                try
                {
                    OnOffsetRemoved?.Invoke(eventData.Offset, eventData.Index);
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }
            
            m_Lock.EnterWriteLock();
            try
            {
                m_IndexToOffset.Clear();
                m_OffsetToIndex.Clear();
                OnOffsetRemoved = null;
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