using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

[CustomEditor(typeof(VolumeCloud))]
public class VolumeCloudEditor : Editor
{
    private static string TexconvPath => Path.Combine(Application.dataPath, "VolumeCloud/Tools/texconv.exe");
    private static string TexassemblePath => Path.Combine(Application.dataPath, "VolumeCloud/Tools/texassemble.exe");

    public override void OnInspectorGUI()
    {
        VolumeCloud cloud = (VolumeCloud)target;

        // 使用默认 Inspector 绘制大部分内容
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        
        // 显示计算后的 BaseWorleyScale 值
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Actual Base Worley Scale:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(cloud.baseWorleyScale.ToString("F3"));
        EditorGUILayout.EndHorizontal();
        
        // 显示指数对应表
        EditorGUILayout.HelpBox(
            "Base Worley Scale Exponent 对应表:\n" +
            "  -2 → 0.25  (1/4)\n" +
            "  -1 → 0.5   (1/2)\n" +
            "   0 → 1.0\n" +
            "   1 → 2.0\n" +
            "   2 → 4.0\n" +
            "   3 → 8.0",
            MessageType.Info
        );

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

        // 生成噪声按钮
        if (GUILayout.Button("Generate Noise (Editor Mode)", GUILayout.Height(30)))
        {
            cloud.GenerateCloudNoise();
        }

        EditorGUILayout.Space(5);

        // 预设按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fine Detail\n(Exponent = 0)"))
        {
            Undo.RecordObject(cloud, "Set Fine Detail");
            cloud.baseWorleyScaleExponent = 0;
            cloud.GenerateCloudNoise();
        }
        if (GUILayout.Button("Medium Detail\n(Exponent = -1)"))
        {
            Undo.RecordObject(cloud, "Set Medium Detail");
            cloud.baseWorleyScaleExponent = -1;
            cloud.GenerateCloudNoise();
        }
        if (GUILayout.Button("Coarse Detail\n(Exponent = -2)"))
        {
            Undo.RecordObject(cloud, "Set Coarse Detail");
            cloud.baseWorleyScaleExponent = -2;
            cloud.GenerateCloudNoise();
        }
        EditorGUILayout.EndHorizontal();

        // 导出工具
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Export Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Export as Volume Texture DDS", GUILayout.Height(30)))
        {
            ExportTexture3DAsDDS(cloud);
        }

        EditorGUILayout.HelpBox(
            "导出流程:\n" +
            "1. 首次使用将自动下载 texconv.exe 和 texassemble.exe\n" +
            "2. 选择保存目录\n" +
            "3. 根据 'Compress DDS' 设置选择是否压缩\n" +
            "4. 生成 PNG 和 EXR 切片序列\n" +
            "5. 使用 texassemble 从 PNG 创建 Volume Texture\n" +
            "6. (可选) 使用 texconv 压缩为 BC6H/BC7\n" +
            "7. PNG 和 EXR 切片都会保留",
            MessageType.Info
        );
    }

