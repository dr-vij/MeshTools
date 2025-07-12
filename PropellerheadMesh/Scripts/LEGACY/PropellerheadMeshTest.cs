using UnityEngine;
using Unity.Mathematics;
using ViJMeshTools;
using Legacy;

public class PropellerHeadMeshTest : MonoBehaviour
{
    void Update()
    {
        const int vertexCount = 1024 * 1024;

        Stopwatcher.ResetAll();
        Stopwatcher.Start("MeshDataCreation");
        
        // Create attributes directly
        var posAttribute = new Attribute<float3>(0);
        var sumAttribute = new Attribute<float>(1);
        
        // Fill positions with sequential data
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new float3(i, i, i);
            posAttribute.Set(i, pos);
        }
        Stopwatcher.Pause("MeshDataCreation");

        Stopwatcher.Start("Calculation");
        // Calculate sums
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = posAttribute.Get(i);
            var sum = pos.x + pos.y + pos.z;
            sumAttribute.Set(i, sum);
        }
        Stopwatcher.Pause("Calculation");
        
        Stopwatcher.DebugLogMicroseconds();

        // Read some results
        for (int i = 0; i < 10; i++)
        {
            var pos = posAttribute.Get(i);
            var sum = sumAttribute.Get(i);
            Debug.Log($"Position[{i}]: {pos}, Sum: {sum}");
        }

        posAttribute.Dispose();
        sumAttribute.Dispose();
    }
}