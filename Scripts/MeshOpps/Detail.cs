using System.Collections.Generic;
using Unity.Mathematics;

namespace PropellerHead
{
    public class Detail
    {
        public OffsetMap Points = new();
        public OffsetMap Vertices = new();
        public OffsetMap Prims = new();

        public Dictionary<int, IAttribute> PointAttribs = new();
        public Dictionary<int, IAttribute> VertexAttribs = new();
        public Dictionary<int, IAttribute> PrimAttribs = new();

        public Dictionary<long, Primitive> Primitives = new();
        public Dictionary<long, long> VertexToPoint = new();

        public Detail()
        {
            AddPointAttrib(new Attribute<float3>(AttribID.Position));
        }

        public void AddPointAttrib<T>(Attribute<T> attrib) => PointAttribs[attrib.ID] = attrib;
        
        public void AddVertexAttrib<T>(Attribute<T> attrib) => VertexAttribs[attrib.ID] = attrib;
        
        public void AddPrimAttrib<T>(Attribute<T> attrib) => PrimAttribs[attrib.ID] = attrib;

        public Attribute<T> GetPointAttrib<T>(int id) => PointAttribs.TryGetValue(id, out var attr) && attr is Attribute<T> typed ? typed : null;

        public long AddPoint(float3 pos)
        {
            long offset = Points.Allocate();
            GetPointAttrib<float3>(AttribID.Position)?.Set(offset, pos, Points);
            return offset;
        }

        public long AddVertex(long pointOffset)
        {
            long offset = Vertices.Allocate();
            VertexToPoint[offset] = pointOffset;
            return offset;
        }

        public long AddPrim(long[] pointOffsets)
        {
            var vertices = new List<long>();
            foreach (long pointOffset in pointOffsets)
                vertices.Add(AddVertex(pointOffset));

            long offset = Prims.Allocate();
            var prim = new Primitive();
            vertices.ForEach(prim.AddVertex);
            Primitives[offset] = prim;
            return offset;
        }

        public float3 GetPointPos(long offset) => GetPointAttrib<float3>(AttribID.Position)?.Get(offset, Points) ?? float3.zero;
        
        public void SetPointPos(long offset, float3 pos) => GetPointAttrib<float3>(AttribID.Position)?.Set(offset, pos, Points);
    }
}