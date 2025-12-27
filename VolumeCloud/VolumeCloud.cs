using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class VolumeCloud : MonoBehaviour
{
    [Header("Noise Generation")]
    public ComputeShader noiseCompute;
    public int noiseResolution = 128;
    [Tooltip("Generate noise on Start. Uncheck for manual generation.")]
    public bool generateOnStart = true;

    [Header("Base Shape (R Channel)")]
    [Range(2, 4)]
    public int baseFrequencyExponent = 1;
    [Range(1, 8)]
    public int baseOctaves = 4;
    [Range(0f, 1f)]
    public float basePersistence = 0.5f;

    [Header("Base Worley Settings")]
    
    [Tooltip("Powers of 2 scale: -3=0.125, -2=0.25, -1=0.5, 0=1, 1=2, 2=4, 3=8")]
    [Range(-2, 3)]
    public int baseWorleyScaleExponent = -1;  // 默认 0.5
    
    [HideInInspector]
    public float baseWorleyScale = 0.5f;  // 自动计算
    
    public float baseCoverage = 0.5f;

    [Header("Detail (G Channel)")]
    public float detailFrequency = 4.0f;
    [Range(1, 6)]
    public int detailOctaves = 3;
    [Range(0f, 1f)]
    public float detailPersistence = 0.5f;

    [Header("Worley Settings")]
    [Range(1, 10)]
    public int worleyCellCount = 4;

    [Header("Rendering")]
    public Material cloudMaterial;
    public Material debugMaterial;
    public GameObject cloudBox;
    [Tooltip("If null, will automatically find or create a box.")]
    public bool autoCreateBox = true;

    [Header("Export Settings")]
    [Tooltip("导出 DDS 时是否使用 BC6H 压缩")]
    public bool compressDDS = true;

    [Header("Lighting")]
    [Tooltip("Main directional light. If null, will auto-find.")]
    public Light mainLight;

    private RenderTexture cloudNoiseTexture;
    private int kernelHandle;

    void Start()
    {
        // 查找或创建Box
        if (cloudBox == null && autoCreateBox)
        {
            cloudBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloudBox.name = "CloudVolume";
            cloudBox.transform.parent = transform;
            cloudBox.transform.localPosition = Vector3.zero;
            cloudBox.transform.localScale = Vector3.one * 10f;

            // 配置Mesh Renderer
            MeshRenderer mr = cloudBox.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        // 查找主光源
        if (mainLight == null)
        {
            mainLight = RenderSettings.sun;
            if (mainLight == null)
            {
                Light[] lights = FindObjectsOfType<Light>();
                foreach (Light light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        mainLight = light;
                        break;
                    }
                }
            }
        }

        // 生成噪声纹理
        if (generateOnStart && noiseCompute != null)
        {
            GenerateCloudNoise();
        }

        // 应用材质到Box
        if (cloudBox != null && cloudMaterial != null)
        {
            MeshRenderer mr = cloudBox.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = cloudMaterial;
            }
        }
    }

    void Update()
    {
        // Editor和Runtime都执行
        UpdateLighting();
        UpdateMaterialTexture();
        GenerateCloudNoise();
    }

    private void UpdateLighting()
    {
        if (cloudMaterial == null) return;
        
        // 查找光源（如果丢失）
        if (mainLight == null)
        {
            mainLight = RenderSettings.sun;
            if (mainLight == null)
            {
                Light[] lights = FindObjectsOfType<Light>();
                foreach (Light light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        mainLight = light;
                        break;
                    }
                }
            }
        }
        
        if (mainLight != null)
        {
            cloudMaterial.SetVector("_LightDir", -mainLight.transform.forward);
            //cloudMaterial.SetColor("_LightColor", mainLight.color * mainLight.intensity);
        }
    }

    private void UpdateMaterialTexture()
    {
        if (cloudMaterial != null && cloudNoiseTexture != null)
        {
            cloudMaterial.SetTexture("_CloudNoiseTex", cloudNoiseTexture);
            debugMaterial.SetTexture("_CloudNoiseTex", cloudNoiseTexture);
        }
    }

    public void GenerateCloudNoise()
    {
        if (noiseCompute == null)
        {
            Debug.LogError("NoiseCompute shader is not assigned!");
            return;
        }

        // 根据指数计算 BaseWorleyScale (2^exponent)
        baseWorleyScale = Mathf.Pow(2f, baseWorleyScaleExponent);

        // 创建或重新创建RenderTexture
        if (cloudNoiseTexture == null ||
            cloudNoiseTexture.width != noiseResolution)
        {
            CreateRenderTexture3D();
        }

        // 获取Kernel
        kernelHandle = noiseCompute.FindKernel("GenerateCloudNoise");

        // 设置参数
        noiseCompute.SetTexture(kernelHandle, "Result", cloudNoiseTexture);
        noiseCompute.SetInt("Resolution", noiseResolution);
        noiseCompute.SetFloat("BaseWorleyScale", baseWorleyScale);
        noiseCompute.SetFloat("BaseCoverage", baseCoverage);
        noiseCompute.SetFloat("BaseFrequency", Mathf.Pow(2f, baseFrequencyExponent));
        noiseCompute.SetInt("BaseOctaves", baseOctaves);
        noiseCompute.SetFloat("BasePersistence", basePersistence);

        noiseCompute.SetFloat("DetailFrequency", detailFrequency);
        noiseCompute.SetInt("DetailOctaves", detailOctaves);
        noiseCompute.SetFloat("DetailPersistence", detailPersistence);

        noiseCompute.SetInt("WorleyCellCount", worleyCellCount);

        // 调度ComputeShader
        int threadGroups = Mathf.CeilToInt(noiseResolution / 8.0f);
        noiseCompute.Dispatch(kernelHandle, threadGroups, threadGroups, threadGroups);

    }

    private void CreateRenderTexture3D()
    {
        // 释放旧纹理
        if (cloudNoiseTexture != null)
        {
            cloudNoiseTexture.Release();
        }

        // 创建新的3D RenderTexture (RGBA格式)
        cloudNoiseTexture = new RenderTexture(noiseResolution, noiseResolution, 0, RenderTextureFormat.ARGBFloat);
        cloudNoiseTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        cloudNoiseTexture.volumeDepth = noiseResolution;
        cloudNoiseTexture.enableRandomWrite = true;
        cloudNoiseTexture.wrapMode = TextureWrapMode.Repeat;
        cloudNoiseTexture.filterMode = FilterMode.Trilinear;
        cloudNoiseTexture.Create();
    }

    void OnDestroy()
    {
        if (cloudNoiseTexture != null)
        {
            cloudNoiseTexture.Release();
        }
    }

    // 公共访问器，用于 Editor 导出功能
    public RenderTexture GetCloudNoiseTexture() => cloudNoiseTexture;
    public int GetNoiseResolution() => noiseResolution;
    public bool GetCompressDDS() => compressDDS;
}
