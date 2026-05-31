using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class StrokeOccupancyRasterizer
	{
		private readonly Transaction _tr;

		private List<OccupancyCell2d> _cells;
		private Dictionary<string, OccupancyCell2d> _cellMap;

		private double _minX;
		private double _minY;
		private double _cellW;
		private double _cellH;

		private int _maxRow;
		private int _maxCol;

		public int RasterizedEntityCount { get; private set; }

		public StrokeOccupancyRasterizer(Transaction tr)
		{
			_tr = tr;
		}

		public void RasterizeEntity(
			Entity ent,
			Matrix3d transform,
			List<OccupancyCell2d> cells)
		{
			if (ent == null || cells == null || cells.Count == 0)
				return;

			EnsureCellCache(cells);

			BlockReference br = ent as BlockReference;
			if (br != null)
			{
				RasterizeBlockReference(br, transform, cells, 0);
				return;
			}

			Curve curve = ent as Curve;
			if (curve != null)
			{
				RasterizeCurve(curve, transform, cells);
				RasterizedEntityCount++;
			}
		}

		private void RasterizeBlockReference(
			BlockReference br,
			Matrix3d parentTransform,
			List<OccupancyCell2d> cells,
			int depth)
		{
			if (br == null)
				return;

			if (depth > 20)
				return;

			Matrix3d currentTransform =
				br.BlockTransform.PreMultiplyBy(parentTransform);

			BlockTableRecord btr =
				_tr.GetObject(
					br.BlockTableRecord,
					OpenMode.ForRead) as BlockTableRecord;

			if (btr == null)
				return;

			foreach (ObjectId id in btr)
			{
				Entity child = _tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (child == null)
					continue;

				BlockReference childBr = child as BlockReference;
				if (childBr != null)
				{
					RasterizeBlockReference(
						childBr,
						currentTransform,
						cells,
						depth + 1);
					continue;
				}

				Curve curve = child as Curve;
				if (curve != null)
				{
					RasterizeCurve(curve, currentTransform, cells);
					RasterizedEntityCount++;
				}
			}
		}

		private void RasterizeCurve(
			Curve curve,
			Matrix3d transform,
			List<OccupancyCell2d> cells)
		{
			double startParam;
			double endParam;

			try
			{
				startParam = curve.StartParam;
				endParam = curve.EndParam;
			}
			catch
			{
				return;
			}

			int sampleCount = EstimateSampleCount(curve);

			for (int i = 0; i <= sampleCount; i++)
			{
				double t = startParam +
					(endParam - startParam) * i / sampleCount;

				Point3d p;

				try
				{
					p = curve.GetPointAtParameter(t);
				}
				catch
				{
					continue;
				}

				Point3d wp = p.TransformBy(transform);

				MarkCellContainingPoint(
					new Point2d(wp.X, wp.Y),
					cells);
			}
		}

		private int EstimateSampleCount(Curve curve)
		{
			double length = 0.0;

			try
			{
				length =
					curve.GetDistanceAtParameter(curve.EndParam) -
					curve.GetDistanceAtParameter(curve.StartParam);
			}
			catch
			{
				length = 100.0;
			}

			double baseSize = Math.Min(_cellW, _cellH);
			if (baseSize <= 0.000001)
				baseSize = 10.0;

			int count = (int)Math.Ceiling(length / baseSize * 4.0);

			if (count < 24)
				count = 24;

			if (count > 2000)
				count = 2000;

			return count;
		}

		private void MarkCellContainingPoint(
	Point2d p,
	List<OccupancyCell2d> cells)
		{
			if (p.X < _minX || p.Y < _minY)
				return;

			int col = (int)((p.X - _minX) / _cellW);
			int row = (int)((p.Y - _minY) / _cellH);

			if (row < 0)
				row = 0;

			if (col < 0)
				col = 0;

			if (row > _maxRow)
				row = _maxRow;

			if (col > _maxCol)
				col = _maxCol;

			_maxRow = cells.Max(c => c.Row);
			_maxCol = cells.Max(c => c.Col);

			string key = MakeKey(row, col);

			OccupancyCell2d cell;
			if (!_cellMap.TryGetValue(key, out cell))
				return;

			cell.Occupied = true;
		}

		private void EnsureCellCache(List<OccupancyCell2d> cells)
		{
			if (_cells == cells && _cellMap != null)
				return;

			_cells = cells;
			_cellMap = new Dictionary<string, OccupancyCell2d>();

			_minX = cells.Min(c => c.Min.X);
			_minY = cells.Min(c => c.Min.Y);

			OccupancyCell2d first = cells[0];

			_cellW = first.Max.X - first.Min.X;
			_cellH = first.Max.Y - first.Min.Y;

			foreach (OccupancyCell2d cell in cells)
			{
				string key = MakeKey(cell.Row, cell.Col);

				if (!_cellMap.ContainsKey(key))
					_cellMap.Add(key, cell);
			}
		}

		private string MakeKey(int row, int col)
		{
			return row.ToString() + ":" + col.ToString();
		}
	}
}