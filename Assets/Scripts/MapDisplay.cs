using UnityEngine;
using Unity.AI.Navigation; // NavMeshSurface kullanabilmek için gerekli paket

public class MapDisplay : MonoBehaviour
{
    public Renderer textureRender;
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;

    // YENÝ EKLENEN REFERANSLAR
    public MeshCollider meshCollider;
    public NavMeshSurface navMeshSurface;

    public void DrawTexture(Texture2D texture)
    {
        textureRender.sharedMaterial.mainTexture = texture;
        textureRender.transform.localScale = new Vector3(texture.width, 1, texture.height);
    }

    public void DrawMesh(MeshData meshData, Texture2D texture)
    {
        Mesh generatedMesh = meshData.CreateMesh();

        meshFilter.sharedMesh = generatedMesh;
        meshRenderer.sharedMaterial.mainTexture = texture;

        // Harita her deðiþtiðinde 3D fiziksel çarpýþma yüzeyini güncelle
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = generatedMesh;
        }

        // Harita oluþtuktan hemen sonra NavMesh'i otomatik olarak piþir (Bake)
        if (navMeshSurface != null)
        {
            navMeshSurface.BuildNavMesh();
        }
    }
}