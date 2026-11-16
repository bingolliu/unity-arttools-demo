using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;

namespace PsdParser
{
    /// <summary>
    /// PSD 图层效果解析器 - 解析描边、投影、渐变等效果
    /// 支持 lfx2 格式 (Photoshop CS6+)
    /// </summary>
    public class PsdEffectParser
    {
        private static int? _globalAngle;

        public static void ParseEffects(byte[] data, PsdLayerInfo layer, string layerName = "")
        {
            if (data == null || data.Length == 0) return;
            _globalAngle = null;

            try
            {
                if (TryParseEffectBlockList(data, layer, layerName))
                {
                }
                else if (data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "8BIM")
                {
                    Parse8BIMEffects(data, 0, layer, layerName);
                }
                else
                {
                    ParseDescriptorEffects(data, 0, layer, layerName);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PsdParser] lfx2解析失败: {e.Message}");
            }
        }

        private static bool TryParseEffectBlockList(byte[] data, PsdLayerInfo layer, string layerName)
        {
            if (data == null || data.Length < 8) return false;

            int version = ReadBigEndianInt32At(data, 0);
            int effectCount = ReadBigEndianInt32At(data, 4);
            if (version < 0 || version > 3 || effectCount <= 0 || effectCount > 64)
            {
                return false;
            }

            int offset = 8;
            bool parsedAny = false;

            for (int i = 0; i < effectCount && offset + 12 <= data.Length; i++)
            {
                string sig = Encoding.ASCII.GetString(data, offset, 4);
                if (sig != "8BIM" && sig != "8B64")
                {
                    return parsedAny;
                }

                offset += 4;
                string effectKey = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;
                uint effectDataLen = ReadBigEndianUInt32At(data, offset);
                offset += 4;

                if (offset + effectDataLen > data.Length)
                {
                    return parsedAny;
                }

                string effectKeyType = effectKey.ToLowerInvariant();
                switch (effectKeyType)
                {
                    case "frfx":
                        layer.stroke = ParseStrokeEffect8BIM(data, offset, effectDataLen, layerName);
                        parsedAny = true;
                        break;
                    case "grfl":
                    case "gdgl":
                    case "grfi":
                        layer.gradient = ParseGradientEffect8BIM(data, offset, effectDataLen, layerName);
                        parsedAny = true;
                        break;
                    case "dsdw":
                    case "drsh":
                        layer.dropShadow = ParseShadowEffect8BIM(data, offset, effectDataLen, layerName);
                        parsedAny = true;
                        break;
                }

                offset += (int)effectDataLen;
            }

            return parsedAny;
        }

        private static void ParseDescriptorEffects(byte[] data, int dataOffset, PsdLayerInfo layer, string layerName)
        {
            ParseDescriptorEffects(data, dataOffset, data.Length, layer, layerName);
        }

        private static void ParseDescriptorEffects(byte[] data, int dataOffset, int dataEnd, PsdLayerInfo layer, string layerName)
        {
            Dictionary<string, string> effectKeyMap = new Dictionary<string, string>
            {
                { "FrFX", "stroke" },
                { "GrFl", "gradient" },
                { "DrSh", "dropShadow" },
                { "frameFX", "stroke" },
                { "gradientFill", "gradient" }
            };

            foreach (var kvp in effectKeyMap)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(kvp.Key);
                int searchFrom = dataOffset;
                while (searchFrom < dataEnd)
                {
                    int foundAt = FindSequenceInData(data, keyBytes, searchFrom, dataEnd);
                    if (foundAt < 0) break;

                    TryParseEffectAt(data, foundAt, kvp.Key, kvp.Value, layer, layerName);
                    searchFrom = foundAt + 1;
                }
            }
        }

        private static void TryParseEffectAt(byte[] data, int keyOffset, string searchKey, string effectKey, PsdLayerInfo layer, string layerName)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(searchKey);
            int candidateStart = keyOffset + keyBytes.Length;
            int dataEnd = data.Length;

            int typeOffset = -1;
            int probeEnd = Math.Min(dataEnd - 4, candidateStart + 20);
            for (int i = candidateStart; i <= probeEnd; i++)
            {
                if (data[i] == (byte)'O' && data[i + 1] == (byte)'b' && data[i + 2] == (byte)'j' && data[i + 3] == (byte)'c')
                {
                    typeOffset = i;
                    break;
                }
            }

