
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Unity.Mathematics;

namespace PropellerHead
{
    /// <summary>
    /// Represents a geometric detail with points, vertices, and primitives
    /// </summary>
    public class Detail : IDisposable
    {
        private readonly object m_Lock = new object();
        private bool m_Disposed = false;

        public OffsetMap Points { get; private set; } = new();
        public OffsetMap Vertices { get; private set; } = new();
        public OffsetMap Prims { get; private set; } = new();

        private readonly Dictionary<int, IAttribute> m_PointAttribs = new();
        private readonly Dictionary<int, IAttribute> m_VertexAttribs = new();
        private readonly Dictionary<int, IAttribute> m_PrimAttribs = new();

        private readonly Dictionary<long, Primitive> m_Primitives = new();
        private readonly Dictionary<long, long> m_VertexToPoint = new();
        
        // Reverse lookup for optimization
        private readonly Dictionary<long, HashSet<long>> m_PointToVertices = new();
        private readonly Dictionary<long, HashSet<long>> m_VertexToPrimitives = new();

        // Cached read-only views
        private IReadOnlyDictionary<int, IAttribute> m_CachedPointAttribs;
        private IReadOnlyDictionary<int, IAttribute> m_CachedVertexAttribs;
        private IReadOnlyDictionary<int, IAttribute> m_CachedPrimAttribs;
        private IReadOnlyDictionary<long, Primitive> m_CachedPrimitives;
        private IReadOnlyDictionary<long, long> m_CachedVertexToPoint;

        /// <summary>
        /// Gets a read-only view of the point attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> PointAttribs
        {
            get
            {
                lock (m_Lock)
                {
                    return m_CachedPointAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_PointAttribs);
                }
            }
        }

