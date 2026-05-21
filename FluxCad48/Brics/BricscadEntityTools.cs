using FluxCad48.Geometry;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.Brics
{
	public static class BricscadEntityTools
	{
		private const double BoundsEpsilon = 0.0001;

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

				Bounds2D b = GetEntityBoundsSafe(entity);
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

		public static List<WorldEntityInfo> CollectWorldEntitiesDeep(
	Transaction tr,
	Database db)
		{
			List<WorldEntityInfo> result = new List<WorldEntityInfo>();

			BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

			foreach (ObjectId id in modelSpace)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (ent == null)
					continue;

				CollectWorldEntityRecursive(
					tr,
					ent,
					Matrix3d.Identity,
					0,
					ObjectId.Null,
					result);
			}

			return result;
		}

		private static void CollectWorldEntityRecursive(
			Transaction tr,
			Entity ent,
			Matrix3d currentTransform,
			int depth,
			ObjectId ownerBlockReferenceId,
			List<WorldEntityInfo> result)
		{
			if (ent == null)
				return;

			Bounds2D localBounds = GetEntityBoundsSafe(ent);
			Bounds2D worldBounds = TransformBounds(localBounds, currentTransform);

			WorldEntityInfo info = new WorldEntityInfo();
			info.SourceId = ent.ObjectId;
			info.OwnerBlockReferenceId = ownerBlockReferenceId;
			info.EntityType = ent.GetType().Name;
			info.Layer = ent.Layer;
			info.LocalBounds = localBounds;
			info.WorldBounds = worldBounds;
			info.AccumulatedTransform = currentTransform;
			info.BlockDepth = depth;

			result.Add(info);

			if (ent is BlockReference br)
			{
				Matrix3d nextTransform = currentTransform * br.BlockTransform;

				BlockTableRecord btr =
					tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

				if (btr == null)
					return;

				foreach (ObjectId childId in btr)
				{
					Entity child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
					if (child == null)
						continue;

					CollectWorldEntityRecursive(
						tr,
						child,
						nextTransform,
						depth + 1,
						br.ObjectId,
						result);
				}
			}
		}

		public static Bounds2D GetEntityBoundsSafe(Entity entity)
		{
			if (entity == null)
				return null;

			try
			{
				Bounds2D b = GetEntityBounds(entity);
				b = NormalizeThinBounds(b);

				if (b != null && b.IsValid)
					return b;
			}
			catch
			{
			}

			if (entity is Line line)
			{
				Point3d s = line.StartPoint;
				Point3d e = line.EndPoint;

				return NormalizeThinBounds(new Bounds2D(
					Math.Min(s.X, e.X),
					Math.Min(s.Y, e.Y),
					Math.Max(s.X, e.X),
					Math.Max(s.Y, e.Y)));
			}

			if (entity is Circle circle)
			{
				Point3d c = circle.Center;
				double r = circle.Radius;

				return NormalizeThinBounds(new Bounds2D(
					c.X - r,
					c.Y - r,
					c.X + r,
					c.Y + r));
			}

			if (entity is Arc arc)
			{
				Point3d c = arc.Center;
				double r = arc.Radius;

				return NormalizeThinBounds(new Bounds2D(
					c.X - r,
					c.Y - r,
					c.X + r,
					c.Y + r));
			}

			if (entity is DBText text)
			{
				try
				{
					Extents3d ext = text.GeometricExtents;

					return NormalizeThinBounds(new Bounds2D(
						ext.MinPoint.X,
						ext.MinPoint.Y,
						ext.MaxPoint.X,
						ext.MaxPoint.Y));
				}
				catch
				{
					Point3d p = text.Position;

					return NormalizeThinBounds(new Bounds2D(
						p.X,
						p.Y,
						p.X,
						p.Y));
				}
			}

			return null;
		}


		public static Bounds2D TransformBounds(
			Bounds2D bounds,
			Matrix3d transform)
		{
			if (bounds == null || !bounds.IsValid)
				return null;

			Point3d p1 = new Point3d(bounds.MinX, bounds.MinY, 0).TransformBy(transform);
			Point3d p2 = new Point3d(bounds.MaxX, bounds.MinY, 0).TransformBy(transform);
			Point3d p3 = new Point3d(bounds.MaxX, bounds.MaxY, 0).TransformBy(transform);
			Point3d p4 = new Point3d(bounds.MinX, bounds.MaxY, 0).TransformBy(transform);

			double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
			double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
			double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
			double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

			return NormalizeThinBounds(new Bounds2D(minX, minY, maxX, maxY));
		}

		//선처럼 얇은 객체도 Bounds 판정에서 탈락하지 않게 최소 두께를 부여
		public static Bounds2D NormalizeThinBounds(Bounds2D b)
		{
			if (b == null)
				return null;

			double minX = b.MinX;
			double minY = b.MinY;
			double maxX = b.MaxX;
			double maxY = b.MaxY;

			if (Math.Abs(maxX - minX) < BoundsEpsilon)
			{
				minX -= BoundsEpsilon;
				maxX += BoundsEpsilon;
			}

			if (Math.Abs(maxY - minY) < BoundsEpsilon)
			{
				minY -= BoundsEpsilon;
				maxY += BoundsEpsilon;
			}

			return new Bounds2D(minX, minY, maxX, maxY);
		}
	}
}