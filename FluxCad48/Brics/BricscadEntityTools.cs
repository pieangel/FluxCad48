using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using FluxCad48.Geometry;

namespace FluxCad48.Brics
{
	public static class BricscadEntityTools
	{
		public static Bounds2D GetEntityBounds(Entity entity)
		{
			if (entity == null)
				return null;

			try
			{
				Extents3d ext = entity.GeometricExtents;

				return new Bounds2D(
					ext.MinPoint.X,
					ext.MinPoint.Y,
					ext.MaxPoint.X,
					ext.MaxPoint.Y);
			}
			catch
			{
				return null;
			}
		}

		public static Bounds2D GetEntityBounds(Transaction tr, ObjectId id)
		{
			Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

			if (ent == null)
				return null;

			try
			{
				Extents3d ext = ent.GeometricExtents;

				Bounds2D b = new Bounds2D(
					ext.MinPoint.X,
					ext.MinPoint.Y,
					ext.MaxPoint.X,
					ext.MaxPoint.Y);

				if (b.IsValid)
					return b;
			}
			catch
			{
			}

			Polyline pl = ent as Polyline;

			if (pl != null)
				return GetPolylineBounds(pl);

			return null;
		}

		private static Bounds2D GetPolylineBounds(Polyline pl)
		{
			if (pl == null || pl.NumberOfVertices <= 0)
				return null;

			double minX = double.MaxValue;
			double minY = double.MaxValue;
			double maxX = double.MinValue;
			double maxY = double.MinValue;

			for (int i = 0; i < pl.NumberOfVertices; i++)
			{
				Point2d p = pl.GetPoint2dAt(i);

				minX = System.Math.Min(minX, p.X);
				minY = System.Math.Min(minY, p.Y);
				maxX = System.Math.Max(maxX, p.X);
				maxY = System.Math.Max(maxY, p.Y);
			}

			Bounds2D bounds = new Bounds2D(minX, minY, maxX, maxY);

			// 수평선/수직선/점형 polyline 구제
			if (!bounds.IsValid)
				bounds = bounds.Expand(0.01);

			return bounds;
		}

		public static Polyline CreateRectanglePolyline(Bounds2D bounds)
		{
			Polyline pl = new Polyline();

			pl.AddVertexAt(0, new Point2d(bounds.MinX, bounds.MinY), 0, 0, 0);
			pl.AddVertexAt(1, new Point2d(bounds.MaxX, bounds.MinY), 0, 0, 0);
			pl.AddVertexAt(2, new Point2d(bounds.MaxX, bounds.MaxY), 0, 0, 0);
			pl.AddVertexAt(3, new Point2d(bounds.MinX, bounds.MaxY), 0, 0, 0);

			pl.Closed = true;

			return pl;
		}

		public static Bounds2D GetModelSpaceBounds(Transaction tr, Database db)
		{
			Bounds2D result = new Bounds2D();

			BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

			foreach (ObjectId id in modelSpace)
			{
				Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (entity == null)
					continue;

				Bounds2D b = GetEntityBounds(entity);
				result.ExpandToInclude(b);
			}

			return result;
		}

		public static Bounds2D GetEntitiesBounds(
			Transaction tr,
			IEnumerable<ObjectId> ids)
		{
			Bounds2D result = null;

			foreach (ObjectId id in ids)
			{
				Bounds2D bounds = GetEntityBounds(tr, id);

				if (bounds == null || !bounds.IsValid)
					continue;

				if (result == null)
				{
					result = new Bounds2D(
						bounds.MinX,
						bounds.MinY,
						bounds.MaxX,
						bounds.MaxY);
				}
				else
				{
					result.ExpandToInclude(bounds);
				}
			}

			return result;
		}
	}
}