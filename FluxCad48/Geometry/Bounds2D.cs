using System;
using Teigha.Geometry;

namespace FluxCad48.Geometry
{
	public class Bounds2D
	{
		public double MinX { get; set; }
		public double MinY { get; set; }
		public double MaxX { get; set; }
		public double MaxY { get; set; }

		public double Width
		{
			get { return MaxX - MinX; }
		}

		public double Height
		{
			get { return MaxY - MinY; }
		}

		public double CenterX
		{
			get { return (MinX + MaxX) / 2.0; }
		}

		public double CenterY
		{
			get { return (MinY + MaxY) / 2.0; }
		}

		public bool IsValid
		{
			get { return MaxX > MinX && MaxY > MinY; }
		}

		public Bounds2D()
		{
		}

		public Bounds2D(double minX, double minY, double maxX, double maxY)
		{
			MinX = minX;
			MinY = minY;
			MaxX = maxX;
			MaxY = maxY;
		}

		public void ExpandToInclude(Bounds2D other)
		{
			if (other == null || !other.IsValid)
				return;

			if (!IsValid)
			{
				MinX = other.MinX;
				MinY = other.MinY;
				MaxX = other.MaxX;
				MaxY = other.MaxY;
				return;
			}

			MinX = System.Math.Min(MinX, other.MinX);
			MinY = System.Math.Min(MinY, other.MinY);
			MaxX = System.Math.Max(MaxX, other.MaxX);
			MaxY = System.Math.Max(MaxY, other.MaxY);
		}

		public bool ContainsPoint(double x, double y)
		{
			if (!IsValid)
				return false;

			return x >= MinX &&
				   x <= MaxX &&
				   y >= MinY &&
				   y <= MaxY;
		}

		public bool ContainsBounds(Bounds2D other)
		{
			if (!IsValid || other == null || !other.IsValid)
				return false;

			return ContainsPoint(other.MinX, other.MinY) &&
				   ContainsPoint(other.MaxX, other.MaxY);
		}

		public Bounds2D Expand(double margin)
		{
			return new Bounds2D(
				MinX - margin,
				MinY - margin,
				MaxX + margin,
				MaxY + margin);
		}

		public bool Intersects(Bounds2D other)
		{
			if (!IsValid || other == null || !other.IsValid)
				return false;

			if (MaxX < other.MinX)
				return false;

			if (MinX > other.MaxX)
				return false;

			if (MaxY < other.MinY)
				return false;

			if (MinY > other.MaxY)
				return false;

			return true;
		}

		public double Area
		{
			get
			{
				if (!IsValid)
					return 0.0;

				return Width * Height;
			}
		}

		public double IntersectionArea(Bounds2D other)
		{
			if (!IsValid || other == null || !other.IsValid)
				return 0.0;

			double minX = System.Math.Max(MinX, other.MinX);
			double minY = System.Math.Max(MinY, other.MinY);
			double maxX = System.Math.Min(MaxX, other.MaxX);
			double maxY = System.Math.Min(MaxY, other.MaxY);

			double width = maxX - minX;
			double height = maxY - minY;

			if (width <= 0 || height <= 0)
				return 0.0;

			return width * height;
		}

		public double IntersectionAreaRatio(Bounds2D other)
		{
			if (Area <= 0.0)
				return 0.0;

			return IntersectionArea(other) / Area;
		}

		public void IncludePoint(double x, double y)
		{
			if (!IsValid)
			{
				MinX = x;
				MinY = y;
				MaxX = x;
				MaxY = y;
				return;
			}

			MinX = System.Math.Min(MinX, x);
			MinY = System.Math.Min(MinY, y);
			MaxX = System.Math.Max(MaxX, x);
			MaxY = System.Math.Max(MaxY, y);
		}

		public override string ToString()
		{
			return
				$"Min=({MinX:0.##},{MinY:0.##}) " +
				$"Max=({MaxX:0.##},{MaxY:0.##}) " +
				$"W={Width:0.##}, H={Height:0.##}";
		}

		public Point2d CenterPoint
		{
			get
			{
				return new Point2d(
					(MinX + MaxX) / 2.0,
					(MinY + MaxY) / 2.0);
			}
		}

		public double GetIntersectionArea(Bounds2D other)
		{
			if (other == null || !other.IsValid || !IsValid)
				return 0.0;

			double minX = Math.Max(MinX, other.MinX);
			double minY = Math.Max(MinY, other.MinY);
			double maxX = Math.Min(MaxX, other.MaxX);
			double maxY = Math.Min(MaxY, other.MaxY);

			if (maxX <= minX || maxY <= minY)
				return 0.0;

			return (maxX - minX) * (maxY - minY);
		}

		public bool ContainsPoint(Point2d p)
		{
			if (!IsValid)
				return false;

			return
				p.X >= MinX &&
				p.X <= MaxX &&
				p.Y >= MinY &&
				p.Y <= MaxY;
		}

		public bool ContainsBounds(Bounds2D other, double tolerance = 0.0)
		{
			if (other == null || !other.IsValid || !this.IsValid)
				return false;

			return other.MinX >= this.MinX - tolerance &&
				   other.MaxX <= this.MaxX + tolerance &&
				   other.MinY >= this.MinY - tolerance &&
				   other.MaxY <= this.MaxY + tolerance;
		}

		public Bounds2D Offset(double dx, double dy)
		{
			if (!IsValid)
				return new Bounds2D();

			return new Bounds2D(
				MinX + dx,
				MinY + dy,
				MaxX + dx,
				MaxY + dy);
		}

		public bool Contains_Old(Point2D p)
		{
			return p.X >= MinX && p.X <= MaxX &&
				   p.Y >= MinY && p.Y <= MaxY;
		}


		public Point2D Center
		{
			get
			{
				return new Point2D(
					(MinX + MaxX) * 0.5,
					(MinY + MaxY) * 0.5);
			}
		}

		public bool Contains(Point2D p)
		{
			if (!IsValid)
				return false;

			return p.X >= MinX && p.X <= MaxX &&
				   p.Y >= MinY && p.Y <= MaxY;
		}

		public double ContainedRatioIn(Bounds2D parent)
		{
			if (parent == null || !IsValid || !parent.IsValid)
				return 0.0;

			double area = Area;
			if (area <= 0.0)
				return 0.0;

			return IntersectionArea(parent) / area;
		}

		public double OverlapRatioWith(Bounds2D other)
		{
			if (other == null || !IsValid || !other.IsValid)
				return 0.0;

			double smallerArea = Math.Min(Area, other.Area);
			if (smallerArea <= 0.0)
				return 0.0;

			return IntersectionArea(other) / smallerArea;
		}

		public bool IsMostlyContainedIn(Bounds2D parent, double ratio)
		{
			return ContainedRatioIn(parent) >= ratio;
		}

		public bool IsOverlappingSignificantly(Bounds2D other, double ratio)
		{
			return OverlapRatioWith(other) >= ratio;
		}
	}
}