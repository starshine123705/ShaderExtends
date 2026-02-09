using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader;

namespace ShaderExtends.Base
{
    public class ModGet
    {
        public static Mod GetCallingMod()
        {
            var stackTrace = new StackTrace();

            for (int i = 2; i < stackTrace.FrameCount; i++)
            {
                var method = stackTrace.GetFrame(i).GetMethod();
                if (method == null) continue;

                if (method.Name.Contains("GetCallingMod"))
                {
                    continue;
                }
                string rootNamespace = method.DeclaringType?.Name ?? "";

                if (ModLoader.TryGetMod(rootNamespace, out var foundMod))
                {
                    return foundMod;
                }
            }
            return ShaderExtends.Instance;
        }
    }
}
