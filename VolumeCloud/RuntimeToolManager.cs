using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class RuntimeToolManager : MonoBehaviour
{
    // 进程执行结果类
    public class ProcessResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }
    
    private static string ToolsPath => Path.Combine(Application.persistentDataPath, "Tools");
    public static string TexconvPath => Path.Combine(ToolsPath, "texconv.exe");
    public static string TexassemblePath => Path.Combine(ToolsPath, "texassemble.exe");
    
    public static IEnumerator EnsureToolsExist()
    {
        if (!Directory.Exists(ToolsPath))
        {
            Directory.CreateDirectory(ToolsPath);
        }
        
        // 验证现有文件是否有效
        bool needsRedownload = false;
        
        if (File.Exists(TexconvPath))
        {
            FileInfo fi = new FileInfo(TexconvPath);
            if (fi.Length < 100000) // 小于100KB，肯定不对
            {
                UnityEngine.Debug.LogWarning($"texconv.exe 文件大小异常 ({fi.Length} bytes)，将重新下载");
                File.Delete(TexconvPath);
                needsRedownload = true;
            }
        }
        else
        {
            needsRedownload = true;
        }
        
        if (File.Exists(TexassemblePath))
        {
            FileInfo fi = new FileInfo(TexassemblePath);
            if (fi.Length < 100000) // 小于100KB，肯定不对
            {
                UnityEngine.Debug.LogWarning($"texassemble.exe 文件大小异常 ({fi.Length} bytes)，将重新下载");
                File.Delete(TexassemblePath);
                needsRedownload = true;
            }
        }
        else
        {
            needsRedownload = true;
        }
        
        if (needsRedownload)
        {
            // 使用 GitHub latest release 链接（会自动重定向到最新版本）
            // 注意：如果自动下载失败，请手动从以下地址下载并放置到 Tools 目录：
            // https://github.com/microsoft/DirectXTex/releases/latest
            string baseUrl = "https://github.com/microsoft/DirectXTex/releases/latest/download";
            
            // 下载 texconv
            if (!File.Exists(TexconvPath))
            {
                UnityEngine.Debug.Log("Downloading texconv.exe...");
                yield return DownloadFile($"{baseUrl}/texconv.exe", TexconvPath);
                
                // 验证下载
                if (File.Exists(TexconvPath))
                {
                    FileInfo fi = new FileInfo(TexconvPath);
                    UnityEngine.Debug.Log($"texconv.exe 下载完成，大小: {fi.Length / 1024}KB");
                }
                else
                {
                    UnityEngine.Debug.LogError("texconv.exe 下载失败！请手动下载：https://github.com/microsoft/DirectXTex/releases/latest");
                }
            }
            
            // 下载 texassemble
            if (!File.Exists(TexassemblePath))
            {
                UnityEngine.Debug.Log("Downloading texassemble.exe...");
                yield return DownloadFile($"{baseUrl}/texassemble.exe", TexassemblePath);
                
                // 验证下载
                if (File.Exists(TexassemblePath))
                {
                    FileInfo fi = new FileInfo(TexassemblePath);
                    UnityEngine.Debug.Log($"texassemble.exe 下载完成，大小: {fi.Length / 1024}KB");
                }
                else
                {
                    UnityEngine.Debug.LogError("texassemble.exe 下载失败！请手动下载：https://github.com/microsoft/DirectXTex/releases/latest");
                }
            }
        }
        
        UnityEngine.Debug.Log("Tools are ready!");
    }
    
    static IEnumerator DownloadFile(string url, string savePath)
    {
        // 尝试使用 UnityWebRequest 下载
        bool downloadSuccess = false;
        
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.downloadHandler = new DownloadHandlerFile(savePath);
            www.timeout = 60;
            
            var operation = www.SendWebRequest();
            
            while (!operation.isDone)
            {
                if (www.downloadProgress > 0)
                {
                    UnityEngine.Debug.Log($"下载进度: {www.downloadProgress * 100:F0}%");
                }
                yield return new WaitForSeconds(0.5f);
            }
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                downloadSuccess = true;
            }
            else
            {
                UnityEngine.Debug.LogWarning($"UnityWebRequest 下载失败: {www.error}\n将尝试使用 PowerShell 下载...");
                
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }
        
        // 如果 UnityWebRequest 失败，尝试使用 PowerShell
        if (!downloadSuccess)
        {
            UnityEngine.Debug.Log("使用 PowerShell 下载...");
            
            // 构建 PowerShell 命令
            string psCommand = $"Invoke-WebRequest -Uri '{url}' -OutFile '{savePath}' -UseBasicParsing";
            
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{psCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            System.Diagnostics.Process process = null;
            
            // 启动进程（不能在 try-catch 中使用 yield）
            try
            {
                process = System.Diagnostics.Process.Start(processInfo);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"PowerShell 启动失败: {ex.Message}");
            }
            
            // 等待进程完成（在 try-catch 外使用 yield）
            if (process != null)
            {
                while (!process.HasExited)
                {
                    yield return null;
                }
                
                process.WaitForExit();
                
                if (process.ExitCode == 0 && File.Exists(savePath))
                {
                    downloadSuccess = true;
                    UnityEngine.Debug.Log("PowerShell 下载成功");
                }
                else
                {
                    string error = process.StandardError.ReadToEnd();
                    UnityEngine.Debug.LogError($"PowerShell 下载失败: {error}");
                }
                
                process.Dispose();
            }
        }
        
        // 验证下载的文件
        if (downloadSuccess && File.Exists(savePath))
        {
            FileInfo fi = new FileInfo(savePath);
            
            if (fi.Length < 1000)
            {
                UnityEngine.Debug.LogError($"下载的文件太小 ({fi.Length} bytes)，可能不是有效文件");
                File.Delete(savePath);
            }
            else
            {
                // 检查文件头是否是有效的 PE 文件 (MZ 签名)
                using (FileStream fs = File.OpenRead(savePath))
                {
                    byte[] header = new byte[2];
                    fs.Read(header, 0, 2);
                    
                    if (header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                    {
                        UnityEngine.Debug.LogError($"下载的文件不是有效的 Windows 可执行文件！\n文件头: {header[0]:X2} {header[1]:X2} (应为 4D 5A)");
                        File.Delete(savePath);
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"✓ 已下载: {Path.GetFileName(savePath)} ({fi.Length / 1024}KB)");
                    }
                }
            }
        }
        else
        {
            UnityEngine.Debug.LogError($"所有下载方法都失败了。\n请手动下载工具：\n1. 访问 https://github.com/microsoft/DirectXTex/releases\n2. 下载最新版本的 texconv.exe 和 texassemble.exe\n3. 放置到: {Path.GetDirectoryName(savePath)}");
        }
    }
    
    public static IEnumerator RunProcess(string exe, string args, System.Action<ProcessResult> onComplete = null)
    {
        var result = new ProcessResult();
        
        if (!File.Exists(exe))
        {
            UnityEngine.Debug.LogError($"Executable not found: {exe}");
            result.Success = false;
            result.ExitCode = -1;
            result.Error = $"Executable not found: {exe}";
            onComplete?.Invoke(result);
            yield break;
        }
        
        UnityEngine.Debug.Log($"Running: {Path.GetFileName(exe)} {args}");
        
        var processInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)
        };
        
        Process process = null;
        string output = "";
        string error = "";
        
        // 启动进程
        try
        {
            process = Process.Start(processInfo);
            
            // 异步读取输出，避免死锁
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output += e.Data + "\n"; };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error += e.Data + "\n"; };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to start process: {ex.Message}");
            result.Success = false;
            result.ExitCode = -1;
            result.Error = ex.Message;
            onComplete?.Invoke(result);
            yield break;
        }
        
        // 等待进程完成
        while (process != null && !process.HasExited)
        {
            yield return null;
        }
        
        // 等待输出读取完成
        if (process != null)
        {
            process.WaitForExit(); // 确保所有输出都被读取
            
            result.ExitCode = process.ExitCode;
            result.Output = output;
            result.Error = error;
            result.Success = process.ExitCode == 0;
            
            if (process.ExitCode != 0)
            {
                UnityEngine.Debug.LogError($"Process failed (exit code {process.ExitCode}):\nError: {error}\nOutput: {output}");
            }
            else
            {
                UnityEngine.Debug.Log($"Process completed successfully:\n{output}");
            }
            
            process.Dispose();
        }
        
        onComplete?.Invoke(result);
    }
}

