using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NgxDecrypt
{
    public static class Crypto
    {
        const int MAX_SPLIT_CONFIG_FILE_SIZE = 0x100000;

        public static byte[] GameStore(byte[] data, byte[] key)
        {
            // gamestore encryption
            // sound -> snk_desktop.bin; main_themes/left_sound_track.wav
            // nxu.dge; main_themes/right_sound_track.wav
            // nxu_test.dge; main_themes/right_sound_track.wav
            // neogeoX.wav; main_themes/Joystick-1_H20.jpg
            // /mnt/mmc/card_game/game_card_configure.conf; /mnt/mmc/card_game/game1.png

            const int MAX_KEY_LENGTH = 0x19000;
            if (key.Length > MAX_KEY_LENGTH)
            {
                key = (byte[])key.Clone();
                Array.Resize(ref key, MAX_KEY_LENGTH);
            }
            byte[] output = new byte[data.Length];
            for (int i = 0; i < output.Length; ++i)
            {
                output[i] = (byte)(data[i] ^ key[key.Length - 1 - (i % key.Length)]);
            }
            return output;
        }

        public static void SplitConfig(byte[] inData, out byte[] outData1, out byte[] outData2)
        {
            using (MemoryStream ms = new MemoryStream(inData))
            {
                BinaryReader br = new BinaryReader(ms);
                int file1Size = br.ReadInt32();
                int file2Size = br.ReadInt32();
                if (file1Size > MAX_SPLIT_CONFIG_FILE_SIZE || file2Size > MAX_SPLIT_CONFIG_FILE_SIZE)
                    throw new InvalidDataException("Data too big.");
                outData1 = br.ReadBytes(file1Size);
                outData2 = br.ReadBytes(file2Size);
            }
        }

        public static byte[] JoinConfig(byte[] inData1, byte[] inData2)
        {
            if (inData1.Length > MAX_SPLIT_CONFIG_FILE_SIZE)
                throw new ArgumentException("Data too big.", nameof(inData1));
            if (inData2.Length > MAX_SPLIT_CONFIG_FILE_SIZE)
                throw new ArgumentException("Data too big.", nameof(inData2));

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter bw = new BinaryWriter(ms);
                bw.Write(inData1.Length);
                bw.Write(inData2.Length);
                bw.Write(inData1);
                bw.Write(inData2);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static string CalculateHeavensRoad(Stream fs)
        {
            const int BUFFER_SIZE = 0x20000;
            if (fs.Length < BUFFER_SIZE) throw new ArgumentException("File too small.", nameof(fs));
            BinaryReader br = new BinaryReader(fs);
            fs.Seek(-BUFFER_SIZE, SeekOrigin.End);
            byte[] data = br.ReadBytes(BUFFER_SIZE);

            using (SHA1 sha1 = SHA1.Create())
            {
                sha1.TransformBlock(data, 0, data.Length, data, 0);
                byte[] appendData = Encoding.ASCII.GetBytes("jacky_made");
                sha1.TransformFinalBlock(appendData, 0, appendData.Length);
                return BitConverter.ToString(sha1.Hash).Replace("-", string.Empty).ToLower();
            }
        }

        public static string CalculateSha1Hash(Stream fs)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
            }
        }

        public static List<string> DeserializeHeavensRoad(byte[] data)
        {
            List<string> hashes = new List<string>();
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader br = new BinaryReader(ms);
                int entryLength = br.ReadInt32();
                int dataLength = br.ReadInt32();
                int entryCount = dataLength / entryLength;
                for (int i = 0; i < entryCount; ++i)
                {
                    hashes.Add(new string(br.ReadChars(entryLength)));
                }
            }
            return hashes;
        }

        public static byte[] SerializeHeavensRoad(List<string> hashes)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter bw = new BinaryWriter(ms);
                bw.Write(40);
                bw.Write(0);
                foreach (var hash in hashes)
                {
                    if (hash.Length != 40) throw new ArgumentException("Incorrect hash length.", nameof(hashes));
                    bw.Write(hash.ToCharArray());
                }
                ms.Seek(4, SeekOrigin.Begin);
                bw.Write((int)(ms.Length - 8));
                bw.Flush();
                return ms.ToArray();
            }
        }

        const int ROM_KEY_OFFSET_MULTIPLE = 10;
        const int ROM_ENCRYPTABLE_BLOCKS = 6; // There's only 5 encrypted blocks excluding the header though...
        const int ROM_KEY_EXTENSION = ROM_KEY_OFFSET_MULTIPLE * ROM_ENCRYPTABLE_BLOCKS;

        public static void DecryptNgxRom(string inPath, string outPath)
        {
            File.Copy(inPath, outPath, true);
            using (var fs = File.Open(outPath, FileMode.Open, FileAccess.ReadWrite))
            {
                BinaryReader br = new BinaryReader(fs);
                fs.Seek(-8, SeekOrigin.End);
                int unencryptedStart = br.ReadInt32() ^ 0x12345678;
                int unencryptedEnd = br.ReadInt32() ^ 0x12345678;
                int keyLength;
                byte[] keyData = GetNgxRomKey(br, unencryptedStart, unencryptedEnd, out keyLength);

                CryptNgxRom(fs, null, keyData, keyLength);

                fs.SetLength(fs.Length - 8);
            }
        }

        public static void EncryptNgxRom(string inPath, string outPath)
        {
            File.Copy(inPath, outPath, true);
            using (var fs = File.Open(outPath, FileMode.Open, FileAccess.ReadWrite))
            {
                BinaryReader br = new BinaryReader(fs);
                BurnCacheHeader header = new BurnCacheHeader();
                header.Read(br);
                // Unencrypted region (Sprite (1) and Text (2))
                var unencryptedStart = (int)header.Blocks[1].Offset;
                var unencryptedEnd = (int)(header.Blocks[2].Offset + header.GetLength(2));
                int keyLength;
                byte[] keyData = GetNgxRomKey(br, unencryptedStart, unencryptedEnd, out keyLength);

                CryptNgxRom(fs, header, keyData, keyLength);

                BinaryWriter bw = new BinaryWriter(fs);
                fs.Seek(0, SeekOrigin.End);
                bw.Write(unencryptedStart ^ 0x12345678);
                bw.Write(unencryptedEnd ^ 0x12345678);
            }
        }

        static byte[] GetNgxRomKey(BinaryReader br, int unencryptedStart, int unencryptedEnd, out int keyLength)
        {
            var unencryptedLength = unencryptedEnd - unencryptedStart;
            keyLength = Math.Min(0x100, unencryptedLength - ROM_KEY_EXTENSION);
            if (keyLength <= 0) throw new InvalidDataException("Key length is invalid.");
            // Key is in the middle of the range
            int keyOffset = unencryptedStart + (unencryptedLength - ROM_KEY_EXTENSION - keyLength) / 2;
            br.BaseStream.Seek(keyOffset, SeekOrigin.Begin);
            byte[] keyData = br.ReadBytes(keyLength + ROM_KEY_EXTENSION);
            for (int i = 0; i < keyData.Length; ++i)
            {
                // Fill any zero bytes
                if (keyData[i] == 0)
                    keyData[i] = (byte)~(i % keyLength);
            }
            return keyData;
        }

        static void CryptNgxRom(Stream fs, BurnCacheHeader header, byte[] keyData, int keyLength)
        {
            BinaryReader br = new BinaryReader(fs);
            BinaryWriter bw = new BinaryWriter(fs);

            // Crypt cache header
            fs.Seek(0, SeekOrigin.Begin);
            byte[] cacheHeader = br.ReadBytes(0x100);
            for (int i = 0; i < cacheHeader.Length; ++i)
            {
                cacheHeader[i] ^= keyData[i % keyLength];
            }

            fs.Seek(0, SeekOrigin.Begin);
            bw.Write(cacheHeader);

            if (header == null)
            {
                using (MemoryStream ms = new MemoryStream(cacheHeader))
                {
                    BinaryReader hbr = new BinaryReader(ms);
                    header = new BurnCacheHeader();
                    header.Read(hbr);
                }
            }

            // Encryption map (key index -> cache block)
            Dictionary<int, int> encryptionMap = new Dictionary<int, int>
                {
                    { 1, 0 }, // Code
                    { 2, 3 }, // PCM A
                    { 3, 4 }, // PCM B
                    { 4, 5 }, // Sprite Attr
                    { 5, 6 } // Text Attr
                };

            foreach (var pair in encryptionMap)
            {
                CryptNeoGeoBlock(fs, header.Blocks[pair.Value].Offset, header.GetLength(pair.Value), keyData,
                    pair.Key * ROM_KEY_OFFSET_MULTIPLE, keyLength);
            }
        }

        static void CryptNeoGeoBlock(Stream fs, uint offset, uint length, byte[] key, int keyBase, int keyLength)
        {
            const int BLOCK_SIZE = 4 * 1024;

            BinaryReader br = new BinaryReader(fs);
            BinaryWriter bw = new BinaryWriter(fs);

            fs.Seek(offset, SeekOrigin.Begin);
            while (length > 0)
            {
                int readLength = (int)Math.Min(BLOCK_SIZE, length);
                byte[] data = br.ReadBytes(readLength);
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] ^= key[keyBase + (i % keyLength)];
                }
                fs.Seek(-readLength, SeekOrigin.Current); // rewind to write
                bw.Write(data);
                length -= (uint)readLength;
            }
        }
    }
}
