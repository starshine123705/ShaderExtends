using System.ComponentModel;
using Terraria.ModLoader.Config;

public class ShaderControlConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;
    public static bool EnableVSPSStatic;
    public static bool EnableCSStatic;
    public override void OnChanged()
    {
        EnableCSStatic = EnableCS;
        EnableVSPSStatic = EnableVSPS;
        base.OnChanged();
    }
    [Header("后期渲染开关")]

    [DefaultValue(true)]
    public bool EnableVSPS;

    [DefaultValue(false)]
    public bool EnableCS;
}