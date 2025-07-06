using System;
using System.Collections.Generic;

namespace PropellerHead
{
    public interface IAttribute
    {
        Type DataType { get; }
        int ID { get; }
        object GetValue(long offset, OffsetMap map);
        void SetValue(long offset, object value, OffsetMap map);
    }

    public class Attribute<T> : IAttribute
    {
        private readonly List<T> m_Data = new();
        private readonly T m_DefaultValue;

        public Type DataType => typeof(T);
        public int ID { get; private set; }

        public Attribute(int id, T defaultVal = default)
        {
            ID = id;
            m_DefaultValue = defaultVal;
        }

        public T Get(long offset, OffsetMap map)
        {
            int index = map.GetIndex(offset);
            return index >= 0 && index < m_Data.Count ? m_Data[index] : m_DefaultValue;
        }

        public void Set(long offset, T value, OffsetMap map)
        {
            int index = map.GetIndex(offset);
            if (index >= 0)
            {
                while (m_Data.Count <= index) m_Data.Add(m_DefaultValue);
                m_Data[index] = value;
            }
        }

        public object GetValue(long offset, OffsetMap map) => Get(offset, map);

        public void SetValue(long offset, object value, OffsetMap map) => Set(offset, (T)value, map);
    }
}