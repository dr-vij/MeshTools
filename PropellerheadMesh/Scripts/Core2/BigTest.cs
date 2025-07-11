using UnityEngine;
using Unity.Mathematics;
using ViJMeshTools;
using Legacy;
using Unity.Collections;
using Unity.Jobs;

public class BigTest : MonoBehaviour
{
    void Update()
    {
        Debug.Log("-------------------------------");
        Stopwatcher.ResetAll();
        CheckNativeVersion();
        CheckRefTypeVersion();
    }

    static unsafe void CheckNativeVersion()
    {
        const int vertexCount = 1024 * 1024;
        
        Stopwatcher.Start("NativeMeshDataCreation");
        var meshData = new PropellerheadMesh.MeshData(vertexCount, Allocator.TempJob);
        
        var posPtr = meshData.AttributeMap.GetBasePointerUnchecked<float3>(0);

        for (int i = 0; i < vertexCount; i++)
            posPtr[i] = new float3(i, i, i);
        Stopwatcher.Pause("NativeMeshDataCreation");
        
        Stopwatcher.Start("BurstJobCalculation");
        var job = new PropellerheadMesh.CalculateSumJob
        {
            AttributeMap = meshData.AttributeMap
        };
        
        var jobHandle = job.Schedule(vertexCount, 512);
        jobHandle.Complete();
        Stopwatcher.Pause("BurstJobCalculation");
        
        Debug.Log("Native Version Result:");
        Stopwatcher.DebugLogMicroseconds();
        
        // Read results
        var sumPtr = meshData.AttributeMap.GetBasePointerUnchecked<float>(1);
        for (int i = 0; i < 10; i++)
            Debug.Log($"Position[{i}]: {posPtr[i]}, Sum: {sumPtr[i]}");
        
        meshData.Dispose();
    }

    static void CheckRefTypeVersion()
    {
        const int vertexCount = 1024 * 1024;

        Stopwatcher.Start("RefMeshDataCreation");
        
        // Create attributes directly
        var posAttribute = new Attribute<float3>(0);
        var sumAttribute = new Attribute<float>(1);
        
        // Fill positions with sequential data
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new float3(i, i, i);
            posAttribute.Set(i, pos);
        }
        Stopwatcher.Pause("RefMeshDataCreation");

        Stopwatcher.Start("RefCalculation");
        // Calculate sums
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = posAttribute.Get(i);
            var sum = pos.x + pos.y + pos.z;
            sumAttribute.Set(i, sum);
        }
        Stopwatcher.Pause("RefCalculation");
        
        Debug.Log("Reference Version Result");
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