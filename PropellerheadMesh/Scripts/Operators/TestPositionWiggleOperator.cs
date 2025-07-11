using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Legacy.Operators
{
    /// <summary>
    /// Operator for wiggling/shaking point positions using noise
    /// </summary>
    public class PositionWiggler
    {
        // Store original positions to keep wiggling around the original shape
        private readonly Dictionary<long, float3> m_OriginalPositions = new();
        
        // Wiggle parameters
        public float NoiseScale { get; set; } = 1f;
        public float WiggleAmplitude { get; set; } = 0.3f;
        public float3 TimeMultiplier { get; set; } = new(0.8f, 0.6f, 0.4f);
        public float3 NoiseOffset { get; set; } = new(0.3f, 0.9f, 0.7f);
        
        /// <summary>
        /// Initialize the wiggler with a detail. Stores original positions.
        /// </summary>
        /// <param name="detail">The detail to wiggle</param>
        public void Initialize(Detail detail)
        {
            if (detail == null)
                return;
                
            m_OriginalPositions.Clear();
            StoreOriginalPositions(detail);
        }
        
        /// <summary>
        /// Apply wiggle effect to all points in the detail
        /// </summary>
        /// <param name="detail">The detail to wiggle</param>
        /// <param name="time">Time value for animation (usually Time.time)</param>
        public void Apply(Detail detail, float time)
        {
            if (detail == null)
                return;

            // Get the position attribute
            var positionAttrib = detail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            // Store original positions if not already stored
            if (m_OriginalPositions.Count == 0)
            {
                StoreOriginalPositions(detail);
            }

            // Apply noise-based wiggling to all points
            foreach (var pointOffset in detail.GetAllPointOffsets())
            {
                if (m_OriginalPositions.TryGetValue(pointOffset, out var originalPos))
                {
                    var newPosition = CalculateWiggledPosition(originalPos, time);
                    positionAttrib.Set(pointOffset, newPosition);
                }
            }
        }
        
        /// <summary>
        /// Apply wiggle effect to specific points only
        /// </summary>
        /// <param name="detail">The detail to wiggle</param>
        /// <param name="pointOffsets">Specific point offsets to wiggle</param>
        /// <param name="time">Time value for animation</param>
        public void ApplyToPoints(Detail detail, IEnumerable<long> pointOffsets, float time)
        {
            if (detail == null || pointOffsets == null)
                return;

            var positionAttrib = detail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            // Store original positions if not already stored
            if (m_OriginalPositions.Count == 0)
            {
                StoreOriginalPositions(detail);
            }

            foreach (var pointOffset in pointOffsets)
            {
                if (m_OriginalPositions.TryGetValue(pointOffset, out var originalPos))
                {
                    var newPosition = CalculateWiggledPosition(originalPos, time);
                    positionAttrib.Set(pointOffset, newPosition);
                }
            }
        }
        
        /// <summary>
        /// Reset all points to their original positions
        /// </summary>
        /// <param name="detail">The detail to reset</param>
        public void Reset(Detail detail)
        {
            if (detail == null)
                return;

            var positionAttrib = detail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            foreach (var kvp in m_OriginalPositions)
            {
                var pointOffset = kvp.Key;
                var originalPos = kvp.Value;
                
                if (positionAttrib.HasValue(pointOffset))
                {
                    positionAttrib.Set(pointOffset, originalPos);
                }
            }
        }
        
        /// <summary>
        /// Clear stored original positions. Call this when the detail structure changes.
        /// </summary>
        public void ClearOriginalPositions()
        {
            m_OriginalPositions.Clear();
        }
        
        /// <summary>
        /// Update original positions for new or changed points
        /// </summary>
        /// <param name="detail">The detail to update from</param>
        public void UpdateOriginalPositions(Detail detail)
        {
            if (detail == null)
                return;
                
            StoreOriginalPositions(detail);
        }
        
        private void StoreOriginalPositions(Detail detail)
        {
            var positionAttrib = detail.GetPointAttrib<float3>(AttribID.Position);
            if (positionAttrib == null)
                return;

            foreach (var pointOffset in detail.GetAllPointOffsets())
            {
                if (!m_OriginalPositions.ContainsKey(pointOffset))
                {
                    var originalPos = positionAttrib.Get(pointOffset);
                    m_OriginalPositions[pointOffset] = originalPos;
                }
            }
        }
        
        private float3 CalculateWiggledPosition(float3 originalPos, float time)
        {
            // Generate 3D noise for each axis using custom 3D noise
            var noiseX = Misc.Noises.Noise3D(
                originalPos.x * NoiseScale + time * TimeMultiplier.x, 
                originalPos.y * NoiseScale + time * NoiseOffset.x,
                originalPos.z * NoiseScale
            );
    
            var noiseY = Misc.Noises.Noise3D(
                originalPos.y * NoiseScale + time * TimeMultiplier.y, 
                originalPos.z * NoiseScale + time * NoiseOffset.y,
                originalPos.x * NoiseScale
            );
    
            var noiseZ = Misc.Noises.Noise3D(
                originalPos.z * NoiseScale + time * TimeMultiplier.z, 
                originalPos.x * NoiseScale + time * NoiseOffset.z,
                originalPos.y * NoiseScale
            );

            // Apply wiggle offset
            var wiggleOffset = new float3(noiseX, noiseY, noiseZ) * WiggleAmplitude;
            return originalPos + wiggleOffset;
        }
    }
}