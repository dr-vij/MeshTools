using System.Collections.Generic;

namespace PropellerHead
{
    public static class AttribID
    {
        private static Dictionary<string, int> m_NameToHash = new();
        private static Dictionary<int, string> m_HashToName = new();

        // Standard Unity Mesh Attributes
        public static readonly int Position = Register("Position"); // Vertex positions
        public static readonly int Normal = Register("Normal"); // Vertex normals
        public static readonly int Tangent = Register("Tangent"); // Vertex tangents
        public static readonly int Color = Register("Color"); // Vertex colors
        public static readonly int UV0 = Register("UV0"); // Primary texture coordinates
        public static readonly int UV1 = Register("UV1"); // Secondary texture coordinates
        public static readonly int UV2 = Register("UV2"); // Third texture coordinates
        public static readonly int UV3 = Register("UV3"); // Fourth texture coordinates
        public static readonly int UV4 = Register("UV4"); // Fifth texture coordinates
        public static readonly int UV5 = Register("UV5"); // Sixth texture coordinates
        public static readonly int UV6 = Register("UV6"); // Seventh texture coordinates
        public static readonly int UV7 = Register("UV7"); // Eighth texture coordinates
        public static readonly int BoneWeights = Register("BoneWeights"); // Skinned mesh bone weights
        public static readonly int BlendIndices = Register("BlendIndices"); // Skinned mesh bone indices

        public static int Register(string name)
        {
            if (m_NameToHash.TryGetValue(name, out int existing))
                return existing;

            int hash = name.GetHashCode();

            // Handle hash collisions
            while (m_HashToName.ContainsKey(hash))
                hash++;

            m_NameToHash[name] = hash;
            m_HashToName[hash] = name;
            return hash;
        }

        public static string GetName(int hash) => m_HashToName.TryGetValue(hash, out string name) ? name : $"Unknown_{hash}";
    }
}