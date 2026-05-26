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
		private const string SheetCodeAppName = "FLUX_SHEET";

		[CommandMethod("FLUX_COPY_DETECTED_SHEETS_TO_RIGHT")]
		public void FluxCopyDetectedSheetsToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptPointOptions ppo1 = new PromptPointOptions(
				"\n복사할 쉬트 영역의 첫 번째 구석점을 지정하세요: ");

			PromptPointResult ppr1 = ed.GetPoint(ppo1);

			if (ppr1.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n첫 번째 점 선택이 취소되었습니다.");
				return;
			}

			PromptCornerOptions pco = new PromptCornerOptions(
				"\n반대 구석점을 지정하세요: ",
				ppr1.Value);

			PromptPointResult ppr2 = ed.GetCorner(pco);

			if (ppr2.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n반대 구석점 선택이 취소되었습니다.");
				return;
			}

			PromptSelectionResult psr =
				ed.SelectCrossingWindow(
					ppr1.Value,
					ppr2.Value);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택된 객체가 없습니다.");
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

				int nextSheetIndex = GetNextSheetIndexFromDrawing(tr, db, ed);

				List<SheetRegion> sheets =
					BuildSheetRegionsFromDetectedFrames(
						tr,
						db,
						frames,
						nextSheetIndex,
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
					string sheetCode = GetSheetCode(placement.SourceSheet);

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

						//clonedEntity.Layer = CopiedLayerName;
						SetSheetCodeXData(tr, db, clonedEntity, sheetCode);

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

					DBText sourceLabel =
						CreateSheetCodeText(
							placement.SourceBounds,
							0,
							0,
							sheetCode,
							MarkerLayerName,
							2);

					modelSpace.AppendEntity(sourceLabel);
					tr.AddNewlyCreatedDBObject(sourceLabel, true);
					SetSheetCodeXData(tr, db, sourceLabel, sheetCode);

					DBText copiedLabel =
						CreateSheetCodeText(
							placement.SourceBounds,
							placement.MoveX,
							placement.MoveY,
							sheetCode,
							CopiedLayerName,
							3);

					modelSpace.AppendEntity(copiedLabel);
					tr.AddNewlyCreatedDBObject(copiedLabel, true);
					SetSheetCodeXData(tr, db, copiedLabel, sheetCode);

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

		private static Bounds2D GetBoundsFromObjectIds(
			Transaction tr,
			ObjectId[] ids)
		{
			Bounds2D result = null;

			foreach (ObjectId id in ids)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				if (result == null)
				{
					result = new Bounds2D(
						b.MinX,
						b.MinY,
						b.MaxX,
						b.MaxY);
				}
				else
				{
					result = new Bounds2D(
						System.Math.Min(result.MinX, b.MinX),
						System.Math.Min(result.MinY, b.MinY),
						System.Math.Max(result.MaxX, b.MaxX),
						System.Math.Max(result.MaxY, b.MaxY));
				}
			}

			return result;
		}

		private static List<SheetFrameCandidate> SortFramesTopToBottomLeftToRight(
			List<SheetFrameCandidate> frames)
		{
			if (frames == null || frames.Count == 0)
				return new List<SheetFrameCandidate>();

			var ordered = frames
				.OrderByDescending(f => f.Bounds.Center.Y)
				.ThenBy(f => f.Bounds.MinX)
				.ToList();

			var rows = new List<List<SheetFrameCandidate>>();

			foreach (SheetFrameCandidate frame in ordered)
			{
				double cy = frame.Bounds.Center.Y;

				List<SheetFrameCandidate> targetRow = null;

				foreach (List<SheetFrameCandidate> row in rows)
				{
					double rowCenterY = row.Average(f => f.Bounds.Center.Y);
					double rowMaxHeight = row.Max(f => f.Bounds.Height);

					double tolerance = rowMaxHeight * 0.5;

					if (System.Math.Abs(cy - rowCenterY) <= tolerance)
					{
						targetRow = row;
						break;
					}
				}

				if (targetRow == null)
				{
					targetRow = new List<SheetFrameCandidate>();
					rows.Add(targetRow);
				}

				targetRow.Add(frame);
			}

			var result = new List<SheetFrameCandidate>();

			foreach (List<SheetFrameCandidate> row in rows)
			{
				row.Sort((a, b) => a.Bounds.MinX.CompareTo(b.Bounds.MinX));

				foreach (SheetFrameCandidate frame in row)
					result.Add(frame);
			}

			return result;
		}

		private static List<SheetRegion> BuildSheetRegionsFromDetectedFrames(
			Transaction tr,
			Database db,
			List<SheetFrameCandidate> frames,
			int startIndex,
			Editor ed)
		{
			var result = new List<SheetRegion>();

			frames = SortFramesTopToBottomLeftToRight(frames);


			BlockTable bt =
				(BlockTable)tr.GetObject(
					db.BlockTableId,
					OpenMode.ForRead);

			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForRead);


			foreach (SheetFrameCandidate frame in frames)
			{
				SheetRegion sheet = new SheetRegion();
				sheet.Bounds = frame.Bounds;

				foreach (ObjectId id in modelSpace)
				{
					Entity ent =
						tr.GetObject(id, OpenMode.ForRead) as Entity;

					if (ent == null)
						continue;

					Bounds2D b =
						BricscadEntityTools.GetEntityBounds(ent);

					if (b == null || !b.IsValid)
						continue;

					if (IsEntityInsideSheetFrame(frame.Bounds, b))
						sheet.EntityIds.Add(id);
				}

				if (sheet.EntityIds.Count == 0)
				{
					ed.WriteMessage(
						"\n[DetectedSheetCopy] SkipEmptySheet FrameHandle=" +
						frame.Handle +
						", Bounds=" +
						sheet.Bounds);

					continue;
				}

				sheet.Index = startIndex + result.Count;
				result.Add(sheet);

				ed.WriteMessage(
					"\n[DetectedSheetCopy] BuildSheet Code=" +
					GetSheetCode(sheet) +
					", Index=" +
					sheet.Index +
					", FrameHandle=" +
					frame.Handle +
					", EntityCount=" +
					sheet.EntityIds.Count +
					", Bounds=" +
					sheet.Bounds);
			}

			return result;
		}

		private static bool IsValidSheetRegion(
			SheetRegion sheet,
			SheetFrameCandidate frame)
		{
			if (sheet == null)
				return false;

			if (sheet.Bounds == null || !sheet.Bounds.IsValid)
				return false;

			if (frame == null || frame.Bounds == null || !frame.Bounds.IsValid)
				return false;

			// 프레임 자체가 이미 검출되었다면 가장 강한 증거다.
			// 내부 객체 수가 적어도 정상 쉬트일 수 있으므로 EntityCount 기준으로 버리지 않는다.
			if (sheet.EntityIds.Count > 0)
				return true;

			return false;
		}

		private static bool IsEntityInsideSheetFrame(
			Bounds2D frameBounds,
			Bounds2D entityBounds)
		{
			if (entityBounds == null || !entityBounds.IsValid)
				return false;

			if (frameBounds.Contains(entityBounds.Center))
				return true;

			double ratio = entityBounds.ContainedRatioIn(frameBounds);

			if (ratio >= 0.20)
				return true;

			if (IsBoundsOverlapped(frameBounds, entityBounds))
			{
				double minSize =
					System.Math.Min(entityBounds.Width, entityBounds.Height);

				if (minSize <= 1.0)
					return true;
			}

			return false;
		}

		private static bool IsBoundsOverlapped(
			Bounds2D a,
			Bounds2D b)
		{
			if (a.MaxX < b.MinX)
				return false;

			if (a.MinX > b.MaxX)
				return false;

			if (a.MaxY < b.MinY)
				return false;

			if (a.MinY > b.MaxY)
				return false;

			return true;
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

		private static string GetSheetCode(SheetRegion sheet)
		{
			return (sheet.Index + 1).ToString("000");
		}

		private static DBText CreateSheetCodeText(
			Bounds2D bounds,
			double offsetX,
			double offsetY,
			string sheetCode,
			string layerName,
			short colorIndex)
		{
			double baseSize = System.Math.Min(bounds.Width, bounds.Height);

			double margin = baseSize * 0.04;

			// 기존 0.06의 2배
			double height = baseSize * 0.12;

			if (height < 10.0)
				height = 10.0;

			if (height > 60.0)
				height = 60.0;

			DBText text = new DBText();
			text.TextString = sheetCode;

			text.Position =
				new Point3d(
					bounds.MinX + offsetX,
					bounds.MaxY + margin + offsetY,
					0);

			text.Height = height;

			// 글자를 조금 넓게 만들어 더 굵고 강하게 보이게 함
			text.WidthFactor = 1.15;

			text.Layer = layerName;
			text.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);

			return text;
		}

		private static void EnsureRegApp(
			Transaction tr,
			Database db,
			string appName)
		{
			RegAppTable table =
				(RegAppTable)tr.GetObject(
					db.RegAppTableId,
					OpenMode.ForRead);

			if (table.Has(appName))
				return;

			table.UpgradeOpen();

			RegAppTableRecord record = new RegAppTableRecord();
			record.Name = appName;

			table.Add(record);
			tr.AddNewlyCreatedDBObject(record, true);
		}

		private static void SetSheetCodeXData(
			Transaction tr,
			Database db,
			Entity entity,
			string sheetCode)
		{
			if (entity == null)
				return;

			EnsureRegApp(tr, db, SheetCodeAppName);

			ResultBuffer rb = new ResultBuffer(
				new TypedValue(
					(int)DxfCode.ExtendedDataRegAppName,
					SheetCodeAppName),
				new TypedValue(
					(int)DxfCode.ExtendedDataAsciiString,
					sheetCode));

			entity.XData = rb;
		}

		private static int GetNextSheetIndexFromDrawing(
			Transaction tr,
			Database db,
			Editor ed)
		{
			int maxNumber = 0;

			BlockTable bt =
				(BlockTable)tr.GetObject(
					db.BlockTableId,
					OpenMode.ForRead);

			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForRead);

			foreach (ObjectId id in modelSpace)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				int number = TryReadSheetNumberFromEntity(ent);

				if (number > maxNumber)
					maxNumber = number;
			}

			ed.WriteMessage(
				"\n[DetectedSheetCopy] ExistingMaxSheetCode=" +
				maxNumber.ToString("000") +
				", NextSheetCode=" +
				(maxNumber + 1).ToString("000"));

			return maxNumber;
		}

		private static int TryReadSheetNumberFromEntity(Entity ent)
		{
			// 일반 DBText / MText는 절대 읽지 않는다.
			// 도면 안의 치수값, 품번, 번호, 표 번호와 충돌하기 때문.
			return TryReadSheetNumberFromXData(ent);
		}

		private static int TryReadSheetNumberFromXData(Entity ent)
		{
			ResultBuffer rb = ent.GetXDataForApplication(SheetCodeAppName);

			if (rb == null)
				return 0;

			TypedValue[] values = rb.AsArray();

			foreach (TypedValue value in values)
			{
				if (value.TypeCode != (int)DxfCode.ExtendedDataAsciiString)
					continue;

				string text = value.Value as string;

				int number = TryParseSheetCode(text);

				if (number > 0)
					return number;
			}

			return 0;
		}

		private static int TryParseSheetCode(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return 0;

			text = text.Trim();

			if (text.Length != 3)
				return 0;

			int number;

			if (!int.TryParse(text, out number))
				return 0;

			if (number <= 0)
				return 0;

			return number;
		}
	}
}