    private async Task EnsureTexconvExists()
    {
        // 检查 texassemble 和 texconv 是否都存在且有效
        bool texconvNeedsDownload = true;
        bool texassembleNeedsDownload = true;
        
        if (File.Exists(TexconvPath))
        {
            FileInfo fi = new FileInfo(TexconvPath);
            if (fi.Length > 100000) // 大于100KB才算有效
            {
                texconvNeedsDownload = false;
            }
            else
            {
                UnityEngine.Debug.LogWarning($"texconv.exe 文件大小异常 ({fi.Length} bytes)，将重新下载");
                File.Delete(TexconvPath);
            }
        }
        
        if (File.Exists(TexassemblePath))
        {
            FileInfo fi = new FileInfo(TexassemblePath);
            if (fi.Length > 100000) // 大于100KB才算有效
            {
                texassembleNeedsDownload = false;
            }
            else
            {
                UnityEngine.Debug.LogWarning($"texassemble.exe 文件大小异常 ({fi.Length} bytes)，将重新下载");
                File.Delete(TexassemblePath);
            }
        }
        
        if (!texconvNeedsDownload && !texassembleNeedsDownload) return;

        string toolsDir = Path.GetDirectoryName(TexconvPath);
        if (!Directory.Exists(toolsDir))
            Directory.CreateDirectory(toolsDir);

        // 使用 GitHub latest release 链接
        string baseUrl = "https://github.com/microsoft/DirectXTex/releases/latest/download";

        try
        {
            // 下载 texconv（用于格式转换）
            if (texconvNeedsDownload)
            {
                EditorUtility.DisplayProgressBar("下载工具", "正在下载 texconv.exe...", 0.33f);
                string url = $"{baseUrl}/texconv.exe";
                
                using (var client = new System.Net.WebClient())
                {
                    await client.DownloadFileTaskAsync(url, TexconvPath);
                }
                
                // 验证下载的文件
                if (File.Exists(TexconvPath))
                {
                    FileInfo fi = new FileInfo(TexconvPath);
                    
                    // 检查文件头是否是有效的 PE 文件
                    using (FileStream fs = File.OpenRead(TexconvPath))
                    {
                        byte[] header = new byte[2];
                        fs.Read(header, 0, 2);
                        
                        if (header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                        {
                            File.Delete(TexconvPath);
                            throw new System.Exception("下载的 texconv.exe 不是有效的 Windows 可执行文件（可能是HTML重定向页面）");
                        }
                    }
                    
                    UnityEngine.Debug.Log($"texconv.exe 已下载: {fi.Length / 1024}KB");
                }
            }
            
            // 下载 texassemble（用于创建 Volume Texture）
            if (texassembleNeedsDownload)
            {
                EditorUtility.DisplayProgressBar("下载工具", "正在下载 texassemble.exe...", 0.66f);
                string url = $"{baseUrl}/texassemble.exe";
                
                using (var client = new System.Net.WebClient())
                {
                    await client.DownloadFileTaskAsync(url, TexassemblePath);
                }
                
                // 验证下载的文件
                if (File.Exists(TexassemblePath))
                {
                    FileInfo fi = new FileInfo(TexassemblePath);
                    
                    // 检查文件头是否是有效的 PE 文件
                    using (FileStream fs = File.OpenRead(TexassemblePath))
                    {
                        byte[] header = new byte[2];
                        fs.Read(header, 0, 2);
                        
                        if (header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                        {
                            File.Delete(TexassemblePath);
                            throw new System.Exception("下载的 texassemble.exe 不是有效的 Windows 可执行文件（可能是HTML重定向页面）");
                        }
                    }
                    
                    UnityEngine.Debug.Log($"texassemble.exe 已下载: {fi.Length / 1024}KB");
                }
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("下载失败",
                $"无法下载工具:\n{ex.Message}\n\n请手动从以下地址下载并放置到:\n{toolsDir}\n\n下载地址:\nhttps://github.com/microsoft/DirectXTex/releases/latest",
                "确定");
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void ExportPNGSlices(VolumeCloud cloud, string outputDir)
    {
        RenderTexture rt3D = cloud.GetCloudNoiseTexture();
        int resolution = cloud.GetNoiseResolution();

        RenderTexture tempRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        Texture2D texture2D = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);

        try
        {
            for (int z = 0; z < resolution; z++)
            {
                Graphics.CopyTexture(rt3D, z, tempRT, 0);
                RenderTexture.active = tempRT;
                texture2D.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                texture2D.Apply();

                // 导出为 PNG（DirectXTex 工具支持）
                byte[] pngData = texture2D.EncodeToPNG();
                string filePath = Path.Combine(outputDir, $"Slice_{z:000}.png");
                File.WriteAllBytes(filePath, pngData);

                EditorUtility.DisplayProgressBar("导出 PNG 切片", $"切片 {z + 1}/{resolution}", (float)z / resolution);
            }
        }
        finally
        {
            RenderTexture.active = null;
            tempRT.Release();
            DestroyImmediate(texture2D);
            EditorUtility.ClearProgressBar();
        }
    }

    private void ExportEXRSlices(VolumeCloud cloud, string outputDir)
    {
        RenderTexture rt3D = cloud.GetCloudNoiseTexture();
        int resolution = cloud.GetNoiseResolution();

        RenderTexture tempRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        Texture2D texture2D = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);

        try
        {
            for (int z = 0; z < resolution; z++)
            {
                Graphics.CopyTexture(rt3D, z, tempRT, 0);
                RenderTexture.active = tempRT;
                texture2D.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                texture2D.Apply();

                // 导出为 EXR（高精度）
                byte[] exrData = texture2D.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                string filePath = Path.Combine(outputDir, $"Slice_{z:000}.exr");
                File.WriteAllBytes(filePath, exrData);

                EditorUtility.DisplayProgressBar("导出 EXR 切片", $"切片 {z + 1}/{resolution}", (float)z / resolution);
            }
        }
        finally
        {
            RenderTexture.active = null;
            tempRT.Release();
            DestroyImmediate(texture2D);
            EditorUtility.ClearProgressBar();
        }
    }

    private async void ExportTexture3DAsDDS(VolumeCloud cloud)
    {
        // 1. 检查纹理
        RenderTexture rt3D = cloud.GetCloudNoiseTexture();
        if (rt3D == null)
        {
            EditorUtility.DisplayDialog("错误", "请先生成噪声纹理", "确定");
            return;
        }

        try
        {
            // 2. 确保 texconv 存在
            await EnsureTexconvExists();

            // 3. 选择保存目录
            string outputDir = EditorUtility.SaveFolderPanel("选择导出目录", "Assets/VolumeCloud/Exports", "");
            if (string.IsNullOrEmpty(outputDir)) return;

            // 4. 根据 Inspector 中的设置决定是否压缩
            bool shouldCompress = cloud.GetCompressDDS();
            string format = "BC6H_UF16"; // 默认使用 BC6H HDR 压缩
            
            if (shouldCompress)
            {
                // 选择压缩格式
                int formatChoice = EditorUtility.DisplayDialogComplex(
                    "选择 DDS 压缩格式",
                    "BC6H: HDR 压缩 (推荐)\nBC7: 高质量 LDR",
                    "BC6H (HDR)", "BC7 (LDR)", "取消"
                );
                
                if (formatChoice == 2) return; // 用户取消
                
                format = formatChoice switch
                {
                    0 => "BC6H_UF16",
                    1 => "BC7_UNORM",
                    _ => "BC6H_UF16"
                };
            }
            else
            {
                format = "R32G32B32A32_FLOAT"; // 无压缩
            }

            // 5. 导出 PNG 切片（用于 DDS 生成）
            string pngDir = Path.Combine(outputDir, "PNG");
            Directory.CreateDirectory(pngDir);
            ExportPNGSlices(cloud, pngDir);
            
            // 6. 导出 EXR 切片（高精度备份）
            string exrDir = Path.Combine(outputDir, "EXR");
            Directory.CreateDirectory(exrDir);
            ExportEXRSlices(cloud, exrDir);

            // 7. 使用 texassemble 直接从 PNG 创建 Volume Texture
            EditorUtility.DisplayProgressBar("创建 Volume Texture", "正在使用 texassemble...", 0.7f);
            
            string tempVolumeDDS = Path.Combine(outputDir, "temp_volume.dds");
            
            // texassemble 不支持通配符，需要显式列出所有文件
            string[] pngFiles = Directory.GetFiles(pngDir, "Slice_*.png");
            if (pngFiles.Length == 0)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("错误", "PNG 切片文件未找到！", "确定");
                return;
            }
            
            // 构建文件列表参数
            string fileList = string.Join(" ", pngFiles.Select(f => $"\"{f}\""));
            string assembleArgs = $"volume -o \"{tempVolumeDDS}\" {fileList}";

            var assembleInfo = new ProcessStartInfo
            {
                FileName = TexassemblePath,
                Arguments = assembleArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(TexassemblePath)
            };

            var assembleProcess = Process.Start(assembleInfo);
            string assembleOutput = await assembleProcess.StandardOutput.ReadToEndAsync();
            string assembleError = await assembleProcess.StandardError.ReadToEndAsync();
            assembleProcess.WaitForExit();

            if (assembleProcess.ExitCode != 0)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("错误",
                    $"texassemble 创建 Volume Texture 失败 (退出码: {assembleProcess.ExitCode}):\n{assembleError}\n\n输出:\n{assembleOutput}",
                    "确定");
                UnityEngine.Debug.LogError($"texassemble 失败:\n{assembleError}\n{assembleOutput}");
                return;
            }

            // 8. 使用 texconv 转换为目标格式（如果需要压缩）
            string finalDDS = Path.Combine(outputDir, "CloudNoise_Volume.dds");
            
            if (format != "R32G32B32A32_FLOAT")
            {
                EditorUtility.DisplayProgressBar("压缩", "正在压缩为目标格式...", 0.85f);
                
                string convertArgs = $"-f {format} -y -o \"{outputDir}\" \"{tempVolumeDDS}\"";

                var convertInfo = new ProcessStartInfo
                {
                    FileName = TexconvPath,
                    Arguments = convertArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var convertProcess = Process.Start(convertInfo);
                string convertOutput = await convertProcess.StandardOutput.ReadToEndAsync();
                string convertError = await convertProcess.StandardError.ReadToEndAsync();
                convertProcess.WaitForExit();

                EditorUtility.ClearProgressBar();

                if (convertProcess.ExitCode == 0)
                {
                    // texconv 会生成 temp_volume.dds 的压缩版本，可能命名不同
                    // 查找生成的 DDS 文件
                    string[] generatedFiles = Directory.GetFiles(outputDir, "*.dds");
                    string compressedFile = null;
                    
                    foreach (string file in generatedFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName != "temp_volume.dds" && fileName.Contains("temp_volume"))
                        {
                            compressedFile = file;
                            break;
                        }
                    }
                    
                    // 如果没找到，检查是否直接覆盖了 temp_volume.dds
                    if (compressedFile == null && File.Exists(tempVolumeDDS))
                    {
                        compressedFile = tempVolumeDDS;
                    }
                    
                    if (compressedFile != null)
                    {
                        // 重命名为最终文件名
                        if (File.Exists(finalDDS))
                            File.Delete(finalDDS);
                        File.Move(compressedFile, finalDDS);
                        
                        // 删除临时文件
                        if (File.Exists(tempVolumeDDS) && tempVolumeDDS != compressedFile)
                            File.Delete(tempVolumeDDS);
                    }
                    
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("成功",
                        $"DDS Volume Texture 已导出:\n{finalDDS}\n\n格式: {format}\nPNG 切片: {pngDir}\nEXR 切片: {exrDir}",
                        "确定");
                    UnityEngine.Debug.Log($"DDS 导出成功:\n压缩输出:\n{convertOutput}");
                }
                else
                {
                    EditorUtility.DisplayDialog("错误",
                        $"texconv 压缩失败 (退出码: {convertProcess.ExitCode}):\n{convertError}\n\n输出:\n{convertOutput}",
                        "确定");
                    UnityEngine.Debug.LogError($"texconv 压缩失败:\n{convertError}\n{convertOutput}");
                }
            }
            else
            {
                // 无压缩，直接重命名
                if (File.Exists(finalDDS))
                    File.Delete(finalDDS);
                File.Move(tempVolumeDDS, finalDDS);
                
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("成功",
                    $"DDS Volume Texture 已导出:\n{finalDDS}\n\n格式: 未压缩 (R32G32B32A32_FLOAT)\nPNG 切片: {pngDir}\nEXR 切片: {exrDir}",
                    "确定");
                UnityEngine.Debug.Log($"DDS 导出成功 (未压缩):\n{assembleOutput}");
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("错误", $"导出过程出错:\n{ex.Message}\n\n{ex.StackTrace}", "确定");
            UnityEngine.Debug.LogException(ex);
        }
    }
}
