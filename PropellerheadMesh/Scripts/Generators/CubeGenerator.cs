using Unity.Mathematics;

namespace PropellerHead.Generators
{
    public static class CubeGenerator
    {
        public static void GenerateCube(this Detail detail, float3 size)
        {
            var half = size * 0.5f;

            // Create 8 vertices of a cube
            var cubeVertices = new float3[]
            {
                new(-half.x, -half.y, -half.z), // 0: bottom-left-back
                new(half.x, -half.y, -half.z),  // 1: bottom-right-back
                new(half.x, half.y, -half.z),   // 2: top-right-back
                new(-half.x, half.y, -half.z),  // 3: top-left-back
                new(-half.x, -half.y, half.z),  // 4: bottom-left-front
                new(half.x, -half.y, half.z),   // 5: bottom-right-front
                new(half.x, half.y, half.z),    // 6: top-right-front
                new(-half.x, half.y, half.z)    // 7: top-left-front
            };

            // Add points to detail
            var pointOffsets = new long[8];
            for (var i = 0; i < cubeVertices.Length; i++)
                pointOffsets[i] = detail.AddPoint(cubeVertices[i]);

            // Create 6 faces (quads) of the cube - same order as original
            var cubeFaces = new[]
            {
                new[] { 0, 1, 2, 3 }, // Back face
                new[] { 4, 7, 6, 5 }, // Front face
                new[] { 0, 4, 5, 1 }, // Bottom face
                new[] { 3, 2, 6, 7 }, // Top face
                new[] { 0, 3, 7, 4 }, // Left face
                new[] { 1, 5, 6, 2 }  // Right face
            };

            // Add primitives (faces) to detail
            foreach (var face in cubeFaces)
            {
                var facePointOffsets = new long[face.Length];
                for (var i = 0; i < face.Length; i++)
                    facePointOffsets[i] = pointOffsets[face[i]];

                // Add the primitive
                detail.AddPrim(facePointOffsets);
            }
        }
    }
}