namespace Csg
{
    /// <param name="Width">X 方向宽度</param>
    /// <param name="Height">Y 方向高度</param>
    public record RectangleProfile(double Width, double Height) : Profile2D;

    /// <summary>H型钢截面（工字形），web 为竖直腹板，flange 为水平翼缘。</summary>
    /// <param name="WebHeight">腹板高度（总高减两倍翼缘厚）</param>
    /// <param name="FlangeWidth">翼缘宽度</param>
    /// <param name="WebThickness">腹板厚度</param>
    /// <param name="FlangeThickness">翼缘厚度</param>
    public record HBeamProfile(double WebHeight, double FlangeWidth,
                                double WebThickness, double FlangeThickness) : Profile2D;

    /// <summary>槽钢截面（C形），web 为竖直腹板，flange 为上下水平翼缘。</summary>
    /// <param name="WebHeight">腹板高度（总高减两倍翼缘厚）</param>
    /// <param name="FlangeWidth">翼缘宽度</param>
    /// <param name="WebThickness">腹板厚度</param>
    /// <param name="FlangeThickness">翼缘厚度</param>
    public record ChannelProfile(double WebHeight, double FlangeWidth,
                                  double WebThickness, double FlangeThickness) : Profile2D;

    /// <summary>方管截面（空心矩形）。仅返回外轮廓，空心效果通过 CsgNode 层 SubtractNode 实现。</summary>
    /// <param name="Width">X 方向外宽</param>
    /// <param name="Height">Y 方向外高</param>
    /// <param name="Thickness">壁厚</param>
    public record SquareTubeProfile(double Width, double Height, double Thickness) : Profile2D;

    /// <summary>等腰梯形截面，顶边和底边平行于 X 轴。</summary>
    /// <param name="TopWidth">顶边宽度</param>
    /// <param name="BottomWidth">底边宽度</param>
    /// <param name="Height">Y 方向高度</param>
    public record TrapezoidProfile(double TopWidth, double BottomWidth, double Height) : Profile2D;

    /// <summary>胶囊形截面（矩形 + 两端半圆），长轴沿 X 方向。</summary>
    /// <param name="RectWidth">矩形部分宽度</param>
    /// <param name="Radius">两端半圆半径（也等于半高）</param>
    public record CapsuleProfile(double RectWidth, double Radius) : Profile2D;

    /// <summary>L形截面。竖边沿 Y 轴，水平边沿 X 轴。</summary>
    /// <param name="Vertical">竖边高度（Y 方向）</param>
    /// <param name="Horizontal">水平边宽度（X 方向）</param>
    /// <param name="Thickness">壁厚</param>
    public record LShapeProfile(double Vertical, double Horizontal, double Thickness) : Profile2D;

    /// <summary>参数化截面便捷工厂。</summary>
    public static class Profiles
    {
        public static RectangleProfile Rectangle(double width, double height)
            => new RectangleProfile(width, height);

        public static HBeamProfile HBeam(double webHeight, double flangeWidth,
                                          double webThickness, double flangeThickness)
            => new HBeamProfile(webHeight, flangeWidth, webThickness, flangeThickness);

        public static ChannelProfile Channel(double webHeight, double flangeWidth,
                                              double webThickness, double flangeThickness)
            => new ChannelProfile(webHeight, flangeWidth, webThickness, flangeThickness);

        public static SquareTubeProfile SquareTube(double width, double height, double thickness)
            => new SquareTubeProfile(width, height, thickness);

        public static TrapezoidProfile Trapezoid(double topWidth, double bottomWidth, double height)
            => new TrapezoidProfile(topWidth, bottomWidth, height);

        public static CapsuleProfile Capsule(double rectWidth, double radius)
            => new CapsuleProfile(rectWidth, radius);

        public static LShapeProfile LShape(double vertical, double horizontal, double thickness)
            => new LShapeProfile(vertical, horizontal, thickness);
    }
}
