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
        private bool m_Disposed;

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

        // Offset generators
        private long m_NextPointOffset = 1;
        private long m_NextVertexOffset = 1;
        private long m_NextPrimOffset = 1;

        // Collections for tracking allocated offsets
        private readonly HashSet<long> m_PointOffsets = new();
        private readonly HashSet<long> m_VertexOffsets = new();
        private readonly HashSet<long> m_PrimOffsets = new();

        /// <summary>
        /// Gets a read-only view of the point attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> PointAttribs
        {
            get
            {
                return m_CachedPointAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_PointAttribs);
            }
        }

        /// <summary>
        /// Gets a read-only view of the vertex attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> VertexAttribs
        {
            get
            {
                return m_CachedVertexAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_VertexAttribs);
            }
        }

        /// <summary>
        /// Gets a read-only view of the primitive attributes
        /// </summary>
        public IReadOnlyDictionary<int, IAttribute> PrimAttribs
        {
            get
            {
                return m_CachedPrimAttribs ??= new ReadOnlyDictionary<int, IAttribute>(m_PrimAttribs);
            }
        }

        /// <summary>
        /// Gets a read-only view of the primitives
        /// </summary>
        public IReadOnlyDictionary<long, Primitive> Primitives
        {
            get
            {
                return m_CachedPrimitives ??= new ReadOnlyDictionary<long, Primitive>(m_Primitives);
            }
        }

        /// <summary>
        /// Gets a read-only view of the vertex-to-point mapping
        /// </summary>
        public IReadOnlyDictionary<long, long> VertexToPoint
        {
            get
            {
                return m_CachedVertexToPoint ??= new ReadOnlyDictionary<long, long>(m_VertexToPoint);
            }
        }

        public Detail()
        {
            // Initialize with position attribute
            AddPointAttrib(new Attribute<float3>(AttribID.Position, float3.zero));
        }

        #region Offset Access Methods

        /// <summary>
        /// Gets all point offsets
        /// </summary>
        /// <returns>A collection of all point offsets</returns>
        public IEnumerable<long> GetAllPointOffsets()
        {
            return m_PointOffsets;
        }

        /// <summary>
        /// Gets all vertex offsets
        /// </summary>
        /// <returns>A collection of all vertex offsets</returns>
        public IEnumerable<long> GetAllVertexOffsets()
        {
            return m_VertexOffsets;
        }

        /// <summary>
        /// Gets all primitive offsets
        /// </summary>
        /// <returns>A collection of all primitive offsets</returns>
        public IEnumerable<long> GetAllPrimOffsets()
        {
            return m_PrimOffsets;
        }

        #endregion

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

            if (m_PointAttribs.ContainsKey(attrib.ID))
                throw new ArgumentException($"Point attribute with ID {attrib.ID} ({attrib.Name}) already exists");

            m_PointAttribs[attrib.ID] = attrib;
            InvalidateCache();
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

            if (!m_VertexAttribs.TryAdd(attrib.ID, attrib))
                throw new ArgumentException($"Vertex attribute with ID {attrib.ID} ({attrib.Name}) already exists");

            InvalidateCache();
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

            if (!m_PrimAttribs.TryAdd(attrib.ID, attrib))
                throw new ArgumentException($"Primitive attribute with ID {attrib.ID} ({attrib.Name}) already exists");

            InvalidateCache();
        }

        /// <summary>
        /// Gets a point attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetPointAttrib<T>(int id)
        {
            return m_PointAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
        }

        /// <summary>
        /// Gets a vertex attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetVertexAttrib<T>(int id)
        {
            return m_VertexAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
        }

        /// <summary>
        /// Gets a primitive attribute by ID
        /// </summary>
        /// <typeparam name="T">The attribute data type</typeparam>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute, or null if not found or wrong type</returns>
        public Attribute<T> GetPrimAttrib<T>(int id)
        {
            return m_PrimAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;
        }

        /// <summary>
        /// Removes a point attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemovePointAttrib(int id)
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

        /// <summary>
        /// Removes a vertex attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemoveVertexAttrib(int id)
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

        /// <summary>
        /// Removes a primitive attribute
        /// </summary>
        /// <param name="id">The attribute ID to remove</param>
        /// <returns>True if the attribute was found and removed</returns>
        public bool RemovePrimAttrib(int id)
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

            long offset = m_NextPointOffset++;
            m_PointOffsets.Add(offset);
            
            var posAttrib = GetPointAttrib<float3>(AttribID.Position);
            posAttrib?.Set(offset, pos);
            
            // Initialize reverse lookup
            m_PointToVertices[offset] = new HashSet<long>();
            
            return offset;
        }

        /// <summary>
        /// Removes a point and all associated vertices and primitives
        /// </summary>
        /// <param name="pointOffset">The point offset to remove</param>
        /// <returns>True if the point was found and removed, false otherwise</returns>
        public bool RemovePoint(long pointOffset)
        {
            if (!m_PointOffsets.Contains(pointOffset))
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

            // Remove from all point attributes
            foreach (var attr in m_PointAttribs.Values)
                attr.RemoveValue(pointOffset);

            m_PointOffsets.Remove(pointOffset);
            m_PointToVertices.Remove(pointOffset);
            return true;
        }

        /// <summary>
        /// Gets the position of a point
        /// </summary>
        /// <param name="offset">The point offset</param>
        /// <returns>The position, or float3.zero if not found</returns>
        public float3 GetPointPos(long offset)
        {
            var posAttrib = GetPointAttrib<float3>(AttribID.Position);
            return posAttrib?.Get(offset) ?? float3.zero;
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

            if (!m_PointOffsets.Contains(offset))
                throw new ArgumentException($"Point offset {offset} does not exist", nameof(offset));

            var posAttrib = GetPointAttrib<float3>(AttribID.Position);
            posAttrib?.Set(offset, pos);
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
            if (!m_PointOffsets.Contains(pointOffset))
                throw new ArgumentException($"Point offset {pointOffset} does not exist", nameof(pointOffset));

            long offset = m_NextVertexOffset++;
            m_VertexOffsets.Add(offset);
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

        /// <summary>
        /// Removes a vertex and updates any primitives that reference it
        /// </summary>
        /// <param name="vertexOffset">The vertex offset to remove</param>
        /// <returns>True if the vertex was found and removed, false otherwise</returns>
        public bool RemoveVertex(long vertexOffset)
        {
            return RemoveVertexInternal(vertexOffset);
        }

        private bool RemoveVertexInternal(long vertexOffset)
        {
            if (!m_VertexOffsets.Contains(vertexOffset))
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

            // Remove from all vertex attributes
            foreach (var attr in m_VertexAttribs.Values)
            {
                attr.RemoveValue(vertexOffset);
            }

            m_VertexOffsets.Remove(vertexOffset);
            m_VertexToPoint.Remove(vertexOffset);
            m_VertexToPrimitives.Remove(vertexOffset);
            return true;
        }

        /// <summary>
        /// Gets the point offset referenced by a vertex
        /// </summary>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <returns>The point offset, or -1 if not found</returns>
        public long GetVertexPoint(long vertexOffset)
        {
            return m_VertexToPoint.GetValueOrDefault(vertexOffset, -1);
        }

        /// <summary>
        /// Gets all vertices that reference a specific point
        /// </summary>
        /// <param name="pointOffset">The point offset</param>
        /// <returns>A collection of vertex offsets</returns>
        public IEnumerable<long> GetVerticesForPoint(long pointOffset)
        {
            if (m_PointToVertices.TryGetValue(pointOffset, out var vertices))
                return vertices;
            return Enumerable.Empty<long>();
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

            // Validate all point offsets exist
            foreach (long pointOffset in pointOffsets)
            {
                if (!m_PointOffsets.Contains(pointOffset))
                    throw new ArgumentException($"Point offset {pointOffset} does not exist", nameof(pointOffsets));
            }

            // Create vertices for each point
            var vertices = new List<long>();
            foreach (long pointOffset in pointOffsets)
                vertices.Add(AddVertex(pointOffset));

            // Create primitive
            long offset = m_NextPrimOffset++;
            m_PrimOffsets.Add(offset);
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
                m_PrimOffsets.Remove(offset);

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

        /// <summary>
        /// Removes a primitive and its associated vertices
        /// </summary>
        /// <param name="primOffset">The primitive offset to remove</param>
        /// <returns>True if the primitive was found and removed, false otherwise</returns>
        public bool RemovePrim(long primOffset)
        {
            return RemovePrimInternal(primOffset);
        }

        private bool RemovePrimInternal(long primOffset)
        {
            if (!m_PrimOffsets.Contains(primOffset) || !m_Primitives.TryGetValue(primOffset, out var primitive))
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

            // Remove from all primitive attributes
            foreach (var attr in m_PrimAttribs.Values)
            {
                attr.RemoveValue(primOffset);
            }

            primitive.Dispose();
            m_Primitives.Remove(primOffset);
            m_PrimOffsets.Remove(primOffset);
            InvalidateCache();
            return true;
        }

        /// <summary>
        /// Gets a primitive by its offset
        /// </summary>
        /// <param name="primOffset">The primitive offset</param>
        /// <returns>The primitive, or null if not found</returns>
        public Primitive GetPrimitive(long primOffset)
        {
            return m_Primitives.TryGetValue(primOffset, out var primitive) ? primitive : null;
        }

        /// <summary>
        /// Gets all primitives that contain a specific vertex
        /// </summary>
        /// <param name="vertexOffset">The vertex offset</param>
        /// <returns>A collection of primitive offsets</returns>
        public IEnumerable<long> GetPrimitivesForVertex(long vertexOffset)
        {
            if (m_VertexToPrimitives.TryGetValue(vertexOffset, out var primitives))
            {
                return primitives.ToList(); // Return a copy to avoid modification issues
            }
            return Enumerable.Empty<long>();
        }

        #endregion

        #region Validation and Utility

        /// <summary>
        /// Validates the integrity of the detail structure
        /// </summary>
        /// <returns>True if the structure is valid, false otherwise</returns>
        public bool ValidateIntegrity()
        {
            // Check that all vertex-to-point mappings are valid
            foreach (var kvp in m_VertexToPoint)
            {
                if (!m_PointOffsets.Contains(kvp.Value))
                    return false;
            }

            // Check that all primitive vertices exist
            foreach (var kvp in m_Primitives)
            {
                var primitive = kvp.Value;
                foreach (var vertexOffset in primitive.VertexOffsets)
                {
                    if (!m_VertexOffsets.Contains(vertexOffset))
                        return false;
                }
            }

            // Check reverse lookup consistency
            foreach (var kvp in m_PointToVertices)
            {
                var pointOffset = kvp.Key;
                var vertices = kvp.Value;
                
                if (!m_PointOffsets.Contains(pointOffset))
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
                
                if (!m_VertexOffsets.Contains(vertexOffset))
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

        /// <summary>
        /// Gets statistics about this detail
        /// </summary>
        /// <returns>A tuple containing point count, vertex count, and primitive count</returns>
        public (int PointCount, int VertexCount, int PrimCount) GetStats()
        {
            return (m_PointOffsets.Count, m_VertexOffsets.Count, m_PrimOffsets.Count);
        }

        /// <summary>
        /// Gets detailed statistics about memory usage
        /// </summary>
        /// <returns>A detailed statistics object</returns>
        public DetailStatistics GetDetailedStats()
        {
            var stats = new DetailStatistics
            {
                PointCount = m_PointOffsets.Count,
                VertexCount = m_VertexOffsets.Count,
                PrimitiveCount = m_PrimOffsets.Count,
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

        #endregion

        #region Private Helpers

        private static bool IsValidFloat3(float3 value)
        {
            return math.isfinite(value.x) && math.isfinite(value.y) && math.isfinite(value.z);
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
                // Dispose all primitives
                foreach (var primitive in m_Primitives.Values)
                    primitive.Dispose();
                m_Primitives.Clear();

                // Dispose all attributes
                foreach (var attr in m_PointAttribs.Values)
                    attr.Dispose();
                m_PointAttribs.Clear();

                foreach (var attr in m_VertexAttribs.Values)
                    attr.Dispose();
                m_VertexAttribs.Clear();

                foreach (var attr in m_PrimAttribs.Values)
                    attr.Dispose();
                
                m_PrimAttribs.Clear();

                m_VertexToPoint.Clear();
                m_PointToVertices.Clear();
                m_VertexToPrimitives.Clear();

                m_PointOffsets.Clear();
                m_VertexOffsets.Clear();
                m_PrimOffsets.Clear();

                InvalidateCache();
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