            if (typeOffset < 0 || typeOffset + 4 > dataEnd) return;

            string typeStr = Encoding.ASCII.GetString(data, typeOffset, 4);

            if (typeStr == "Objc" || typeStr == "ObjC")
            {
                int parseOffset = typeOffset + 4;
                ParseEffectFromObjc(data, ref parseOffset, dataEnd, effectKey, layer, layerName);
            }
        }

        private static int FindSequenceInData(byte[] data, byte[] pattern, int searchStart, int searchEnd)
        {
            if (searchStart + pattern.Length > searchEnd) return -1;
            for (int i = searchStart; i <= searchEnd - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                    if (data[i + j] != pattern[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        private static void ParseEffectFromObjc(byte[] data, ref int offset, int dataEnd, string effectKey, PsdLayerInfo layer, string layerName)
        {
            bool enabled = false;
            float size = 0, angle = 0, distance = 0, blur = 0, opacity = 100;
            Color color = Color.black;
            Color gradStart = Color.white;
            Color gradEnd = Color.black;
            bool hasGradientColors = false;
            int strokePosition = 0;

            // Objc 值本体：class + itemCount + items（无 version 字段）
            if (!SkipDescriptorClass(data, ref offset, dataEnd))
            {
                return;
            }
            if (offset + 4 > dataEnd)
            {
                return;
            }
            uint itemCnt = ReadBigEndianUInt32At(data, offset);
            offset += 4;
            if (itemCnt == 0 || itemCnt > 100)
            {
                return;
            }

            for (uint i = 0; i < itemCnt && offset < dataEnd - 8; i++)
            {
                if (!TryReadKeyString(data, ref offset, dataEnd, out string key)) break;

                // Type: 4字节ASCII
                if (offset + 4 > dataEnd) break;
                string type = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;

                string k = key.Trim();
                switch (k)
                {
                    case "enab":
                        if (type == "bool") { enabled = data[offset] != 0; offset += 1; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "Md":
                        if (type == "enum")
                        {
                            if (!TryReadEnum(data, ref offset, dataEnd, out _, out _))
                                offset = dataEnd;
                        }
                        else
                        {
                            offset = SkipDescriptorType(data, offset, dataEnd, type);
                        }
                        break;
                    case "Sz  ":
                    case "Sz":
                    case "size":
                        if (type == "UntF") { offset += 4; size = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else if (type == "doub") { size = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "Angl":
                    case "lagl":
                        if (type == "UntF") { offset += 4; angle = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else if (type == "doub") { angle = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "Dstn":
                        if (type == "UntF") { offset += 4; distance = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else if (type == "doub") { distance = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "blur":
                    case "Blr ":
                        if (type == "UntF") { offset += 4; blur = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else if (type == "doub") { blur = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "Opct":
                        if (type == "UntF") { offset += 4; opacity = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else if (type == "doub") { opacity = (float)ReadBigEndianDouble(data, offset); offset += 8; }
                        else offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                    case "Styl":
                        if (type == "enum")
                        {
                            if (TryReadEnum(data, ref offset, dataEnd, out _, out string enumValue))
                            {
                                strokePosition = ParseStrokePosition(enumValue);
                            }
                        }
                        else
                        {
                            offset = SkipDescriptorType(data, offset, dataEnd, type);
                        }
                        break;
                    case "Clr ":
                    case "Clr":
                        color = ParseColorObjc(data, ref offset, dataEnd, type);
                        break;
                    case "Grad":
                        if (effectKey == "gradient" && type == "Objc")
                        {
                            if (TryParseGradientColorsFromObjc(data, ref offset, dataEnd, out gradStart, out gradEnd))
                                hasGradientColors = true;
                        }
                        else
                        {
                            offset = SkipDescriptorType(data, offset, dataEnd, type);
                        }
                        break;
                    default:
                        offset = SkipDescriptorType(data, offset, dataEnd, type);
                        break;
                }
            }

            ApplyEffectToLayer(layer, effectKey, enabled, size, angle, distance, blur, opacity, color, strokePosition, hasGradientColors, gradStart, gradEnd, layerName);
        }

        private static void ApplyEffectToLayer(PsdLayerInfo layer, string effectKey, bool enabled, float size, float angle, float distance, float blur, float opacity, Color color, int strokePosition, bool hasGradientColors, Color gradStart, Color gradEnd, string layerName)
        {
            if (!enabled) return;

            switch (effectKey)
            {
                case "stroke":
                    layer.stroke = new PsdStrokeEffect
                    {
                        enabled = enabled,
                        size = (int)Mathf.RoundToInt(size),
                        position = strokePosition,
                        opacity = (int)(opacity / 100f * 100),
                        color = color
                    };
                    break;
                case "gradient":
                    layer.gradient = new PsdGradientEffect
                    {
                        enabled = enabled,
                        angle = angle,
                        opacity = (int)(opacity / 100f * 100),
                        startColor = hasGradientColors ? gradStart : Color.white,
                        endColor = hasGradientColors ? gradEnd : Color.black
                    };
                    break;
                case "dropShadow":
                    layer.dropShadow = new PsdDropShadowEffect { enabled = enabled, distance = (int)Mathf.RoundToInt(distance), blur = (int)Mathf.RoundToInt(blur), angle = (int)((360 - angle) % 360), opacity = (int)(opacity / 100f * 100), color = color };
                    break;
            }
        }

        private static Color ParseColorObjc(byte[] data, ref int offset, int dataEnd, string ostype)
        {
            Color color = Color.black;
            if (ostype != "Objc") { offset = SkipDescriptorType(data, offset, dataEnd, ostype); return color; }

            if (!SkipDescriptorClass(data, ref offset, dataEnd)) return color;
            if (offset + 4 > dataEnd) return color;

            uint cnt = ReadBigEndianUInt32At(data, offset);
            offset += 4;
            for (uint i = 0; i < cnt && offset < dataEnd - 8; i++)
            {
                if (!TryReadKeyString(data, ref offset, dataEnd, out string k)) break;
                if (offset + 4 > dataEnd) break;
                string t = Encoding.ASCII.GetString(data, offset, 4); offset += 4;

                string kk = k.Trim();
                if ((kk == "Rd" || kk == "Rd  " || kk == "Rd") && t == "doub" && offset + 8 <= dataEnd)
                {
                    color.r = (float)ReadBigEndianDouble(data, offset) / 255f; offset += 8;
                }
                else if ((kk == "Grn" || kk == "Grn ") && t == "doub" && offset + 8 <= dataEnd)
                {
                    color.g = (float)ReadBigEndianDouble(data, offset) / 255f; offset += 8;
                }
                else if ((kk == "Bl" || kk == "Bl  ") && t == "doub" && offset + 8 <= dataEnd)
                {
                    color.b = (float)ReadBigEndianDouble(data, offset) / 255f; offset += 8;
                }
                else offset = SkipDescriptorType(data, offset, dataEnd, t);
            }
            color.a = 1f;
            return color;
        }

        private static bool TryParseGradientColorsFromObjc(byte[] data, ref int offset, int dataEnd, out Color startColor, out Color endColor)
        {
            startColor = Color.white;
            endColor = Color.black;

            if (!SkipDescriptorClass(data, ref offset, dataEnd)) return false;
            if (offset + 4 > dataEnd) return false;

            uint itemCount = ReadBigEndianUInt32At(data, offset);
            offset += 4;

            List<Color> colors = new List<Color>();

            for (uint i = 0; i < itemCount && offset < dataEnd - 8; i++)
            {
                if (!TryReadKeyString(data, ref offset, dataEnd, out string key)) break;
                if (offset + 4 > dataEnd) break;
                string type = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;

                string k = key.Trim();
                if (k == "Clrs" && type == "VlLs")
                {
                    if (offset + 4 > dataEnd) break;
                    uint listCount = ReadBigEndianUInt32At(data, offset);
                    offset += 4;

                    for (uint j = 0; j < listCount && offset < dataEnd - 4; j++)
                    {
                        if (offset + 4 > dataEnd) break;
                        string itemType = Encoding.ASCII.GetString(data, offset, 4);
                        offset += 4;

                        if (itemType == "Objc")
                        {
                            if (TryParseGradientColorStop(data, ref offset, dataEnd, out Color c))
                                colors.Add(c);
                        }
                        else
                        {
                            offset = SkipDescriptorType(data, offset, dataEnd, itemType);
                        }
                    }
                }
                else
                {
                    offset = SkipDescriptorType(data, offset, dataEnd, type);
                }
            }

            if (colors.Count >= 2)
            {
                startColor = colors[0];
                endColor = colors[colors.Count - 1];
                return true;
            }

            if (colors.Count == 1)
            {
                startColor = colors[0];
                endColor = colors[0];
                return true;
            }

            return false;
        }

        private static bool TryParseGradientColorStop(byte[] data, ref int offset, int dataEnd, out Color color)
        {
            color = Color.white;
            bool hasColor = false;

            if (!SkipDescriptorClass(data, ref offset, dataEnd)) return false;
            if (offset + 4 > dataEnd) return false;

            uint itemCount = ReadBigEndianUInt32At(data, offset);
            offset += 4;

            for (uint i = 0; i < itemCount && offset < dataEnd - 8; i++)
            {
                if (!TryReadKeyString(data, ref offset, dataEnd, out string key)) break;
                if (offset + 4 > dataEnd) break;

                string type = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;

                if (key.Trim() == "Clr" || key == "Clr ")
                {
                    color = ParseColorObjc(data, ref offset, dataEnd, type);
                    hasColor = true;
                    continue;
                }

                offset = SkipDescriptorType(data, offset, dataEnd, type);
            }

            return hasColor;
        }

        private static bool TryReadKeyString(byte[] data, ref int offset, int dataEnd, out string key)
        {
            key = string.Empty;
            if (offset + 4 > dataEnd) return false;

            uint length = ReadBigEndianUInt32At(data, offset);
            offset += 4;

            if (length == 0)
            {
                if (offset + 4 > dataEnd) return false;
                key = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;
                return true;
            }

            if (length > 0 && offset + (int)length <= dataEnd)
            {
                key = Encoding.UTF8.GetString(data, offset, (int)length);
                offset += (int)length;
                return true;
            }

            return false;
        }

        private static bool TryReadEnum(byte[] data, ref int offset, int dataEnd, out string enumType, out string enumValue)
        {
            enumType = string.Empty;
            enumValue = string.Empty;

            if (!TryReadKeyString(data, ref offset, dataEnd, out enumType)) return false;
            if (!TryReadKeyString(data, ref offset, dataEnd, out enumValue)) return false;
            return true;
        }

        private static int ParseStrokePosition(string enumValue)
        {
            switch (enumValue)
            {
                case "InsF":
                    return 0;
                case "CtrF":
                    return 1;
                case "OutF":
                    return 2;
                default:
                    return 0;
            }
        }

        private static bool SkipDescriptorClass(byte[] data, ref int offset, int dataEnd)
        {
            if (offset + 4 > dataEnd) return false;

            uint classNameLen = ReadBigEndianUInt32At(data, offset);
            offset += 4;
            int unicodeBytes = (int)classNameLen * 2;
            if (unicodeBytes < 0 || offset + unicodeBytes > dataEnd) return false;
            offset += unicodeBytes;

            return TryReadKeyString(data, ref offset, dataEnd, out _);
        }

        private static int SkipDescriptorType(byte[] data, int offset, int dataEnd, string ostype)
        {
            if (offset > dataEnd) return offset;
            switch (ostype)
            {
                case "bool": return Math.Min(offset + 1, dataEnd);
                case "long": case "comp": return Math.Min(offset + 4, dataEnd);
                case "doub": return Math.Min(offset + 8, dataEnd);
                case "UntF": return Math.Min(offset + 12, dataEnd);
                case "TEXT":
                    if (offset + 4 <= dataEnd) { uint sl = ReadBigEndianUInt32At(data, offset); return Math.Min(offset + 4 + (int)sl * 2, dataEnd); }
                    return Math.Min(offset + 4, dataEnd);
                case "Objc":
                {
                    int cur = offset;
                    if (!SkipDescriptorClass(data, ref cur, dataEnd)) return dataEnd;
                    if (cur + 4 > dataEnd) return dataEnd;
                    uint cnt = ReadBigEndianUInt32At(data, cur);
                    cur += 4;
                    for (uint i = 0; i < cnt && cur < dataEnd - 4; i++)
                    {
                        if (!TryReadKeyString(data, ref cur, dataEnd, out _)) return dataEnd;
                        if (cur + 4 > dataEnd) return dataEnd;
                        string subType = Encoding.ASCII.GetString(data, cur, 4);
                        cur += 4;
                        cur = SkipDescriptorType(data, cur, dataEnd, subType);
                    }
                    return cur;
                }
                case "VlLs": return SkipListBody(data, offset, dataEnd);
                case "enum":
                    if (!TryReadEnum(data, ref offset, dataEnd, out _, out _)) return dataEnd;
                    return offset;
                default: return Math.Min(offset + 16, dataEnd);
            }
        }

        private static int SkipListBody(byte[] data, int offset, int dataEnd)
        {
            if (offset + 4 > dataEnd) return offset;
            uint listCnt = ReadBigEndianUInt32At(data, offset);
            offset += 4;
            for (uint i = 0; i < listCnt && offset < dataEnd - 4; i++)
            {
                if (offset + 4 > dataEnd) break;
                string itemType = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;
                offset = SkipDescriptorType(data, offset, dataEnd, itemType);
            }
            return offset;
        }

        private static int SkipPascalUnicode(byte[] data, int offset, int dataEnd)
        {
            if (offset + 4 > dataEnd) return offset;
            uint strLen = ReadBigEndianUInt32At(data, offset);
            offset += 4;
            if (strLen > 0 && offset + (int)strLen * 2 <= dataEnd) offset += (int)strLen * 2;
            return offset;
        }

        /// <summary>
        /// 解析全局光源角度
        /// </summary>
        public static void ParseGlobalAngle(byte[] data)
        {
            if (data == null || data.Length < 4) return;
            try { _globalAngle = ReadBigEndianInt32At(data, 0); }
            catch { }
        }

        /// <summary>
        /// 解析旧版 8BIM 块格式的 lfx2 数据
        /// </summary>
        private static void Parse8BIMEffects(byte[] data, int dataOffset, PsdLayerInfo layer, string layerName)
        {
            int offset = dataOffset;
            while (offset + 12 <= data.Length)
            {
                string sig = Encoding.ASCII.GetString(data, offset, 4);
                if (sig != "8BIM") { offset++; continue; }

                offset += 4;
                string effectKey = Encoding.ASCII.GetString(data, offset, 4);
                offset += 4;
                uint effectDataLen = ReadBigEndianUInt32At(data, offset);
                offset += 4;

                if (offset + effectDataLen > data.Length) break;

                string effectKeyType = effectKey.ToLowerInvariant();
                switch (effectKeyType)
                {
                    case "frfx":
                        layer.stroke = ParseStrokeEffect8BIM(data, offset, effectDataLen, layerName);
                        break;
                    case "grfl":
                    case "gdgl":
                    case "grfi":
                        layer.gradient = ParseGradientEffect8BIM(data, offset, effectDataLen, layerName);
                        break;
                    case "dsdw":
                    case "drsh":
                        layer.dropShadow = ParseShadowEffect8BIM(data, offset, effectDataLen, layerName);
                        break;
                }

                offset += (int)effectDataLen;
            }
        }

        /// <summary>
        /// 8BIM 格式描边效果解析
        /// </summary>
        private static PsdStrokeEffect ParseStrokeEffect8BIM(byte[] data, int offset, uint dataLen, string layerName)
        {
            var stroke = new PsdStrokeEffect { size = 0, enabled = false };
            int end = offset + (int)dataLen;

            try
            {
                if (offset + 22 > end) return stroke;

                offset += 4; // version
                bool enabled = data[offset] != 0; offset++;
                offset++; // present
                offset += 4; // blendMode

                byte[] colorData = new byte[10];
                Array.Copy(data, offset, colorData, 0, 10);
                offset += 10;

                byte opacity = data[offset]; offset++;
                float size = ReadBigEndianFloatAt(data, offset); offset += 4;
                int position = ReadBigEndianInt32At(data, offset); offset += 4;
                int fillType = ReadBigEndianInt32At(data, offset); offset += 4;

                Color color = Color.black;
                if (colorData[0] == 1 || colorData[0] == 8)
                {
                    color.r = colorData[4] / 255f;
                    color.g = colorData[6] / 255f;
                    color.b = colorData[8] / 255f;
                    color.a = opacity / 255f;
                }

                stroke.enabled = enabled;
                stroke.size = (int)Mathf.RoundToInt(size);
                stroke.position = position;
                stroke.opacity = (int)(opacity / 255f * 100);
                stroke.color = color;

            }
            catch { }

            return stroke;
        }

        private static PsdDropShadowEffect ParseShadowEffect8BIM(byte[] data, int offset, uint dataLen, string layerName)
        {
            var shadow = new PsdDropShadowEffect { enabled = false };
            int end = offset + (int)dataLen;

            try
            {
                if (offset + 22 > end) return shadow;
                offset += 4;
                bool enabled = data[offset] != 0; offset++;
                offset++; offset += 4;
                byte[] colorData = new byte[10];
                Array.Copy(data, offset, colorData, 0, 10);
                offset += 10;
                byte opacity = data[offset]; offset++;

                Color color = new Color(0, 0, 0, 0.5f);
                if (colorData[0] == 1 || colorData[0] == 8)
                {
                    color.r = colorData[4] / 255f;
                    color.g = colorData[6] / 255f;
                    color.b = colorData[8] / 255f;
                    color.a = opacity / 255f;
                }

                float angle = ReadBigEndianFloatAt(data, offset); offset += 4;
                float distance = ReadBigEndianFloatAt(data, offset); offset += 4;
                float blur = ReadBigEndianFloatAt(data, offset); offset += 4;

                shadow.enabled = enabled;
                shadow.color = color;
                shadow.distance = (int)Mathf.RoundToInt(distance);
                shadow.blur = (int)Mathf.RoundToInt(blur);
                shadow.angle = (int)((360 - angle) % 360);
                shadow.opacity = (int)(opacity / 255f * 100);
            }
            catch { }

            return shadow;
        }

        private static PsdGradientEffect ParseGradientEffect8BIM(byte[] data, int offset, uint dataLen, string layerName)
        {
            var grad = new PsdGradientEffect { enabled = false };
            int end = offset + (int)dataLen;

            try
            {
                if (offset + 22 > end) return grad;

                offset += 4; // version
                bool enabled = data[offset] != 0; offset++;
                offset++; // present
                offset += 4; // blendMode
                offset += 10; // color
                byte opacity = data[offset]; offset++;
                offset += 4 + 4; // reverse, dither
                float angle = ReadBigEndianFloatAt(data, offset); offset += 4;
                int type = ReadBigEndianInt32At(data, offset); offset += 4;
                offset += 4; // alignToLayer
                float scale = ReadBigEndianFloatAt(data, offset); offset += 4;
                offset += 8; // offsetX, offsetY

                grad.enabled = enabled;
                grad.angle = angle;
                grad.type = type;
                grad.scale = scale;
                grad.opacity = (int)(opacity / 255f * 100);
                TryExtractGradientColors(data, offset, end, grad);

            }
            catch { }

            return grad;
        }

        private static void TryExtractGradientColors(byte[] data, int offset, int end, PsdGradientEffect grad)
        {
            if (grad == null || data == null || offset >= end) return;

            List<Color> colors = new List<Color>();
            for (int i = offset; i + 10 <= end; i++)
            {
                ushort mode = (ushort)((data[i] << 8) | data[i + 1]);
                if (mode != 1 && mode != 8) continue;

                Color color = new Color(
                    data[i + 4] / 255f,
                    data[i + 6] / 255f,
                    data[i + 8] / 255f,
                    1f
                );

                if (colors.Count == 0 || !Approximately(colors[colors.Count - 1], color))
                {
                    colors.Add(color);
                }

                i += 9;
            }

            if (colors.Count >= 2)
            {
                grad.startColor = colors[0];
                grad.endColor = colors[colors.Count - 1];
            }
            else if (colors.Count == 1)
            {
                grad.startColor = colors[0];
                grad.endColor = colors[0];
            }
        }

        private static bool Approximately(Color lhs, Color rhs)
        {
            return Mathf.Abs(lhs.r - rhs.r) < 0.001f
                && Mathf.Abs(lhs.g - rhs.g) < 0.001f
                && Mathf.Abs(lhs.b - rhs.b) < 0.001f
                && Mathf.Abs(lhs.a - rhs.a) < 0.001f;
        }

        #region 大端序读取

        private static uint ReadBigEndianUInt32At(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static int ReadBigEndianInt32At(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static float ReadBigEndianFloatAt(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0f;
            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++) bytes[i] = data[offset + 3 - i];
            return BitConverter.ToSingle(bytes, 0);
        }

        private static double ReadBigEndianDouble(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0;
            byte[] bytes = new byte[8];
            for (int i = 0; i < 8; i++) bytes[i] = data[offset + 7 - i];
            return BitConverter.ToDouble(bytes, 0);
        }

        #endregion
    }
}