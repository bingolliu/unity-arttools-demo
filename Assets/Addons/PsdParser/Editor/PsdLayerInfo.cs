using UnityEngine;
using System.Collections.Generic;

namespace PsdParser
{
    /// <summary>
    /// PSD 图层类型枚举
    /// </summary>
    public enum PsdLayerType
    {
        Group,      // 图层组
        Image,      // 普通图层/图片
        Text,       // 文本图层
        Shape,      // 形状图层
        Unknown     // 其他类型
    }

    /// <summary>
    /// PSD 图层信息（替代原 JSON LayerData）
    /// </summary>
    public class PsdLayerInfo
    {
        public string name;
        public PsdLayerType type;
        public bool visible;
        public Rect bounds;         // PSD 坐标系 (top, left, bottom, right → x, y, width, height)
        public int depth;           // 图层深度（从底层到顶层）
        public float opacity;       // 透明度 0-1
        public string blendMode;    // 混合模式

        // 子图层（组）
        public List<PsdLayerInfo> children;

        // 文本信息
        public PsdTextInfo textInfo;

        // 效果信息
        public PsdStrokeEffect stroke;
        public PsdDropShadowEffect dropShadow;
        public PsdGradientEffect gradient;

        // --- 用户配置的资源分配 ---
        public ImageSourceType imageSource = ImageSourceType.Texture;
        public string spriteName;
        public Texture assignedTexture;
        public Sprite assignedSprite;

        /// <summary>
        /// 获取图层中心点（PSD 坐标系）
        /// bounds: x=left, y=bottom, width, height（PSD坐标）
        /// </summary>
        public Vector2 GetCenter()
        {
            return new Vector2(bounds.x + bounds.width / 2, bounds.y - bounds.height / 2);
        }

        /// <summary>
        /// 转换为 uGUI 坐标（画布中心对齐到 (0,0)）
        /// PSD 原点(0,0)在左上角，uGUI 以中心为原点、Y 轴向上。
        /// </summary>
        public Vector3 GetCanvasPosition(float psdHeight, float psdWidth)
        {
            // PSD 图层中心点（PSD 坐标系，Y 向下）
            float centerXPsd = bounds.x + bounds.width / 2;
            float centerYPsd = bounds.y - bounds.height / 2; // bounds.y 是 bottom，中心点要减去 height/2

            // 将 PSD 画布中心映射到 uGUI(0,0)
            float canvasX = centerXPsd - psdWidth / 2;
            float canvasY = psdHeight / 2 - centerYPsd;

            return new Vector3(canvasX, canvasY, 0);
        }
    }

    /// <summary>
    /// PSD 文本信息
    /// </summary>
    public class PsdTextInfo
    {
        public string text;
        public string fontName;
        public int fontSize = 24;
        public Color color = Color.black;
        public bool bold;
        public bool italic;
        public int alignment = 0; // 0=Left, 1=Right, 2=Center
        public float lineHeight;
        public float tracking;    // 字间距

        // 渐变颜色（如果有）
        public bool hasGradient;
        public Color gradientTop;
        public Color gradientBottom;

        // 描边效果（从 EngineData 解析）
        public bool hasStroke;
        public Color strokeColor = Color.black;
        public int strokeWidth = 1;

        // 投影效果（从 EngineData 解析）
        public bool hasDropShadow;
        public Color shadowColor = new Color(0, 0, 0, 0.5f);
        public int shadowDistance = 5;
        public int shadowBlur = 5;
        public int shadowAngle = 45;
    }

    /// <summary>
    /// PSD 描边效果
    /// </summary>
    public class PsdStrokeEffect
    {
        public bool enabled;
        public Color color = Color.black;
        public int size;               // 默认 0，没有描边
        public int position = 0;      // 0=内部, 1=外部
        public int opacity = 100;     // 透明度百分比
    }

    /// <summary>
    /// PSD 投影效果
    /// </summary>
    public class PsdDropShadowEffect
    {
        public bool enabled;
        public Color color = new Color(0, 0, 0, 0.5f);
        public int distance = 5;      // 像素
        public int blur = 5;          // 像素
        public int angle = 45;        // 角度
        public int opacity = 50;      // 透明度百分比
    }

    /// <summary>
    /// PSD 渐变效果
    /// </summary>
    public class PsdGradientEffect
    {
        public bool enabled;
        public Color startColor = Color.white;
        public Color endColor = Color.black;
        public float angle = 0;       // 角度
        public int type = 0;          // 0=线性 1=径向 2=角度 3=对称 4=菱形
        public float scale = 100f;    // 缩放百分比
        public int opacity = 100;
    }

    /// <summary>
    /// 图片来源类型
    /// </summary>
    public enum ImageSourceType
    {
        Sprite,   // uGUI Image
        Texture   // uGUI RawImage
    }

    /// <summary>
    /// PSD 文档信息
    /// </summary>
    public class PsdDocument
    {
        public int width;
        public int height;
        public int channels;
        public int depth;            // 每通道位数 (8/16/32)
        public int colorMode;        // 颜色模式
        public List<PsdLayerInfo> layers;

        public PsdDocument()
        {
            layers = new List<PsdLayerInfo>();
        }
    }

    /// <summary>
    /// PSD 文件头信息
    /// </summary>
    public class PsdHeader
    {
        public string signature;     // "8BPS"
        public int version;          // 1=PSD, 2=PSB
        public int channels;
        public int height;
        public int width;
        public int depth;            // 每通道位数
        public int colorMode;        // 0=Bitmap, 1=Grayscale, 2=Indexed, 3=RGB, 4=CMYK, 7=Multichannel, 8=Duotone, 9=Lab
    }
}