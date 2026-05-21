using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.Sheets
{
	public class SheetArranger
	{
		public List<SheetPlacement> CreateHorizontalPlacements(
			IList<SheetRegion> sheets,
			Bounds2D sourceTotalBounds,
			SheetArrangeOptions options)
		{
			List<SheetPlacement> result = new List<SheetPlacement>();

			if (sheets == null || sheets.Count == 0)
				return result;

			if (sourceTotalBounds == null || !sourceTotalBounds.IsValid)
				return result;

			if (options == null)
				options = new SheetArrangeOptions();

			double startX = sourceTotalBounds.MaxX + options.StartGapFromSource;
			double currentX = startX;
			double firstRowMaxHeight = GetFirstRowMaxHeight(sheets, options.MaxColumnsPerRow);
			double currentRowBottomY = sourceTotalBounds.MaxY - firstRowMaxHeight;

			double rowMaxHeight = 0.0;

			for (int i = 0; i < sheets.Count; i++)
			{
				SheetRegion sheet = sheets[i];

				if (sheet == null || sheet.Bounds == null || !sheet.Bounds.IsValid)
					continue;

				int columnIndex = result.Count % options.MaxColumnsPerRow;
				int rowIndex = result.Count / options.MaxColumnsPerRow;

				if (columnIndex == 0 && result.Count > 0)
				{
					currentX = startX;
					currentRowBottomY = currentRowBottomY - rowMaxHeight - options.RowGap;
					rowMaxHeight = 0.0;
				}

				Bounds2D b = sheet.Bounds;

				double targetMinX = currentX;
				double targetMinY = currentRowBottomY;

				double moveX = targetMinX - b.MinX;
				double moveY = targetMinY - b.MinY;

				SheetPlacement placement = new SheetPlacement
				{
					SourceSheet = sheet,
					SourceBounds = b,
					TargetBottomLeft = new CadPoint2D(targetMinX, targetMinY),
					MoveX = moveX,
					MoveY = moveY,
					RowIndex = rowIndex,
					ColumnIndex = columnIndex
				};

				result.Add(placement);

				currentX = currentX + b.Width + options.ColumnGap;

				if (b.Height > rowMaxHeight)
					rowMaxHeight = b.Height;
			}

			return result;
		}

		private double GetFirstRowMaxHeight(IList<SheetRegion> sheets, int maxColumns)
		{
			double maxHeight = 0.0;

			int count = System.Math.Min(sheets.Count, maxColumns);

			for (int i = 0; i < count; i++)
			{
				if (sheets[i] == null || sheets[i].Bounds == null || !sheets[i].Bounds.IsValid)
					continue;

				if (sheets[i].Bounds.Height > maxHeight)
					maxHeight = sheets[i].Bounds.Height;
			}

			return maxHeight;
		}
	}
}