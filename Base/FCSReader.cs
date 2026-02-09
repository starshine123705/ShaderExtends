using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Terraria.ModLoader;

namespace ShaderExtends.Base
{
    public class FCSMetadata
    {
        public List<CBufferMeta> Buffers { get; set; } = new();
        public List<InputElementMeta> InputElements { get; set; } = new();

        public string? VsEntry { get; set; }
        public string? PsEntry { get; set; }
        public string? CsEntry { get; set; }
    }

    public class InputElementMeta
    {
        public string SemanticName { get; set; } = "";
        public int SemanticIndex { get; set; }
        public string Format { get; set; } = "";
        public int AlignedByteOffset { get; set; }
    }

    public class CBufferMeta
    {
        public string Name { get; set; } = "";
        public int Slot { get; set; }
        public int TotalSize { get; set; }
        public Dictionary<string, VariableMeta> Variables { get; set; } = new();
    }

    public class VariableMeta
    {
        public int Offset { get; set; }
        public int Size { get; set; }
    }

    public class FCSReader
    {
        public byte[] AlignedHlsl;
        public byte[] DxbcVS, DxbcPS, DxbcCS; 
        public byte[] SpirvVS, SpirvPS, SpirvCS; 
        public string GlslVS, GlslPS, GlslCS;
        public FCSMetadata Metadata;

        public static FCSReader Load(string path)
        {
            using var fs = ModGet.GetCallingMod().GetFileStream(path);
            byte[] decompressedData;

            int header = fs.ReadByte();
            if (header == 0x5A)
            {
                using var br = new BrotliStream(fs, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                br.CopyTo(ms);
                decompressedData = ms.ToArray();
            }
            else
            {
                fs.Seek(0, SeekOrigin.Begin);
                decompressedData = new byte[fs.Length];
                fs.Read(decompressedData, 0, decompressedData.Length);
            }

            using var dataMs = new MemoryStream(decompressedData);
            using var reader = new BinaryReader(dataMs);
            var fcs = new FCSReader();

            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "FCSC")
                throw new Exception("Not a valid FCS file");

            int version = reader.ReadInt32();

            while (dataMs.Position <= dataMs.Length - 8)
            {
                int blockType = reader.ReadInt32();
                int blockLen = reader.ReadInt32();

                if (blockLen < 0 || dataMs.Position + blockLen > dataMs.Length) break;

                byte[] data = reader.ReadBytes(blockLen);

                switch (blockType)
                {
                    case 1: fcs.AlignedHlsl = data; break;
                    case 2: fcs.DxbcVS = data; break;
                    case 3: fcs.DxbcPS = data; break;
                    case 4: fcs.DxbcCS = data; break;
                    case 30: fcs.GlslVS = Encoding.UTF8.GetString(data); break;
                    case 31: fcs.GlslPS = Encoding.UTF8.GetString(data); break;
                    case 32: fcs.GlslCS = Encoding.UTF8.GetString(data); break;
                    case 40: fcs.SpirvVS = data; break;
                    case 41: fcs.SpirvPS = data; break;
                    case 42: fcs.SpirvCS = data; break;
                    case 100:
                        fcs.Metadata = JsonSerializer.Deserialize<FCSMetadata>(data) ?? new FCSMetadata();
                        break;
                }
            }

            return fcs;
        }
    }
}