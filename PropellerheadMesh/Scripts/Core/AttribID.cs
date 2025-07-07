using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace PropellerHead
{
    /// <summary>
    /// Manages attribute IDs with registration and lookup
    /// Features:
    /// - Performance monitoring and metrics
    /// - Reserved ID ranges for system attributes
    /// - Comprehensive validation and error handling
    /// </summary>
    public static class AttribID
    {
        private static readonly Dictionary<string, int> s_NameToId = new();
        private static readonly Dictionary<int, string> s_IdToName = new();
        private static int s_NextId;
        
        // Performance counters
        private static long s_RegisterCount;
        private static long s_LookupCount;
        
        // Reserved ranges
        private const int SYSTEM_RESERVED_START = 0;
        private const int SYSTEM_RESERVED_END = 99;
        private const int USER_RESERVED_START = 100;

        // Standard Unity Mesh Attributes (System Reserved Range)
        public static readonly int Position = RegisterSystemAttribute("Position"); // Vertex positions
        public static readonly int Normal = RegisterSystemAttribute("Normal"); // Vertex normals
        public static readonly int Tangent = RegisterSystemAttribute("Tangent"); // Vertex tangents
        public static readonly int Color = RegisterSystemAttribute("Color"); // Vertex colors
        public static readonly int UV0 = RegisterSystemAttribute("UV0"); // Primary texture coordinates
        public static readonly int UV1 = RegisterSystemAttribute("UV1"); // Secondary texture coordinates
        public static readonly int UV2 = RegisterSystemAttribute("UV2"); // Third texture coordinates
        public static readonly int UV3 = RegisterSystemAttribute("UV3"); // Fourth texture coordinates
        public static readonly int UV4 = RegisterSystemAttribute("UV4"); // Fifth texture coordinates
        public static readonly int UV5 = RegisterSystemAttribute("UV5"); // Sixth texture coordinates
        public static readonly int UV6 = RegisterSystemAttribute("UV6"); // Seventh texture coordinates
        public static readonly int UV7 = RegisterSystemAttribute("UV7"); // Eighth texture coordinates
        public static readonly int BoneWeights = RegisterSystemAttribute("BoneWeights"); // Skinned mesh bone weights
        public static readonly int BlendIndices = RegisterSystemAttribute("BlendIndices"); // Skinned mesh bone indices

        /// <summary>
        /// Gets performance metrics for monitoring
        /// </summary>
        public static AttribIDMetrics GetMetrics()
        {
            var systemCount = 0;
            foreach (var kvp in s_IdToName)
            {
                if (kvp.Key < USER_RESERVED_START)
                    systemCount++;
            }

            return new AttribIDMetrics(
                s_NameToId.Count,
                s_RegisterCount,
                s_LookupCount,
                s_NextId,
                systemCount
            );
        }

        /// <summary>
        /// Registers a new attribute name and returns its ID
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="name">The name of the attribute</param>
        /// <returns>The attribute ID</returns>
        /// <exception cref="ArgumentException">Thrown if the name is invalid</exception>
        public static int Register(string name)
        {
            ValidateAttributeName(name);
            
            name = name.Trim();
            
            s_RegisterCount++;
            
            // Try to get existing ID first
            if (s_NameToId.TryGetValue(name, out int existingId))
                return existingId;

            // Generate new ID (user range)
            int newId = GenerateUserId();

            // Register the mapping
            s_NameToId[name] = newId;
            s_IdToName[newId] = name;
            
            Debug.WriteLine($"Registered attribute: {name} -> ID {newId}");

            return newId;
        }

        /// <summary>
        /// Registers a system attribute (internal use only)
        /// </summary>
        private static int RegisterSystemAttribute(string name)
        {
            ValidateAttributeName(name);
            
            name = name.Trim();
            
            // Try to get existing ID first
            if (s_NameToId.TryGetValue(name, out int existingId))
                return existingId;

            // Generate system ID
            int newId = GenerateSystemId();

            // Register the mapping
            s_NameToId[name] = newId;
            s_IdToName[newId] = name;
            
            Debug.WriteLine($"Registered system attribute: {name} -> ID {newId}");

            return newId;
        }

        private static int GenerateSystemId()
        {
            int id;
            do
            {
                id = s_NextId++;
            } while (id >= SYSTEM_RESERVED_END);
            
            return id;
        }

        private static int GenerateUserId()
        {
            int id;
            do
            {
                id = s_NextId++;
            } while (id < USER_RESERVED_START);
            
            return id;
        }

        private static void ValidateAttributeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Attribute name cannot be null, empty, or whitespace", nameof(name));
            
            if (name.Length > 64)
                throw new ArgumentException("Attribute name cannot exceed 64 characters", nameof(name));
            
            if (name.Contains('\0'))
                throw new ArgumentException("Attribute name cannot contain null characters", nameof(name));
            
            // Check for reserved prefixes
            var trimmed = name.Trim();
            if (trimmed.StartsWith("__") || trimmed.StartsWith("Unity_"))
                throw new ArgumentException("Attribute name uses reserved prefix", nameof(name));
        }

        /// <summary>
        /// Gets the name associated with an attribute ID
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="id">The attribute ID</param>
        /// <returns>The attribute name, or null if not found</returns>
        public static string GetName(int id)
        {
            s_LookupCount++;
            return s_IdToName.GetValueOrDefault(id);
        }

        /// <summary>
        /// Gets the ID associated with an attribute name
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="name">The attribute name</param>
        /// <returns>The attribute ID, or -1 if not found</returns>
        public static int GetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return -1;

            s_LookupCount++;
            if (s_NameToId.TryGetValue(name.Trim(), out int id))
                return id;
            return -1;
        }

        /// <summary>
        /// Checks if an attribute ID is registered
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="id">The attribute ID to check</param>
        /// <returns>True if the ID is registered, false otherwise</returns>
        public static bool IsRegistered(int id)
        {
            return s_IdToName.ContainsKey(id);
        }

        /// <summary>
        /// Checks if an attribute name is registered
        /// Performance: O(1) average case
        /// </summary>
        /// <param name="name">The attribute name to check</param>
        /// <returns>True if the name is registered, false otherwise</returns>
        public static bool IsRegistered(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && s_NameToId.ContainsKey(name.Trim());
        }

        /// <summary>
        /// Checks if an ID is in the system reserved range
        /// </summary>
        /// <param name="id">The attribute ID to check</param>
        /// <returns>True if the ID is system reserved</returns>
        public static bool IsSystemReserved(int id)
        {
            return id >= SYSTEM_RESERVED_START && id < SYSTEM_RESERVED_END;
        }

        /// <summary>
        /// Gets all registered attribute names
        /// Performance: O(n) - creates snapshot
        /// </summary>
        /// <returns>A collection of all registered attribute names</returns>
        public static IEnumerable<string> GetAllNames()
        {
            return s_NameToId.Keys.ToList();
        }

        /// <summary>
        /// Gets all registered attribute IDs
        /// Performance: O(n) - creates snapshot
        /// </summary>
        /// <returns>A collection of all registered attribute IDs</returns>
        public static IEnumerable<int> GetAllIds()
        {
            return s_IdToName.Keys.ToList();
        }

        /// <summary>
        /// Gets system attribute names only
        /// </summary>
        /// <returns>Collection of system attribute names</returns>
        public static IEnumerable<string> GetSystemAttributeNames()
        {
            return s_IdToName.Where(kvp => IsSystemReserved(kvp.Key)).Select(kvp => kvp.Value).ToList();
        }

        /// <summary>
        /// Gets user attribute names only
        /// </summary>
        /// <returns>Collection of user attribute names</returns>
        public static IEnumerable<string> GetUserAttributeNames()
        {
            return s_IdToName.Where(kvp => !IsSystemReserved(kvp.Key)).Select(kvp => kvp.Value).ToList();
        }

        /// <summary>
        /// Gets the total number of registered attributes
        /// </summary>
        public static int Count => s_NameToId.Count;

        /// <summary>
        /// Validates the internal consistency of the attribute registry
        /// </summary>
        /// <returns>True if internal state is consistent</returns>
        public static bool ValidateIntegrity()
        {
            try
            {
                // Check that both dictionaries have the same count
                if (s_NameToId.Count != s_IdToName.Count)
                    return false;

                // Check bidirectional mapping consistency
                foreach (var kvp in s_NameToId)
                {
                    if (!s_IdToName.TryGetValue(kvp.Value, out string name) || name != kvp.Key)
                        return false;
                }

                foreach (var kvp in s_IdToName)
                {
                    if (!s_NameToId.TryGetValue(kvp.Value, out int id) || id != kvp.Key)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clears all registered attributes (for testing purposes)
        /// </summary>
        internal static void ClearForTesting()
        {
            s_NameToId.Clear();
            s_IdToName.Clear();
            s_NextId = 0;
            s_RegisterCount = 0;
            s_LookupCount = 0;
        }
    }

    /// <summary>
    /// Performance metrics for AttribID monitoring
    /// </summary>
    public struct AttribIDMetrics
    {
        public int RegisteredCount { get; private set; }
        public long RegisterCount { get; private set; }
        public long LookupCount { get; private set; }
        public int NextId { get; private set; }
        public int SystemAttributeCount { get; private set; }

        public AttribIDMetrics(int registeredCount, long registerCount, long lookupCount, int nextId, int systemAttributeCount)
        {
            RegisteredCount = registeredCount;
            RegisterCount = registerCount;
            LookupCount = lookupCount;
            NextId = nextId;
            SystemAttributeCount = systemAttributeCount;
        }
        
        public override string ToString()
        {
            return $"Registered: {RegisteredCount}, Registers: {RegisterCount}, Lookups: {LookupCount}, " +
                   $"NextID: {NextId}, System: {SystemAttributeCount}";
        }
    }
}