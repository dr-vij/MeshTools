using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace PropellerheadMesh
{
    public struct Page
    {
        public int StartAddress;
        public int Length;
        public int Capacity;
    }

    public struct ActivePageEnumerator
    {
        private NativeBitArray m_ActivePages;
        private int m_CurrentIndex;
        private int m_MaxIndex;

        public ActivePageEnumerator(NativeBitArray activePages, int maxIndex)
        {
            m_ActivePages = activePages;
            m_CurrentIndex = -1;
            m_MaxIndex = maxIndex;
        }

        public bool MoveNext()
        {
            m_CurrentIndex++;
            while (m_CurrentIndex < m_MaxIndex)
            {
                if (m_ActivePages.IsSet(m_CurrentIndex))
                    return true;
                m_CurrentIndex++;
            }

            return false;
        }

        public int Current => m_CurrentIndex;

        public ActivePageEnumerator GetEnumerator() => this;
    }

    public struct NativeArray2D<T> : IDisposable where T : unmanaged
    {
        private NativeList<Page> m_Pages;
        private NativeBitArray m_ActivePages;
        private NativeList<T> m_DataRecords;
        private Allocator m_Allocator;
        private int m_DefaultPageSize;
        private int m_CurrentRecordIndex;
        private int m_ActivePageCount;

        public int Count => m_Pages.Length;
        public int ActivePageCount => m_ActivePageCount;

        public T this[int row, int column]
        {
            get
            {
                var page = m_Pages[row];
                var linearIndex = page.StartAddress + column;
                return m_DataRecords[linearIndex];
            }
            set
            {
                var page = m_Pages[row];
                var linearIndex = page.StartAddress + column;
                m_DataRecords[linearIndex] = value;
            }
        }

        public NativeArray2D(int initialCapacity, int defaultPageSize = 8, Allocator allocator = Allocator.Persistent)
        {
            m_DefaultPageSize = defaultPageSize;
            m_Allocator = allocator;
            m_Pages = new NativeList<Page>(initialCapacity, allocator);
            m_DataRecords = new NativeList<T>(initialCapacity * 4, allocator);
            m_ActivePages = new NativeBitArray(initialCapacity, allocator);
            m_CurrentRecordIndex = -1;
            m_ActivePageCount = 0;
        }

        public ActivePageEnumerator GetActivePageEnumerator()
        {
            return new ActivePageEnumerator(m_ActivePages, m_Pages.Length);
        }

        public Page GetPageInfo(int index) => m_Pages[index];

        public void GetActivePageIndices(NativeList<int> activeIndices)
        {
            activeIndices.Clear();
            if (activeIndices.Capacity < m_ActivePageCount)
                activeIndices.Capacity = m_ActivePageCount;

            for (int i = 0; i < m_Pages.Length; i++)
            {
                if (m_ActivePages.IsSet(i))
                    activeIndices.Add(i);
            }
        }

        private void EnsureActivePagesCapacity(int requiredCapacity)
        {
            if (requiredCapacity > m_ActivePages.Length)
            {
                int newCapacity = m_ActivePages.Length;
                while (newCapacity < requiredCapacity)
                    newCapacity *= 2;

                var newBitArray = new NativeBitArray(newCapacity, m_Allocator);
                newBitArray.Copy(0, ref m_ActivePages, 0, m_ActivePages.Length);

                m_ActivePages.Dispose();
                m_ActivePages = newBitArray;
            }
        }

        public int CreateArrayRecord()
        {
            int startAddress = m_DataRecords.Length;

            var page = new Page
            {
                StartAddress = startAddress,
                Length = 0,
                Capacity = m_DefaultPageSize
            };

            m_Pages.Add(page);
            m_CurrentRecordIndex = m_Pages.Length - 1;

            EnsureActivePagesCapacity(m_CurrentRecordIndex + 1);
            m_ActivePages.Set(m_CurrentRecordIndex, true);
            m_ActivePageCount++;

            for (int i = 0; i < m_DefaultPageSize; i++)
            {
                m_DataRecords.Add(default(T));
            }

            return m_CurrentRecordIndex;
        }

        public void Append(T element)
        {
            var page = m_Pages[m_CurrentRecordIndex];

            if (page.Length >= page.Capacity)
            {
                var newCapacity = page.Capacity + m_DefaultPageSize;

                for (int i = 0; i < m_DefaultPageSize; i++)
                    m_DataRecords.Add(default);

                page.Capacity = newCapacity;
            }

            var elementIndex = page.StartAddress + page.Length;
            m_DataRecords[elementIndex] = element;

            page.Length++;
            m_Pages[m_CurrentRecordIndex] = page;
        }

        public int AddArray(NativeArray<T> rowData)
        {
            int recordIndex = CreateArrayRecord();

            for (int i = 0; i < rowData.Length; i++)
                Append(rowData[i]);

            return recordIndex;
        }

        public int AppendAt(int recordIndex, T element)
        {
            if (recordIndex < 0 || recordIndex >= m_Pages.Length)
                return -1;

            var page = m_Pages[recordIndex];

            if (page.Length < page.Capacity)
            {
                var elementIndex = page.StartAddress + page.Length;
                m_DataRecords[elementIndex] = element;

                page.Length++;
                m_Pages[recordIndex] = page;

                return recordIndex;
            }

            if (recordIndex == m_CurrentRecordIndex)
            {
                m_CurrentRecordIndex = recordIndex;
                Append(element);
                return recordIndex;
            }

            m_ActivePages.Set(recordIndex, false);
            m_ActivePageCount--;

            var newRecordIndex = CreateArrayRecord();
            for (int i = 0; i < page.Length; i++)
                Append(m_DataRecords[page.StartAddress + i]);
            Append(element);
            return newRecordIndex;
        }
        
        public bool RemoveAtArray(int pageIndex, int elementIndex)
        {
            if (pageIndex < 0 || pageIndex >= m_Pages.Length)
                return false;
    
            if (!m_ActivePages.IsSet(pageIndex))
                return false;
    
            var page = m_Pages[pageIndex];
    
            if (elementIndex < 0 || elementIndex >= page.Length)
                return false;
    
            for (int i = elementIndex; i < page.Length - 1; i++)
            {
                int currentPos = page.StartAddress + i;
                int nextPos = page.StartAddress + i + 1;
                m_DataRecords[currentPos] = m_DataRecords[nextPos];
            }
    
            page.Length--;
            m_Pages[pageIndex] = page;
    
            if (page.Length == 0)
            {
                m_ActivePages.Set(pageIndex, false);
                m_ActivePageCount--;
            }
    
            return true;
        }

        public int GetLength(int rowIndex)
        {
            return m_Pages[rowIndex].Length;
        }

        public NativeSlice<T> GetRowSlice(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return new NativeSlice<T>(m_DataRecords.AsArray(), page.StartAddress, page.Length);
        }

        public NativeArray<T> GetRowArray(int rowIndex)
        {
            var page = m_Pages[rowIndex];
            return m_DataRecords.AsArray().GetSubArray(page.StartAddress, page.Length);
        }

        public void Dispose()
        {
            m_Pages.Dispose();
            m_DataRecords.Dispose();
            m_ActivePages.Dispose();
        }

        public JobHandle Dispose(JobHandle dependencies)
        {
            dependencies = m_Pages.Dispose(dependencies);
            dependencies = m_DataRecords.Dispose(dependencies);
            dependencies = m_ActivePages.Dispose(dependencies);
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
                if (m_ActivePages.IsSet(i))
                    action(i);
            }
        }

        public void ForEachActivePageSlice(Action<int, NativeSlice<T>> action)
        {
            for (int i = 0; i < m_Pages.Length; i++)
            {
                if (m_ActivePages.IsSet(i))
                {
                    var page = m_Pages[i];
                    var slice = new NativeSlice<T>(m_DataRecords.AsArray(), page.StartAddress, page.Length);
                    action(i, slice);
                }
            }
        }

        #endregion
    }
}