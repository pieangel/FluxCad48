using System.Collections.Generic;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public static class ClosedLoopDebugDrawer
	{
		public static void Draw(
			Database db,
			Editor ed,
			ViewIsland island,
			ClosedLoopExtractionResult result)
		{
			if (db == null || result == null)
				return;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTableRecord space =
					tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

				if (space == null)
					return;

				for (int i = 0; i < result.ClosedLoops.Count; i++)
				{
					ClosedLoopCandidate loop = result.ClosedLoops[i];

					short colorIndex = 3; // green

					if (loop == result.LargestClosedLoop)
						colorIndex = 1; // red

					if (loop.IsHole)
						colorIndex = 5; // blue

					DrawLoop(space, tr, loop.Vertices, true, colorIndex);
				}

				for (int i = 0; i < result.OpenChains.Count; i++)
				{
					ClosedLoopCandidate chain = result.OpenChains[i];
					DrawLoop(space, tr, chain.Vertices, false, 2); // yellow
				}

				tr.Commit();
			}
		}

		private static void DrawLoop(
			BlockTableRecord space,
			Transaction tr,
			List<Point2D> points,
			bool closed,
			short colorIndex)
		{
			if (points == null || points.Count < 2)
				return;

			Polyline pl = new Polyline();

			for (int i = 0; i < points.Count; i++)
			{
				Point2D p = points[i];

				pl.AddVertexAt(
					i,
					new Point2d(p.X, p.Y),
					0.0,
					0.0,
					0.0);
			}

			pl.Closed = closed;
			pl.ColorIndex = colorIndex;

			space.AppendEntity(pl);
			tr.AddNewlyCreatedDBObject(pl, true);
		}
	}
}