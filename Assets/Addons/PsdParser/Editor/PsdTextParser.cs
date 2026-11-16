using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PsdParser
{
    /// <summary>
    /// PSD 文本图层解析器 - 解析 TypeToolObject 数据
    /// </summary>
    public class PsdTextParser
    {
        private static long _dataLength;

        /// <summary>
        /// 解析文本工具对象数据
        /// </summary>
        public static PsdTextInfo Parse(byte[] data, string layerName = "")
        {
            if (data == null || data.Length == 0) return null;

            PsdTextInfo textInfo = new PsdTextInfo();
            _dataLength = data.Length;

            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    // TypeToolObject 结构
                    ushort version = ReadBigEndianUInt16(reader);

                    // 6x Transform 坐标矩阵 (各 8 字节 double) = 48 字节
                    SafeReadBytes(reader, 48);

                    // 在数据中搜索 "TxLr" 来定位 descriptor
                    int txlrPos = -1;
                    for (int i = 0; i < data.Length - 4; i++)
                    {
                        if (data[i] == 0x54 && data[i + 1] == 0x78 && data[i + 2] == 0x4C && data[i + 3] == 0x72)
                        {
                            txlrPos = i;
                            break;
                        }
                    }

                    if (txlrPos >= 0)
                    {
                        int descriptorStart = txlrPos - 4;
                        if (descriptorStart >= 0)
                        {
                            reader.BaseStream.Position = descriptorStart;

                            // 读取 className
                            uint classNameLen = ReadBigEndianUInt32(reader);
                            if (classNameLen > 0 && classNameLen < 10000)
                            {
                                SafeReadBytes(reader, (int)(classNameLen * 2));
                            }

                            // 读取 classID
                            string classId = ReadKeyString(reader);

                            // 读取 itemCount
                            uint itemCount = ReadBigEndianUInt32(reader);

                            if (itemCount > 0 && itemCount < 100)
                            {
                                for (uint i = 0; i < itemCount; i++)
                                {
                                    string key = ReadKeyString(reader);
                                    string type = ReadOSType(reader);

                                    if (key == "Txt " && type == "TEXT")
                                    {
                                        textInfo.text = ReadUnicodeString(reader);
                                        continue;
                                    }

                                    switch (key)
                                    {
                                        case "Txt ":
                                            if (type == "TEXT")
                                                textInfo.text = ReadUnicodeString(reader);
                                            else
                                                SkipValue(reader, type);
                                            break;
                                        case "FStl":
                                        case "Fstl":
                                            ReadStyleDescriptor(reader, textInfo);
                                            break;
                                        default:
                                            SkipValue(reader, type);
                                            break;
                                    }
                                }
                            }
                        }
                    }

                    // 搜索样式数据（字体大小、颜色等）
                    ParseTextStyleData(data, textInfo);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"文本解析失败: {e.Message}");
            }

            return textInfo;
        }

        /// <summary>
        /// 安全读取字节数组
        /// </summary>
        private static byte[] SafeReadBytes(BinaryReader reader, int count)
        {
            long remaining = _dataLength - reader.BaseStream.Position;
            if (remaining < count)
            {
                count = (int)remaining;
            }
            if (count <= 0) return new byte[0];
            return reader.ReadBytes(count);
        }

        /// <summary>
        /// 解析文本样式数据（从原始数据中搜索）
        /// </summary>
        private static void ParseTextStyleData(byte[] data, PsdTextInfo textInfo)
        {
            // 搜索所有 EngineData 并解析
            int searchPos = 0;
            while (searchPos < data.Length - 10)
            {
                int engineDataPos = -1;
                for (int i = searchPos; i < data.Length - 10; i++)
                {
                    // "EngineData" = 45 6E 67 69 6E 65 44 61 74 61
                    if (data[i] == 0x45 && data[i + 1] == 0x6E && data[i + 2] == 0x67 && data[i + 3] == 0x69 &&
                        data[i + 4] == 0x6E && data[i + 5] == 0x65 && data[i + 6] == 0x44 && data[i + 7] == 0x61)
                    {
                        engineDataPos = i;
                        break;
                    }
                }

                if (engineDataPos >= 0)
                {
                    ParseEngineData(data, engineDataPos, textInfo);
                    searchPos = engineDataPos + 1;  // 继续搜索下一个 EngineData
                }
                else
                {
                    break;
                }
            }

            // 搜索字体大小
            for (int i = 0; i < data.Length - 16; i++)
            {
                if (data[i] == 0x53 && data[i + 1] == 0x7A && data[i + 2] == 0x20 && data[i + 3] == 0x20)
                {
                    try
                    {
                        if (i + 16 < data.Length)
                        {
                            double fontSize = ReadDoubleAt(data, i + 8);
                            if (fontSize > 4 && fontSize < 200 && textInfo.fontSize == 0)
                            {
                                textInfo.fontSize = (int)fontSize;
                            }
                        }
                    }
                    catch { }
                }
            }

            textInfo.color.a = 1f;
        }

        /// <summary>
        /// 解析 EngineData (PostScript 格式) - 支持描边/阴影/渐变
        /// </summary>
        private static void ParseEngineData(byte[] data, int start, PsdTextInfo textInfo)
        {
            string dataStr = Encoding.UTF8.GetString(data, start, Math.Min(16000, data.Length - start));

            // 1. 解析文本颜色 (/FillColor)
            ParseFillColorFromEngineData(dataStr, textInfo);

            // 2. 解析描边效果 (/StrokeColor)
            ParseStrokeFromEngineData(dataStr, textInfo);

            // 3. 解析投影效果 (/DropShadow)
            ParseShadowFromEngineData(dataStr, textInfo);

            // 4. 解析渐变填充 (/GradientFill)
            ParseGradientFromEngineData(dataStr, textInfo);
        }

        /// <summary>
        /// 从 EngineData 解析文本颜色
        /// </summary>
        private static void ParseFillColorFromEngineData(string dataStr, PsdTextInfo textInfo)
        {
            int fillColorIdx = dataStr.IndexOf("/FillColor");
            if (fillColorIdx < 0) return;

            int valuesIdx = dataStr.IndexOf("/Values", fillColorIdx);
            if (valuesIdx < 0 || valuesIdx > fillColorIdx + 200) return;

            int bracketStart = dataStr.IndexOf('[', valuesIdx);
            int bracketEnd = dataStr.IndexOf(']', bracketStart);
            if (bracketStart < 0 || bracketEnd < 0) return;

            string valuesStr = dataStr.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            string[] values = valuesStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (values.Length >= 4)
            {
                float v0 = ParsePostScriptFloat(values[0]); // Alpha
                float v1 = ParsePostScriptFloat(values[1]); // R
                float v2 = ParsePostScriptFloat(values[2]); // G
                float v3 = ParsePostScriptFloat(values[3]); // B

                textInfo.color.r = v1;
                textInfo.color.g = v2;
                textInfo.color.b = v3;
                textInfo.color.a = v0;
            }
        }

        /// <summary>
        /// 从 EngineData 解析描边效果
        /// 文本描边数据通常存在于 EngineData 的样式字段中，并非 lfx2。
        /// </summary>
        private static void ParseStrokeFromEngineData(string dataStr, PsdTextInfo textInfo)
        {
            if (string.IsNullOrEmpty(dataStr)) return;

            // 每次解析先清零，避免旧值残留导致误判。
            textInfo.hasStroke = false;

            MatchCollection strokeColorMatches = Regex.Matches(dataStr, @"/(OutlineColor|StrokeColor)\b", RegexOptions.IgnoreCase);
            if (strokeColorMatches.Count == 0) return;

            foreach (Match strokeColorMatch in strokeColorMatches)
            {
                if (!strokeColorMatch.Success) continue;

                // 以 StrokeColor 为中心取局部样式片段
                int segStart = Math.Max(0, strokeColorMatch.Index - 512);
                int segEnd = Math.Min(dataStr.Length, strokeColorMatch.Index + 4096);
                string segment = dataStr.Substring(segStart, segEnd - segStart);
                string colorToken = strokeColorMatch.Value;

                if (!TryExtractBooleanAfterToken(segment, "/StrokeFlag", out bool strokeFlagEnabled, segment.Length)
                    || !strokeFlagEnabled)
                {
                    continue;
                }

                if (!TryExtractValuesAfterToken(segment, colorToken, out float[] values, segment.Length))
                {
                    continue;
                }

                if (values.Length < 4) continue;

                // 获取描边宽度
                float widthVal = 0f;
                bool hasStrokeWidth = TryExtractSingleAfterToken(segment, "/OutlineWidth", out widthVal, segment.Length)
                    || TryExtractSingleAfterToken(segment, "/StrokeWidth", out widthVal, segment.Length);

                if (!hasStrokeWidth || widthVal <= 0f)
                {
                    continue;
                }

                int strokeWidth = Mathf.Clamp(Mathf.RoundToInt(widthVal), 1, 8);

                float a = Mathf.Clamp01(values[0]);
                float r = Mathf.Clamp01(values[1]);
                float g = Mathf.Clamp01(values[2]);
                float b = Mathf.Clamp01(values[3]);

                // 透明纯黑通常是默认样式占位值，不应当作有效描边
                if (a <= 0.01f && r <= 0.01f && g <= 0.01f && b <= 0.01f)
                {
                    continue;
                }

                textInfo.hasStroke = true;
                textInfo.strokeWidth = strokeWidth;
                textInfo.strokeColor = new Color(r, g, b, a);
                return;
            }
        }

        /// <summary>
        /// 从 EngineData 解析投影效果
        /// 格式：/DropShadow [/Values [angle distance blur R G B A]] def
        /// </summary>
        private static void ParseShadowFromEngineData(string dataStr, PsdTextInfo textInfo)
        {
            // 查找 /DropShadow
            int shadowIdx = dataStr.IndexOf("/DropShadow");
            if (shadowIdx < 0) return;

            // 查找 /Values 数组
            int valuesIdx = dataStr.IndexOf("/Values", shadowIdx);
            if (valuesIdx < 0 || valuesIdx >= shadowIdx + 300) return;

            int bracketStart = dataStr.IndexOf('[', valuesIdx);
            if (bracketStart < 0) return;

            int bracketEnd = dataStr.IndexOf(']', bracketStart);
            if (bracketEnd < 0) return;

            string valuesStr = dataStr.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            string[] values = valuesStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // 解析投影参数：[angle distance blur R G B A]
            if (values.Length >= 7)
            {
                textInfo.shadowAngle = (int)ParsePostScriptFloat(values[0]);
                textInfo.shadowDistance = (int)ParsePostScriptFloat(values[1]);
                textInfo.shadowBlur = (int)ParsePostScriptFloat(values[2]);
                textInfo.shadowColor.r = ParsePostScriptFloat(values[3]);
                textInfo.shadowColor.g = ParsePostScriptFloat(values[4]);
                textInfo.shadowColor.b = ParsePostScriptFloat(values[5]);
                textInfo.shadowColor.a = ParsePostScriptFloat(values[6]);
                textInfo.hasDropShadow = true;
            }
            else if (values.Length >= 3)
            {
                // 简化的格式，只有角度/距离/模糊
                textInfo.shadowAngle = (int)ParsePostScriptFloat(values[0]);
                textInfo.shadowDistance = (int)ParsePostScriptFloat(values[1]);
                textInfo.shadowBlur = (int)ParsePostScriptFloat(values[2]);
                textInfo.hasDropShadow = true;
            }
        }

        /// <summary>
        /// 从 EngineData 解析渐变填充
        /// 格式：/GradientFill [/Colors [[/Values [...]] [/Values [...]] ...]] def
        /// </summary>
        private static void ParseGradientFromEngineData(string dataStr, PsdTextInfo textInfo)
        {
            if (string.IsNullOrEmpty(dataStr)) return;

            // 查找 /GradientFill 或 /Gradient
            int gradientIdx = dataStr.IndexOf("/GradientFill", StringComparison.Ordinal);
            if (gradientIdx < 0)
            {
                gradientIdx = dataStr.IndexOf("/Gradient", StringComparison.Ordinal);
            }
            if (gradientIdx < 0) return;

            int segmentEnd = dataStr.IndexOf(" def", gradientIdx, StringComparison.Ordinal);
            if (segmentEnd < 0)
            {
                segmentEnd = Math.Min(dataStr.Length, gradientIdx + 3000);
            }

            int segmentLen = Math.Max(0, segmentEnd - gradientIdx);
            if (segmentLen <= 0) return;

            string segment = dataStr.Substring(gradientIdx, segmentLen);
            int colorsIdx = segment.IndexOf("/Colors", StringComparison.OrdinalIgnoreCase);
            if (colorsIdx >= 0)
            {
                segment = segment.Substring(colorsIdx);
            }

            MatchCollection valueMatches = Regex.Matches(segment, @"/Values\s*\[([^\]]+)\]");

            List<Color> colors = new List<Color>();
            foreach (Match m in valueMatches)
            {
                if (!m.Success || m.Groups.Count < 2) continue;
                float[] nums = ParseNumbers(m.Groups[1].Value);

                // 文本渐变颜色通常是 [A R G B]，并且都在 [0,1]。
                if (nums.Length >= 4 && IsLikelyColorVector(nums))
                {
                    colors.Add(new Color(
                        Mathf.Clamp01(nums[1]),
                        Mathf.Clamp01(nums[2]),
                        Mathf.Clamp01(nums[3]),
                        Mathf.Clamp01(nums[0])
                    ));
                }
            }

            if (colors.Count >= 2)
            {
                textInfo.hasGradient = true;
                textInfo.gradientTop = colors[0];
                textInfo.gradientBottom = colors[colors.Count - 1];
            }
            else if (colors.Count == 1)
            {
                textInfo.hasGradient = true;
                textInfo.gradientTop = colors[0];
                textInfo.gradientBottom = colors[0];
            }
        }

        private static bool TryExtractValuesAfterToken(string source, string token, out float[] values, int lookahead)
        {
            values = null;
            int tokenIdx = source.IndexOf(token, StringComparison.Ordinal);
            if (tokenIdx < 0) return false;

            int end = Math.Min(source.Length, tokenIdx + Math.Max(lookahead, token.Length + 16));
            string segment = source.Substring(tokenIdx, end - tokenIdx);

            Match m = Regex.Match(segment, @"/Values\s*\[([^\]]+)\]");
            if (!m.Success || m.Groups.Count < 2) return false;

            values = ParseNumbers(m.Groups[1].Value);
            return values != null && values.Length > 0;
        }

        private static bool TryExtractSingleAfterToken(string source, string token, out float value, int lookahead)
        {
            value = 0f;
            int tokenIdx = source.IndexOf(token, StringComparison.Ordinal);
            if (tokenIdx < 0) return false;

            int end = Math.Min(source.Length, tokenIdx + Math.Max(lookahead, token.Length + 16));
            string segment = source.Substring(tokenIdx, end - tokenIdx);
            float[] nums = ParseNumbers(segment);
            if (nums.Length == 0) return false;

            value = nums[0];
            return true;
        }

        private static bool TryExtractBooleanAfterToken(string source, string token, out bool value, int lookahead)
        {
            value = false;
            int tokenIdx = source.IndexOf(token, StringComparison.Ordinal);
            if (tokenIdx < 0) return false;

            int end = Math.Min(source.Length, tokenIdx + Math.Max(lookahead, token.Length + 16));
            string segment = source.Substring(tokenIdx, end - tokenIdx);

            Match m = Regex.Match(segment, @"\b(true|false)\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                value = string.Equals(m.Value, "true", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            float[] nums = ParseNumbers(segment);
            if (nums.Length > 0)
            {
                value = nums[0] != 0f;
                return true;
            }

            return false;
        }

        private static float[] ParseNumbers(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];

            MatchCollection matches = Regex.Matches(s, @"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?");
            if (matches.Count == 0) return new float[0];

            List<float> values = new List<float>(matches.Count);
            foreach (Match m in matches)
            {
                values.Add(ParsePostScriptFloat(m.Value));
            }

            return values.ToArray();
        }

        private static bool IsLikelyColorVector(float[] nums)
        {
            if (nums == null || nums.Length < 4) return false;

            for (int i = 0; i < 4; i++)
            {
                if (nums[i] < 0f || nums[i] > 1f) return false;
            }

            return true;
        }

        private static float ParsePostScriptFloat(string s)
        {
            s = s.Trim();
            if (s.StartsWith(".")) s = "0" + s;
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            return 0f;
        }

        private static double ReadDoubleAt(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0;
            byte[] bytes = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                bytes[i] = data[offset + 7 - i];
            }
            return BitConverter.ToDouble(bytes, 0);
        }

        /// <summary>
        /// 解析样式 Descriptor
        /// </summary>
        private static void ReadStyleDescriptor(BinaryReader reader, PsdTextInfo textInfo)
        {
            ReadDescriptorClass(reader);
            uint itemCount = ReadBigEndianUInt32(reader);

            for (uint i = 0; i < itemCount && i < 50; i++)
            {
                string key = ReadKeyString(reader);
                string type = ReadOSType(reader);

                switch (key)
                {
                    case "FontSize":
                    case "Sz  ":
                        textInfo.fontSize = (int)ReadValue(reader, type);
                        break;

                    case "color":
                    case "Clr ":
                        ParseColor(reader, type, textInfo);
                        break;

                    case "FontName":
                    case "FtNm":
                        textInfo.fontName = ReadUnicodeString(reader);
                        break;

                    case "Bold":
                    case "FntS":
                        textInfo.bold = ReadBool(reader);
                        break;

                    case "Italic":
                    case "Ital":
                        textInfo.italic = ReadBool(reader);
                        break;

                    case "Algn":
                        string alignment = ReadKeyString(reader);
                        textInfo.alignment = ParseAlignment(alignment);
                        break;

                    case "gradientFill":
                    case "Grad":
                        ParseGradient(reader, textInfo);
                        break;

                    default:
                        SkipValue(reader, type);
                        break;
                }
            }
        }

        /// <summary>
        /// 解析颜色
        /// </summary>
        private static void ParseColor(BinaryReader reader, string type, PsdTextInfo textInfo)
        {
            if (type != "Objc")
            {
                SkipValue(reader, type);
                return;
            }

            ReadDescriptorClass(reader);
            uint itemCount = ReadBigEndianUInt32(reader);

            for (uint i = 0; i < itemCount && i < 20; i++)
            {
                string key = ReadKeyString(reader);
                string valType = ReadOSType(reader);

                // PSD 可能使用多种键名表示 RGB
                switch (key)
                {
                    case "Rd  ":
                    case "R   ":
                    case "red ":
                        textInfo.color.r = ReadValue(reader, valType) / 255f;
                        break;
                    case "Grn ":
                    case "G   ":
                    case "green":
                        textInfo.color.g = ReadValue(reader, valType) / 255f;
                        break;
                    case "Bl  ":
                    case "B   ":
                    case "blue":
                        textInfo.color.b = ReadValue(reader, valType) / 255f;
                        break;
                    default:
                        SkipValue(reader, valType);
                        break;
                }
            }

            textInfo.color.a = 1f;
        }

        /// <summary>
        /// 解析渐变填充
        /// </summary>
        private static void ParseGradient(BinaryReader reader, PsdTextInfo textInfo)
        {
            ReadDescriptorClass(reader);
            uint itemCount = ReadBigEndianUInt32(reader);

            Color startColor = Color.white;
            Color endColor = Color.black;

            for (uint i = 0; i < itemCount && i < 20; i++)
            {
                string key = ReadKeyString(reader);
                string type = ReadOSType(reader);

                switch (key)
                {
                    case "Grad":
                        ParseGradientColors(reader, ref startColor, ref endColor);
                        break;
                    case "Type":
                        ReadKeyString(reader);
                        break;
                    default:
                        SkipValue(reader, type);
                        break;
                }
            }

            textInfo.hasGradient = true;
            textInfo.gradientTop = startColor;
            textInfo.gradientBottom = endColor;
        }

        /// <summary>
        /// 解析渐变颜色
        /// </summary>
        private static void ParseGradientColors(BinaryReader reader, ref Color startColor, ref Color endColor)
        {
            ReadDescriptorClass(reader);
            uint itemCount = ReadBigEndianUInt32(reader);

            List<Color> colors = new List<Color>();

            for (uint i = 0; i < itemCount && i < 20; i++)
            {
                string key = ReadKeyString(reader);
                string type = ReadOSType(reader);

                switch (key)
                {
                    case "Clrs":
                        ParseColorList(reader, colors);
                        break;
                    default:
                        SkipValue(reader, type);
                        break;
                }
            }

            if (colors.Count >= 2)
            {
                startColor = colors[0];
                endColor = colors[colors.Count - 1];
            }
            else if (colors.Count == 1)
            {
                startColor = colors[0];
                endColor = colors[0];
            }
        }

        /// <summary>
        /// 解析颜色列表
        /// </summary>
        private static void ParseColorList(BinaryReader reader, List<Color> colors)
        {
            uint listCount = ReadBigEndianUInt32(reader);

            for (uint i = 0; i < listCount && i < 20; i++)
            {
                string itemType = ReadOSType(reader);
                if (itemType == "Objc")
                {
                    Color color = ParseColorObject(reader);
                    colors.Add(color);
                }
                else
                {
                    SkipValue(reader, itemType);
                }
            }
        }

        /// <summary>
        /// 解析颜色对象
        /// </summary>
        private static Color ParseColorObject(BinaryReader reader)
        {
            Color color = Color.black;

            ReadDescriptorClass(reader);
            uint itemCount = ReadBigEndianUInt32(reader);

            for (uint i = 0; i < itemCount && i < 20; i++)
            {
                string key = ReadKeyString(reader);
                string type = ReadOSType(reader);

                switch (key)
                {
                    case "Clr ":
                        ReadDescriptorClass(reader);
                        uint colorItemCount = ReadBigEndianUInt32(reader);
                        for (uint j = 0; j < colorItemCount && j < 20; j++)
                        {
                            string colorKey = ReadKeyString(reader);
                            string colorType = ReadOSType(reader);
                            switch (colorKey)
                            {
                                case "Rd  ": color.r = ReadValue(reader, colorType) / 255f; break;
                                case "Grn ": color.g = ReadValue(reader, colorType) / 255f; break;
                                case "Bl  ": color.b = ReadValue(reader, colorType) / 255f; break;
                                default: SkipValue(reader, colorType); break;
                            }
                        }
                        break;
                    default:
                        SkipValue(reader, type);
                        break;
                }
            }

            return color;
        }

        private static int ParseAlignment(string alignment)
        {
            switch (alignment)
            {
                case "Left": return 0;
                case "Rght": return 1;
                case "Cntr": return 2;
                default: return 0;
            }
        }

        // --- 辅助读取方法 ---

        /// <summary>
        /// 读取 OSType (4 字节 ASCII 字符串)
        /// </summary>
        private static string ReadOSType(BinaryReader reader)
        {
            byte[] bytes = SafeReadBytes(reader, 4);
            if (bytes.Length < 4) return "";
            return Encoding.ASCII.GetString(bytes);
        }

        private static ushort ReadBigEndianUInt16(BinaryReader reader)
        {
            byte[] bytes = SafeReadBytes(reader, 2);
            if (bytes.Length < 2) return 0;
            return (ushort)((bytes[0] << 8) | bytes[1]);
        }

        private static uint ReadBigEndianUInt32(BinaryReader reader)
        {
            byte[] bytes = SafeReadBytes(reader, 4);
            if (bytes.Length < 4) return 0;
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        private static string ReadPascalString(BinaryReader reader)
        {
            byte[] lenBytes = SafeReadBytes(reader, 1);
            if (lenBytes.Length < 1) return "";
            byte length = lenBytes[0];
            if (length == 0) return "";
            byte[] bytes = SafeReadBytes(reader, length);
            if (bytes.Length < length) return "";
            return Encoding.UTF8.GetString(bytes);
        }

        private static string ReadKeyString(BinaryReader reader)
        {
            byte[] lenBytes = SafeReadBytes(reader, 4);
            if (lenBytes.Length < 4) return "";

            uint length = (uint)((lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3]);

            if (length == 0)
            {
                byte[] keyBytes = SafeReadBytes(reader, 4);
                if (keyBytes.Length < 4) return "";
                return Encoding.ASCII.GetString(keyBytes);
            }
            else if (length <= 256)
            {
                byte[] keyBytes = SafeReadBytes(reader, (int)length);
                if (keyBytes.Length < length) return "";
                return Encoding.UTF8.GetString(keyBytes);
            }
            return "";
        }

        private static string ReadUnicodeString(BinaryReader reader)
        {
            uint length = ReadBigEndianUInt32(reader);
            if (length == 0) return "";
            if (length > 10000) length = 10000; // 限制长度

            byte[] bytes = SafeReadBytes(reader, (int)(length * 2));
            if (bytes.Length < length * 2) return "";

            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = (char)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
            }
            return new string(chars);
        }

        private static bool ReadBool(BinaryReader reader)
        {
            byte[] bytes = SafeReadBytes(reader, 1);
            return bytes.Length > 0 && bytes[0] != 0;
        }

        private static void ReadDescriptorClass(BinaryReader reader)
        {
            uint classNameLen = ReadBigEndianUInt32(reader);
            if (classNameLen > 0 && classNameLen < 10000)
            {
                SafeReadBytes(reader, (int)(classNameLen * 2));
            }

            ReadKeyString(reader);
        }

        private static float ReadValue(BinaryReader reader, string type)
        {
            switch (type)
            {
                case "doub":
                    return (float)ReadBigEndianDouble(reader);
                case "Unt ":
                case "UntF":
                    ReadKeyString(reader);
                    return (float)ReadBigEndianDouble(reader);
                case "long":
                    return ReadBigEndianUInt32(reader);
                case "bool":
                    byte[] bytes = SafeReadBytes(reader, 1);
                    return bytes.Length > 0 ? bytes[0] : 0;
                case "enum":
                    ReadKeyString(reader); // Type ID
                    ReadKeyString(reader); // Enum value
                    return 0;
                default:
                    return 0;
            }
        }

        private static void SkipValue(BinaryReader reader, string type)
        {
            try
            {
                switch (type)
                {
                    case "Objc":
                        ReadDescriptorClass(reader);
                        uint itemCount = ReadBigEndianUInt32(reader);
                        for (uint i = 0; i < itemCount && i < 100; i++)
                        {
                            ReadKeyString(reader);
                            string subType = ReadOSType(reader);
                            SkipValue(reader, subType);
                        }
                        break;
                    case "VlLs":
                        uint listCount = ReadBigEndianUInt32(reader);
                        for (uint i = 0; i < listCount && i < 100; i++)
                        {
                            string itemType = ReadOSType(reader);
                            SkipValue(reader, itemType);
                        }
                        break;
                    case "doub":
                        ReadBigEndianDouble(reader);
                        break;
                    case "Unt ":
                    case "UntF":
                        ReadKeyString(reader);
                        ReadBigEndianDouble(reader);
                        break;
                    case "TEXT":
                        ReadUnicodeString(reader);
                        break;
                    case "long":
                        ReadBigEndianUInt32(reader);
                        break;
                    case "bool":
                        SafeReadBytes(reader, 1);
                        break;
                    case "enum":
                        ReadKeyString(reader); // Type ID
                        ReadKeyString(reader); // Enum value
                        break;
                    case "Pnt ":
                        ReadKeyString(reader);
                        ReadBigEndianDouble(reader);
                        ReadKeyString(reader);
                        ReadBigEndianDouble(reader);
                        break;
                    case "type":
                    case "GlbC":
                    case "GlbE":
                    case "GlbO":
                        ReadPascalString(reader);
                        ReadKeyString(reader);
                        break;
                    default:
                        SafeReadBytes(reader, 8);
                        break;
                }
            }
            catch
            {
                // 忽略跳过时的错误
            }
        }

        private static double ReadBigEndianDouble(BinaryReader reader)
        {
            byte[] bytes = SafeReadBytes(reader, 8);
            if (bytes.Length < 8) return 0;
            byte[] reversed = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                reversed[i] = bytes[7 - i];
            }
            return BitConverter.ToDouble(reversed, 0);
        }
    }
}