using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class Segment2D
	{
		public Point2D Start { get; set; }
		public Point2D End { get; set; }

		public SheetEntity SourceEntity { get; set; }

		public int SourceSegmentIndex { get; set; }

		public bool IsArcApproxSegment { get; set; }
		public bool IsCircleApproxSegment { get; set; }

		public Bounds2D Bounds
		{
			get
			{
				var b = new Bounds2D();
				b.IncludePoint(Start.X, Start.Y);
				b.IncludePoint(End.X, End.Y);
				return b;
			}
		}

		public double Length
		{
			get
			{
				double dx = End.X - Start.X;
				double dy = End.Y - Start.Y;
				return System.Math.Sqrt(dx * dx + dy * dy);
			}
		}

		public Segment2D()
		{
			SourceSegmentIndex = -1;
		}
	}
}