        /// <summary>
        /// Gets a read-only view of the vertex attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> VertexAttribs
        {
            get
            {
                lock (m_Lock)
                {
                    return m_CachedVertexAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_VertexAttribs);
                }
            }
        }

        /// <summary>
        /// Gets a read-only view of the primitive attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> PrimAttribs
        {
            get
            {
                lock (m_Lock)
                {
                    return m_CachedPrimAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_PrimAttribs);
                }
            }
        }

        /// <summary>
        /// Gets a read-only view of the primitives
        /// </summary>
        public IReadOnlyDictionary<long, Primitive> Primitives
        {
            get
            {
                lock (m_Lock)
                {
                    return m_CachedPrimitives ??= new ReadOnlyDictionary<long, Primitive>(m_Primitives);
                }
            }
        }

        /// <summary>
        /// Gets a read-only view of the vertex-to-point mapping
        /// </summary>
        public IReadOnlyDictionary<long, long> VertexToPoint
        {
            get
            {
                lock (m_Lock)
                {
                    return m_CachedVertexToPoint ??= new ReadOnlyDictionary<long, long>(m_VertexToPoint);
                }
            }
        }

        public Detail()
        {
            // Initialize with position attribute
            AddPointAttrib(new Attribute<float3>(AttribID.Position, float3.zero));

            // Subscribe to offset removal events for cleanup
            Points.OnOffsetRemoved += OnPointOffsetRemoved;
            Vertices.OnOffsetRemoved += OnVertexOffsetRemoved;
            Prims.OnOffsetRemoved += OnPrimOffsetRemoved;
        }

        #region Attribute Management

        /// <summary>
        /// Adds a point attribute
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="attrib">The attribute to add</param>
        /// <exception cref="ArgumentException">Thrown if an attribute with the same ID already exists</exception>
        public void AddPointAttrib<T>(Attribute<T> attrib)
        {
            if (attrib == null)
                throw new ArgumentNullException(nameof(attrib));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_PointAttribs.ContainsKey(attrib.ID))
                    throw new ArgumentException($"Point attribute with ID {attrib.ID} ({attrib.Name}) already exists");

                m_PointAttribs[attrib.ID] = attrib;
                InvalidateCache();
            }
        }

        /// <summary>
        /// Adds a vertex attribute
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="attrib">The attribute to add</param>
        /// <exception cref="ArgumentException">Thrown if an attribute with the same ID already exists</exception>
        public void AddVertexAttrib<T>(Attribute<T> attrib)
        {
            if (attrib == null)
                throw new ArgumentNullException(nameof(attrib));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_VertexAttribs.ContainsKey(attrib.ID))
                    throw new ArgumentException($"Vertex attribute with ID {attrib.ID} ({attrib.Name}) already exists");

                m_VertexAttribs[attrib.ID] = attrib;
                InvalidateCache();
            }
        }

        /// <summary>
        /// Adds a primitive attribute
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="attrib">The attribute to add</param>
        /// <exception cref="ArgumentException">Thrown if an attribute with the same ID already exists</exception>
        public void AddPrimAttrib<T>(Attribute<T> attrib)
        {
            if (attrib == null)
                throw new ArgumentNullException(nameof(attrib));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_PrimAttribs.ContainsKey(attrib.ID))
                    throw new ArgumentException($"Primitive attribute with ID {attrib.ID} ({attrib.Name}) already exists");

                m_PrimAttribs[attrib.ID] = attrib;
                InvalidateCache();
            }
        }

        /// <summary>
        /// Gets a point attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetPointAttrib<T>(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return m_PointAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
            }
        }

        /// <summary>
        /// Gets a vertex attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetVertexAttrib<T>(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return m_VertexAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
            }
        }

        /// <summary>
        /// Gets a primitive attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetPrimAttrib<T>(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return m_PrimAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
            }
        }

        /// <summary>
        /// Removes a point attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemovePointAttrib(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (id == AttribID.Position)
                    throw new InvalidOperationException("Cannot remove the Position attribute");

                if (m_PointAttribs.TryGetValue(id, out var attr))
                {
                    attr.Dispose();
                    m_PointAttribs.Remove(id);
                    InvalidateCache();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Removes a vertex attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemoveVertexAttrib(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_VertexAttribs.TryGetValue(id, out var attr))
                {
                    attr.Dispose();
                    m_VertexAttribs.Remove(id);
                    InvalidateCache();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Removes a primitive attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemovePrimAttrib(int id)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_PrimAttribs.TryGetValue(id, out var attr))
                {
                    attr.Dispose();
                    m_PrimAttribs.Remove(id);
                    InvalidateCache();
                    return true;
                }
                return false;
            }
        }

        #endregion

        #region Point Management

        /// <summary>
        /// Adds a point with the specified position
        /// </summary>
        /// <param name="pos">The position of the point</param>
        /// <returns>The offset of the created point</returns>
        /// <exception cref="ArgumentException">Thrown if the position contains invalid values</exception>
        public long AddPoint(float3 pos)
        {
            if (!IsValidFloat3(pos))
                throw new ArgumentException("Position contains invalid values (NaN or Infinity)", nameof(pos));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                long offset = Points.Allocate();
                var posAttrib = GetPointAttrib<float3>(AttribID.Position);
                posAttrib?.Set(offset, pos, Points);
                
                // Initialize reverse lookup
                m_PointToVertices[offset] = new HashSet<long>();
                
                return offset;
            }
        }

        /// <summary>
        /// Removes a point and all associated vertices and primitives
        /// </summary>
        /// <param name="pointOffset">The point offset to remove</param>
        /// <returns>True if the point was found and removed, false otherwise</returns>
        public bool RemovePoint(long pointOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (!Points.Contains(pointOffset))
                    return false;

                // Use reverse lookup for efficient vertex removal
                if (m_PointToVertices.TryGetValue(pointOffset, out var vertices))
                {
                    var vertexList = vertices.ToList(); // Copy to avoid modification during iteration
                    foreach (var vertexOffset in vertexList)
                    {
                        RemoveVertexInternal(vertexOffset);
                    }
                }

                m_PointToVertices.Remove(pointOffset);
                return Points.Remove(pointOffset);
            }
        }

        /// <summary>
        /// Gets the position of a point
        /// </summary>
        /// <param name="offset">The point offset</param>
        /// <returns>The position, or float3.zero if not found</returns>
        public float3 GetPointPos(long offset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                var posAttrib = GetPointAttrib<float3>(AttribID.Position);
                return posAttrib?.Get(offset, Points) ?? float3.zero;
            }
        }

        /// <summary>
        /// Sets the position of a point
        /// </summary>
        /// <param name="offset">The point offset</param>
        /// <param name="pos">The new position</param>
        /// <exception cref="ArgumentException">Thrown if the position contains invalid values or the point doesn't exist</exception>
        public void SetPointPos(long offset, float3 pos)
        {
            if (!IsValidFloat3(pos))
                throw new ArgumentException("Position contains invalid values (NaN or Infinity)", nameof(pos));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (!Points.Contains(offset))
                    throw new ArgumentException($"Point offset {offset} does not exist", nameof(offset));

                var posAttrib = GetPointAttrib<float3>(AttribID.Position);
                posAttrib?.Set(offset, pos, Points);
            }
        }

        #endregion

        #region Vertex Management

        /// <summary>
        /// Adds a vertex that references the specified point
        /// </summary>
        /// <param name="pointOffset">The point offset to reference</param>
        /// <returns>The offset of the created vertex</returns>
        /// <exception cref="ArgumentException">Thrown if the point offset doesn't exist</exception>
        public long AddVertex(long pointOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (!Points.Contains(pointOffset))
                    throw new ArgumentException($"Point offset {pointOffset} does not exist", nameof(pointOffset));

                long offset = Vertices.Allocate();
                m_VertexToPoint[offset] = pointOffset;
                
                // Update reverse lookup
                if (!m_PointToVertices.TryGetValue(pointOffset, out var vertices))
                {
                    vertices = new HashSet<long>();
                    m_PointToVertices[pointOffset] = vertices;
                }
                vertices.Add(offset);
                
                // Initialize vertex-to-primitives lookup
                m_VertexToPrimitives[offset] = new HashSet<long>();
                
                return offset;
            }
        }

        /// <summary>
        /// Removes a vertex and updates any primitives that reference it
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to remove</param>
        /// <returns>True if the vertex was found and removed, false otherwise</returns>
        public bool RemoveVertex(long vertexOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return RemoveVertexInternal(vertexOffset);
            }
        }

        private bool RemoveVertexInternal(long vertexOffset)
        {
            if (!Vertices.Contains(vertexOffset))
                return false;

            // Use optimized lookup to find primitives containing this vertex
            if (m_VertexToPrimitives.TryGetValue(vertexOffset, out var primitiveOffsets))
            {
                var primList = primitiveOffsets.ToList(); // Copy to avoid modification during iteration
                foreach (var primOffset in primList)
                {
                    if (m_Primitives.TryGetValue(primOffset, out var primitive))
                    {
                        primitive.RemoveVertex(vertexOffset);

                        // Remove a primitive if it becomes invalid
                        if (!primitive.IsValid())
                        {
                            RemovePrimInternal(primOffset);
                        }
                    }
                }
            }

            // Update reverse lookup
            if (m_VertexToPoint.TryGetValue(vertexOffset, out long pointOffset))
            {
                if (m_PointToVertices.TryGetValue(pointOffset, out var vertices))
                {
                    vertices.Remove(vertexOffset);
                }
            }

            m_VertexToPoint.Remove(vertexOffset);
            m_VertexToPrimitives.Remove(vertexOffset);
            return Vertices.Remove(vertexOffset);
        }

        /// <summary>
        /// Gets the point offset referenced by a vertex
        /// </summary>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <returns>The point offset, or -1 if not found</returns>
        public long GetVertexPoint(long vertexOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return m_VertexToPoint.GetValueOrDefault(vertexOffset, -1);
            }
        }

        /// <summary>
        /// Gets all vertices that reference a specific point
        /// </summary>
        /// <param name="pointOffset">The point offset</param>
        /// <returns>A collection of vertex offsets</returns>
        public IEnumerable<long> GetVerticesForPoint(long pointOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_PointToVertices.TryGetValue(pointOffset, out var vertices))
                    return vertices;
                return Enumerable.Empty<long>();
            }
        }

        #endregion

        #region Primitive Management

        /// <summary>
        /// Adds a primitive with the specified point offsets
        /// </summary>
        /// <param name="pointOffsets">The point offsets to include in the primitive</param>
        /// <returns>The offset of the created primitive</returns>
        /// <exception cref="ArgumentException">Thrown if any point offset doesn't exist or if there are insufficient points</exception>
        public long AddPrim(long[] pointOffsets)
        {
            if (pointOffsets == null)
                throw new ArgumentNullException(nameof(pointOffsets));

            if (pointOffsets.Length < 3)
                throw new ArgumentException("A primitive requires at least 3 points", nameof(pointOffsets));

            // Check for duplicate points
            var uniquePoints = new HashSet<long>(pointOffsets);
            if (uniquePoints.Count != pointOffsets.Length)
                throw new ArgumentException("Primitive cannot contain duplicate points", nameof(pointOffsets));

            ThrowIfDisposed();
            lock (m_Lock)
            {
                // Validate all point offsets exist
                foreach (long pointOffset in pointOffsets)
                {
                    if (!Points.Contains(pointOffset))
                        throw new ArgumentException($"Point offset {pointOffset} does not exist", nameof(pointOffsets));
                }

                // Create vertices for each point
                // TODO: What if I want to make a sphere? should I calculate normals or maybe share a vertex?
                // I do not fucking know now
                var vertices = new List<long>();
                foreach (long pointOffset in pointOffsets)
                    vertices.Add(AddVertex(pointOffset));

                // Create primitive
                long offset = Prims.Allocate();
                var prim = new Primitive();

                try
                {
                    foreach (var vertexOffset in vertices)
                    {
                        prim.AddVertex(vertexOffset);
                        
                        // Update vertex-to-primitive lookup
                        if (!m_VertexToPrimitives.TryGetValue(vertexOffset, out var primSet))
                        {
                            primSet = new HashSet<long>();
                            m_VertexToPrimitives[vertexOffset] = primSet;
                        }
                        primSet.Add(offset);
                    }

                    m_Primitives[offset] = prim;
                    InvalidateCache();
                    return offset;
                }
                catch
                {
                    // Cleanup on failure
                    prim.Dispose();
                    Prims.Remove(offset);

                    // Remove created vertices and their primitive associations
                    foreach (var vertexOffset in vertices)
                    {
                        if (m_VertexToPrimitives.TryGetValue(vertexOffset, out var primSet))
                        {
                            primSet.Remove(offset);
                        }
                        RemoveVertexInternal(vertexOffset);
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// Removes a primitive and its associated vertices
        /// </summary>
        /// <param name="primOffset">The primitive offset to remove</param>
        /// <returns>True if the primitive was found and removed, false otherwise</returns>
        public bool RemovePrim(long primOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return RemovePrimInternal(primOffset);
            }
        }

        private bool RemovePrimInternal(long primOffset)
        {
            if (!m_Primitives.TryGetValue(primOffset, out var primitive))
                return false;

            // Remove all vertices associated with this primitive and update lookup
            foreach (var vertexOffset in primitive.VertexOffsets.ToList())
            {
                if (m_VertexToPrimitives.TryGetValue(vertexOffset, out var primSet))
                {
                    primSet.Remove(primOffset);
                }
                RemoveVertexInternal(vertexOffset);
            }

            primitive.Dispose();
            m_Primitives.Remove(primOffset);
            InvalidateCache();
            return Prims.Remove(primOffset);
        }

        /// <summary>
        /// Gets a primitive by its offset
        /// </summary>
        /// <param name="primOffset">The primitive offset</param>
        /// <returns>The primitive, or null if not found</returns>
        public Primitive GetPrimitive(long primOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return m_Primitives.TryGetValue(primOffset, out var primitive) ? primitive : null;
            }
        }

        /// <summary>
        /// Gets all primitives that contain a specific vertex - NOW OPTIMIZED!
        /// </summary>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <returns>A collection of primitive offsets</returns>
        public IEnumerable<long> GetPrimitivesForVertex(long vertexOffset)
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                if (m_VertexToPrimitives.TryGetValue(vertexOffset, out var primitives))
                {
                    return primitives.ToList(); // Return a copy to avoid modification issues
                }
                return Enumerable.Empty<long>();
            }
        }

        #endregion

        #region Validation and Utility

        /// <summary>
        /// Validates the integrity of the detail structure
        /// </summary>
        /// <returns>True if the structure is valid, false otherwise</returns>
        public bool ValidateIntegrity()
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                // Check that all vertex-to-point mappings are valid
                foreach (var kvp in m_VertexToPoint)
                {
                    if (!Points.Contains(kvp.Value))
                        return false;
                }

                // Check that all primitive vertices exist
                foreach (var kvp in m_Primitives)
                {
                    var primitive = kvp.Value;
                    foreach (var vertexOffset in primitive.VertexOffsets)
                    {
                        if (!Vertices.Contains(vertexOffset))
                            return false;
                    }
                }

                // Check reverse lookup consistency
                foreach (var kvp in m_PointToVertices)
                {
                    var pointOffset = kvp.Key;
                    var vertices = kvp.Value;
                    
                    if (!Points.Contains(pointOffset))
                        return false;
                    
                    foreach (var vertexOffset in vertices)
                    {
                        if (!m_VertexToPoint.TryGetValue(vertexOffset, out long mappedPoint) || 
                            mappedPoint != pointOffset)
                            return false;
                    }
                }

                // Check vertex-to-primitives lookup consistency
                foreach (var kvp in m_VertexToPrimitives)
                {
                    var vertexOffset = kvp.Key;
                    var primitives = kvp.Value;
                    
                    if (!Vertices.Contains(vertexOffset))
                        return false;
                    
                    foreach (var primOffset in primitives)
                    {
                        if (!m_Primitives.TryGetValue(primOffset, out var primitive) ||
                            !primitive.ContainsVertex(vertexOffset))
                            return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Gets statistics about this detail
        /// </summary>
        /// <returns>A tuple containing point count, vertex count, and primitive count</returns>
        public (int PointCount, int VertexCount, int PrimCount) GetStats()
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                return (Points.Count, Vertices.Count, Prims.Count);
            }
        }

        /// <summary>
        /// Gets detailed statistics about memory usage
        /// </summary>
        /// <returns>A detailed statistics object</returns>
        public DetailStatistics GetDetailedStats()
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                var stats = new DetailStatistics
                {
                    PointCount = Points.Count,
                    VertexCount = Vertices.Count,
                    PrimitiveCount = Prims.Count,
                    PointAttributeCount = m_PointAttribs.Count,
                    VertexAttributeCount = m_VertexAttribs.Count,
                    PrimitiveAttributeCount = m_PrimAttribs.Count
                };

                // Calculate attribute memory usage
                foreach (var attr in m_PointAttribs.Values)
                {
                    stats.PointAttributeMemory += attr.AllocatedCount;
                }
                
                foreach (var attr in m_VertexAttribs.Values)
                {
                    stats.VertexAttributeMemory += attr.AllocatedCount;
                }
                
                foreach (var attr in m_PrimAttribs.Values)
                {
                    stats.PrimitiveAttributeMemory += attr.AllocatedCount;
                }

                return stats;
            }
        }

        /// <summary>
        /// Compacts all attribute storage to optimize memory usage
        /// </summary>
        public void CompactAttributes()
        {
            ThrowIfDisposed();
            lock (m_Lock)
            {
                foreach (var attr in m_PointAttribs.Values.OfType<Attribute<object>>())
                {
                    attr.Compact();
                }
                
                foreach (var attr in m_VertexAttribs.Values.OfType<Attribute<object>>())
                {
                    attr.Compact();
                }
                
                foreach (var attr in m_PrimAttribs.Values.OfType<Attribute<object>>())
                {
                    attr.Compact();
                }
            }
        }

        #endregion

        #region Event Handlers

        private void OnPointOffsetRemoved(long pointOffset, int removedIndex)
        {
            lock (m_Lock)
            {
                // Clean up attributes
                foreach (var attr in m_PointAttribs.Values)
                {
                    if (attr is Attribute<object> typedAttr)
                    {
                        typedAttr.OnOffsetRemoved(pointOffset, removedIndex);
                    }
                }
            }
        }

        private void OnVertexOffsetRemoved(long vertexOffset, int removedIndex)
        {
            lock (m_Lock)
            {
                // Clean up attributes
                foreach (var attr in m_VertexAttribs.Values)
                {
                    if (attr is Attribute<object> typedAttr)
                    {
                        typedAttr.OnOffsetRemoved(vertexOffset, removedIndex);
                    }
                }
            }
        }

        private void OnPrimOffsetRemoved(long primOffset, int removedIndex)
        {
            lock (m_Lock)
            {
                // Clean up attributes
                foreach (var attr in m_PrimAttribs.Values)
                {
                    if (attr is Attribute<object> typedAttr)
                    {
                        typedAttr.OnOffsetRemoved(primOffset, removedIndex);
                    }
                }
            }
        }

        #endregion

        #region Private Helpers

        private static bool IsValidFloat3(float3 value)
        {
            return math.isfinite(value.x) && math.isfinite(value.y) && math.isfinite(value.z);
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Detail));
        }

        private void InvalidateCache()
        {
            m_CachedPointAttribs = null;
            m_CachedVertexAttribs = null;
            m_CachedPrimAttribs = null;
            m_CachedPrimitives = null;
            m_CachedVertexToPoint = null;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!m_Disposed)
            {
                lock (m_Lock)
                {
                    // Unsubscribe from events
                    Points.OnOffsetRemoved -= OnPointOffsetRemoved;
                    Vertices.OnOffsetRemoved -= OnVertexOffsetRemoved;
                    Prims.OnOffsetRemoved -= OnPrimOffsetRemoved;

                    // Dispose all primitives
                    foreach (var primitive in m_Primitives.Values)
                    {
                        primitive.Dispose();
                    }
                    m_Primitives.Clear();

                    // Dispose all attributes
                    foreach (var attr in m_PointAttribs.Values)
                    {
                        attr.Dispose();
                    }
                    m_PointAttribs.Clear();

                    foreach (var attr in m_VertexAttribs.Values)
                    {
                        attr.Dispose();
                    }
                    m_VertexAttribs.Clear();

                    foreach (var attr in m_PrimAttribs.Values)
                    {
                        attr.Dispose();
                    }
                    m_PrimAttribs.Clear();

                    m_VertexToPoint.Clear();
                    m_PointToVertices.Clear();
                    m_VertexToPrimitives.Clear(); // Clean up new lookup table

                    // Dispose offset maps
                    Points?.Dispose();
                    Vertices?.Dispose();
                    Prims?.Dispose();

                    InvalidateCache();
                }

                m_Disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Detailed statistics about a Detail object
    /// </summary>
    public class DetailStatistics
    {
        public int PointCount { get; set; }
        public int VertexCount { get; set; }
        public int PrimitiveCount { get; set; }
        public int PointAttributeCount { get; set; }
        public int VertexAttributeCount { get; set; }
        public int PrimitiveAttributeCount { get; set; }
        public int PointAttributeMemory { get; set; }
        public int VertexAttributeMemory { get; set; }
        public int PrimitiveAttributeMemory { get; set; }

        public override string ToString()
        {
            return $"Points: {PointCount}, Vertices: {VertexCount}, Primitives: {PrimitiveCount}, " +
                   $"Attributes: P{PointAttributeCount}/V{VertexAttributeCount}/Pr{PrimitiveAttributeCount}, " +
                   $"Memory: {PointAttributeMemory + VertexAttributeMemory + PrimitiveAttributeMemory} entries";
        }
    }
}