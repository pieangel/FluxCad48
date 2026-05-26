using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System.Collections.Generic;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxCopyDetectedSheetsToRightCommand
	{
		private const string CopiedLayerName = "FLUX_COPIED";
		private const string MarkerLayerName = "FLUX_MARKER";

		[CommandMethod("FLUX_COPY_DETECTED_SHEETS_TO_RIGHT")]
		public void FluxCopyDetectedSheetsToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\n오른쪽으로 복사할 쉬트 프레임들을 드래그 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 완료되지 않았습니다.");
				return;
			}

			ObjectId[] selectedIds = psr.Value.GetObjectIds();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				var selectedEntities = new List<Entity>();

				foreach (ObjectId id in selectedIds)
				{
					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					selectedEntities.Add(ent);
				}

				ed.WriteMessage(
					"\n[DetectedSheetCopy] SelectedEntities=" +
					selectedEntities.Count);

				List<SheetFrameCandidate> frames =
					SheetFrameDetector.Detect(selectedEntities, ed);

				ed.WriteMessage(
					"\n[DetectedSheetCopy] DetectedFrames=" +
					frames.Count);

				if (frames.Count == 0)
				{
					ed.WriteMessage(
						"\n[DetectedSheetCopy] 탐지된 쉬트 프레임이 없습니다.");
					return;
				}

				List<SheetRegion> sheets =
					BuildSheetRegionsFromDetectedFrames(
						tr,
						selectedIds,
						frames,
						ed);

				ed.WriteMessage(
					"\n[DetectedSheetCopy] BuiltSheets=" +
					sheets.Count);

				if (sheets.Count == 0)
				{
					ed.WriteMessage(
						"\n[DetectedSheetCopy] 복사할 쉬트 내부 객체가 없습니다.");
					return;
				}

				Bounds2D drawingBounds =
					BricscadEntityTools.GetModelSpaceBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n전체 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				SheetArrangeOptions options = new SheetArrangeOptions();

				List<SheetPlacement> placements =
					CreateRowPreservingPlacements(
						sheets,
						drawingBounds,
						options,
						ed);

				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				BricscadEntityTools.EnsureLayer(tr, db, CopiedLayerName);
				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

				int totalCloned = 0;

				foreach (SheetPlacement placement in placements)
				{
					ObjectIdCollection idsToClone =
						new ObjectIdCollection();

					foreach (ObjectId id in placement.SourceSheet.EntityIds)
						idsToClone.Add(id);

					IdMapping mapping = new IdMapping();

					db.DeepCloneObjects(
						idsToClone,
						modelSpace.ObjectId,
						mapping,
						false);

					Vector3d displacement =
						new Vector3d(
							placement.MoveX,
							placement.MoveY,
							0);

					int clonedCount = 0;

					foreach (IdPair pair in mapping)
					{
						if (!pair.IsCloned)
							continue;

						Entity clonedEntity =
							tr.GetObject(pair.Value, OpenMode.ForWrite)
							as Entity;

						if (clonedEntity == null)
							continue;

						clonedEntity.TransformBy(
							Matrix3d.Displacement(displacement));

						clonedEntity.Layer = CopiedLayerName;
						clonedCount++;
					}

					totalCloned += clonedCount;

					Polyline marker =
						BricscadEntityTools.CreateRectanglePolyline(
							placement.SourceBounds);

					marker.Color =
						Color.FromColorIndex(ColorMethod.ByAci, 2);

					marker.LineWeight = LineWeight.LineWeight050;
					marker.Layer = MarkerLayerName;


					modelSpace.AppendEntity(marker);
					tr.AddNewlyCreatedDBObject(marker, true);

					ed.WriteMessage(
						"\n[DetectedSheetCopy] Sheet=" +
						placement.SourceSheet.Index +
						", Entities=" +
						placement.SourceSheet.EntityIds.Count +
						", Cloned=" +
						clonedCount +
						", Bounds=" +
						placement.SourceBounds);
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_COPY_DETECTED_SHEETS_TO_RIGHT 완료: " +
					"DetectedSheets=" + sheets.Count +
					", TotalCloned=" + totalCloned);
			}
		}

		private static List<SheetRegion> BuildSheetRegionsFromDetectedFrames(
			Transaction tr,
			ObjectId[] selectedIds,
			List<SheetFrameCandidate> frames,
			Editor ed)
		{
			var result = new List<SheetRegion>();

			int index = 0;

			foreach (SheetFrameCandidate frame in frames)
			{
				SheetRegion sheet = new SheetRegion();
				sheet.Index = index;
				sheet.Bounds = frame.Bounds;

				foreach (ObjectId id in selectedIds)
				{
					Entity ent =
						tr.GetObject(id, OpenMode.ForRead) as Entity;

					if (ent == null)
						continue;

					Bounds2D b =
						BricscadEntityTools.GetEntityBounds(ent);

					if (b == null || !b.IsValid)
						continue;

					if (frame.Bounds.Contains(b.Center))
						sheet.EntityIds.Add(id);
				}

				if (sheet.EntityIds.Count < 20)
				{
					ed.WriteMessage(
						"\n[DetectedSheetCopy] SkipSmallSheet FrameHandle=" +
						frame.Handle +
						", EntityCount=" +
						sheet.EntityIds.Count +
						", Bounds=" +
						sheet.Bounds);

					continue;
				}

				result.Add(sheet);

				ed.WriteMessage(
					"\n[DetectedSheetCopy] BuildSheet Index=" +
					index +
					", FrameHandle=" +
					frame.Handle +
					", EntityCount=" +
					sheet.EntityIds.Count +
					", Bounds=" +
					sheet.Bounds);

				index++;
			}

			return result;
		}

		private static List<SheetPlacement> CreateRowPreservingPlacements(
			List<SheetRegion> sheets,
			Bounds2D drawingBounds,
			SheetArrangeOptions options,
			Editor ed)
		{
			var result = new List<SheetPlacement>();

			if (sheets == null || sheets.Count == 0)
				return result;

			double columnGap = options.ColumnGap;
			double rowGap = options.RowGap;
			double startGap = options.StartGapFromSource;

			var ordered = sheets
				.OrderByDescending(s => s.Bounds.Center.Y)
				.ThenBy(s => s.Bounds.MinX)
				.ToList();

			var rows = new List<List<SheetRegion>>();

			foreach (SheetRegion sheet in ordered)
			{
				double cy = sheet.Bounds.Center.Y;

				List<SheetRegion> targetRow = null;

				foreach (List<SheetRegion> row in rows)
				{
					double rowCenterY = row.Average(s => s.Bounds.Center.Y);
					double rowMaxHeight = row.Max(s => s.Bounds.Height);
					double tolerance = rowMaxHeight * 0.5;

					if (System.Math.Abs(cy - rowCenterY) <= tolerance)
					{
						targetRow = row;
						break;
					}
				}

				if (targetRow == null)
				{
					targetRow = new List<SheetRegion>();
					rows.Add(targetRow);
				}

				targetRow.Add(sheet);
			}

			foreach (List<SheetRegion> row in rows)
			{
				row.Sort((a, b) => a.Bounds.MinX.CompareTo(b.Bounds.MinX));
			}

			double startX = drawingBounds.MaxX + startGap;
			double currentTopY = drawingBounds.MaxY;

			for (int r = 0; r < rows.Count; r++)
			{
				List<SheetRegion> row = rows[r];

				double rowMaxHeight = row.Max(s => s.Bounds.Height);
				double currentX = startX;

				ed.WriteMessage(
					"\n[RowArrange] Row=" + r +
					", Count=" + row.Count +
					", MaxHeight=" + rowMaxHeight);

				for (int c = 0; c < row.Count; c++)
				{
					SheetRegion sheet = row[c];

					double targetMinX = currentX;
					double targetMaxY = currentTopY;

					double moveX = targetMinX - sheet.Bounds.MinX;
					double moveY = targetMaxY - sheet.Bounds.MaxY;

					CadPoint2D targetBottomLeft =
						new CadPoint2D(
							targetMinX,
							targetMaxY - sheet.Bounds.Height);

					SheetPlacement placement = new SheetPlacement();
					placement.SourceSheet = sheet;
					placement.SourceBounds = sheet.Bounds;
					placement.TargetBottomLeft = targetBottomLeft;
					placement.MoveX = moveX;
					placement.MoveY = moveY;
					placement.RowIndex = r;
					placement.ColumnIndex = c;

					result.Add(placement);

					currentX += sheet.Bounds.Width + columnGap;
				}

				currentTopY -= rowMaxHeight + rowGap;
			}

			return result;
		}
	}
}