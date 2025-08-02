using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

namespace PropellerheadMesh
{
    public partial struct NativeDetail
    {
        public NativeDetail GetTriangulatedCopy(Allocator persistent)
        {
            return new NativeDetail(0, persistent);
        }
    }
}