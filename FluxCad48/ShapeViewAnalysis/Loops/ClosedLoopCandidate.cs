using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class ClosedLoopCandidate
	{
		public List<Segment2D> Segments { get; private set; }
		public List<int> NodeIds { get; private set; }
		public List<Point2D> Vertices { get; private set; }

		public bool IsClosed { get; set; }

		public bool IsHole { get; set; }
		public int NestingDepth { get; set; }
		public int? ParentLoopIndex { get; set; }

		public double Area { get; private set; }
		public double AbsArea { get; private set; }
		public double Perimeter { get; private set; }
		public double ClosureGap { get; set; }

		public Bounds2D Bounds { get; private set; }

		public ClosedLoopCandidate()
		{
			Segments = new List<Segment2D>();
			NodeIds = new List<int>();
			Vertices = new List<Point2D>();

			Bounds = new Bounds2D();
			ParentLoopIndex = null;
		}

		public void FinalizeGeometry()
		{
			Area = ComputeSignedArea(Vertices);
			AbsArea = System.Math.Abs(Area);
			Perimeter = ComputePerimeter(Vertices);
			Bounds = ComputeBounds(Vertices);
		}

		private static double ComputeSignedArea(List<Point2D> points)
		{
			if (points == null || points.Count < 3)
				return 0.0;

			double sum = 0.0;

			for (int i = 0; i < points.Count; i++)
			{
				Point2D a = points[i];
				Point2D b = points[(i + 1) % points.Count];

				sum += a.X * b.Y - b.X * a.Y;
			}

			return sum * 0.5;
		}

		private static double ComputePerimeter(List<Point2D> points)
		{
			if (points == null || points.Count < 2)
				return 0.0;

			double sum = 0.0;

			for (int i = 0; i < points.Count - 1; i++)
				sum += Distance(points[i], points[i + 1]);

			return sum;
		}

		private static Bounds2D ComputeBounds(List<Point2D> points)
		{
			var bounds = new Bounds2D();

			if (points == null || points.Count == 0)
				return bounds;

			for (int i = 0; i < points.Count; i++)
				bounds.IncludePoint(points[i].X, points[i].Y);

			return bounds;
		}

		private static double Distance(Point2D a, Point2D b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return System.Math.Sqrt(dx * dx + dy * dy);
		}
	}
}