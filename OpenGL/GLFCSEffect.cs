using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;

public class GLFCSEffect : IFCSEffect
{
    public FCSMetadata Metadata { get; }

    public int GLProgram { get; private set; } 
    public int GLCS { get; private set; } 

    private int _refCount = 0;
    public void AddRef() => _refCount++;
    public void Release()
    {
        _refCount--;
        if (_refCount <= 0) Dispose();
    }

    public GLFCSEffect(FCSReader fcs)
    {
        Metadata = fcs.Metadata;

        if (!string.IsNullOrEmpty(fcs.GlslVS) && !string.IsNullOrEmpty(fcs.GlslPS))
        {
            GLProgram = CreateProgram(fcs.GlslVS, fcs.GlslPS);
        }

        if (!string.IsNullOrEmpty(fcs.GlslCS))
        {
            GLCS = CreateComputeProgram(fcs.GlslCS);
        }
    }

    private int CreateProgram(string vsSource, string psSource)
    {
        int vs = CompileShader(ShaderType.VertexShader, vsSource);
        int ps = CompileShader(ShaderType.FragmentShader, psSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vs);
        GL.AttachShader(program, ps);
        GL.LinkProgram(program);
        GL.DeleteShader(vs);
        GL.DeleteShader(ps);
        return program;
    }

    private int CreateComputeProgram(string csSource)
    {
        int cs = CompileShader(ShaderType.ComputeShader, csSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, cs);
        GL.LinkProgram(program);
        GL.DeleteShader(cs);
        return program;
    }

    private int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        return shader;
    }

    public void Dispose()
    {
        if (GLProgram != 0) GL.DeleteProgram(GLProgram);
        if (GLCS != 0) GL.DeleteProgram(GLCS);
    }
}