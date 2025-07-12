using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;

namespace PropellerheadMesh
{
    public class CubeGeneratorTest : MonoBehaviour
    {
        [SerializeField] private float radius = 1f;
        [SerializeField] private int subdivisionLevel = 2;
        
        private void Start()
        {
            CreateSphere();
        }
        
        private void CreateSphere()
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            var capa = 64;
            
            var nativeDetail = new NativeDetail(capa, entityManager, Allocator.Persistent);
            nativeDetail.AddPointAttribute<float3>(1); 
            
            // Generate cube geometry
            NativeCubeGenerator.GenerateCube(ref nativeDetail, new float3(1,1,1), true);
            
            var mesh = new Mesh();
            nativeDetail.FillUnityMesh(mesh);
            
            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = mesh;
            nativeDetail.Dispose();
        }
    }
}