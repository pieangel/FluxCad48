using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering;
using FluxCad48.ShapeViewAnalysis;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using System;

namespace FluxCad48.Commands
{
	public struct GridCellKey
	{
		public int Col;
		public int Row;

		public GridCellKey(int col, int row)
		{
			Col = col;
			Row = row;
		}
	}

	public class GridCellKeyComparer : IEqualityComparer<GridCellKey>
	{
		public bool Equals(GridCellKey a, GridCellKey b)
		{
			return a.Col == b.Col && a.Row == b.Row;
		}

		public int GetHashCode(GridCellKey obj)
		{
			unchecked
			{
				return (obj.Col * 397) ^ obj.Row;
			}
		}
	}


	public class FluxDebugOutsideInRingFilterCommand
	{
		[CommandMethod("FLUX_DEBUG_OUTSIDE_IN_RING_FILTER")]
		public void FluxDebugOutsideInRingFilter()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptEntityOptions peo = new PromptEntityOptions(
				"\n복사된 쉬트 내부의 아무 개체나 하나 선택하세요: ");

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[OutsideInRingFilter] 선택이 취소되었습니다.");
				return;
			}

			List<ObjectId> visibleGeometryIds =
				new List<ObjectId>();

			Extents3d sheetBounds;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				CopiedSheetSelectionResult pickResult =
					CopiedSheetSelectionService.SelectByPickedEntity(
						db,
						tr,
						per.ObjectId);

				if (!pickResult.Success)
				{
					AppendLog(ed, "[OutsideInRingFilter] 쉬트 선택 실패: " + pickResult.ErrorMessage);
					return;
				}

				if (pickResult.HasFrameBounds)
					sheetBounds = pickResult.FrameBounds;
				else if (pickResult.HasGroupBounds)
					sheetBounds = pickResult.GroupBounds;
				else
				{
					AppendLog(ed, "[OutsideInRingFilter] 쉬트 Bounds를 찾지 못했습니다.");
					return;
				}

				for (int i = 0; i < pickResult.SelectedIds.Count; i++)
				{
					ObjectId id = pickResult.SelectedIds[i];

					Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
					if (ent == null)
						continue;

					if (!IsVisibleGeometryLike(ent))
						continue;

					visibleGeometryIds.Add(id);
				}

				AppendLog(ed, "---------- Visible Geometry Types ----------");

				for (int i = 0; i < visibleGeometryIds.Count; i++)
				{
					ObjectId id = visibleGeometryIds[i];

					using (Transaction tr2 = db.TransactionManager.StartTransaction())
					{
						Entity ent = tr2.GetObject(id, OpenMode.ForRead, false) as Entity;
						if (ent != null)
						{
							AppendLog(
								ed,
								"[" + i + "] Handle=" + ent.Handle +
								", Type=" + ent.GetType().Name +
								", Layer=" + ent.Layer);
						}

						tr2.Commit();
					}
				}


				AppendLog(ed, "========== Picked Copied Sheet ==========");
				AppendLog(ed, "SheetCode           : " + pickResult.SheetCode);
				AppendLog(ed, "Selected Group Count: " + pickResult.SelectedIds.Count);
				AppendLog(ed, "Visible Geometry Count : " + visibleGeometryIds.Count);

				if (pickResult.HasFrameBounds)
					AppendLog(ed, "Bounds Source       : FrameBounds");
				else
					AppendLog(ed, "Bounds Source       : GroupBounds");

