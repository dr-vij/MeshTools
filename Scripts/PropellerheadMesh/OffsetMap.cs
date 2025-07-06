using System.Collections.Generic;

namespace PropellerHead
{
    public class OffsetMap
    {
        private List<long> m_IndexToOffset = new();
        private Dictionary<long, int> m_OffsetToIndex = new();
        private long m_NextOffset;

        public long Allocate()
        {
            long offset = m_NextOffset++;
            int index = m_IndexToOffset.Count;
            m_IndexToOffset.Add(offset);
            m_OffsetToIndex[offset] = index;
            return offset;
        }

        public void Remove(long offset)
        {
            if (!m_OffsetToIndex.TryGetValue(offset, out int removedIndex))
                return;

            int lastIndex = m_IndexToOffset.Count - 1;
            if (removedIndex != lastIndex)
            {
                long lastOffset = m_IndexToOffset[lastIndex];
                m_IndexToOffset[removedIndex] = lastOffset;
                m_OffsetToIndex[lastOffset] = removedIndex;
            }

            m_IndexToOffset.RemoveAt(lastIndex);
            m_OffsetToIndex.Remove(offset);
        }

        public int GetIndex(long offset) => m_OffsetToIndex.GetValueOrDefault(offset, -1);

        public int Count => m_IndexToOffset.Count;
    }
}