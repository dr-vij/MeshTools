using System;
using Unity.Collections;
using Unity.Jobs;

namespace PropellerheadMesh
{
    public struct Page
    {
        public int StartIndex;
        public int DataLength;
        public int Capacity;
    }

    public struct ActivePageEnumerator
    {
        private NativeList<Page> m_ActivePages;
        private int m_CurrentIndex;

        public ActivePageEnumerator(NativeList<Page> activePages)
        {
            m_ActivePages = activePages;
            m_CurrentIndex = -1;
        }

        public bool MoveNext()
        {
            m_CurrentIndex++;
            return m_CurrentIndex < m_ActivePages.Length;
        }

        public Page CurrentPage => m_ActivePages[m_CurrentIndex];
        
        public int CurrentIndex => m_CurrentIndex;

        public ActivePageEnumerator GetEnumerator() => this;
    }

    public struct NativeArray2D<T> : IDisposable where T : unmanaged
    {
        private NativeList<Page> m_Pages;
        private NativeList<T> m_DataRecords;
        private Allocator m_Allocator;
        private int m_DefaultPageSize;
        private int m_LastRecordIndex;

        public int Count => m_Pages.Length;

        public T this[int row, int column]
        {
            get
            {
                var page = m_Pages[row];
                var linearIndex = page.StartIndex + column;
                return m_DataRecords[linearIndex];
            }
            set
            {
                var page = m_Pages[row];
                var linearIndex = page.StartIndex + column;
                m_DataRecords[linearIndex] = value;
            }
        }

        public NativeArray2D(int initialCapacity, int defaultPageSize = 8, Allocator allocator = Allocator.Persistent)
        {
            m_DefaultPageSize = defaultPageSize;
            m_Allocator = allocator;
            m_Pages = new NativeList<Page>(initialCapacity, allocator);
            m_DataRecords = new NativeList<T>(initialCapacity * 4, allocator);
            m_LastRecordIndex = -1;
        }

        public ActivePageEnumerator GetActivePageEnumerator()
        {
            return new ActivePageEnumerator(m_Pages);
        }

        public Page GetPageInfo(int index) => m_Pages[index];

        public int CreateArrayRecord(int pageSize = -1)
        {
            int startAddress = m_DataRecords.Length;
            int actualPageSize = pageSize < 0 ? m_DefaultPageSize : pageSize;

            var page = new Page
            {
                StartIndex = startAddress,
                DataLength = 0,
                Capacity = actualPageSize
            };

            m_Pages.Add(page);
            m_LastRecordIndex = m_Pages.Length - 1;

            for (int i = 0; i < actualPageSize; i++)
            {
                m_DataRecords.Add(default(T));
            }

            return m_LastRecordIndex;
        }

        public void Append(T element)
        {
            var page = m_Pages[m_LastRecordIndex];

            if (page.DataLength >= page.Capacity)
            {
                var newCapacity = page.Capacity + m_DefaultPageSize;

                for (int i = 0; i < m_DefaultPageSize; i++)
                    m_DataRecords.Add(default);

                page.Capacity = newCapacity;
            }

            var elementIndex = page.StartIndex + page.DataLength;
            m_DataRecords[elementIndex] = element;

            page.DataLength++;
            m_Pages[m_LastRecordIndex] = page;
        }

        public int AddArray(NativeArray<T> rowData)
        {
            int recordIndex = CreateArrayRecord();

            for (int i = 0; i < rowData.Length; i++)
                Append(rowData[i]);

            return recordIndex;
        }

        public bool AppendAt(int recordIndex, T element)
        {
            if (recordIndex < 0 || recordIndex >= m_Pages.Length)
                return false;

            var page = m_Pages[recordIndex];

            if (page.DataLength < page.Capacity)
            {
                var elementIndex = page.StartIndex + page.DataLength;
                m_DataRecords[elementIndex] = element;

                page.DataLength++;
                m_Pages[recordIndex] = page;

                return true;
            }

            if (recordIndex == m_LastRecordIndex)
            {
                Append(element);
                return true;
            }

            int newStartIndex = m_DataRecords.Length;
            int newCapacity = page.Capacity * 2;

            for (int i = 0; i < newCapacity; i++)
                m_DataRecords.Add(default);

            for (int i = 0; i < page.DataLength; i++)
                m_DataRecords[newStartIndex + i] = m_DataRecords[page.StartIndex + i];

            m_DataRecords[newStartIndex + page.DataLength] = element;

            page.StartIndex = newStartIndex;
            page.DataLength++;
            page.Capacity = newCapacity;
            m_Pages[recordIndex] = page;

            m_LastRecordIndex = recordIndex;

            return true;
        }

        public bool RemoveAtArray(int pageIndex, int elementIndex)
        {
            if (pageIndex < 0 || pageIndex >= m_Pages.Length)
                return false;

            var page = m_Pages[pageIndex];

            if (elementIndex < 0 || elementIndex >= page.DataLength)
                return false;

            for (int i = elementIndex; i < page.DataLength - 1; i++)
            {
                int currentPos = page.StartIndex + i;
                int nextPos = page.StartIndex + i + 1;
                m_DataRecords[currentPos] = m_DataRecords[nextPos];
            }

            page.DataLength--;
            m_Pages[pageIndex] = page;
            return true;
        }

        public int GetLength(int rowIndex)
        {
            return m_Pages[rowIndex].DataLength;
        }

        public NativeSlice<T> GetRowSlice(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return new NativeSlice<T>(m_DataRecords.AsArray(), page.StartIndex, page.DataLength);
        }

        public NativeArray<T> GetRowArray(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return m_DataRecords.AsArray().GetSubArray(page.StartIndex, page.DataLength);
        }

        public void Dispose()
        {
            m_Pages.Dispose();
            m_DataRecords.Dispose();
        }

        public JobHandle Dispose(JobHandle dependencies)
        {
            dependencies = m_Pages.Dispose(dependencies);
            dependencies = m_DataRecords.Dispose(dependencies);
            return dependencies;
        }

        #region Managed Zone

        public int AddArray(T[] rowData)
        {
            using var nativeData = new NativeArray<T>(rowData, Allocator.Temp);
            return AddArray(nativeData);
        }

        public void ForEachActivePage(Action<int> action)
        {
            for (int i = 0; i < m_Pages.Length; i++)
            {
                action(i);
            }
        }

        public void ForEachActivePageSlice(Action<int, NativeSlice<T>> action)
        {
            for (int i = 0; i < m_Pages.Length; i++)
            {
                var page = m_Pages[i];
                var slice = new NativeSlice<T>(m_DataRecords.AsArray(), page.StartIndex, page.DataLength);
                action(i, slice);
            }
        }

        #endregion
    }
}