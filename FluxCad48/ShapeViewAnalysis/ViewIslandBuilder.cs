using System;
using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class ViewIslandBuilder
	{
		public static List<ViewIsland> Build(
			IList<SheetEntity> geometryEntities,
			ViewIslandBuildOptions options)
		{
			List<ViewIsland> islands = new List<ViewIsland>();

			if (geometryEntities == null || geometryEntities.Count == 0)
				return islands;

			if (options == null)
				options = new ViewIslandBuildOptions();

			bool[] visited = new bool[geometryEntities.Count];

			for (int i = 0; i < geometryEntities.Count; i++)
			{
				if (visited[i])
					continue;

				ViewIsland island = BuildOneIsland(
					i,
					geometryEntities,
					visited,
					options);

				FinalizeIsland(island);

				if (island.GeometryEntities.Count >= options.MinGeometryCount)
				{
					island.Index = islands.Count;
					islands.Add(island);
				}
			}

			return islands;
		}

		private static ViewIsland BuildOneIsland(
			int startIndex,
			IList<SheetEntity> entities,
			bool[] visited,
			ViewIslandBuildOptions options)
		{
			ViewIsland island = new ViewIsland();

			Queue<int> queue = new Queue<int>();
			queue.Enqueue(startIndex);
			visited[startIndex] = true;

			while (queue.Count > 0)
			{
				int currentIndex = queue.Dequeue();
				SheetEntity current = entities[currentIndex];

				island.GeometryEntities.Add(current);

				for (int i = 0; i < entities.Count; i++)
				{
					if (visited[i])
						continue;

					SheetEntity other = entities[i];

					if (IsConnectedOrNear(current, other, options))
					{
						visited[i] = true;
						queue.Enqueue(i);
					}
				}
			}

			return island;
		}

		private static void FinalizeIsland(ViewIsland island)
		{
			Bounds2D bounds = null;

			for (int i = 0; i < island.GeometryEntities.Count; i++)
			{
				bounds = BoundsTools.Merge(
					bounds,
					island.GeometryEntities[i].Bounds);
			}

			island.Bounds = bounds;

			if (bounds == null || !bounds.IsValid)
				return;

			island.Width = bounds.Width;
			island.Height = bounds.Height;

			double longSide = Math.Max(island.Width, island.Height);
			double shortSide = Math.Min(island.Width, island.Height);

			if (longSide > 0.0)
				island.ThinnessRatio = shortSide / longSide;

			island.IsShapeViewCandidate = island.GeometryEntities.Count >= 2;
			island.IsThinViewCandidate = island.ThinnessRatio > 0.0 &&
										 island.ThinnessRatio < 0.18;
		}

		public static bool IsConnectedOrNear(
			SheetEntity a,
			SheetEntity b,
			ViewIslandBuildOptions options)
		{
			if (a == null || b == null)
				return false;

			if (a.Bounds == null || b.Bounds == null)
				return false;

			if (!a.Bounds.IsValid || !b.Bounds.IsValid)
				return false;

			Bounds2D aBounds = a.Bounds.Expand(options.BoundsInflate);
			Bounds2D bBounds = b.Bounds.Expand(options.BoundsInflate);

			if (aBounds.Intersects(bBounds))
				return true;

			if (BoundsTools.Distance(a.Bounds, b.Bounds) <= options.NearDistance)
				return true;

			if (HasNearEndpoint(a, b, options.EndpointTolerance))
				return true;

			return false;
		}

		private static bool HasNearEndpoint(
			SheetEntity a,
			SheetEntity b,
			double tolerance)
		{
			List<Point2D> pointsA = CollectKeyPoints(a);
			List<Point2D> pointsB = CollectKeyPoints(b);

			for (int i = 0; i < pointsA.Count; i++)
			{
				for (int j = 0; j < pointsB.Count; j++)
				{
					if (BoundsTools.Distance(pointsA[i], pointsB[j]) <= tolerance)
						return true;
				}
			}

			return false;
		}

		private static List<Point2D> CollectKeyPoints(SheetEntity e)
		{
			List<Point2D> points = new List<Point2D>();

			if (e.StartPoint.HasValue)
				points.Add(e.StartPoint.Value);

			if (e.EndPoint.HasValue)
				points.Add(e.EndPoint.Value);

			if (e.CenterPoint.HasValue)
				points.Add(e.CenterPoint.Value);

			if (e.Vertices != null)
			{
				for (int i = 0; i < e.Vertices.Count; i++)
					points.Add(e.Vertices[i]);
			}

			return points;
		}
	}
}