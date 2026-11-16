using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace PsdParser
{
    public class PsdFileReader
    {
        private BinaryReader _reader;
        private PsdHeader _header;
        private long _fileLength;

        private const int SECTION_OTHER = 0;          // 普通图层
        private const int SECTION_GROUP_OPEN = 1;   // 打开的文件夹（组开始）
        private const int SECTION_GROUP_CLOSED = 2; // 关闭的文件夹（组开始，折叠显示）
        private const int SECTION_GROUP_END = 3;    // 分隔符结束（组结束标记）

        public PsdDocument Read(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"PSD 文件不存在: {path}");
                return null;
            }

            using (FileStream fs = File.OpenRead(path))
            {
                _fileLength = fs.Length;
                _reader = new BinaryReader(fs, Encoding.Default);

                try
                {
                    return ParseDocument();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"PSD 解析异常: {e.Message}\n{e.StackTrace}");
                    return null;
                }
            }
        }

        private PsdDocument ParseDocument()
        {
            PsdDocument doc = new PsdDocument();

            // 解析文件头
            _header = ParseHeader();
            if (_header == null || _header.signature != "8BPS")
            {
                Debug.LogError("无效的 PSD 文件");
                return null;
            }

            doc.width = _header.width;
            doc.height = _header.height;
            doc.channels = _header.channels;
            doc.depth = _header.depth;
            doc.colorMode = _header.colorMode;

            // 跳过颜色模式数据
            SkipSection();

            // 跳过图像资源
            SkipSection();

            // 解析图层和蒙版信息
            doc.layers = ParseLayerAndMaskInfo();

            return doc;
        }

        private PsdHeader ParseHeader()
        {
            byte[] sigBytes = SafeReadBytes(4);
            if (sigBytes == null) return null;

            PsdHeader h = new PsdHeader();
            h.signature = Encoding.ASCII.GetString(sigBytes);
            h.version = ReadUInt16BE();
            SafeReadBytes(6); // 保留
            h.channels = ReadUInt16BE();
            h.height = ReadInt32BE();
            h.width = ReadInt32BE();
            h.depth = ReadUInt16BE();
            h.colorMode = ReadUInt16BE();
            return h;
        }

        private void SkipSection()
        {
            uint len = ReadUInt32BE();
            if (len > 0) SafeReadBytes((int)len);
        }

        private List<PsdLayerInfo> ParseLayerAndMaskInfo()
        {
            uint sectionLen = ReadUInt32BE();
            if (sectionLen == 0) return new List<PsdLayerInfo>();

            long startPos = _reader.BaseStream.Position;
            long endPos = startPos + sectionLen;

            // 图层信息长度
            uint layerInfoLen = ReadUInt32BE();
            if (layerInfoLen == 0)
            {
                _reader.BaseStream.Position = endPos;
                return new List<PsdLayerInfo>();
            }

            long layerInfoEnd = _reader.BaseStream.Position + layerInfoLen;

            // 图层计数
            short layerCount = ReadInt16BE();
            if (layerCount < 0) layerCount = (short)(-layerCount);

            // 解析所有图层记录
            List<PsdLayerRecord> records = new List<PsdLayerRecord>();
            for (int i = 0; i < layerCount; i++)
            {
                var record = ParseLayerRecord(i);
                records.Add(record);
            }

            _reader.BaseStream.Position = layerInfoEnd;

            // 跳过通道图像数据
            foreach (var r in records)
            {
                foreach (var ch in r.channels)
                {
                    if (ch.dataLength > 0)
                        SafeReadBytes((int)ch.dataLength);
                }
            }

            _reader.BaseStream.Position = endPos;

            // 构建图层树
            return BuildLayerTree(records);
        }

        private PsdLayerRecord ParseLayerRecord(int index)
        {
            PsdLayerRecord r = new PsdLayerRecord();

            // 边界
            r.top = ReadInt32BE();
            r.left = ReadInt32BE();
            r.bottom = ReadInt32BE();
            r.right = ReadInt32BE();
            r.width = r.right - r.left;
            r.height = r.bottom - r.top;

            // 通道
            short channelCount = ReadInt16BE();
            r.channels = new List<ChannelInfo>();
            for (int i = 0; i < channelCount && i < 56; i++)
            {
                ChannelInfo ch = new ChannelInfo();
                ch.id = ReadInt16BE();
                ch.dataLength = ReadUInt32BE();
                r.channels.Add(ch);
            }

            // 混合模式签名 + 键
            byte[] blendSig = SafeReadBytes(4);
            byte[] blendKey = SafeReadBytes(4);
            r.blendMode = blendKey != null ? Encoding.ASCII.GetString(blendKey) : "norm";

            // 透明度、调裁、标志、填充
            r.opacity = _reader.ReadByte();
            _reader.ReadByte(); // clipping
            byte flags = _reader.ReadByte();
            // PSD flags: bit 2 (0x02) = 不可见标志，1=隐藏，0=可见
            r.visible = (flags & 0x02) == 0;
            r.flags = flags; // 保存 flags 用于调试
            _reader.ReadByte(); // filler

            // 额外数据
            uint extraLen = ReadUInt32BE();
            long extraStart = _reader.BaseStream.Position;
            long extraEnd = extraStart + extraLen;

            // 解析额外数据头部
            // 结构: [混合范围长度 4字节] + [混合范围数据] + [Pascal 图层名称] + [8BIM 标签]

            // PSD 规范：只有一个混合范围长度字段
            uint blendRangeLen = ReadUInt32BE();
            if (blendRangeLen > 0)
            {
                SafeReadBytes((int)blendRangeLen);
            }

            // 读取 Pascal 字符串图层名称
            byte nameLen = _reader.ReadByte();
            if (nameLen > 0)
            {
                byte[] nameBytes = SafeReadBytes(nameLen);
                if (nameBytes != null)
                {
                    r.layerName = Encoding.UTF8.GetString(nameBytes);
                    // Debug.Log($"[PSD] 图层{index} 名称(pascal): {r.layerName}");
                }
                // 填充到偶数长度
                int pad = (nameLen + 1) % 2;
                if (pad != 0) _reader.ReadByte();
            }
            else
            {
                // 空名称，跳过填充字节
                _reader.ReadByte();
            }

            // 搜索第一个 8BIM 标签位置（跳过可能的额外数据）
            while (_reader.BaseStream.Position < extraEnd - 12)
            {
                long checkPos = _reader.BaseStream.Position;
                byte[] sigCheck = SafeReadBytes(4);
                if (sigCheck == null) break;

                string sigStr = Encoding.ASCII.GetString(sigCheck);
                if (sigStr == "8BIM" || sigStr == "8B64")
                {
                    // 找到了 8BIM，回到这个位置开始解析
                    _reader.BaseStream.Position = checkPos;
                    break;
                }

                // 不是 8BIM，继续搜索
                _reader.BaseStream.Position = checkPos + 1;
            }

            // 解析附加信息块 (8BIM 标签)
            while (_reader.BaseStream.Position < extraEnd - 12)
            {
                byte[] sig = SafeReadBytes(4);
                if (sig == null) break;

                string sigStr = Encoding.ASCII.GetString(sig);
                if (sigStr != "8BIM" && sigStr != "8B64")
                {
                    // 不是有效标签，回退并退出
                    _reader.BaseStream.Position -= 4;
                    break;
                }

                byte[] keyBytes = SafeReadBytes(4);
                if (keyBytes == null) break;
                string key = Encoding.ASCII.GetString(keyBytes);

                uint dataLen = ReadUInt32BE();
                long dataEnd = _reader.BaseStream.Position + dataLen;

                // Debug.Log($"[PSD] 图层{index} '{r.layerName}' 标签: {key}, 长度: {dataLen}");

                switch (key)
                {
                    case "lyid": // 图层ID
                    case "lspf": // 保护设置
                    case "lclr": // 颜色
                        break;

                    case "lsct": // 图层组分隔符
                    case "lsdk":
                        if (dataLen >= 4)
                        {
                            r.sectionType = ReadInt32BE();
                        }
                        break;

                    case "luni": // Unicode 图层名称 ★重要★
                        r.layerName = ReadUnicodeString((int)dataLen);
                        break;

                    case "lnsr": // 资源名称
                        break;

                    case "clbl": // 混合剪切
                    case "infx": // 混合内部
                    case "knko": // 透明形状
                    case "lmgm": // 图层蒙版
                    case "vmgm": // 矢量蒙版
                        break;

                    case "TySh": // 文本图层
                        r.hasText = true;
                        r.textData = SafeReadBytes((int)dataLen);
                        break;

                    case "lrFX": // 效果 (旧版本)
                        if (r.effectData == null) // 只有没有 lfx2 时才用 lrFX
                            r.effectData = SafeReadBytes((int)dataLen);
                        break;
                    case "lfx2": // 图层效果 (新版本，包含描边/阴影/渐变等)
                        r.effectData = SafeReadBytes((int)dataLen); // lfx2 优先，覆盖 lrFX
                        break;

                    default:
                        // 跳过未知标签
                        break;
                }

                _reader.BaseStream.Position = dataEnd;
            }

            _reader.BaseStream.Position = extraEnd;

            // 如果没有名称，使用默认
            if (string.IsNullOrEmpty(r.layerName))
            {
                r.layerName = $"Layer_{index}";
            }

            return r;
        }

        private string ReadUnicodeString(int totalLen)
        {
            if (totalLen < 4) return "";

            // 4字节字符数
            uint charCount = ReadUInt32BE();
            if (charCount == 0 || charCount > 1000) return "";

            // 读取 UTF-16BE 字符
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < charCount; i++)
            {
                byte[] charBytes = SafeReadBytes(2);
                if (charBytes == null) break;
                char c = (char)((charBytes[0] << 8) | charBytes[1]);
                if (c != 0) sb.Append(c);
            }

            return sb.ToString().TrimEnd('\0');
        }

        private List<PsdLayerInfo> BuildLayerTree(List<PsdLayerRecord> records)
        {
            List<PsdLayerInfo> result = new List<PsdLayerInfo>();

            if (records == null || records.Count == 0)
                return result;

            // PSD 图层记录是从顶层到底层排列的
            // 但组结构是：先遇到组内图层，再遇到组结束标记，最后遇到组开始标记
            // 所以需要反向遍历，才能正确构建树结构
            Stack<PsdLayerInfo> groupStack = new Stack<PsdLayerInfo>();
            Stack<bool> groupVisibilityStack = new Stack<bool>();

            // 反向遍历图层记录（从底层到顶层）
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                var layer = CreateLayerInfo(record);

                // 1=打开的文件夹, 2=关闭的文件夹, 都是组开始
                if (record.sectionType == SECTION_GROUP_OPEN || record.sectionType == SECTION_GROUP_CLOSED)
                {
                    // 组开始（反向遍历时先遇到组开始）
                    layer.type = PsdLayerType.Group;
                    layer.children = new List<PsdLayerInfo>();

                    if (groupStack.Count > 0)
                        groupStack.Peek().children.Add(layer);
                    else
                        result.Add(layer);

                    groupStack.Push(layer);
                    groupVisibilityStack.Push(layer.visible);
                }
                else if (record.sectionType == SECTION_GROUP_END)
                {
                    // 组结束（反向遍历时最后遇到组结束，弹出栈）
                    if (groupStack.Count > 0)
                    {
                        groupVisibilityStack.Pop();
                        groupStack.Pop();
                    }
                }
                else
                {
                    // 普通图层 (sectionType=0)
                    bool parentGroupVisible = true;
                    if (groupVisibilityStack.Count > 0)
                    {
                        var visibilityArray = groupVisibilityStack.ToArray();
                        for (int j = 0; j < visibilityArray.Length; j++)
                        {
                            if (!visibilityArray[j])
                            {
                                parentGroupVisible = false;
                                break;
                            }
                        }
                    }

                    layer.visible = layer.visible && parentGroupVisible;

                    if (groupStack.Count > 0)
                        groupStack.Peek().children.Add(layer);
                    else
                        result.Add(layer);
                }
            }

            // 由于反向遍历后添加，result 和 children 顺序也是反的，需要反转
            result.Reverse();
            foreach (var layer in result)
            {
                ReverseChildren(layer);
                RecalculateGroupBounds(layer);
            }

            return result;
        }

        private void ReverseChildren(PsdLayerInfo layer)
        {
            if (layer.children != null && layer.children.Count > 0)
            {
                layer.children.Reverse();
                foreach (var child in layer.children)
                {
                    ReverseChildren(child);
                }
            }
        }

        private void RecalculateGroupBounds(PsdLayerInfo layer)
        {
            if (layer == null || layer.children == null || layer.children.Count == 0)
                return;

            for (int i = 0; i < layer.children.Count; i++)
            {
                RecalculateGroupBounds(layer.children[i]);
            }

            if (layer.type != PsdLayerType.Group)
                return;

            bool hasValidChild = false;
            float left = 0f;
            float right = 0f;
            float top = 0f;
            float bottom = 0f;

            for (int i = 0; i < layer.children.Count; i++)
            {
                Rect child = layer.children[i].bounds;
                if (child.width <= 0f || child.height <= 0f)
                    continue;

                float childLeft = child.x;
                float childRight = child.x + child.width;
                float childBottom = child.y;
                float childTop = child.y - child.height;

                if (!hasValidChild)
                {
                    left = childLeft;
                    right = childRight;
                    top = childTop;
                    bottom = childBottom;
                    hasValidChild = true;
                }
                else
                {
                    left = Mathf.Min(left, childLeft);
                    right = Mathf.Max(right, childRight);
                    top = Mathf.Min(top, childTop);
                    bottom = Mathf.Max(bottom, childBottom);
                }
            }

            if (!hasValidChild)
                return;

            layer.bounds = new Rect(left, bottom, right - left, bottom - top);
        }

        private PsdLayerInfo CreateLayerInfo(PsdLayerRecord r)
        {
            PsdLayerInfo layer = new PsdLayerInfo();
            layer.name = r.layerName;
            layer.type = r.hasText ? PsdLayerType.Text : PsdLayerType.Image;
            // Unity Rect: x=left, y=bottom, width, height
            // PSD 坐标系 Y 向下，所以 bottom > top，bottom 是图层的底部边缘
            layer.bounds = new Rect(r.left, r.bottom, r.width, r.height);
            layer.visible = r.visible;
            layer.opacity = r.opacity / 255f;
            layer.blendMode = r.blendMode;

            if (r.hasText && r.textData != null)
            {
                try
                {
                    layer.textInfo = PsdTextParser.Parse(r.textData, r.layerName);
                }
                catch { }
            }

            if (r.effectData != null)
            {
                try
                {
                    PsdEffectParser.ParseEffects(r.effectData, layer, r.layerName);
                }
                catch { }
            }

            return layer;
        }

        // --- 读取辅助方法 ---
        private byte[] SafeReadBytes(int count)
        {
            if (count <= 0) return null;
            if (_reader.BaseStream.Position + count > _fileLength)
            {
                count = (int)(_fileLength - _reader.BaseStream.Position);
                if (count <= 0) return null;
            }
            return _reader.ReadBytes(count);
        }

        private ushort ReadUInt16BE()
        {
            byte[] b = SafeReadBytes(2);
            return b != null ? (ushort)((b[0] << 8) | b[1]) : (ushort)0;
        }

        private short ReadInt16BE()
        {
            byte[] b = SafeReadBytes(2);
            return b != null ? (short)((b[0] << 8) | b[1]) : (short)0;
        }

        private int ReadInt32BE()
        {
            byte[] b = SafeReadBytes(4);
            return b != null ? (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3] : 0;
        }

        private uint ReadUInt32BE()
        {
            byte[] b = SafeReadBytes(4);
            return b != null ? (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]) : 0;
        }
    }

    // 内部类
    internal class PsdLayerRecord
    {
        public int top, left, bottom, right, width, height;
        public List<ChannelInfo> channels;
        public string blendMode;
        public byte opacity;
        public byte flags;      // 图层标志（用于调试）
        public bool visible;
        public string layerName;
        public int sectionType; // 0=普通, 1=组开始, 2=组结束
        public bool hasText;
        public byte[] textData;
        public byte[] effectData;
    }

    internal class ChannelInfo
    {
        public short id;
        public uint dataLength;
    }
}