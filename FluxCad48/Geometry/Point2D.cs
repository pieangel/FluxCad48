using System;

namespace FluxCad48.Geometry
{
	public readonly struct Point2D
	{
		public double X { get; }
		public double Y { get; }

		public Point2D(double x, double y)
		{
			X = x;
			Y = y;
		}

		public static Point2D Zero
		{
			get { return new Point2D(0.0, 0.0); }
		}

		public double DistanceTo(Point2D other)
		{
			double dx = X - other.X;
			double dy = Y - other.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		public double DistanceSquaredTo(Point2D other)
		{
			double dx = X - other.X;
			double dy = Y - other.Y;
			return dx * dx + dy * dy;
		}

		public Point2D Offset(double dx, double dy)
		{
			return new Point2D(X + dx, Y + dy);
		}

		public Point2D Scale(double scale)
		{
			return new Point2D(X * scale, Y * scale);
		}

		public bool IsNear(Point2D other, double tolerance)
		{
			return DistanceSquaredTo(other) <= tolerance * tolerance;
		}

		public override string ToString()
		{
			return string.Format("({0:0.###}, {1:0.###})", X, Y);
		}
	}
}