				tr.Commit();
			}

			List<OccupancyCell2d> cells =
	BuildSheetGridCells(sheetBounds, 40, 30);

			HashSet<int> occupiedCellIndexes =
				BuildOccupiedCellIndexes(
					db,
					visibleGeometryIds,
					cells);

			DrawOccupiedGridCells(
				db,
				cells,
				occupiedCellIndexes);

			AppendLog(ed, "Grid Cell Count     : " + cells.Count);
			AppendLog(ed, "Occupied Cell Count : " + occupiedCellIndexes.Count);
			AppendLog(ed, "[OutsideInRingFilter] Occupied cell drawing 완료.");


			/*
			OutsideInRingFilter filter = new OutsideInRingFilter();

			OutsideInRingFilterResult result =
				filter.Analyze(
					sheetBounds,
					entityBoundsMap,
					5);

			AppendLog(ed, "");
			AppendLog(ed, "========== Outside-In Ring Filter ==========");
			AppendLog(ed, "Ring Count            : " + result.Rings.Count);
			AppendLog(ed, "Excluded Count        : " + result.ExcludedEntityIds.Count);
			AppendLog(ed, "Inner Candidate Count : " + result.InnerCandidateIds.Count);

			AppendLog(ed, "");
			AppendLog(ed, "---------- Inner Candidate Handles ----------");

			PrintObjectIdHandles(ed, result.InnerCandidateIds);
			*/

			AppendLog(ed, "[OutsideInRingFilter] 완료.");
		}


		private static void DrawOccupiedGridCells(
	Database db,
	List<OccupancyCell2d> cells,
	HashSet<int> occupiedCellIndexes)
		{
			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord ms =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				foreach (int index in occupiedCellIndexes)
				{
					if (index < 0 || index >= cells.Count)
						continue;

					OccupancyCell2d cell = cells[index];

					Polyline pl = new Polyline();

					pl.AddVertexAt(
						0,
						new Point2d(cell.Min.X, cell.Min.Y),
						0,
						0,
						0);

					pl.AddVertexAt(
						1,
						new Point2d(cell.Max.X, cell.Min.Y),
						0,
						0,
						0);

					pl.AddVertexAt(
						2,
						new Point2d(cell.Max.X, cell.Max.Y),
						0,
						0,
						0);

					pl.AddVertexAt(
						3,
						new Point2d(cell.Min.X, cell.Max.Y),
						0,
						0,
						0);

					pl.Closed = true;
					pl.ColorIndex = 1;

					ms.AppendEntity(pl);
					tr.AddNewlyCreatedDBObject(pl, true);
				}

				tr.Commit();
			}
		}


		private static HashSet<int> BuildOccupiedCellIndexes(
	Database db,
	List<ObjectId> visibleGeometryIds,
	List<OccupancyCell2d> cells)
		{
			HashSet<int> occupied =
				new HashSet<int>();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				for (int i = 0; i < visibleGeometryIds.Count; i++)
				{
					ObjectId id = visibleGeometryIds[i];

					Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
					if (ent == null)
						continue;

					Line line = ent as Line;
					if (line != null)
					{
						MarkLineOccupiedCells(
							line,
							cells,
							occupied);

						continue;
					}

					Polyline pline = ent as Polyline;
					if (pline != null)
					{
						MarkPolylineOccupiedCells(
							pline,
							cells,
							occupied);

						continue;
					}

					Arc arc = ent as Arc;
					if (arc != null)
					{
						MarkArcOccupiedCells(
							arc,
							cells,
							occupied);

						continue;
					}
				}

				tr.Commit();
			}

			return occupied;
		}

		private static void MarkArcOccupiedCells(
	Arc arc,
	List<OccupancyCell2d> cells,
	HashSet<int> occupied)
		{
			double length =
				Math.Abs(arc.TotalAngle) * arc.Radius;

			if (length <= 1e-9)
				return;

			double cellSize = EstimateCellSize(cells);
			double step = cellSize * 0.35;

			int sampleCount =
				Math.Max(8, (int)Math.Ceiling(length / step));

			for (int i = 0; i <= sampleCount; i++)
			{
				double t = (double)i / sampleCount;

				double angle =
					arc.StartAngle +
					arc.TotalAngle * t;

				Point3d p = new Point3d(
					arc.Center.X + Math.Cos(angle) * arc.Radius,
					arc.Center.Y + Math.Sin(angle) * arc.Radius,
					0);

				MarkPointOccupiedCell(
					p,
					cells,
					occupied);
			}
		}

		private static void MarkLineOccupiedCells(
	Line line,
	List<OccupancyCell2d> cells,
	HashSet<int> occupied)
		{
			Point3d p0 = line.StartPoint;
			Point3d p1 = line.EndPoint;

			double length = p0.DistanceTo(p1);
			if (length <= 1e-9)
				return;

			double cellSize = EstimateCellSize(cells);
			double step = cellSize * 0.35;

			int sampleCount =
				Math.Max(2, (int)Math.Ceiling(length / step));

			for (int i = 0; i <= sampleCount; i++)
			{
				double t = (double)i / sampleCount;

				Point3d p = new Point3d(
					p0.X + (p1.X - p0.X) * t,
					p0.Y + (p1.Y - p0.Y) * t,
					0);

				MarkPointOccupiedCell(
					p,
					cells,
					occupied);
			}
		}

		private static void MarkPolylineOccupiedCells(
	Polyline pline,
	List<OccupancyCell2d> cells,
	HashSet<int> occupied)
		{
			int n = pline.NumberOfVertices;
			if (n < 2)
				return;

			for (int i = 0; i < n - 1; i++)
			{
				Point3d p0 = pline.GetPoint3dAt(i);
				Point3d p1 = pline.GetPoint3dAt(i + 1);

				MarkSegmentOccupiedCells(
					p0,
					p1,
					cells,
					occupied);
			}

			if (pline.Closed)
			{
				Point3d p0 = pline.GetPoint3dAt(n - 1);
				Point3d p1 = pline.GetPoint3dAt(0);

				MarkSegmentOccupiedCells(
					p0,
					p1,
					cells,
					occupied);
			}
		}

		private static void MarkSegmentOccupiedCells(
	Point3d p0,
	Point3d p1,
	List<OccupancyCell2d> cells,
	HashSet<int> occupied)
		{
			double length = p0.DistanceTo(p1);
			if (length <= 1e-9)
				return;

			double cellSize = EstimateCellSize(cells);
			double step = cellSize * 0.35;

			int sampleCount =
				Math.Max(2, (int)Math.Ceiling(length / step));

			for (int i = 0; i <= sampleCount; i++)
			{
				double t = (double)i / sampleCount;

				Point3d p = new Point3d(
					p0.X + (p1.X - p0.X) * t,
					p0.Y + (p1.Y - p0.Y) * t,
					0);

				MarkPointOccupiedCell(
					p,
					cells,
					occupied);
			}
		}

		private static void MarkPointOccupiedCell(
	Point3d p,
	List<OccupancyCell2d> cells,
	HashSet<int> occupied)
		{
			for (int i = 0; i < cells.Count; i++)
			{
				if (PointInCell(p, cells[i]))
				{
					occupied.Add(i);
					return;
				}
			}
		}

		private static bool PointInCell(
	Point3d p,
	OccupancyCell2d cell)
		{
			return
				p.X >= cell.Min.X &&
				p.X <= cell.Max.X &&
				p.Y >= cell.Min.Y &&
				p.Y <= cell.Max.Y;
		}

		private static double EstimateCellSize(
	List<OccupancyCell2d> cells)
		{
			if (cells == null || cells.Count == 0)
				return 1.0;

			OccupancyCell2d cell = cells[0];

			double w = cell.Width;
			double h = cell.Height;

			return Math.Max(1e-6, Math.Min(w, h));
		}



		private static bool TryGetCellKey(
	Point3d pt,
	Extents3d sheetBounds,
	double cellSize,
	int colCount,
	int rowCount,
	out GridCellKey key)
		{
			key = new GridCellKey(-1, -1);

			double minX = sheetBounds.MinPoint.X;
			double minY = sheetBounds.MinPoint.Y;

			int col = (int)Math.Floor((pt.X - minX) / cellSize);
			int row = (int)Math.Floor((pt.Y - minY) / cellSize);

			if (col < 0 || row < 0 || col >= colCount || row >= rowCount)
				return false;

			key = new GridCellKey(col, row);
			return true;
		}

		private static void MarkLineOccupiedCells(
	Line line,
	Extents3d sheetBounds,
	double cellSize,
	int colCount,
	int rowCount,
	HashSet<GridCellKey> occupied)
		{
			Point3d p0 = line.StartPoint;
			Point3d p1 = line.EndPoint;

			double length = p0.DistanceTo(p1);

			if (length <= 1e-9)
				return;

			double step = cellSize * 0.35;
			int sampleCount = Math.Max(2, (int)Math.Ceiling(length / step));

			for (int i = 0; i <= sampleCount; i++)
			{
				double t = (double)i / sampleCount;

				Point3d p = new Point3d(
					p0.X + (p1.X - p0.X) * t,
					p0.Y + (p1.Y - p0.Y) * t,
					0);

				GridCellKey key;
				if (TryGetCellKey(p, sheetBounds, cellSize, colCount, rowCount, out key))
					occupied.Add(key);
			}
		}

		private static void DrawOccupiedCells(
	Transaction tr,
	BlockTableRecord ms,
	Extents3d sheetBounds,
	double cellSize,
	HashSet<GridCellKey> occupied)
		{
			foreach (GridCellKey key in occupied)
			{
				double x0 = sheetBounds.MinPoint.X + key.Col * cellSize;
				double y0 = sheetBounds.MinPoint.Y + key.Row * cellSize;
				double x1 = x0 + cellSize;
				double y1 = y0 + cellSize;

				Polyline pl = new Polyline();
				pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
				pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
				pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
				pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
				pl.Closed = true;

				pl.ColorIndex = 1; // 빨강

				ms.AppendEntity(pl);
				tr.AddNewlyCreatedDBObject(pl, true);
			}
		}



		private static List<OccupancyCell2d> BuildSheetGridCells(
	Extents3d sheetBounds,
	int colCount,
	int rowCount)
		{
			List<OccupancyCell2d> cells = new List<OccupancyCell2d>();

			double minX = sheetBounds.MinPoint.X;
			double minY = sheetBounds.MinPoint.Y;
			double maxX = sheetBounds.MaxPoint.X;
			double maxY = sheetBounds.MaxPoint.Y;

			double width = maxX - minX;
			double height = maxY - minY;

			if (width <= 0.0 || height <= 0.0)
				return cells;

			double cellW = width / colCount;
			double cellH = height / rowCount;

			for (int row = 0; row < rowCount; row++)
			{
				for (int col = 0; col < colCount; col++)
				{
					double x1 = minX + col * cellW;
					double y1 = minY + row * cellH;
					double x2 = x1 + cellW;
					double y2 = y1 + cellH;

					OccupancyCell2d cell = new OccupancyCell2d();
					cell.Row = row;
					cell.Col = col;
					cell.Min = new Point2d(x1, y1);
					cell.Max = new Point2d(x2, y2);

					cells.Add(cell);
				}
			}

			return cells;
		}

		private static void DrawGridCells(
	Database db,
	List<OccupancyCell2d> cells)
		{
			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord ms =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				for (int i = 0; i < cells.Count; i++)
				{
					OccupancyCell2d cell = cells[i];

					Polyline pl = new Polyline();
					pl.SetDatabaseDefaults();

					pl.AddVertexAt(
						0,
						new Point2d(cell.Min.X, cell.Min.Y),
						0.0,
						0.0,
						0.0);

					pl.AddVertexAt(
						1,
						new Point2d(cell.Max.X, cell.Min.Y),
						0.0,
						0.0,
						0.0);

					pl.AddVertexAt(
						2,
						new Point2d(cell.Max.X, cell.Max.Y),
						0.0,
						0.0,
						0.0);

					pl.AddVertexAt(
						3,
						new Point2d(cell.Min.X, cell.Max.Y),
						0.0,
						0.0,
						0.0);

					pl.Closed = true;

					ms.AppendEntity(pl);
					tr.AddNewlyCreatedDBObject(pl, true);
				}

				tr.Commit();
			}
		}



		private static bool IsVisibleGeometryLike(Entity ent)
		{
			if (ent == null)
				return false;

			if (!ent.Visible)
				return false;

			if (ent is DBText)
				return false;

			if (ent is MText)
				return false;

			if (ent is Dimension)
				return false;

			if (ent is Leader)
				return false;

			if (ent is MLeader)
				return false;

			if (ent is Line)
				return true;

			if (ent is Arc)
				return true;

			if (ent is Circle)
				return true;

			if (ent is Polyline)
				return true;

			if (ent is Polyline2d)
				return true;

			if (ent is Polyline3d)
				return true;

			if (ent is Ellipse)
				return true;

			if (ent is Spline)
				return true;

			if (ent is BlockReference)
				return true;

			return false;
		}

		private static Extents3d? TryGetEntityBounds(Entity ent)
		{
			try
			{
				return ent.GeometricExtents;
			}
			catch
			{
				return null;
			}
		}

		private static void PrintObjectIdHandles(Editor ed, List<ObjectId> ids)
		{
			for (int i = 0; i < ids.Count; i++)
			{
				AppendLog(ed, "[" + i + "] Handle=" + ids[i].Handle.ToString());
			}
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}