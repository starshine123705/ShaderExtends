using System.ComponentModel;
using Terraria.ModLoader.Config;

public class ShaderControlConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;
    public static bool EnableSampleStatic;
    public static float Threshold = 0.6f;
    public static float Smoothness = 0.3f;
    public static float ChromaticAberration = 0.5f;
    public static float BloomIntensity = 2f;
    public static float VignetteStrength = 1.2f;
    public static float FilmGrainAmount = 0.05f;
    public static float Saturation = 1.1f;
    public static float Contrast = 1.1f;
    public static float Brightness = 0.0f;

    public override void OnChanged()
    {
        EnableSampleStatic = EnableSampleShader;
        Threshold = _Threshold;
        Smoothness = _Smoothness;
        ChromaticAberration = _ChromaticAberration;
        BloomIntensity = _BloomIntensity;
        VignetteStrength = _VignetteStrength;
        FilmGrainAmount = _FilmGrainAmount;
        Saturation = _Saturation;
        Contrast = _Contrast;
        Brightness = _Brightness;
        base.OnChanged();
    }

    [Header("后期渲染开关")]
    [DefaultValue(true)]
    public bool EnableSampleShader;

    [Header("后期处理参数")]
    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(0.6f)]
    public float _Threshold;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(0.3f)]
    public float _Smoothness;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(0.5f)]        
    public float _ChromaticAberration;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(2f)]
    public float _BloomIntensity;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(1.2f)]
    public float _VignetteStrength;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(0.05f)]
    public float _FilmGrainAmount;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(1.1f)]
    public float _Saturation;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(1.1f)]
    public float _Contrast;

    [Increment(0.01f)]
    [DrawTicks]
    [DefaultValue(0.0f)]
    public float _Brightness;
}