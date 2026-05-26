using System.Collections.Generic;
using Teigha.Geometry;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class SheetEntityTransformTools
	{
		public static Point2D TransformPoint(Point2D p, Matrix3d matrix)
		{
			Point3d p3 = new Point3d(p.X, p.Y, 0.0);
			Point3d t = p3.TransformBy(matrix);

			return new Point2D(t.X, t.Y);
		}

		public static Point2D? TransformPoint(Point2D? p, Matrix3d matrix)
		{
			if (!p.HasValue)
				return null;

			return TransformPoint(p.Value, matrix);
		}

		public static void Transform(SheetEntity e, Matrix3d matrix)
		{
			if (e == null)
				return;

			e.Anchor = TransformPoint(e.Anchor, matrix);

			e.StartPoint = TransformPoint(e.StartPoint, matrix);
			e.EndPoint = TransformPoint(e.EndPoint, matrix);
			e.CenterPoint = TransformPoint(e.CenterPoint, matrix);

			if (e.Vertices != null && e.Vertices.Count > 0)
			{
				for (int i = 0; i < e.Vertices.Count; i++)
					e.Vertices[i] = TransformPoint(e.Vertices[i], matrix);
			}

			RebuildBounds(e);
			e.IsWorldCoordinate = true;
		}

		public static void RebuildBounds(SheetEntity e)
		{
			if (e == null)
				return;

			List<Point2D> points = new List<Point2D>();

			AddPoint(points, e.Anchor);
			AddPoint(points, e.StartPoint);
			AddPoint(points, e.EndPoint);
			AddPoint(points, e.CenterPoint);

			if (e.Vertices != null)
			{
				for (int i = 0; i < e.Vertices.Count; i++)
					AddPoint(points, e.Vertices[i]);
			}

			if (points.Count == 0)
				return;

			double minX = points[0].X;
			double minY = points[0].Y;
			double maxX = points[0].X;
			double maxY = points[0].Y;

			for (int i = 1; i < points.Count; i++)
			{
				Point2D p = points[i];

				if (p.X < minX) minX = p.X;
				if (p.Y < minY) minY = p.Y;
				if (p.X > maxX) maxX = p.X;
				if (p.Y > maxY) maxY = p.Y;
			}

			e.Bounds = new Bounds2D(minX, minY, maxX, maxY);
			e.Anchor = new Point2D(e.Bounds.CenterX, e.Bounds.CenterY);
		}

		private static void AddPoint(List<Point2D> points, Point2D p)
		{
			points.Add(p);
		}

		private static void AddPoint(List<Point2D> points, Point2D? p)
		{
			if (p.HasValue)
				points.Add(p.Value);
		}
	}
}