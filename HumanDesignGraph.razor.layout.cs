using System.Globalization;
using SharpAstrology.Enums;

namespace SharpAstrology.HumanDesign.BlazorComponents;

/// <summary>
/// Draws the body graph of a Human Design chart as an SVG. Centers, channels and gates each
/// report their clicks. This part of the class holds the geometry. All of its tables are
/// static, so they are built once per process and not on every render.
/// </summary>
public partial class HumanDesignGraph
{
    /// <summary>
    /// Formats a coordinate for the markup. Rounding keeps the binary representation of a
    /// computed value out of the output. The invariant culture keeps the decimal separator
    /// a point in every culture, because a comma would produce invalid SVG.
    /// </summary>
    private static string _text(double value) => Math.Round(value, 5).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A point in the coordinate system it belongs to. <see cref="X"/> and <see cref="Y"/>
    /// carry the value, <see cref="XText"/> and <see cref="YText"/> the string that goes into
    /// the markup. The strings are built once, together with the tables below.
    /// </summary>
    internal readonly struct Point
    {
        internal Point(double x, double y)
        {
            X = x;
            Y = y;
            XText = _text(x);
            YText = _text(y);
        }

        internal double X { get; }
        internal double Y { get; }
        internal string XText { get; }
        internal string YText { get; }

        /// <summary>
        /// The point halfway between two points.
        /// </summary>
        internal static Point Between(Point from, Point to)
            => new(from.X + (to.X - from.X) / 2, from.Y + (to.Y - from.Y) / 2);

        /// <summary>
        /// The distance to another point.
        /// </summary>
        internal double DistanceTo(Point other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// A width and a height, again with the strings for the markup alongside.
    /// </summary>
    internal readonly struct Extent
    {
        internal Extent(double width, double height)
        {
            Width = width;
            Height = height;
            WidthText = _text(width);
            HeightText = _text(height);
        }

        internal double Width { get; }
        internal double Height { get; }
        internal string WidthText { get; }
        internal string HeightText { get; }
    }

    /// <summary>
    /// A channel that is drawn as a straight line. The two halves are drawn separately,
    /// because each half takes the activation of the gate it starts at.
    /// </summary>
    internal sealed class StraightChannel
    {
        internal StraightChannel(Channels channel, double x1, double y1, double x2, double y2)
        {
            Channel = channel;
            (Gate1, Gate2) = channel.ToGates();
            From = new Point(x1, y1);
            To = new Point(x2, y2);
            Middle = Point.Between(From, To);
        }

        internal Channels Channel { get; }

        /// <summary>The gate at <see cref="From"/>. It gives the first half its colour.</summary>
        internal Gates Gate1 { get; }

        /// <summary>The gate at <see cref="To"/>. It gives the second half its colour.</summary>
        internal Gates Gate2 { get; }

        internal Point From { get; }
        internal Point To { get; }
        internal Point Middle { get; }
    }

    /// <summary>
    /// A channel that is drawn as a curved path. Only the five integration channels are curved.
    /// The path strings are built once, because a cubic curve has too many coordinates to
    /// assemble them on every render.
    /// </summary>
    internal sealed class CurvedChannel
    {
        internal CurvedChannel(Channels channel,
            double x1, double y1, double cx1, double cy1,
            double mx, double my,
            double x2, double y2, double cx2, double cy2)
        {
            Channel = channel;
            (Gate1, Gate2) = channel.ToGates();
            From = new Point(x1, y1);
            To = new Point(x2, y2);
            Middle = new Point(mx, my);

            var start = $"{From.XText} {From.YText}";
            var middle = $"{Middle.XText} {Middle.YText}";
            var end = $"{To.XText} {To.YText}";
            var firstControl = $"{_text(cx1)} {_text(cy1)}";
            var secondControl = $"{_text(cx2)} {_text(cy2)}";

            FirstHalf = $"M {start} C {firstControl} {middle} {middle}";
            SecondHalf = $"M {middle} C {middle} {secondControl} {end}";
            Whole = $"{FirstHalf} C {middle} {secondControl} {end}";
        }

        internal Channels Channel { get; }

        /// <summary>The gate at <see cref="From"/>. It gives <see cref="FirstHalf"/> its colour.</summary>
        internal Gates Gate1 { get; }

        /// <summary>The gate at <see cref="To"/>. It gives <see cref="SecondHalf"/> its colour.</summary>
        internal Gates Gate2 { get; }

        internal Point From { get; }
        internal Point To { get; }
        internal Point Middle { get; }

        /// <summary>The path from the first gate to the middle of the channel.</summary>
        internal string FirstHalf { get; }

        /// <summary>The path from the middle of the channel to the second gate.</summary>
        internal string SecondHalf { get; }

        /// <summary>Both halves in one path. It catches the clicks.</summary>
        internal string Whole { get; }
    }

    /// <summary>
    /// The place of one gate inside its center. The position is the upper left corner of the
    /// box the gate is drawn in, in the coordinates of the center.
    /// </summary>
    internal sealed class GateLayout
    {
        internal GateLayout(Gates gate, double x, double y)
        {
            Gate = gate;
            Number = gate.ToNumber();
            NumberText = Number.ToString(CultureInfo.InvariantCulture);
            Position = new Point(x, y);
        }

        internal Gates Gate { get; }
        internal int Number { get; }
        internal string NumberText { get; }
        internal Point Position { get; }
    }

    /// <summary>
    /// One center with its outline and its gates. A center is a nested viewport, so its gates
    /// carry their own coordinates. <see cref="CenterOfGate"/> maps them back into the
    /// coordinates of the chart.
    /// </summary>
    internal sealed class CenterLayout
    {
        /// <summary>
        /// The corners of a rounded rectangle. The three centers that are rectangles all fill
        /// their whole viewport, so they need no outline of their own.
        /// </summary>
        private static readonly (double X, double Y)[] _unitSquare = [(0, 0), (1, 0), (1, 1), (0, 1)];

        internal CenterLayout(Centers center,
            double x, double y, double width, double height, double viewBoxHeight,
            (double X, double Y)[]? outline, double gateWidth, double gateHeight,
            bool gatesReachOutside, GateLayout[] gates)
        {
            Center = center;
            Name = center.ToString();
            Position = new Point(x, y);
            Size = new Extent(width, height);
            ViewBox = new Extent(1, viewBoxHeight);
            ViewBoxText = $"0 0 {ViewBox.WidthText} {ViewBox.HeightText}";
            OutlinePoints = [..(outline ?? _unitSquare).Select(corner => new Point(corner.X, corner.Y))];
            Outline = outline is null
                ? null
                : string.Join(" ", OutlinePoints.Select(corner => $"{corner.XText},{corner.YText}"));
            GateSize = new Extent(gateWidth, gateHeight);
            Style = gatesReachOutside ? "overflow: visible" : null;
            Gates = gates;
        }

        internal Centers Center { get; }
        internal string Name { get; }

        /// <summary>Upper left corner of the center, in the coordinates of the chart.</summary>
        internal Point Position { get; }

        /// <summary>Size of the center, in the coordinates of the chart.</summary>
        internal Extent Size { get; }

        /// <summary>Size of the coordinate system inside the center.</summary>
        internal Extent ViewBox { get; }

        internal string ViewBoxText { get; }

        /// <summary>
        /// The points of the polygon that outlines the center,
        /// or <see langword="null"/> for the three centers that are rounded rectangles.
        /// </summary>
        internal string? Outline { get; }

        /// <summary>
        /// The corners of the outline, in the coordinates of the center. A rounded rectangle
        /// reports the corners of its viewport and ignores the rounding.
        /// </summary>
        internal Point[] OutlinePoints { get; }

        /// <summary>Size of the box of a gate, in the coordinates of the center.</summary>
        internal Extent GateSize { get; }

        /// <summary>
        /// Inline style of the center, or <see langword="null"/> if it needs none.
        /// A nested svg clips its content, so a center whose gates reach beyond its own
        /// viewport has to allow the overflow. The style has to be a style, not an attribute,
        /// because the browser sets <c>overflow: hidden</c> through its own style sheet.
        /// </summary>
        internal string? Style { get; }

        internal GateLayout[] Gates { get; }

        /// <summary>
        /// Maps a point from the coordinates of the center into the coordinates of the chart.
        /// </summary>
        internal Point ToChart(Point point)
            => new(Position.X + point.X * _scaleX, Position.Y + point.Y * _scaleY);

        /// <summary>
        /// The centre of the circle of a gate, in the coordinates of the chart.
        /// </summary>
        internal Point CenterOfGate(GateLayout gate) => ToChart(
            new Point(gate.Position.X + GateSize.Width / 2, gate.Position.Y + GateSize.Height / 2));

        /// <summary>
        /// The radius of the circle of a gate, in the coordinates of the chart. A gate keeps its
        /// aspect ratio inside its box, so the smaller of both sides decides the diameter.
        /// </summary>
        internal double GateRadius
            => Math.Min(GateSize.Width * _scaleX, GateSize.Height * _scaleY) / 2;

        /// <summary>
        /// Whether the viewport of the center would cut a piece off the circle of a gate.
        /// Only the square in the middle of the box of a gate is drawn, because a gate keeps
        /// its aspect ratio, so only that square has to fit into the viewport.
        /// This is what <see cref="Style"/> answers.
        /// </summary>
        internal bool Clips(GateLayout gate)
        {
            var side = Math.Min(GateSize.Width, GateSize.Height);
            var left = gate.Position.X + (GateSize.Width - side) / 2;
            var top = gate.Position.Y + (GateSize.Height - side) / 2;

            return left < 0 || top < 0 || left + side > ViewBox.Width || top + side > ViewBox.Height;
        }

        private double _scaleX => Size.Width / ViewBox.Width;
        private double _scaleY => Size.Height / ViewBox.Height;
    }

    /// <summary>
    /// The five integration channels. They are the only channels that are drawn as curves,
    /// because they run around the Self center instead of through it.
    /// </summary>
    internal static readonly CurvedChannel[] CurvedChannels =
    [
        new(Channels.Key34Key57, 0.42, 1.07, 0.4, 1, 0.32, 0.93, 0.075, 0.97, 0.2, 0.8),
        new(Channels.Key10Key20, 0.42, 0.8, 0.36, 0.75, 0.37, 0.7, 0.41, 0.57, 0.4, 0.55),
        new(Channels.Key10Key57, 0.42, 0.8, 0.34, 0.8, 0.245, 0.845, 0.08, 0.96, 0.13, 0.9),
        new(Channels.Key10Key34, 0.42, 0.8, 0.32, 0.9, 0.322, 0.94, 0.42, 1.08, 0.32, 1.08),
        new(Channels.Key20Key34, 0.42, 0.56, 0.32, 0.66, 0.32, 0.8, 0.42, 1.07, 0.32, 1)
    ];

    /// <summary>
    /// The remaining thirty one channels. Both end points sit under the circle of their gate,
    /// so a line never sticks out from under a gate.
    /// <para>
    /// Eight of them run from top to bottom, and those share one x for both end points.
    /// Their gates are not always exactly above each other, because a gate stays inside the
    /// outline of its center and the Self center is a diamond. The shared x lies halfway
    /// between both gates, which keeps the line vertical and still inside both circles.
    /// </para>
    /// </summary>
    internal static readonly StraightChannel[] StraightChannels =
    [
        new(Channels.Key26Key44, 0.66, 0.925, 0.12, 0.99),
        // These three ended at 1.3, a hundredth above the upper edge of the Root center,
        // which left a visible gap. They now end inside it, where its fill covers them.
        new(Channels.Key42Key53, 0.45, 1.2, 0.45, 1.32),
        new(Channels.Key3Key60, 0.5, 1.2, 0.5, 1.32),
        new(Channels.Key9Key52, 0.55, 1.2, 0.55, 1.32),
        new(Channels.Key30Key41, 0.98, 1.1, 0.58, 1.47),
        new(Channels.Key39Key55, 0.58, 1.42, 0.96, 1.05),
        new(Channels.Key19Key49, 0.58, 1.37, 0.91, 1.03),
        new(Channels.Key18Key58, 0.025, 1.1, 0.41, 1.46),
        new(Channels.Key28Key38, 0.07, 1.085, 0.41, 1.41),
        new(Channels.Key32Key54, 0.11, 1.05, 0.41, 1.36),
        new(Channels.Key6Key59, 0.84, 1.02, 0.58, 1.145),
        new(Channels.Key27Key50, 0.41, 1.14, 0.16, 1.02),
        new(Channels.Key5Key15, 0.457, 1, 0.457, 0.85),
        new(Channels.Key2Key14, 0.5, 0.85, 0.5, 1),
        new(Channels.Key29Key46, 0.543, 1, 0.543, 0.85),
        new(Channels.Key37Key40, 0.88, 0.995, 0.74, 0.94),
        new(Channels.Key12Key22, 0.59, 0.52, 0.925, 0.96),
        new(Channels.Key35Key36, 0.59, 0.48, 0.965, 0.94),
        new(Channels.Key21Key45, 0.73, 0.86, 0.58, 0.55),
        new(Channels.Key25Key51, 0.58, 0.8, 0.69, 0.89),
        new(Channels.Key16Key48, 0.42, 0.48, 0.03, 0.94),
        new(Channels.Key20Key57, 0.42, 0.56, 0.07, 0.97),
        new(Channels.Key7Key31, 0.457, 0.76, 0.457, 0.6),
        new(Channels.Key1Key8, 0.5, 0.73, 0.5, 0.6),
        new(Channels.Key13Key33, 0.543, 0.76, 0.543, 0.6),
        new(Channels.Key17Key62, 0.46, 0.27, 0.46, 0.45),
        new(Channels.Key23Key43, 0.5, 0.45, 0.5, 0.32),
        new(Channels.Key11Key56, 0.54, 0.27, 0.54, 0.45),
        new(Channels.Key47Key64, 0.45, 0.22, 0.45, 0.12),
        new(Channels.Key24Key61, 0.5, 0.22, 0.5, 0.12),
        new(Channels.Key4Key63, 0.55, 0.22, 0.55, 0.12)
    ];

    /// <summary>
    /// The nine centers in drawing order. They come after the channels, so a center and its
    /// gates cover the ends of the lines that run into them.
    /// </summary>
    internal static readonly CenterLayout[] CenterLayouts =
    [
        new(Centers.Crown,
            x: 0.4, y: 0, width: 0.2, height: 0.15, viewBoxHeight: 0.75,
            outline: [(0, 0.75), (1, 0.75), (0.5, 0)],
            gateWidth: 0.2, gateHeight: 0.2666, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key64, 0.15, 0.5), new(Gates.Key61, 0.4, 0.5), new(Gates.Key63, 0.65, 0.5)
            ]),

        // The three gates of the upper row sit above the outline of the triangle,
        // so this center is the only one that has to show its overflow.
        new(Centers.Mind,
            x: 0.4, y: 0.2, width: 0.2, height: 0.15, viewBoxHeight: 0.75,
            outline: [(0, 0), (1, 0), (0.5, 0.75)],
            gateWidth: 0.2, gateHeight: 0.2666, gatesReachOutside: true,
            gates:
            [
                new(Gates.Key47, 0.15, -0.05), new(Gates.Key24, 0.4, -0.05), new(Gates.Key4, 0.65, -0.05),
                new(Gates.Key43, 0.4, 0.45), new(Gates.Key17, 0.25, 0.23), new(Gates.Key11, 0.55, 0.23)
            ]),

        new(Centers.Throat,
            x: 0.4, y: 0.425, width: 0.2, height: 0.2, viewBoxHeight: 1,
            outline: null,
            gateWidth: 0.2, gateHeight: 0.2, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key56, 0.65, 0), new(Gates.Key23, 0.4, 0), new(Gates.Key62, 0.15, 0),
                new(Gates.Key33, 0.65, 0.8), new(Gates.Key8, 0.4, 0.8), new(Gates.Key31, 0.15, 0.8),
                new(Gates.Key16, 0, 0.2), new(Gates.Key20, 0, 0.6), new(Gates.Key45, 0.8, 0.6),
                new(Gates.Key35, 0.8, 0.2), new(Gates.Key12, 0.8, 0.4)
            ]),

        // Gate 13 and gate 46 mirror gate 7 and gate 15. All four touch the outline
        // of the diamond from the inside.
        new(Centers.Self,
            x: 0.4, y: 0.71, width: 0.2, height: 0.2, viewBoxHeight: 1,
            outline: [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)],
            gateWidth: 0.2, gateHeight: 0.2, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key2, 0.4, 0.75), new(Gates.Key1, 0.4, 0.05), new(Gates.Key13, 0.58, 0.22),
                new(Gates.Key46, 0.58, 0.58), new(Gates.Key25, 0.75, 0.4), new(Gates.Key7, 0.22, 0.22),
                new(Gates.Key15, 0.22, 0.58), new(Gates.Key10, 0.05, 0.4)
            ]),

        new(Centers.Heart,
            x: 0.62, y: 0.82, width: 0.15, height: 0.15, viewBoxHeight: 1,
            outline: [(0, 0.8), (0.8, 0), (1, 1)],
            gateWidth: 0.2666, gateHeight: 0.2666, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key26, 0.15, 0.59), new(Gates.Key51, 0.37, 0.37), new(Gates.Key21, 0.58, 0.15),
                new(Gates.Key40, 0.7, 0.7)
            ]),

        new(Centers.Emotions,
            x: 0.75, y: 0.9, width: 0.25, height: 0.25, viewBoxHeight: 1,
            outline: [(1, 0), (1, 1), (0.2, 0.5)],
            gateWidth: 0.16, gateHeight: 0.16, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key30, 0.8, 0.74), new(Gates.Key55, 0.63, 0.63), new(Gates.Key49, 0.45, 0.52),
                new(Gates.Key6, 0.29, 0.415), new(Gates.Key37, 0.45, 0.32), new(Gates.Key22, 0.63, 0.21),
                new(Gates.Key36, 0.8, 0.1)
            ]),

        new(Centers.Spleen,
            x: 0, y: 0.9, width: 0.25, height: 0.25, viewBoxHeight: 1,
            outline: [(0, 0), (0, 1), (0.8, 0.5)],
            gateWidth: 0.16, gateHeight: 0.16, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key18, 0.05, 0.75), new(Gates.Key48, 0.05, 0.1), new(Gates.Key28, 0.2, 0.65),
                new(Gates.Key57, 0.2, 0.2), new(Gates.Key32, 0.37, 0.55), new(Gates.Key44, 0.37, 0.3),
                new(Gates.Key50, 0.53, 0.41)
            ]),

        new(Centers.Sacral,
            x: 0.4, y: 1, width: 0.2, height: 0.2, viewBoxHeight: 1,
            outline: null,
            gateWidth: 0.2, gateHeight: 0.2, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key29, 0.65, 0), new(Gates.Key14, 0.4, 0), new(Gates.Key5, 0.15, 0),
                new(Gates.Key9, 0.65, 0.8), new(Gates.Key3, 0.4, 0.8), new(Gates.Key42, 0.15, 0.8),
                new(Gates.Key34, 0, 0.2), new(Gates.Key27, 0, 0.6), new(Gates.Key59, 0.8, 0.6)
            ]),

        new(Centers.Root,
            x: 0.4, y: 1.31, width: 0.2, height: 0.2, viewBoxHeight: 1,
            outline: null,
            gateWidth: 0.2, gateHeight: 0.2, gatesReachOutside: false,
            gates:
            [
                new(Gates.Key19, 0.8, 0.2), new(Gates.Key39, 0.8, 0.45), new(Gates.Key41, 0.8, 0.7),
                new(Gates.Key52, 0.65, 0), new(Gates.Key60, 0.4, 0), new(Gates.Key53, 0.15, 0),
                new(Gates.Key54, 0, 0.2), new(Gates.Key38, 0, 0.45), new(Gates.Key58, 0, 0.7)
            ])
    ];
}
