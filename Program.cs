using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace YuRis_Tool
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Defaults
            string ysroot = null;
            string yscom = null;
            byte[] ybnKey = BitConverter.GetBytes(0x4A415E60); // default key
            bool outputJson = false; // default text output
            string format = null;    // optional --format override

            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
            {
                PrintUsage();
                return;
            }

            // Simple argument parsing supporting:
            // -r/--root <dir>
            // -c/--yscd <file>
            // -k/--key <hex32> OR 4 bytes (e.g., 4A 41 5E 60)
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "-r":
                    case "--root":
                        RequireValue(args, ref i, "--root");
                        ysroot = args[++i];
                        break;
                    case "-c":
                    case "--yscd":
                    case "--yscom":
                        RequireValue(args, ref i, "--yscd");
                        yscom = args[++i];
                        break;
                    case "-j":
                    case "--json":
                        outputJson = true;
                        break;
                    case "-f":
                    case "--format":
                        RequireValue(args, ref i, "--format");
                        format = args[++i];
                        break;
                    case "-k":
                    case "--key":
                    {
                        // Try to parse key as either a single 32-bit hex/int or as 4 subsequent byte tokens
                        if (i + 1 >= args.Length)
                        {
                            Fail("--key needs a value. See --help.");
                            return;
                        }

                        // Collect tokens after --key that are not next option, up to 4
                        var tokens = new List<string>();
                        int j = i + 1;
                        while (j < args.Length && !IsOption(args[j]) && tokens.Count < 4)
                        {
                            tokens.Add(args[j]);
                            j++;
                        }

                        if (tokens.Count == 0)
                        {
                            Fail("--key needs a value. See --help.");
                            return;
                        }

                        // Advance i to last consumed
                        i = j - 1;

                        try
                        {
                            ybnKey = ParseKey(tokens);
                        }
                        catch (Exception ex)
                        {
                            Fail($"Invalid --key: {ex.Message}");
                            return;
                        }
                        break;
                    }
                    default:
                        // Fallback: if the user passed bare positional args, try to map:
                        // [root] or [yscd] [root]
                        if (!IsOption(a))
                        {
                            if (ysroot == null)
                            {
                                // First positional: root
                                ysroot = a;
                            }
                            else if (yscom == null)
                            {
                                // Second positional: yscd
                                yscom = a;
                            }
                            else
                            {
                                Fail($"Unknown argument: {a}");
                                return;
                            }
                        }
                        else
                        {
                            Fail($"Unknown option: {a}");
                            return;
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(ysroot))
            {
                Fail("Missing --root. See --help.");
                return;
            }

            if (!Directory.Exists(ysroot))
            {
                Fail($"Root directory not found: {ysroot}");
                return;
            }

            // Normalize format flag
            if (!string.IsNullOrWhiteSpace(format))
            {
                var f = format.Trim().ToLowerInvariant();
                if (f == "json") outputJson = true;
                else if (f == "txt" || f == "text") outputJson = false;
                else
                {
                    Fail($"Unknown --format: {format}. Supported: txt, json");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(yscom))
            {
                if (!File.Exists(yscom))
                {
                    Fail($"YSCD file not found: {yscom}");
                    return;
                }
                YSCD.Load(yscom);
            }

            var yuris = new YuRisScript();
            yuris.Init(ysroot, ybnKey);
            if (outputJson)
                yuris.DecompileProjectJson();
            else
                yuris.DecompileProject();
        }

        static void PrintUsage()
        {
            var exe = AppDomain.CurrentDomain.FriendlyName;
            Console.WriteLine("YuRis script analyze tool");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {exe} --root <ysbin_dir> [--yscd <yscd_file>] [--key <hex32>|<b0 b1 b2 b3>]");
            Console.WriteLine("  Positional fallback: <ysbin_dir> [yscd_file]");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -r, --root     Root directory containing ysc.ybn/ysl.ybn/yst_list.ybn, etc.");
            Console.WriteLine("  -c, --yscd     Path to YSCD file (optional). Also accepts --yscom.");
            Console.WriteLine("  -k, --key      YBN key as 32-bit hex (e.g. 0x4A415E60) or 4 bytes.");
            Console.WriteLine("  -j, --json     Output decompiled data as JSON files instead of text.");
            Console.WriteLine("  -f, --format   Explicitly set output format: txt | json.");
            Console.WriteLine("  -h, --help     Show this help.");
            Console.WriteLine("");
            Console.WriteLine("Key examples:");
            Console.WriteLine($"  {exe} -r D:\\game\\ysbin -k 0x4A415E60");
            Console.WriteLine($"  {exe} -r . -k 4A 41 5E 60");
            Console.WriteLine($"  {exe} -r . -k \"4A,41,5E,60\"");
        }

        static void Fail(string message)
        {
            Console.Error.WriteLine(message);
        }

        static bool IsOption(string s) => s.StartsWith("-");

        static void RequireValue(string[] args, ref int i, string opt)
        {
            if (i + 1 >= args.Length || IsOption(args[i + 1]))
                throw new ArgumentException($"{opt} requires a value");
        }

        static byte[] ParseKey(List<string> tokens)
        {
            if (tokens.Count == 1)
            {
                var t = tokens[0];
                // Accept: 0xXXXXXXXX, XXXXXXXX, "AA,BB,CC,DD", "AA-BB-CC-DD"
                t = t.Trim();
                // Comma/sep list in a single token
                var parts = t.Split(new[] { ',', '-', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    // Single 32-bit value
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        t = t.Substring(2);
                    if (t.Length > 8 || t.Length == 0 || !t.All(IsHex))
                        throw new FormatException("expect 32-bit hex (e.g., 4A415E60)");
                    var value = uint.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return BitConverter.GetBytes(value); // little-endian
                }
                else if (parts.Length == 4)
                {
                    return parts.Select(ParseByte).ToArray();
                }
                else
                {
                    throw new FormatException("expect 4 bytes or 32-bit hex");
                }
            }
            else if (tokens.Count == 4)
            {
                return tokens.Select(ParseByte).ToArray();
            }
            else
            {
                throw new FormatException("--key expects 1 value (hex32) or 4 byte values");
            }

            static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            static byte ParseByte(string s)
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (s.All(char.IsDigit))
                {
                    // decimal
                    if (!byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                        throw new FormatException($"invalid byte: {s}");
                    return b;
                }
                // hex
                if (!byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hb))
                    throw new FormatException($"invalid hex byte: {s}");
                return hb;
            }
        }
    }
}
