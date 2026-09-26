using System;
using UnityEngine;

namespace Yaui
{
    public enum LengthUnit
    {
        /// <summary>Decided by the layout (for min / max: no limit).</summary>
        Auto,

        /// <summary>Canvas units.</summary>
        Point,

        /// <summary>Percent of the parent's size.</summary>
        Percent
    }

    [Serializable]
    public struct Length : IEquatable<Length>
    {
        public float Value;
        public LengthUnit Unit;

        public Length(float value, LengthUnit unit)
        {
            Value = value;
            Unit = unit;
        }

        public static Length Auto => new(0f, LengthUnit.Auto);

        public static Length Points(float value)
        {
            return new Length(value, LengthUnit.Point);
        }

        public static Length Percent(float value)
        {
            return new Length(value, LengthUnit.Percent);
        }

        public static implicit operator Length(float points)
        {
            return Points(points);
        }

        public bool Equals(Length other)
        {
            return Value.Equals(other.Value) && Unit == other.Unit;
        }

        public override bool Equals(object obj)
        {
            return obj is Length other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value, (int)Unit);
        }

        public override string ToString()
        {
            return Unit switch
            {
                LengthUnit.Auto => "auto",
                LengthUnit.Percent => $"{Value}%",
                _ => Value.ToString()
            };
        }
    }

    [Serializable]
    public struct Edges : IEquatable<Edges>
    {
        public Length Left;
        public Length Top;
        public Length Right;
        public Length Bottom;

        public Edges(Length all)
        {
            Left = Top = Right = Bottom = all;
        }

        public Edges(Length horizontal, Length vertical)
        {
            Left = Right = horizontal;
            Top = Bottom = vertical;
        }

        public Edges(Length left, Length top, Length right, Length bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public static Edges Zero => new(Length.Points(0f));

        public static Edges Auto => new(Length.Auto);

        public bool Equals(Edges other)
        {
            return Left.Equals(other.Left) && Top.Equals(other.Top) && Right.Equals(other.Right) &&
                   Bottom.Equals(other.Bottom);
        }

        public override bool Equals(object obj)
        {
            return obj is Edges other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Left, Top, Right, Bottom);
        }
    }

    public enum PositionType
    {
        /// <summary>Placed by the flex layout of the parent, then offset by the insets.</summary>
        Relative,

        /// <summary>Placed by the insets relative to the parent, outside of the flex layout.</summary>
        Absolute
    }

    /// <summary>Properties that affect the layout. Changing them re-runs the layout of the panel.</summary>
    [Serializable]
    public struct LayoutStyle : IEquatable<LayoutStyle>
    {
        public PositionType Position;
        public Edges Inset;

        public FlexDirection Direction;
        public FlexWrap Wrap;
        public FlexJustify JustifyContent;
        public FlexAlign AlignItems;
        public FlexAlign AlignSelf;
        public FlexAlign AlignContent;

        public float Grow;
        public float Shrink;
        public Length Basis;

        public Length Width;
        public Length Height;
        public Length MinWidth;
        public Length MinHeight;
        public Length MaxWidth;
        public Length MaxHeight;

        public Edges Margin;
        public Edges Padding;

        /// <summary>x: between columns, y: between rows.</summary>
        public Vector2 Gap;

        /// <summary>CSS defaults, except that the direction is a column as in Yoga and UI Toolkit.</summary>
        public static LayoutStyle Default => new()
        {
            Position = PositionType.Relative,
            Inset = Edges.Auto,
            Direction = FlexDirection.Column,
            Wrap = FlexWrap.NoWrap,
            JustifyContent = FlexJustify.FlexStart,
            AlignItems = FlexAlign.Stretch,
            AlignSelf = FlexAlign.Auto,
            AlignContent = FlexAlign.FlexStart,
            Grow = 0f,
            Shrink = 1f,
            Basis = Length.Auto,
            Width = Length.Auto,
            Height = Length.Auto,
            MinWidth = Length.Auto,
            MinHeight = Length.Auto,
            MaxWidth = Length.Auto,
            MaxHeight = Length.Auto,
            Margin = Edges.Zero,
            Padding = Edges.Zero,
            Gap = Vector2.zero
        };

        public bool Equals(LayoutStyle other)
        {
            return Position == other.Position && Inset.Equals(other.Inset) && Direction == other.Direction &&
                   Wrap == other.Wrap && JustifyContent == other.JustifyContent && AlignItems == other.AlignItems &&
                   AlignSelf == other.AlignSelf && AlignContent == other.AlignContent && Grow.Equals(other.Grow) &&
                   Shrink.Equals(other.Shrink) && Basis.Equals(other.Basis) && Width.Equals(other.Width) &&
                   Height.Equals(other.Height) && MinWidth.Equals(other.MinWidth) &&
                   MinHeight.Equals(other.MinHeight) &&
                   MaxWidth.Equals(other.MaxWidth) && MaxHeight.Equals(other.MaxHeight) &&
                   Margin.Equals(other.Margin) &&
                   Padding.Equals(other.Padding) && Gap.Equals(other.Gap);
        }

        public override bool Equals(object obj)
        {
            return obj is LayoutStyle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Direction, Width, Height, Margin, Padding);
        }
    }

    public enum TextAlign
    {
        Left,
        Center,
        Right,
        Justified
    }

    public enum VerticalAlign
    {
        Top,
        Middle,
        Bottom
    }

    /// <summary>How a text that does not fit its content box ends.</summary>
    public enum TextOverflow
    {
        /// <summary>The text overflows the box (clip it with <see cref="YauiElement.ClipChildren"/> on a parent).</summary>
        Visible,

        /// <summary>
        /// The text is cut where it overflows the width (without word wrap) or the height (with word wrap), and ends
        /// with an ellipsis.
        /// </summary>
        Ellipsis
    }

    /// <summary>The look of the element's box. Drawn by the uber shader without breaking the batch.</summary>
    [Serializable]
    public struct BoxStyle
    {
        public Color BackgroundColor;

        /// <summary>x: top-left, y: top-right, z: bottom-right, w: bottom-left.</summary>
        public Vector4 CornerRadius;

        /// <summary>Also insets the content in the layout, like CSS.</summary>
        public float BorderWidth;

        public Color BorderColor;

        /// <summary>No shadow when the alpha is zero.</summary>
        public Color ShadowColor;

        public Vector2 ShadowOffset;

        /// <summary>Gaussian sigma in canvas units.</summary>
        public float ShadowBlur;

        public float ShadowSpread;

        public static BoxStyle Default => new()
        {
            BackgroundColor = Color.clear,
            CornerRadius = Vector4.zero,
            BorderWidth = 0f,
            BorderColor = Color.black,
            ShadowColor = Color.clear,
            ShadowOffset = Vector2.zero,
            ShadowBlur = 0f,
            ShadowSpread = 0f
        };

        internal bool HasShadow => ShadowColor.a > 0f;

        internal bool IsVisible => BackgroundColor.a > 0f || (BorderWidth > 0f && BorderColor.a > 0f) || HasShadow;
    }

    /// <summary>
    /// A 2D transform applied after the layout, without re-running it (CSS transform).
    /// Applies to the descendants too.
    /// </summary>
    [Serializable]
    public struct TransformStyle
    {
        public Vector2 Translate;

        /// <summary>Degrees, clockwise.</summary>
        public float Rotation;

        public Vector2 Scale;

        /// <summary>Origin of the rotation and scale, normalized in the element's box.</summary>
        public Vector2 Pivot;

        public static TransformStyle Identity => new()
        {
            Translate = Vector2.zero,
            Rotation = 0f,
            Scale = Vector2.one,
            Pivot = new Vector2(0.5f, 0.5f)
        };
    }
}