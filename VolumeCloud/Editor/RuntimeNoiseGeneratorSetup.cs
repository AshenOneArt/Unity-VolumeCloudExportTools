using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

public class RuntimeNoiseGeneratorSetup
{
    [MenuItem("VolumeCloud/Create Runtime Noise Generator Scene")]
    public static void CreateRuntimeScene()
    {
        // 创建新场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // 添加相机
        GameObject cameraObj = new GameObject("Main Camera");
        Camera cam = cameraObj.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cameraObj.AddComponent<AudioListener>();
        cameraObj.transform.position = new Vector3(0, 0, -10);
        
        // 创建 RuntimeNoiseGenerator 对象
        GameObject generatorObj = new GameObject("RuntimeNoiseGenerator");
        RuntimeNoiseGenerator generator = generatorObj.AddComponent<RuntimeNoiseGenerator>();
        
        // 查找并分配 NoiseGenerator ComputeShader
        string[] guids = AssetDatabase.FindAssets("NoiseGenerator t:ComputeShader");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            ComputeShader noiseCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            generator.noiseCompute = noiseCompute;
            Debug.Log($"Assigned NoiseGenerator ComputeShader from: {path}");
        }
        else
        {
            Debug.LogWarning("NoiseGenerator ComputeShader not found! Please assign it manually.");
        }
        
        // 添加 RuntimeToolManager（作为单例管理器）
        generatorObj.AddComponent<RuntimeToolManager>();
        
        // 保存场景
        string scenePath = "Assets/VolumeCloud/Scenes/NoiseGeneratorTool.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        
        Debug.Log($"Runtime Noise Generator Scene created at: {scenePath}");
        EditorUtility.DisplayDialog("Success", 
            $"Scene created successfully!\n\nPath: {scenePath}\n\n" +
            "Press Play to test the Runtime Noise Generator.", 
            "OK");
    }
    
    [MenuItem("VolumeCloud/Open Runtime Noise Generator Scene")]
    public static void OpenRuntimeScene()
    {
        string scenePath = "Assets/VolumeCloud/Scenes/NoiseGeneratorTool.unity";
        if (System.IO.File.Exists(scenePath))
        {
            EditorSceneManager.OpenScene(scenePath);
        }
        else
        {
            EditorUtility.DisplayDialog("Scene Not Found", 
                "NoiseGeneratorTool scene not found.\n\n" +
                "Please create it first using:\nVolumeCloud > Create Runtime Noise Generator Scene", 
                "OK");
        }
    }
}
#endif




