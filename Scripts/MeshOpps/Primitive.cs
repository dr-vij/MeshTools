using System.Collections.Generic;

namespace PropellerHead
{
    public class Primitive
    {
        public List<long> VertexOffsets = new List<long>();

        public void AddVertex(long vertexOffset) => VertexOffsets.Add(vertexOffset);
        
        public void RemoveVertex(long vertexOffset) => VertexOffsets.Remove(vertexOffset);
        
        public int VertexCount => VertexOffsets.Count;
    }
}