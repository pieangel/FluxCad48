using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using FluxCad48.Geometry;

namespace FluxCad48.Bricscad
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

		public static Bounds2D GetEntitiesBounds(Transaction tr, IEnumerable<ObjectId> ids)
		{
			Bounds2D result = new Bounds2D();

			foreach (ObjectId id in ids)
			{
				Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (entity == null)
					continue;

				Bounds2D b = GetEntityBounds(entity);
				result.ExpandToInclude(b);
			}

			return result;
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
	}
}