using System;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class BoundsTools
	{
		public static Bounds2D Merge(Bounds2D a, Bounds2D b)
		{
			if (a == null)
				return b;

			if (b == null)
				return a;

			Bounds2D result = new Bounds2D(
				a.MinX,
				a.MinY,
				a.MaxX,
				a.MaxY);

			result.ExpandToInclude(b);
			return result;
		}

		public static double Distance(Bounds2D a, Bounds2D b)
		{
			if (a == null || b == null || !a.IsValid || !b.IsValid)
				return double.MaxValue;

			double dx = 0.0;
			double dy = 0.0;

			if (a.MaxX < b.MinX)
				dx = b.MinX - a.MaxX;
			else if (b.MaxX < a.MinX)
				dx = a.MinX - b.MaxX;

			if (a.MaxY < b.MinY)
				dy = b.MinY - a.MaxY;
			else if (b.MaxY < a.MinY)
				dy = a.MinY - b.MaxY;

			return Math.Sqrt(dx * dx + dy * dy);
		}

		public static double Distance(Point2D a, Point2D b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;

			return Math.Sqrt(dx * dx + dy * dy);
		}
	}
}