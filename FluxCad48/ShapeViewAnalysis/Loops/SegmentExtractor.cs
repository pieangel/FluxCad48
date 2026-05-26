using System;
using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class SegmentExtractor
	{
		public List<Segment2D> Extract(
			IReadOnlyList<SheetEntity> entities,
			LoopExtractionOptions options)
		{
			var result = new List<Segment2D>();

			if (entities == null || entities.Count == 0)
				return result;

			if (options == null)
				options = new LoopExtractionOptions();

			for (int i = 0; i < entities.Count; i++)
			{
				SheetEntity e = entities[i];

				if (e == null)
					continue;

				switch (e.Kind)
				{
					case SheetEntityKind.Line:
						AddLine(result, e);
						break;

					case SheetEntityKind.Polyline:
						AddPolyline(result, e);
						break;

					case SheetEntityKind.Arc:
						AddArc(result, e, options);
						break;

					case SheetEntityKind.Circle:
						AddCircle(result, e, options);
						break;
				}
			}

			return result;
		}

		private static void AddLine(
			List<Segment2D> result,
			SheetEntity e)
		{
			if (!e.StartPoint.HasValue || !e.EndPoint.HasValue)
				return;

			AddSegment(
				result,
				e.StartPoint.Value,
				e.EndPoint.Value,
				e,
				0,
				false,
				false);
		}

		private static void AddPolyline(
			List<Segment2D> result,
			SheetEntity e)
		{
			if (e.Vertices == null || e.Vertices.Count < 2)
				return;

			int index = 0;

			for (int i = 0; i < e.Vertices.Count - 1; i++)
			{
				AddSegment(
					result,
					e.Vertices[i],
					e.Vertices[i + 1],
					e,
					index++,
					false,
					false);
			}

			if (e.IsClosed && e.Vertices.Count >= 3)
			{
				AddSegment(
					result,
					e.Vertices[e.Vertices.Count - 1],
					e.Vertices[0],
					e,
					index,
					false,
					false);
			}
		}

		private static void AddArc(
			List<Segment2D> result,
			SheetEntity e,
			LoopExtractionOptions options)
		{
			if (!e.CenterPoint.HasValue)
			{
				AddLine(result, e);
				return;
			}

			if (!e.Radius.HasValue || e.Radius.Value <= 0.0)
			{
				AddLine(result, e);
				return;
			}

			if (!e.StartAngleDeg2D.HasValue || !e.EndAngleDeg2D.HasValue)
			{
				AddLine(result, e);
				return;
			}

			Point2D center = e.CenterPoint.Value;
			double radius = e.Radius.Value;

			double startDeg = e.StartAngleDeg2D.Value;
			double endDeg = e.EndAngleDeg2D.Value;

			double sweepDeg = NormalizeSweepDegrees(startDeg, endDeg);

			if (sweepDeg <= 0.0)
			{
				AddLine(result, e);
				return;
			}

			int countByAngle = Math.Max(
				1,
				(int)Math.Ceiling(sweepDeg / Math.Max(1.0, options.ArcStepDegrees)));

			double arcLength = 2.0 * Math.PI * radius * (sweepDeg / 360.0);

			int countByLength = Math.Max(
				1,
				(int)Math.Ceiling(arcLength / Math.Max(0.1, options.MaxSegmentLength)));

			int segmentCount = Math.Max(countByAngle, countByLength);

			Point2D prev = PointOnCircle(center, radius, startDeg);

			for (int i = 1; i <= segmentCount; i++)
			{
				double t = (double)i / segmentCount;
				double deg = startDeg + sweepDeg * t;
				Point2D next = PointOnCircle(center, radius, deg);

				AddSegment(
					result,
					prev,
					next,
					e,
					i - 1,
					true,
					false);

				prev = next;
			}
		}

		private static void AddCircle(
			List<Segment2D> result,
			SheetEntity e,
			LoopExtractionOptions options)
		{
			if (!e.CenterPoint.HasValue)
				return;

			if (!e.Radius.HasValue || e.Radius.Value <= 0.0)
				return;

			Point2D center = e.CenterPoint.Value;
			double radius = e.Radius.Value;

			int countByAngle = Math.Max(
				12,
				(int)Math.Ceiling(360.0 / Math.Max(1.0, options.ArcStepDegrees)));

			double circumference = 2.0 * Math.PI * radius;

			int countByLength = Math.Max(
				12,
				(int)Math.Ceiling(circumference / Math.Max(0.1, options.MaxSegmentLength)));

			int segmentCount = Math.Max(countByAngle, countByLength);

			Point2D first = PointOnCircle(center, radius, 0.0);
			Point2D prev = first;

			for (int i = 1; i <= segmentCount; i++)
			{
				double deg = 360.0 * i / segmentCount;
				Point2D next = i == segmentCount
					? first
					: PointOnCircle(center, radius, deg);

				AddSegment(
					result,
					prev,
					next,
					e,
					i - 1,
					false,
					true);

				prev = next;
			}
		}

		private static void AddSegment(
			List<Segment2D> result,
			Point2D start,
			Point2D end,
			SheetEntity source,
			int sourceSegmentIndex,
			bool isArcApprox,
			bool isCircleApprox)
		{
			if (Distance(start, end) <= 1e-9)
				return;

			var seg = new Segment2D();
			seg.Start = start;
			seg.End = end;
			seg.SourceEntity = source;
			seg.SourceSegmentIndex = sourceSegmentIndex;
			seg.IsArcApproxSegment = isArcApprox;
			seg.IsCircleApproxSegment = isCircleApprox;

			result.Add(seg);
		}

		private static Point2D PointOnCircle(
			Point2D center,
			double radius,
			double degree)
		{
			double rad = degree * Math.PI / 180.0;

			return new Point2D(
				center.X + Math.Cos(rad) * radius,
				center.Y + Math.Sin(rad) * radius);
		}

		private static double NormalizeSweepDegrees(
			double startDeg,
			double endDeg)
		{
			double sweep = endDeg - startDeg;

			while (sweep <= 0.0)
				sweep += 360.0;

			while (sweep > 360.0)
				sweep -= 360.0;

			return sweep;
		}

		private static double Distance(Point2D a, Point2D b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}
	}
}