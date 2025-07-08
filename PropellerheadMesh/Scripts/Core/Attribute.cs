using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PropellerHead
{
    /// <summary>
    /// Interface for type-erased attribute access
    /// </summary>
    public interface IAttribute : IDisposable
    {
        Type DataType { get; }
        int ID { get; }
        string Name { get; }
        object GetValue(long offset);
        void SetValue(long offset, object value);
        bool HasValue(long offset);
        void RemoveValue(long offset);
        int AllocatedCount { get; }
    }

    /// <summary>
    /// Simple generic attribute storage using direct offset mapping
    /// </summary>
    /// <typeparam name="T">The type of data stored in this attribute</typeparam>
    public class Attribute<T> : IAttribute
    {
        private readonly Dictionary<long, T> m_Values = new();
        private readonly T m_DefaultValue;
        private bool m_Disposed;

        public Type DataType => typeof(T);
        public int ID { get; private set; }
        public string Name { get; }
        public int AllocatedCount => m_Values.Count;

        public Attribute(int id, T defaultVal = default)
        {
            if (id < 0)
                throw new ArgumentException("Attribute ID must be non-negative", nameof(id));

            ID = id;
            m_DefaultValue = defaultVal;
            Name = AttribID.GetName(id) ?? $"UnknownAttribute_{id}";
        }

        public T Get(long offset)
        {
            ThrowIfDisposed();
            return m_Values.GetValueOrDefault(offset, m_DefaultValue);
        }

        public void Set(long offset, T value)
        {
            ThrowIfDisposed();

            if (EqualityComparer<T>.Default.Equals(value, m_DefaultValue))
            {
                m_Values.Remove(offset);
            }
            else
            {
                m_Values[offset] = value;
            }
        }

        public bool HasValue(long offset)
        {
            ThrowIfDisposed();
            return m_Values.ContainsKey(offset);
        }

        public void RemoveValue(long offset)
        {
            ThrowIfDisposed();
            m_Values.Remove(offset);
        }

        public object GetValue(long offset) => Get(offset);

        public void SetValue(long offset, object value)
        {
            if (value == null)
            {
                if (!typeof(T).IsClass && Nullable.GetUnderlyingType(typeof(T)) == null)
                    throw new ArgumentNullException(nameof(value), $"Cannot set null value for non-nullable type {typeof(T)}");
                
                Set(offset, default);
                return;
            }

            try
            {
                Set(offset, (T)value);
            }
            catch (InvalidCastException)
            {
                throw new ArgumentException($"Cannot cast {value.GetType().Name} to {typeof(T).Name}", nameof(value));
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException($"Attribute<{typeof(T).Name}>");
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            
            m_Values.Clear();
            m_Disposed = true;
        }
    }
}