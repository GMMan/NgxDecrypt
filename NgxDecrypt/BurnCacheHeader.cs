using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace NgxDecrypt
{
    public class BurnCacheHeader
    {
        public class BurnCacheBlock
        {
            public uint Offset { get; set; }
            public string Description { get; set; }
        }

        public uint Version { get; set; }
        public string Name { get; set; }
        public BurnCacheBlock[] Blocks { get; set; } = new BurnCacheBlock[15];

        public void Read(BinaryReader br)
        {
            Version = br.ReadUInt32();
            Name = ReadString(br, 12);
            for (int i = 0; i < Blocks.Length; ++i)
            {
                BurnCacheBlock block = new BurnCacheBlock();
                block.Offset = br.ReadUInt32();
                block.Description = ReadString(br, 12);
                Blocks[i] = block;
            }
        }

        string ReadString(BinaryReader br, int blockLength)
        {
            string str = new string(br.ReadChars(blockLength));
            int nullInd = str.IndexOf('\0');
            if (nullInd != -1) str = str.Substring(0, nullInd);
            return str;
        }

        public uint GetLength(int blockIndex)
        {
            if (blockIndex >= Blocks.Length - 1 || blockIndex < 0) throw new ArgumentOutOfRangeException(nameof(Blocks));
            return Blocks[blockIndex + 1].Offset - Blocks[blockIndex].Offset;
        }
    }
}
