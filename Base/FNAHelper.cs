using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;

public static class FNAHooks
{
    private static FieldInfo _textureField;

    public static IntPtr GetNativeTexturePtr(Texture texture)
    {
        if (_textureField == null)
        {
            _textureField = typeof(Texture).GetField("texture", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_textureField == null)
            {
                throw new Exception("无法在 FNA Texture 类中找到 'texture' 字段，FNA 版本可能已更改。");
            }
        }

        return (IntPtr)_textureField.GetValue(texture);
    }
}