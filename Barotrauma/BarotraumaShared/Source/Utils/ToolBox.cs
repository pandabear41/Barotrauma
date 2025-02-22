﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace Barotrauma
{
    public class Pair<T1, T2>
    {
        public T1 First { get; set; }
        public T2 Second { get; set; }

        public Pair(T1 first, T2 second)
        {
            First  = first;
            Second = second;
        }
    }

    public class Triplet<T1, T2, T3>
    {
        public T1 First { get; set; }
        public T2 Second { get; set; }
        public T3 Third { get; set; }

        public Triplet(T1 first, T2 second, T3 third)
        {
            First = first;
            Second = second;
            Third = third;
        }
    }

    public static partial class ToolBox
    {
        static internal class Epoch
        {
            private static readonly DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            /// <summary>
            /// Returns the current Unix Epoch (Coordinated Universal Time )
            /// </summary>
            public static int NowUTC
            {
                get
                {
                    return (int)(DateTime.UtcNow.Subtract(epoch).TotalSeconds);
                }
            }

            /// <summary>
            /// Returns the current Unix Epoch (user's current time)
            /// </summary>
            public static int NowLocal
            {
                get
                {
                    return (int)(DateTime.Now.Subtract(epoch).TotalSeconds);
                }
            }

            /// <summary>
            /// Convert an epoch to a datetime
            /// </summary>
            public static DateTime ToDateTime(decimal unixTime)
            {
                return epoch.AddSeconds((long)unixTime);
            }

            /// <summary>
            /// Convert a DateTime to a unix time
            /// </summary>
            public static uint FromDateTime(DateTime dt)
            {
                return (uint)(dt.Subtract(epoch).TotalSeconds);
            }
        }

        public static bool IsProperFilenameCase(string filename)
        {
            //File case only matters on Linux where the filesystem is case-sensitive, so we don't need these errors in release builds.
            //It also seems Path.GetFullPath may return a path with an incorrect case on Windows when the case of any of the game's
            //parent folders have been changed.
#if !DEBUG && !LINUX
            return true;
#endif
            char[] delimiters = { '/', '\\' };
            string[] subDirs = filename.Split(delimiters);
            string originalFilename = filename;
            filename = "";

            for (int i = 0; i < subDirs.Length - 1; i++)
            {
                filename += subDirs[i] + "/";

                if (i == subDirs.Length - 2)
                {
                    string[] filePaths = Directory.GetFiles(filename);
                    if (filePaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.Ordinal)))
                    {
                        return true;
                    }
                    else if (filePaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.OrdinalIgnoreCase)))
                    {
                        DebugConsole.ThrowError(originalFilename + " has incorrect case!");
                        return false;
                    }
                }

                string[] dirPaths = Directory.GetDirectories(filename);

                if (!dirPaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.Ordinal)))
                {
                    if (dirPaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.OrdinalIgnoreCase)))
                    {
                        DebugConsole.ThrowError(originalFilename + " has incorrect case!");
                    }
                    else
                    {
                        DebugConsole.ThrowError(originalFilename + " doesn't exist!");
                    }
                    return false;
                }
            }
            return true;
        }

        public static string RemoveInvalidFileNameChars(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                fileName = fileName.Replace(invalidChar.ToString(), "");
            }
            return fileName;
        }

        public static string LimitString(string str, int maxCharacters)
        {
            if (str == null || maxCharacters < 0) return null;

            if (maxCharacters < 4 || str.Length <= maxCharacters) return str;

            return str.Substring(0, maxCharacters - 3) + "...";
        }

        public static string RandomSeed(int length)
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(
                Enumerable.Repeat(chars, length)
                          .Select(s => s[Rand.Int(s.Length)])
                          .ToArray());
        }

        public static int StringToInt(string str)
        {
            str = str.Substring(0, Math.Min(str.Length, 32));

            str = str.PadLeft(4, 'a');

            byte[] asciiBytes = Encoding.ASCII.GetBytes(str);

            for (int i = 4; i < asciiBytes.Length; i++)
            {
                asciiBytes[i % 4] ^= asciiBytes[i];
            }

            return BitConverter.ToInt32(asciiBytes, 0);
        }
        /// <summary>
        /// a method for changing inputtypes with old names to the new ones to ensure backwards compatibility with older subs
        /// </summary>
        public static string ConvertInputType(string inputType)
        {
            if (inputType == "ActionHit" || inputType == "Action") return "Use";
            if (inputType == "SecondaryHit" || inputType == "Secondary") return "Aim";

            return inputType;
        }

        // Convert an RGB value into an HLS value.
        public static Vector3 RgbToHLS(Vector3 color)
        {
            double h, l, s;
            
            double double_r = color.X;
            double double_g = color.Y;
            double double_b = color.Z;

            // Get the maximum and minimum RGB components.
            double max = double_r;
            if (max < double_g) max = double_g;
            if (max < double_b) max = double_b;

            double min = double_r;
            if (min > double_g) min = double_g;
            if (min > double_b) min = double_b;

            double diff = max - min;
            l = (max + min) / 2;
            if (Math.Abs(diff) < 0.00001)
            {
                s = 0;
                h = 0;  // H is really undefined.
            }
            else
            {
                if (l <= 0.5) s = diff / (max + min);
                else s = diff / (2 - max - min);

                double r_dist = (max - double_r) / diff;
                double g_dist = (max - double_g) / diff;
                double b_dist = (max - double_b) / diff;

                if (double_r == max) h = b_dist - g_dist;
                else if (double_g == max) h = 2 + r_dist - b_dist;
                else h = 4 + g_dist - r_dist;

                h = h * 60;
                if (h < 0) h += 360;
            }

            return new Vector3((float)h, (float)l, (float)s);
        }

        /// <summary>
        /// Calculates the minimum number of single-character edits (i.e. insertions, deletions or substitutions) required to change one string into the other
        /// </summary>
        public static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0 || m == 0) return 0;

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        public static string SecondsToReadableTime(float seconds)
        {
            int s = (int)(seconds % 60.0f);
            if (seconds < 60.0f)
            {
                return s + " s";
            }

            int h = (int)(seconds / (60.0f * 60.0f));
            int m = (int)((seconds / 60.0f) % 60);

            string text = "";
            if (h != 0) { text = h + " h"; }
            if (m != 0) { text = string.IsNullOrEmpty(text) ? m + " m" : string.Join(" ", text, m, "m"); }
            if (s != 0) { text = string.IsNullOrEmpty(text) ? s + " s" : string.Join(" ", text, s, "s"); }
            return text;
        }

        private static Dictionary<string, List<string>> cachedLines = new Dictionary<string, List<string>>();
        public static string GetRandomLine(string filePath)
        {
            List<string> lines;
            if (cachedLines.ContainsKey(filePath))
            {
                lines = cachedLines[filePath];
            }
            else
            {
                try
                {
                    using (StreamReader file = new StreamReader(filePath))
                    {
                        lines = File.ReadLines(filePath).ToList();
                        cachedLines.Add(filePath, lines);
                        if (lines.Count == 0)
                        {
                            DebugConsole.ThrowError("File \"" + filePath + "\" is empty!");
                            return "";
                        }
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Couldn't open file \"" + filePath + "\"!", e);
                    return "";
                }
            }

            if (lines.Count == 0) return "";
            return lines[Rand.Range(0, lines.Count, Rand.RandSync.Server)];
        }

        /// <summary>
        /// Reads a number of bits from the buffer and inserts them to a new NetBuffer instance
        /// </summary>
        public static IReadMessage ExtractBits(this IReadMessage originalBuffer, int numberOfBits)
        {
            var buffer = new ReadWriteMessage();
            
            for (int i=0;i<numberOfBits;i++)
            {
                bool bit = originalBuffer.ReadBoolean();
                buffer.Write(bit);
            }
            buffer.BitPosition = 0;

            return buffer;
        }

        public static T SelectWeightedRandom<T>(IList<T> objects, IList<float> weights, Rand.RandSync randSync)
        {
            return SelectWeightedRandom(objects, weights, Rand.GetRNG(randSync));
        }

        public static T SelectWeightedRandom<T>(IList<T> objects, IList<float> weights, Random random)
        {
            if (objects.Count == 0) return default(T);

            if (objects.Count != weights.Count)
            {
                DebugConsole.ThrowError("Error in SelectWeightedRandom, number of objects does not match the number of weights.\n" + Environment.StackTrace);
                return objects[0];
            }

            float totalWeight = weights.Sum();

            float randomNum = (float)(random.NextDouble() * totalWeight);
            for (int i = 0; i < objects.Count; i++)
            {
                if (randomNum <= weights[i])
                {
                    return objects[i];
                }
                randomNum -= weights[i];
            }
            return default(T);
        }

        public static UInt32 StringToUInt32Hash(string str, MD5 md5)
        {
            //calculate key based on MD5 hash instead of string.GetHashCode
            //to ensure consistent results across platforms
            byte[] inputBytes = Encoding.ASCII.GetBytes(str);
            byte[] hash = md5.ComputeHash(inputBytes);

            UInt32 key = (UInt32)((str.Length & 0xff) << 24); //could use more of the hash here instead?
            key |= (UInt32)(hash[hash.Length - 3] << 16);
            key |= (UInt32)(hash[hash.Length - 2] << 8);
            key |= (UInt32)(hash[hash.Length - 1]);

            return key;
        }
        /// <summary>
        /// Returns a new instance of the class with all properties and fields copied.
        /// </summary>
        public static T CreateCopy<T>(this T source, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public) where T : new() => CopyValues(source, new T(), flags);
        public static T CopyValuesTo<T>(this T source, T target, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public) => CopyValues(source, target, flags);

        /// <summary>
        /// Copies the values of the source to the destination. May not work, if the source is of higher inheritance class than the destination. Does not work with virtual properties.
        /// </summary>
        public static T CopyValues<T>(T source, T destination, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public)
        {
            if (source == null)
            {
                throw new Exception("Failed to copy object. Source is null.");
            }
            if (destination == null)
            {
                throw new Exception("Failed to copy object. Destination is null.");
            }
            Type type = source.GetType();
            var properties = type.GetProperties(flags);
            foreach (var property in properties)
            {
                if (property.CanWrite)
                {
                    property.SetValue(destination, property.GetValue(source, null), null);
                }
            }
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                field.SetValue(destination, field.GetValue(source));
            }
            // Check that the fields match.Uncomment to apply the test, if in doubt.
            //if (fields.Any(f => { var value = f.GetValue(destination); return value == null || !value.Equals(f.GetValue(source)); }))
            //{
            //    throw new Exception("Failed to copy some of the fields.");
            //}
            return destination;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
    }
}
