using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NgxDecrypt
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("Usage: NgxDecrypt <pack|unpack> <inPath> <outPath>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("\tpack\tEncrypts ROMs and prepares game card.");
                Console.Error.WriteLine("\tunpack\tDecrypts ROMs from game card.");
                Console.Error.WriteLine("\tinPath\tInput path");
                Console.Error.WriteLine("\toutPath\tOutput path");
                Environment.Exit(-1);
            }

            string inPath = args[1];
            string outPath = args[2];

            try
            {
                switch (args[0].ToLower())
                {
                    case "pack":
                        Directory.CreateDirectory(outPath);
                        PrepareCard(inPath, outPath);
                        break;
                    case "unpack":
                        Directory.CreateDirectory(outPath);
                        DecryptCard(inPath, outPath);
                        break;
                    default:
                        Console.Error.WriteLine("Invalid operation specified.");
                        Environment.Exit(-1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Something went wrong: {0}", ex);
                Environment.Exit(1);
            }
        }

        public static void PrepareCard(string inPath, string outPath)
        {
            int numRoms = 0;
            List<string> romHashes = new List<string>();
            List<string> heavensRoadHashes = new List<string>();

            // Encrypt all ROMs
            while (true)
            {
                string fileName = $"game{numRoms + 1}.aes";
                if (!File.Exists(Path.Combine(inPath, fileName)))
                {
                    fileName = Path.ChangeExtension(fileName, ".mvs");
                    if (!File.Exists(Path.Combine(inPath, fileName))) break;
                }

                string thumbName = $"game{numRoms + 1}.png";
                if (!File.Exists(Path.Combine(inPath, thumbName)))
                {
                    Console.Error.WriteLine($"Thumbnail does not exist for game {numRoms + 1}, not processing further games.");
                    break;
                }

                ++numRoms;
                Console.WriteLine($"Encrypting {fileName}");
                // Encrypt ROM
                Crypto.EncryptNgxRom(Path.Combine(inPath, fileName), Path.Combine(outPath, fileName));
                // Copy thumbnail
                File.Copy(Path.Combine(inPath, thumbName), Path.Combine(outPath, thumbName), true);

                using (FileStream fs = File.OpenRead(Path.Combine(outPath, fileName)))
                {
                    // Calculate ROM key
                    romHashes.Add(Crypto.CalculateSha1Hash(fs));
                    fs.Seek(0, SeekOrigin.Begin);
                    // Calculate heaven's road
                    heavensRoadHashes.Add(Crypto.CalculateHeavensRoad(fs));
                }
            }

            if (numRoms < 1) throw new Exception("At least one game is required.");

            // Write card_rom_key.txt
            Console.WriteLine("Writing ROM key");
            using (StreamWriter sw = File.CreateText(Path.Combine(outPath, "card_rom_key.txt")))
            {
                foreach (var hash in romHashes)
                    sw.Write(hash);
            }

            Console.WriteLine("Generating config");
            // Generate heaven's road
            byte[] heavensRoad = Crypto.SerializeHeavensRoad(heavensRoadHashes);
            //File.WriteAllBytes(Path.Combine(outPath, "neogeoX.jpg"), heavensRoad);

            // Generate game_card_configure.conf (text)
            byte[] configBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                StreamWriter sw = new StreamWriter(ms);
                sw.WriteLine("card_game_work_path=/mnt/mmc/card_game/");
                sw.WriteLine($"card_game_number={numRoms}");
                sw.Flush();
                configBytes = ms.ToArray();
            }

            // Combine heaven's road and config
            byte[] combinedConfig = Crypto.JoinConfig(heavensRoad, configBytes);

            // gamestore encrypt and save
            byte[] cryptedConfig = Crypto.GameStore(combinedConfig, File.ReadAllBytes(Path.Combine(outPath, "game1.png")));
            File.WriteAllBytes(Path.Combine(outPath, "game_card_configure.conf"), cryptedConfig);
        }

        public static void DecryptCard(string inPath, string outPath)
        {
            Console.WriteLine("Loading config");

            // gamestore decrypt and split config
            byte[] cryptedConfig = File.ReadAllBytes(Path.Combine(inPath, "game_card_configure.conf"));
            byte[] combinedConfig = Crypto.GameStore(cryptedConfig, File.ReadAllBytes(Path.Combine(inPath, "game1.png")));
            Crypto.SplitConfig(combinedConfig, out var heavensRoad, out var configBytes);

            // Save config
            File.WriteAllBytes(Path.Combine(outPath, "game_card_configure.conf"), configBytes);

            // Parse number of games
            int numRoms;
            using (MemoryStream ms = new MemoryStream(configBytes))
            {
                var config = ParseConfig(ms);
                if (!config.TryGetValue("card_game_number", out var numRomsString))
                    throw new Exception("Cannot get number of games from config.");
                if (!int.TryParse(numRomsString, out numRoms))
                    throw new FormatException("Could not parse number of games.");
            }

            // Deserialize heaven's road
            List<string> heavensRoadHashes = Crypto.DeserializeHeavensRoad(heavensRoad);

            // Parse rom key
            List<string> romHashes = new List<string>();
            using (FileStream fs = File.OpenRead(Path.Combine(inPath, "card_rom_key.txt")))
            {
                BinaryReader br = new BinaryReader(fs);
                for (int i = 0; i < numRoms; ++i)
                {
                    romHashes.Add(new string(br.ReadChars(40)));
                }
            }

            for (int i = 0; i < numRoms; ++i)
            {
                // Validate ROM
                string romName = $"game{i + 1}.aes";
                if (!File.Exists(Path.Combine(inPath, romName))) romName = Path.ChangeExtension(romName, ".mvs");
                Console.WriteLine($"Processing {romName}");

                using (FileStream fs = File.OpenRead(Path.Combine(inPath, romName)))
                {
                    string hash = Crypto.CalculateHeavensRoad(fs);
                    if (hash != heavensRoadHashes[i]) throw new InvalidDataException($"ROM {i + 1} failed DRM check.");
                    fs.Seek(0, SeekOrigin.Begin);
                    hash = Crypto.CalculateSha1Hash(fs);
                    if (hash != romHashes[i]) throw new InvalidDataException($"ROM {i + 1} failed hash check.");
                }

                // Copy and decrypt ROM
                Crypto.DecryptNgxRom(Path.Combine(inPath, romName), Path.Combine(outPath, romName));

                // Copy thumbnail
                string thumbName = $"game{i + 1}.png";
                File.Copy(Path.Combine(inPath, thumbName), Path.Combine(outPath, thumbName), true);
            }
        }

        static Dictionary<string, string> ParseConfig(Stream stream)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            var splitChars = new char[] { '=' };
            StreamReader sr = new StreamReader(stream);
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                int commentIndex = line.IndexOf('#');
                if (commentIndex != -1)
                    line = line.Substring(0, commentIndex);
                string[] splitted = line.Split(splitChars, 2);
                if (splitted.Length != 2) throw new InvalidDataException("Unable to parse config.");
                dict.Add(splitted[0].Trim(), splitted[1].Trim());
            }
            return dict;
        }
    }
}
