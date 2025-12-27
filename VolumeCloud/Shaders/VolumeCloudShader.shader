Shader "VolumeCloud/CloudRayMarching"
{
    Properties
    {
        _CloudNoiseTex ("Cloud Noise Texture", 3D) = "white" {}
        _RayMarchSteps ("Ray March Steps", Int) = 64
        _DensityThreshold ("Density Threshold", Range(0, 1)) = 0.3
        _CloudEdgeSoftness ("Cloud Edge Softness", Range(0, 1)) = 0.1
        _DensityMultiplier ("Density Multiplier", Float) = 1.0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _Absorption ("Absorption", Float) = 1.0
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _LightDir ("Light Direction", Vector) = (0, -1, 0, 0)
        _LightColor ("Light Color", Color) = (1, 1, 1, 1)
        _LightIntensity ("Light Intensity", Float) = 1.0
        _LightAbsorption ("Light Absorption", Float) = 0.5
        
        [Header(Noise Sampling)]
        _NoiseScale ("Noise Scale", Vector) = (1, 1, 1, 0)
        _NoiseOffset ("Noise Offset", Vector) = (0, 0, 0, 0)
        _NoiseSpeed ("Noise Speed", Float) = 1
        
        [Header(Advanced Lighting)]
        _AmbientColor ("Ambient Color", Color) = (0.5, 0.6, 0.7, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.2
        _PowderStrength ("Powder Strength", Range(0, 2)) = 0.5
        _ForwardScattering ("Forward Scattering", Range(-0.99, 0.99)) = 0.3
        _BackScattering ("Back Scattering", Range(-0.99, 0.99)) = -0.3
        
        [Header(Height Gradient)]
        _HeightGradientStrength ("Height Gradient Strength", Range(0, 2)) = 1.0
        _CloudBottomFade ("Bottom Fade", Range(0, 1)) = 0.2
        _CloudTopFade ("Top Fade", Range(0, 1)) = 0.3
        _Debug ("Debug", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent"
            "RenderPipeline" = "HDRenderPipeline"
        }

        Pass
        {
            Name "VolumeCloudPass"
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 5.0

            // HDRP核心文件
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // 属性
            TEXTURE3D(_CloudNoiseTex);
            SAMPLER(sampler_CloudNoiseTex);

            int _RayMarchSteps;
            float _DensityThreshold;
            float _DensityMultiplier;
            float _DetailStrength;
            float _Absorption;
            float4 _CloudColor;
            float3 _LightDir;
            float4 _LightColor;
            float _LightAbsorption;
            float3 _NoiseScale;
            float3 _NoiseOffset;
            float _NoiseSpeed;
            float4 _AmbientColor;
            float _AmbientStrength;
            float _PowderStrength;
            float _LightIntensity;
            float _ForwardScattering;
            float _BackScattering;
            float _HeightGradientStrength;
            float _CloudBottomFade;
            float _CloudTopFade;
            float _Debug;
            float _CloudEdgeSoftness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 objectPos : TEXCOORD1;
            };

            // Ray-AABB求交
            bool RayBoxIntersection(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax, 
                                   out float tNear, out float tFar)
            {
                float3 invDir = 1.0 / (rayDir + 1e-6);
                float3 t0 = (boxMin - rayOrigin) * invDir;
                float3 t1 = (boxMax - rayOrigin) * invDir;
                
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                
                tNear = max(max(tMin.x, tMin.y), tMin.z);
                tFar = min(min(tMax.x, tMax.y), tMax.z);
                
                return tFar > max(tNear, 0.0);
            }
            float Remap(float v, float minOld, float maxOld, float minNew, float maxNew) {
                return minNew + (v - minOld) * (maxNew - minNew) / (maxOld - minOld);
            }

            // 采样云密度
            float SampleCloudDensity(float3 uvw)
            {
                float3 samplingUVW = uvw * _NoiseScale + _NoiseOffset + _Time.y * _NoiseSpeed;
                float4 noise = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex, samplingUVW, 0);
                
                // 密度计算: Base - Detail * Strength
                float density = noise.r - noise.g * _DetailStrength;
                
                // ===== 高度梯度塑形 =====
                float height = uvw.y; // [0, 1]，0=底部，1=顶部
                
                // 创建积云形状：底部收缩，中部膨胀，顶部圆润
                // 使用抛物线形状
                float heightGradient = 1.0;
                
                if (_HeightGradientStrength > 0.01)
                {
                    // 底部淡出（0.0 → _CloudBottomFade）
                    float bottomFade = saturate(height / max(_CloudBottomFade, 0.01));
                    
                    // 顶部淡出（(1-_CloudTopFade) → 1.0）
                    float topFade = saturate((1.0 - height) / max(_CloudTopFade, 0.01));
                    
                    // 组合：中部保持，顶底淡出
                    heightGradient = bottomFade * topFade;
                    
                    // 应用强度
                    heightGradient = lerp(1.0, heightGradient, _HeightGradientStrength);
                }
                
                // 应用高度梯度
                density *= heightGradient;

                float suffix = saturate(Remap(density, _DensityThreshold, _DensityThreshold + _CloudEdgeSoftness, 0.0, 1.0));
                
                // 应用阈值
                density = suffix * _DensityMultiplier;
                
                return max(0, density * _DensityMultiplier);
            }
            
            // Henyey-Greenstein相位函数
            float HenyeyGreenstein(float cosAngle, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(abs(1.0 + g2 - 2.0 * g * cosAngle), 1.5));
            }
            
            // R2 dither噪声
            float R2_dither(float2 samplePositionSS, float frameCount)
            {
                float2 coord = samplePositionSS;
                coord += (frameCount * 2) % 1000;
                
                float2 alpha = float2(0.75487765, 0.56984026);
                return frac(alpha.x * coord.x + alpha.y * coord.y);
            }                        

            //没有任何优化，直接硬算
            float LightMarch(float3 position, float3 lightDir, int steps, float2 screenUV)
            {
                float stepSize = 0.05;
                // 使用R2 dither打破分界
                float dither = R2_dither(screenUV * _ScreenParams.xy, _TaaFrameInfo.z);
                                
                float opticalDepth = 0.0;
                
                for (int i = 0; i < steps; i++)
                {
                    position += lightDir * (i + dither)/steps;
                    
                    // 边界检查 - 越界就提前退出，但保留已采样的结果
                    if (any(position < 0.0) || any(position > 1.0))
                        break;
                    
                    float density = SampleCloudDensity(position);
                    opticalDepth += density * (i + dither)/steps;
                    
                    // Early exit
                    if (opticalDepth > 5.0)
                        break;
                }
                
                // Beer-Lambert + Powder效果
                float transmittance = exp(-opticalDepth * _LightAbsorption);
                float powder = 1.0 - exp(-opticalDepth * 2.0 * _PowderStrength);
                
                return transmittance * (1.0 + powder * 0.5);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.objectPos = input.positionOS.xyz;
                
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // 相机位置和方向
                float3 cameraPos = GetCameraRelativePositionWS(_WorldSpaceCameraPos);
                float3 rayDir = normalize(input.positionWS - cameraPos);
                
                // 屏幕UV用于R2 dither
                float2 screenUV = input.positionCS.xy / _ScreenParams.xy;
                
                // Box边界 (物体空间 [-0.5, 0.5])
                float3 boxMin = float3(-0.5, -0.5, -0.5);
                float3 boxMax = float3(0.5, 0.5, 0.5);
                
                // 将光线转换到物体空间
                float4x4 worldToObject = GetWorldToObjectMatrix();
                float3 rayOriginOS = mul(worldToObject, float4(cameraPos, 1.0)).xyz;
                float3 rayDirOS = mul((float3x3)worldToObject, rayDir);
                rayDirOS = normalize(rayDirOS);
                
                // Ray-Box求交
                float tNear, tFar;
                if (!RayBoxIntersection(rayOriginOS, rayDirOS, boxMin, boxMax, tNear, tFar))
                {
                    discard;
                }
                
                // 起始点（如果相机在Box内，从相机位置开始）
                tNear = max(tNear, 0.0);
                float rayLength = tFar - tNear;
                
                // Ray Marching
                float stepSize = rayLength / float(_RayMarchSteps);
                float3 rayPos = rayOriginOS + rayDirOS * tNear;
                
                float4 color = float4(0, 0, 0, 0);
                float transmittance = 1.0;

                float dither = R2_dither(screenUV * _ScreenParams.xy, _TaaFrameInfo.z);

                //没有任何优化，直接硬算
                for (int i = 0; i < _RayMarchSteps; i++)
                {
                    // 边界检查 - 确保在Box内
                    if (any(rayPos < boxMin) || any(rayPos > boxMax))
                    {
                        rayPos += rayDirOS * stepSize;
                        continue;
                    }
                    
                    // 转换到纹理坐标 [0, 1]
                    float3 uvw = rayPos + 0.5;
                    
                    // 采样密度
                    float density = SampleCloudDensity(uvw);
                    
                    //不知道是PhaseG算的有问题，还是transmittance有问题，有几个角度看上去不太对劲，算了不管了
                    if (density > 0.01)
                    {
                        // 计算光照方向
                        float3 lightDirOS = normalize(mul((float3x3)worldToObject, _LightDir));
                        float cosAngle = dot(rayDirOS, lightDirOS);
                        
                        // 向光采样
                        float lightEnergy = LightMarch(uvw, lightDirOS, 6, screenUV);
                        
                        // 相位函数（双叶散射）
                        float phase = lerp(
                            HenyeyGreenstein(cosAngle, _ForwardScattering),
                            HenyeyGreenstein(cosAngle, _BackScattering),
                            0.6
                        );

                        phase = phase + 0.15;//光线在云内部乱弹后的基础亮度,Multiple Scattering
                        
                        // 环境光（只在薄处和边缘）
                        float ambientStrength = saturate(1.0 - lightEnergy); // 环境光
                        float3 ambient = _AmbientColor.rgb * ambientStrength * _AmbientStrength;
                        
                        // 直接光（受光面应该更亮）
                        float3 direct = _LightColor.rgb * lightEnergy * phase * _LightIntensity;
                        
                        // 合并光照
                        float3 litColor = _CloudColor.rgb * (ambient + direct);
                        
                        // Beer's Law 吸收
                        float densityStep = density * stepSize * _Absorption;
                        float absorption = exp(-densityStep);
                        
                        // 累积颜色
                        float3 sampleColor = litColor * density * transmittance * stepSize;
                        color.rgb += sampleColor;
                        
                        // 更新透射率
                        transmittance *= absorption;
                        
                        // Early termination
                        if (transmittance < 0.01)
                            break;
                    }
                    
                    // 前进
                    rayPos += rayDirOS * stepSize * (1 + dither);
                }
                
                // 最终Alpha
                color.a = 1.0 - transmittance;
                if (_Debug == 1)
                {
                    return float4(SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex, rayPos, 0).rrr, 1.0);
                }
                
                return color;
            }

            ENDHLSL
        }
    }
    
    FallBack "Hidden/InternalErrorShader"
}

