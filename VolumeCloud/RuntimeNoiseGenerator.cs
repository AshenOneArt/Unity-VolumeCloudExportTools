using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class RuntimeNoiseGenerator : MonoBehaviour
{
    [Header("References")]
    public VolumeCloud volumeCloud; // 可选：用于同步参数到云渲染
    
    [Header("Noise Settings")]
    public ComputeShader noiseCompute;
    public int noiseResolution = 64;
    
    [Header("Base Shape (R Channel)")]
    public int baseFrequencyExponent = 1;
    [Range(1, 8)]
    public int baseOctaves = 4;
    [Range(0f, 1f)]
    public float basePersistence = 0.5f;
    
    [Header("Base Worley Settings")]
    [Range(-2, 3)]
    public int baseWorleyScaleExponent = -1;
    [Range(0f, 1f)]
    public float baseCoverage = 0.5f;
    
    [Header("Detail (G Channel)")]
    public float detailFrequency = 8f;
    [Range(1, 6)]
    public int detailOctaves = 4;
    [Range(0f, 1f)]
    public float detailPersistence = 1f;
    
    [Header("Worley Settings")]
    [Range(1, 10)]
    public int worleyCellCount = 1;
    
    [Header("Rendering Settings")]
    public float noiseScale = 2.0f; // 噪声采样缩放
    public Vector3 noiseOffset = Vector3.zero; // 噪声偏移（可用于动画）
    public float noiseSpeed = 0.1f; // 噪声速度
    
    [Header("Export Settings")]
    public bool compressDDS = true;
    
    private RenderTexture cloudNoiseTexture;
    private bool noiseGenerated = false;
    private bool isExporting = false;
    private string exportStatus = "";
    
    // IMGUI
    private Vector2 scrollPosition;
    private bool showUI = true;
    private Rect windowRect = new Rect(20, 350, 450, 1150);
    private GUIStyle boldLabelStyle;
    private GUIStyle buttonStyle;
    
    // 文本输入缓存
    private string resolutionText = "64";
    
    void Start()
    {
        InitializeStyles();
        resolutionText = noiseResolution.ToString();
    }
    
    void InitializeStyles()
    {
        boldLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
    }
    
    void OnGUI()
    {
        if (boldLabelStyle == null) InitializeStyles();
        
        if (!showUI) return;
        
        windowRect = GUILayout.Window(0, windowRect, DrawWindow, "Noise Generator Tool", GUILayout.Width(450), GUILayout.Height(750));
    }
    
    void DrawWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(680));
        
        // Noise Generation Section
        GUILayout.Label("Noise Generation", boldLabelStyle);
        GUILayout.Label("提示: 修改分辨率后需要点击「Generate Noise」重新生成", GUI.skin.box);
        GUILayout.Space(3);
        int oldResolution = noiseResolution;
        noiseResolution = DrawIntFieldEditable("Resolution", ref resolutionText, noiseResolution, 32, 256);
        GUILayout.Space(5);
        
        // Base Shape Section
        GUILayout.Space(10);
        GUILayout.Label("Base Shape (R Channel)", boldLabelStyle);
        baseFrequencyExponent = DrawIntSlider("Base Frequency Exp", baseFrequencyExponent, 2, 4);
        GUILayout.Label($"  Actual Frequency: {Mathf.Pow(2f, baseFrequencyExponent):F1}", GUI.skin.label);
        baseOctaves = DrawIntSlider("Base Octaves", baseOctaves, 1, 8);
        basePersistence = DrawFloatSlider("Base Persistence", basePersistence, 0f, 1f);
        
        // Base Worley Section
        GUILayout.Space(10);
        GUILayout.Label("Base Worley Settings", boldLabelStyle);
        baseWorleyScaleExponent = DrawIntSlider("Worley Scale Exp", baseWorleyScaleExponent, -2, 3);
        GUILayout.Label($"  Actual Scale: {Mathf.Pow(2f, baseWorleyScaleExponent):F3}", GUI.skin.label);
        baseCoverage = DrawFloatSlider("Base Coverage", baseCoverage, 0f, 1f);
        
        // Detail Section
        GUILayout.Space(10);
        GUILayout.Label("Detail (G Channel)", boldLabelStyle);
        detailFrequency = DrawFloatField("Detail Frequency", detailFrequency);
        detailOctaves = DrawIntSlider("Detail Octaves", detailOctaves, 1, 6);
        detailPersistence = DrawFloatSlider("Detail Persistence", detailPersistence, 0f, 1f);
        
        // Worley Settings
        GUILayout.Space(10);
        GUILayout.Label("Worley Settings", boldLabelStyle);
        worleyCellCount = DrawIntSlider("Worley Cell Count", worleyCellCount, 1, 10);
        
        // Rendering Settings
        GUILayout.Space(10);
        GUILayout.Label("Rendering Settings", boldLabelStyle);
        noiseScale = DrawFloatSlider("Noise Scale", noiseScale, 0.1f, 10f);
        GUILayout.Label("  控制噪声采样的密度（不加入烘焙流程）", GUI.skin.label);
        GUILayout.Space(5);
        
        GUILayout.Label("Noise Offset", GUI.skin.label);
        float offsetX = DrawFloatSlider("  Offset X", noiseOffset.x, -2f, 2f);
        float offsetY = DrawFloatSlider("  Offset Y", noiseOffset.y, -2f, 2f);
        float offsetZ = DrawFloatSlider("  Offset Z", noiseOffset.z, -2f, 2f);
        noiseOffset = new Vector3(offsetX, offsetY, offsetZ);
        GUILayout.Label("  噪声偏移量，可用于云动画（不加入烘焙流程）", GUI.skin.label);

        GUILayout.Label("Noise Speed", GUI.skin.label);
        noiseSpeed = DrawFloatSlider("  Speed", noiseSpeed, 0f, 1f);
        GUILayout.Label("  噪声速度，可用于云动画（不加入烘焙流程）", GUI.skin.label);
        
        GUILayout.EndScrollView();
        
        // 实时同步参数（除了分辨率）
        if (noiseGenerated && volumeCloud != null && GUI.changed)
        {
            // 如果分辨率改变，不实时同步（需要重新生成）
            if (noiseResolution == oldResolution)
            {
                SyncParametersToVolumeCloud();
                // 标记需要重新生成云的材质
                if (volumeCloud.cloudMaterial != null && cloudNoiseTexture != null)
                {
                    RegenerateNoiseWithCurrentSettings();
                }
                
                // 实时更新 NoiseScale 和 NoiseOffset 到材质
                UpdateRenderingParametersToMaterial();
            }
        }
        
        // Export Settings
        GUILayout.Space(10);
        GUILayout.Label("Export Settings", boldLabelStyle);
        compressDDS = GUILayout.Toggle(compressDDS, "Compress DDS (BC6H/BC7)");
        
        // Status
        if (!string.IsNullOrEmpty(exportStatus))
        {
            GUILayout.Label(exportStatus, GUI.skin.box);
        }
        
        // Action Buttons
        GUILayout.Space(10);
        
        GUI.enabled = !isExporting;
        if (GUILayout.Button("Generate Noise", buttonStyle, GUILayout.Height(40)))
        {
            GenerateNoise();
        }
        GUILayout.Label("重新生成 3D 噪声纹理（修改分辨率后必须点击）", GUI.skin.label);
        
        GUILayout.Space(5);
        GUI.enabled = noiseGenerated && !isExporting;
        if (GUILayout.Button("Export as DDS", buttonStyle, GUILayout.Height(40)))
        {
            StartCoroutine(ExportDDS());
        }
        GUILayout.Label("导出为 DDS Volume Texture 文件", GUI.skin.label);
        GUI.enabled = true;
        
        GUI.DragWindow();
    }
    
    int DrawIntSlider(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        value = (int)GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(150));
        GUILayout.Label(value.ToString(), GUILayout.Width(50));
        GUILayout.EndHorizontal();
        return value;
    }
    
    float DrawFloatSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(150));
        GUILayout.Label(value.ToString("F3"), GUILayout.Width(50));
        GUILayout.EndHorizontal();
        return value;
    }
    
    float DrawFloatField(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(150));
        if (float.TryParse(text, out float result))
            value = result;
        GUILayout.Space(50);
        GUILayout.EndHorizontal();
        return value;
    }
    
    int DrawIntField(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(150));
        if (int.TryParse(text, out int result))
        {
            value = Mathf.Clamp(result, min, max);
        }
        GUILayout.Label($"[{min}-{max}]", GUILayout.Width(50));
        GUILayout.EndHorizontal();
        return value;
    }
    
    int DrawIntFieldEditable(string label, ref string textCache, int currentValue, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        
        // 使用缓存的文本，允许编辑
        textCache = GUILayout.TextField(textCache, GUILayout.Width(150));
        
        // 尝试解析输入的文本
        int result = currentValue;
        if (int.TryParse(textCache, out int parsed))
        {
            // 只有在范围内才接受，否则保持当前值
            if (parsed >= min && parsed <= max)
            {
                result = parsed;
            }
        }
        
        GUILayout.Label($"[{min}-{max}]", GUILayout.Width(50));
        GUILayout.EndHorizontal();
        return result;
    }
    
    Vector3 DrawVector3Field(Vector3 value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("X", GUILayout.Width(15));
        string xText = GUILayout.TextField(value.x.ToString("F3"), GUILayout.Width(60));
        if (float.TryParse(xText, out float x)) value.x = x;
        
        GUILayout.Label("Y", GUILayout.Width(15));
        string yText = GUILayout.TextField(value.y.ToString("F3"), GUILayout.Width(60));
        if (float.TryParse(yText, out float y)) value.y = y;
        
        GUILayout.Label("Z", GUILayout.Width(15));
        string zText = GUILayout.TextField(value.z.ToString("F3"), GUILayout.Width(60));
        if (float.TryParse(zText, out float z)) value.z = z;
        
        GUILayout.EndHorizontal();
        return value;
    }
    
    void SyncParametersToVolumeCloud()
    {
        if (volumeCloud == null) return;
        
        volumeCloud.baseFrequencyExponent = baseFrequencyExponent;
        volumeCloud.baseOctaves = baseOctaves;
        volumeCloud.basePersistence = basePersistence;
        volumeCloud.baseWorleyScaleExponent = baseWorleyScaleExponent;
        volumeCloud.baseCoverage = baseCoverage;
        volumeCloud.detailFrequency = detailFrequency;
        volumeCloud.detailOctaves = detailOctaves;
        volumeCloud.detailPersistence = detailPersistence;
        volumeCloud.worleyCellCount = worleyCellCount;
    }
    
    void UpdateNoiseScaleToMaterial()
    {
        if (volumeCloud == null || volumeCloud.cloudMaterial == null) return;
        
        // 更新材质的 NoiseScale 参数（使用统一的 float 值）
        volumeCloud.cloudMaterial.SetVector("_NoiseScale", new Vector4(noiseScale, noiseScale, noiseScale, 0));
    }
    
    void UpdateRenderingParametersToMaterial()
    {
        if (volumeCloud == null || volumeCloud.cloudMaterial == null) return;
        
        // 更新材质的 NoiseScale 和 NoiseOffset 参数
        volumeCloud.cloudMaterial.SetVector("_NoiseScale", new Vector4(noiseScale, noiseScale, noiseScale, 0));
        volumeCloud.cloudMaterial.SetVector("_NoiseOffset", new Vector4(noiseOffset.x, noiseOffset.y, noiseOffset.z, 0));
        volumeCloud.cloudMaterial.SetFloat("_NoiseSpeed", noiseSpeed);
    }
    
    void RegenerateNoiseWithCurrentSettings()
    {
        if (cloudNoiseTexture == null || noiseCompute == null) return;
        
        // 不需要重新创建 RenderTexture，只需重新计算
        int kernelHandle = noiseCompute.FindKernel("GenerateCloudNoise");
        noiseCompute.SetTexture(kernelHandle, "Result", cloudNoiseTexture);
        noiseCompute.SetInt("Resolution", noiseResolution);
        noiseCompute.SetFloat("BaseFrequency", Mathf.Pow(2f, baseFrequencyExponent));
        noiseCompute.SetInt("BaseOctaves", baseOctaves);
        noiseCompute.SetFloat("BasePersistence", basePersistence);
        noiseCompute.SetFloat("BaseWorleyScale", Mathf.Pow(2f, baseWorleyScaleExponent));
        noiseCompute.SetFloat("BaseCoverage", baseCoverage);
        noiseCompute.SetFloat("DetailFrequency", detailFrequency);
        noiseCompute.SetInt("DetailOctaves", detailOctaves);
        noiseCompute.SetFloat("DetailPersistence", detailPersistence);
        noiseCompute.SetInt("WorleyCellCount", worleyCellCount);
        
        int threadGroups = Mathf.CeilToInt(noiseResolution / 8.0f);
        noiseCompute.Dispatch(kernelHandle, threadGroups, threadGroups, threadGroups);
        
        // 更新 VolumeCloud 的纹理引用
        if (volumeCloud != null && volumeCloud.cloudMaterial != null)
        {
            volumeCloud.cloudMaterial.SetTexture("_CloudNoiseTex", cloudNoiseTexture);
        }
    }
    
    void GenerateNoise()
    {
        if (noiseCompute == null)
        {
            exportStatus = "错误: NoiseCompute shader 未分配!";
            Debug.LogError("NoiseCompute shader is not assigned!");
            return;
        }
        
        exportStatus = "正在生成噪声...";
        
        // 同步所有参数到 VolumeCloud（如果存在）
        if (volumeCloud != null)
        {
            volumeCloud.noiseResolution = noiseResolution;
            SyncParametersToVolumeCloud();
        }
        
        // 创建或重新创建 RenderTexture
        if (cloudNoiseTexture == null || cloudNoiseTexture.width != noiseResolution)
        {
            CreateRenderTexture3D();
        }
        
        // 设置参数并调度 ComputeShader
        int kernelHandle = noiseCompute.FindKernel("GenerateCloudNoise");
        noiseCompute.SetTexture(kernelHandle, "Result", cloudNoiseTexture);
        noiseCompute.SetInt("Resolution", noiseResolution);
        noiseCompute.SetFloat("BaseFrequency", Mathf.Pow(2f, baseFrequencyExponent));
        noiseCompute.SetInt("BaseOctaves", baseOctaves);
        noiseCompute.SetFloat("BasePersistence", basePersistence);
        noiseCompute.SetFloat("BaseWorleyScale", Mathf.Pow(2f, baseWorleyScaleExponent));
        noiseCompute.SetFloat("BaseCoverage", baseCoverage);
        noiseCompute.SetFloat("DetailFrequency", detailFrequency);
        noiseCompute.SetInt("DetailOctaves", detailOctaves);
        noiseCompute.SetFloat("DetailPersistence", detailPersistence);
        noiseCompute.SetInt("WorleyCellCount", worleyCellCount);
        
        int threadGroups = Mathf.CeilToInt(noiseResolution / 8.0f);
        noiseCompute.Dispatch(kernelHandle, threadGroups, threadGroups, threadGroups);
        
        noiseGenerated = true;
        exportStatus = $"噪声生成成功! ({noiseResolution}³)";
        Debug.Log($"Noise generated successfully! Resolution: {noiseResolution}³");
        
        // 更新渲染参数到材质
        UpdateRenderingParametersToMaterial();
    }
    
    void CreateRenderTexture3D()
    {
        if (cloudNoiseTexture != null)
        {
            cloudNoiseTexture.Release();
        }
        
        cloudNoiseTexture = new RenderTexture(noiseResolution, noiseResolution, 0, RenderTextureFormat.ARGBFloat);
        cloudNoiseTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        cloudNoiseTexture.volumeDepth = noiseResolution;
        cloudNoiseTexture.enableRandomWrite = true;
        cloudNoiseTexture.wrapMode = TextureWrapMode.Repeat;
        cloudNoiseTexture.filterMode = FilterMode.Trilinear;
        cloudNoiseTexture.Create();
    }
    
    IEnumerator ExportDDS()
    {
        isExporting = true;
        exportStatus = "准备导出...";
        
        // 1. 创建导出目录
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputDir = Path.Combine(Application.persistentDataPath, "NoiseExports", timestamp);
        Directory.CreateDirectory(outputDir);
        
        exportStatus = "正在下载工具...";
        yield return RuntimeToolManager.EnsureToolsExist();
        
        exportStatus = "正在导出切片...";
        yield return ExportSlices(outputDir);
        
        exportStatus = "正在转换为 DDS...";
        yield return ConvertToDDS(outputDir);
        
        isExporting = false;
        exportStatus = $"导出完成! 文件保存在:\n{outputDir}";
        Debug.Log($"Export completed! Files saved to: {outputDir}");
        
        // 打开导出目录
        Application.OpenURL(outputDir);
    }
    
    IEnumerator ExportSlices(string outputDir)
    {
        // 创建 PNG 和 EXR 目录
        string pngDir = Path.Combine(outputDir, "PNG");
        string exrDir = Path.Combine(outputDir, "EXR");
        Directory.CreateDirectory(pngDir);
        Directory.CreateDirectory(exrDir);
        
        RenderTexture tempRT = new RenderTexture(noiseResolution, noiseResolution, 0, RenderTextureFormat.ARGBFloat);
        Texture2D texture2D = new Texture2D(noiseResolution, noiseResolution, TextureFormat.RGBAFloat, false);
        
        for (int z = 0; z < noiseResolution; z++)
        {
            Graphics.CopyTexture(cloudNoiseTexture, z, tempRT, 0);
            RenderTexture.active = tempRT;
            texture2D.ReadPixels(new Rect(0, 0, noiseResolution, noiseResolution), 0, 0);
            texture2D.Apply();
            
            // 保存 PNG
            byte[] pngData = texture2D.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(pngDir, $"Slice_{z:000}.png"), pngData);
            
            // 保存 EXR
            byte[] exrData = texture2D.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            File.WriteAllBytes(Path.Combine(exrDir, $"Slice_{z:000}.exr"), exrData);
            
            exportStatus = $"正在导出切片... ({z + 1}/{noiseResolution})";
            
            if (z % 10 == 0)
                yield return null; // 每10个切片暂停一帧
        }
        
        RenderTexture.active = null;
        tempRT.Release();
        Destroy(texture2D);
    }
    
    IEnumerator ConvertToDDS(string outputDir)
    {
        string pngDir = Path.Combine(outputDir, "PNG");
        string tempVolumeDDS = Path.Combine(outputDir, "temp_volume.dds");
        string finalDDS = Path.Combine(outputDir, "CloudNoise_Volume.dds");
        
        // texassemble 不支持通配符，需要显式列出所有文件
        string[] pngFiles = Directory.GetFiles(pngDir, "Slice_*.png");
        if (pngFiles.Length == 0)
        {
            exportStatus = "错误: PNG 切片文件未找到!";
            Debug.LogError("PNG slice files not found!");
            yield break;
        }
        
        // 构建文件列表参数
        string fileList = string.Join(" ", System.Linq.Enumerable.Select(pngFiles, f => $"\"{f}\""));
        
        // 使用 texassemble 创建 Volume Texture
        exportStatus = "正在创建 Volume Texture...";
        string assembleArgs = $"volume -o \"{tempVolumeDDS}\" {fileList}";
        
        RuntimeToolManager.ProcessResult assembleResult = null;
        yield return RuntimeToolManager.RunProcess(RuntimeToolManager.TexassemblePath, assembleArgs, result => assembleResult = result);
        
        if (assembleResult == null || !assembleResult.Success)
        {
            exportStatus = $"错误: texassemble 失败! (退出码: {assembleResult?.ExitCode ?? -1})\n{assembleResult?.Error ?? "Unknown error"}";
            Debug.LogError($"texassemble failed:\n{assembleResult?.Error ?? "Unknown error"}\n{assembleResult?.Output ?? ""}");
            yield break;
        }
        
        if (!File.Exists(tempVolumeDDS))
        {
            exportStatus = "错误: texassemble 执行成功但未生成 DDS 文件!";
            Debug.LogError($"texassemble completed but no DDS file was created.\nOutput: {assembleResult.Output}\nError: {assembleResult.Error}");
            yield break;
        }
        
        // 如果需要压缩
        if (compressDDS)
        {
            exportStatus = "正在压缩 DDS...";
            string format = "BC6H_UF16"; // 默认使用 BC6H HDR
            string convertArgs = $"-f {format} -y -o \"{outputDir}\" \"{tempVolumeDDS}\"";
            
            RuntimeToolManager.ProcessResult convertResult = null;
            yield return RuntimeToolManager.RunProcess(RuntimeToolManager.TexconvPath, convertArgs, result => convertResult = result);
            
            if (convertResult == null || !convertResult.Success)
            {
                exportStatus = $"警告: texconv 压缩失败，将保留未压缩版本\n{convertResult?.Error ?? "Unknown error"}";
                Debug.LogWarning($"texconv failed, keeping uncompressed version:\n{convertResult?.Error ?? "Unknown error"}");
                
                // 使用未压缩版本
                if (File.Exists(finalDDS))
                    File.Delete(finalDDS);
                File.Move(tempVolumeDDS, finalDDS);
            }
            else
            {
                // 查找并重命名生成的文件
                string[] ddsFiles = Directory.GetFiles(outputDir, "*.dds");
                bool foundCompressed = false;
                
                foreach (string file in ddsFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName != "temp_volume.dds" && fileName.Contains("temp_volume"))
                    {
                        if (File.Exists(finalDDS))
                            File.Delete(finalDDS);
                        File.Move(file, finalDDS);
                        foundCompressed = true;
                        break;
                    }
                }
                
                // 如果没有找到压缩文件，使用未压缩版本
                if (!foundCompressed && File.Exists(tempVolumeDDS))
                {
                    if (File.Exists(finalDDS))
                        File.Delete(finalDDS);
                    File.Move(tempVolumeDDS, finalDDS);
                }
                
                // 清理临时文件
                if (File.Exists(tempVolumeDDS))
                    File.Delete(tempVolumeDDS);
            }
        }
        else
        {
            // 不压缩，直接重命名
            if (File.Exists(finalDDS))
                File.Delete(finalDDS);
            File.Move(tempVolumeDDS, finalDDS);
        }
        
        // 最终验证
        if (!File.Exists(finalDDS))
        {
            exportStatus = "错误: 最终 DDS 文件未生成!";
            Debug.LogError("Final DDS file was not created!");
            yield break;
        }
        
        exportStatus = $"成功! DDS 文件大小: {new FileInfo(finalDDS).Length / 1024 / 1024}MB";
        Debug.Log($"DDS export completed successfully! File: {finalDDS}");
    }
    
    void OnDestroy()
    {
        if (cloudNoiseTexture != null)
        {
            cloudNoiseTexture.Release();
        }
    }
}

