using UnityEngine;
using UnityEditor;
using System.IO;

public class VolumeCloudSetup : Editor
{
    [MenuItem("VolumeCloud/Setup Material and Scene")]
    public static void SetupVolumeCloud()
    {
        // 1. 查找或创建材质
        string materialPath = "Assets/VolumeCloud/Materials/CloudMaterial.mat";
        string materialDir = Path.GetDirectoryName(materialPath);
        
        // 确保Materials目录存在
        if (!AssetDatabase.IsValidFolder(materialDir))
        {
            string parentDir = "Assets/VolumeCloud";
            AssetDatabase.CreateFolder(parentDir, "Materials");
        }
        
        Material cloudMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        
        if (cloudMaterial == null)
        {
            // 查找Shader
            Shader cloudShader = Shader.Find("VolumeCloud/CloudRayMarching");
            
            if (cloudShader == null)
            {
                Debug.LogError("Cannot find shader 'VolumeCloud/CloudRayMarching'. Please ensure the shader is compiled.");
                return;
            }
            
            // 创建新材质
            cloudMaterial = new Material(cloudShader);
            
            // 设置默认参数
            cloudMaterial.SetInt("_RayMarchSteps", 64);
            cloudMaterial.SetFloat("_DensityThreshold", 0.3f);
            cloudMaterial.SetFloat("_DensityMultiplier", 1.0f);
            cloudMaterial.SetFloat("_DetailStrength", 0.5f);
            cloudMaterial.SetFloat("_Absorption", 1.0f);
            cloudMaterial.SetColor("_CloudColor", Color.white);
            cloudMaterial.SetFloat("_LightAbsorption", 0.5f);
            cloudMaterial.SetFloat("_ScatteringCoeff", 0.3f);
            
            // 保存材质
            AssetDatabase.CreateAsset(cloudMaterial, materialPath);
            Debug.Log($"Created material at: {materialPath}");
        }
        else
        {
            Debug.Log($"Material already exists at: {materialPath}");
        }
        
        // 2. 查找或创建VolumeCloud GameObject
        VolumeCloud volumeCloud = FindObjectOfType<VolumeCloud>();
        
        if (volumeCloud == null)
        {
            GameObject cloudObj = new GameObject("VolumeCloud");
            volumeCloud = cloudObj.AddComponent<VolumeCloud>();
            Debug.Log("Created VolumeCloud GameObject in scene.");
        }
        
        // 3. 分配引用
        if (volumeCloud.noiseCompute == null)
        {
            ComputeShader noiseShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/VolumeCloud/Shaders/NoiseGenerator.compute");
            if (noiseShader != null)
            {
                volumeCloud.noiseCompute = noiseShader;
                Debug.Log("Assigned NoiseGenerator.compute to VolumeCloud.");
            }
            else
            {
                Debug.LogWarning("Cannot find NoiseGenerator.compute shader.");
            }
        }
        
        if (volumeCloud.cloudMaterial == null)
        {
            volumeCloud.cloudMaterial = cloudMaterial;
            Debug.Log("Assigned CloudMaterial to VolumeCloud.");
        }
        
        // 标记为已修改
        EditorUtility.SetDirty(volumeCloud);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log("=== VolumeCloud Setup Complete ===");
        Debug.Log("Next steps:");
        Debug.Log("1. Select the VolumeCloud GameObject in the scene");
        Debug.Log("2. Click 'Regenerate Noise' in the context menu (right-click component)");
        Debug.Log("3. Adjust parameters in the Inspector");
        Debug.Log("4. Enter Play mode to see the volume cloud");
    }
    
    [MenuItem("VolumeCloud/Create Cloud Box in Scene")]
    public static void CreateCloudBox()
    {
        VolumeCloud volumeCloud = FindObjectOfType<VolumeCloud>();
        
        if (volumeCloud == null)
        {
            Debug.LogError("No VolumeCloud component found in scene. Please run 'VolumeCloud/Setup Material and Scene' first.");
            return;
        }
        
        if (volumeCloud.cloudBox == null)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "CloudVolume";
            box.transform.parent = volumeCloud.transform;
            box.transform.localPosition = Vector3.zero;
            box.transform.localScale = Vector3.one * 10f;
            
            // 配置Mesh Renderer
            MeshRenderer mr = box.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                
                if (volumeCloud.cloudMaterial != null)
                {
                    mr.sharedMaterial = volumeCloud.cloudMaterial;
                }
            }
            
            volumeCloud.cloudBox = box;
            EditorUtility.SetDirty(volumeCloud);
            
            Debug.Log("Created CloudVolume box in scene.");
        }
        else
        {
            Debug.Log("Cloud box already exists.");
        }
    }
}

