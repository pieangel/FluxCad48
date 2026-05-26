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

				Bounds2D selectionBounds = new Bounds2D(
					System.Math.Min(ppr1.Value.X, ppr2.Value.X),
					System.Math.Min(ppr1.Value.Y, ppr2.Value.Y),
					System.Math.Max(ppr1.Value.X, ppr2.Value.X),
					System.Math.Max(ppr1.Value.Y, ppr2.Value.Y));

				// 여기에 추가
				frames = frames
					.Where(f => IsFrameFullyInsideSelection(f.Bounds, selectionBounds))
					.ToList();

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
						selectionBounds.MaxY,
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

						Bounds2D copiedFrameBounds = new Bounds2D(
							placement.SourceBounds.MinX + placement.MoveX,
							placement.SourceBounds.MinY + placement.MoveY,
							placement.SourceBounds.MaxX + placement.MoveX,
							placement.SourceBounds.MaxY + placement.MoveY);

						Bounds2D copiedEntityBounds =
							GetEntityWorldBounds(tr, clonedEntity);

						bool copiedInside = false;

						Line copiedLine = clonedEntity as Line;

						if (copiedLine != null)
						{
							copiedInside =
								IsLineInsideSheetFrameForCopy(
									copiedFrameBounds,
									copiedLine);
						}
						else
						{
							copiedInside =
								copiedEntityBounds != null &&
								IsEntityInsideSheetFrameForCopy(
									copiedFrameBounds,
									copiedEntityBounds);
						}

						if (!copiedInside)
						{
							ed.WriteMessage(
								"\n[CopiedEntityOutOfFrame] Sheet=" +
								sheetCode +
								", EntityHandle=" +
								clonedEntity.Handle +
								", Type=" +
								clonedEntity.GetType().Name +
								", EntityBounds=" +
								copiedEntityBounds +
								", CopiedFrameBounds=" +
								copiedFrameBounds);
						}

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

		private static bool IsLineInsideSheetFrameForCopy(
	Bounds2D frameBounds,
	Line line)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			if (line == null)
				return false;

			double frameMinSize =
				System.Math.Min(frameBounds.Width, frameBounds.Height);

			double tol = frameMinSize * 0.003;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 8.0)
				tol = 8.0;

			bool p1Inside =
				IsPointInsideBounds(
					frameBounds,
					line.StartPoint,
					tol);

			bool p2Inside =
				IsPointInsideBounds(
					frameBounds,
					line.EndPoint,
					tol);

			if (p1Inside && p2Inside)
				return true;

			Bounds2D lineBounds = new Bounds2D(
				System.Math.Min(line.StartPoint.X, line.EndPoint.X),
				System.Math.Min(line.StartPoint.Y, line.EndPoint.Y),
				System.Math.Max(line.StartPoint.X, line.EndPoint.X),
				System.Math.Max(line.StartPoint.Y, line.EndPoint.Y));

			return IsBoundsOverlappedWithTolerance(
				frameBounds,
				lineBounds,
				tol);
		}


		private static bool IsFrameFullyInsideSelection(
			Bounds2D frameBounds,
			Bounds2D selectionBounds)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			if (selectionBounds == null || !selectionBounds.IsValid)
				return false;

			double tol = 1.0;

			if (frameBounds.MinX < selectionBounds.MinX + tol)
				return false;

			if (frameBounds.MaxX > selectionBounds.MaxX - tol)
				return false;

			if (frameBounds.MinY < selectionBounds.MinY + tol)
				return false;

			if (frameBounds.MaxY > selectionBounds.MaxY - tol)
				return false;

			return true;
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
			frames = SortFramesTopToBottomLeftToRight(frames);

			var allSheets = new List<SheetRegion>();

			for (int i = 0; i < frames.Count; i++)
			{
				SheetRegion sheet = new SheetRegion();
				sheet.Bounds = frames[i].Bounds;
				sheet.Index = startIndex + i;
				allSheets.Add(sheet);
			}

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
				Entity ent =
					tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				if (IsFluxGeneratedEntity(ent))
					continue;

				int ownerIndex = -1;

				Line line = ent as Line;

				if (line != null)
				{
					ownerIndex =
						FindBestOwningSheetIndexForLine(
							frames,
							line);
				}

				if (ownerIndex < 0)
				{
					Bounds2D b =
						GetEntityWorldBounds(tr, ent);

					if (b == null || !b.IsValid)
						continue;

					ownerIndex =
						FindBestOwningSheetIndex(
							frames,
							b);
				}

				if (ownerIndex < 0)
					continue;

				if (ownerIndex >= allSheets.Count)
				{
					ed.WriteMessage(
						"\n[OwnerIndexError] OwnerIndex=" +
						ownerIndex +
						", SheetCount=" +
						allSheets.Count +
						", EntityHandle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name);

					continue;
				}

				allSheets[ownerIndex].EntityIds.Add(id);
			}

			var result = new List<SheetRegion>();

			for (int i = 0; i < allSheets.Count; i++)
			{
				SheetRegion sheet = allSheets[i];
				SheetFrameCandidate frame = frames[i];

				if (sheet.EntityIds.Count == 0)
				{
					ed.WriteMessage(
						"\n[DetectedSheetCopy] SkipEmptySheet FrameHandle=" +
						frame.Handle +
						", Bounds=" +
						sheet.Bounds);

					continue;
				}

				// 여기서 실제 반환 순서에 맞게 Index를 다시 정리
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

		private static int FindBestOwningSheetIndex(
	List<SheetFrameCandidate> frames,
	Bounds2D entityBounds)
		{
			if (frames == null || frames.Count == 0)
				return -1;

			int bestIndex = -1;
			double bestScore = 0.0;

			for (int i = 0; i < frames.Count; i++)
			{
				double score =
					GetSheetOwnershipScore(
						frames[i].Bounds,
						entityBounds);

				if (score > bestScore)
				{
					bestScore = score;
					bestIndex = i;
				}
			}

			if (bestScore <= 0.0)
				return -1;

			return bestIndex;
		}

		private static double GetSheetOwnershipScore(
	Bounds2D frameBounds,
	Bounds2D entityBounds)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return 0.0;

			if (entityBounds == null || !entityBounds.IsValid)
				return 0.0;

			double frameMinSize =
				System.Math.Min(frameBounds.Width, frameBounds.Height);

			double tol = frameMinSize * 0.003;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 8.0)
				tol = 8.0;

			bool fullyInside =
				entityBounds.MinX >= frameBounds.MinX - tol &&
				entityBounds.MaxX <= frameBounds.MaxX + tol &&
				entityBounds.MinY >= frameBounds.MinY - tol &&
				entityBounds.MaxY <= frameBounds.MaxY + tol;

			if (fullyInside)
				return 1000.0 + GetBoundsOverlapRatio(entityBounds, frameBounds);

			if (frameBounds.Contains(entityBounds.Center))
				return 500.0 + GetBoundsOverlapRatio(entityBounds, frameBounds);

			if (!IsBoundsOverlapped(frameBounds, entityBounds))
				return 0.0;

			double overlapRatio =
				GetBoundsOverlapRatio(entityBounds, frameBounds);

			if (overlapRatio <= 0.0)
				return 0.0;

			double entityMinSize =
				System.Math.Min(entityBounds.Width, entityBounds.Height);

			double entityMaxSize =
				System.Math.Max(entityBounds.Width, entityBounds.Height);

			bool isThinLineLike =
				entityMinSize <= tol * 2.0 &&
				entityMaxSize <= System.Math.Max(frameBounds.Width, frameBounds.Height) * 1.05;

			if (isThinLineLike && overlapRatio >= 0.50)
				return 100.0 + overlapRatio;

			return 0.0;
		}

		private static int FindBestOwningSheetIndexForLine(
	List<SheetFrameCandidate> frames,
	Line line)
		{
			if (frames == null || line == null)
				return -1;

			int bestIndex = -1;
			double bestScore = 0.0;

			Point3d p1 = line.StartPoint;
			Point3d p2 = line.EndPoint;

			for (int i = 0; i < frames.Count; i++)
			{
				double score =
					GetLineOwnershipScore(
						frames[i].Bounds,
						p1,
						p2);

				if (score > bestScore)
				{
					bestScore = score;
					bestIndex = i;
				}
			}

			if (bestScore <= 0.0)
				return -1;

			return bestIndex;
		}

		private static double GetLineOwnershipScore(
	Bounds2D frameBounds,
	Point3d p1,
	Point3d p2)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return 0.0;

			double frameMinSize =
				System.Math.Min(frameBounds.Width, frameBounds.Height);

			double tol = frameMinSize * 0.003;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 8.0)
				tol = 8.0;

			bool p1Inside = IsPointInsideBounds(frameBounds, p1, tol);
			bool p2Inside = IsPointInsideBounds(frameBounds, p2, tol);

			if (p1Inside && p2Inside)
				return 1000.0;

			if (p1Inside || p2Inside)
				return 500.0;

			Bounds2D lineBounds = new Bounds2D(
				System.Math.Min(p1.X, p2.X),
				System.Math.Min(p1.Y, p2.Y),
				System.Math.Max(p1.X, p2.X),
				System.Math.Max(p1.Y, p2.Y));

			if (!IsBoundsOverlappedWithTolerance(frameBounds, lineBounds, tol))
				return 0.0;

			return 100.0;
		}

		private static bool IsPointInsideBounds(
	Bounds2D bounds,
	Point3d p,
	double tol)
		{
			if (p.X < bounds.MinX - tol)
				return false;

			if (p.X > bounds.MaxX + tol)
				return false;

			if (p.Y < bounds.MinY - tol)
				return false;

			if (p.Y > bounds.MaxY + tol)
				return false;

			return true;
		}

		private static bool IsBoundsOverlappedWithTolerance(
	Bounds2D a,
	Bounds2D b,
	double tol)
		{
			if (a.MaxX + tol < b.MinX)
				return false;

			if (a.MinX - tol > b.MaxX)
				return false;

			if (a.MaxY + tol < b.MinY)
				return false;

			if (a.MinY - tol > b.MaxY)
				return false;

			return true;
		}


		private static bool IsFluxGeneratedEntity(Entity ent)
		{
			if (ent == null)
				return false;

			if (ent.Layer == CopiedLayerName)
				return true;

			if (ent.Layer == MarkerLayerName)
				return true;

			ResultBuffer rb = ent.GetXDataForApplication(SheetCodeAppName);

			if (rb != null)
				return true;

			return false;
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

		private static bool IsEntityStrictlyInsideSheetFrame(
			Bounds2D frameBounds,
			Bounds2D entityBounds)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			if (entityBounds == null || !entityBounds.IsValid)
				return false;

			double frameMinSize =
				System.Math.Min(frameBounds.Width, frameBounds.Height);

			double tol = frameMinSize * 0.002;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 5.0)
				tol = 5.0;

			if (entityBounds.MinX < frameBounds.MinX - tol)
				return false;

			if (entityBounds.MaxX > frameBounds.MaxX + tol)
				return false;

			if (entityBounds.MinY < frameBounds.MinY - tol)
				return false;

			if (entityBounds.MaxY > frameBounds.MaxY + tol)
				return false;

			return true;
		}

		private static bool IsEntityInsideSheetFrameForCopy(
			Bounds2D frameBounds,
			Bounds2D entityBounds)
		{
			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			if (entityBounds == null || !entityBounds.IsValid)
				return false;

			double frameMinSize =
				System.Math.Min(frameBounds.Width, frameBounds.Height);

			double tol = frameMinSize * 0.003;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 8.0)
				tol = 8.0;

			// 1차: 완전 내부
			if (entityBounds.MinX >= frameBounds.MinX - tol &&
				entityBounds.MaxX <= frameBounds.MaxX + tol &&
				entityBounds.MinY >= frameBounds.MinY - tol &&
				entityBounds.MaxY <= frameBounds.MaxY + tol)
			{
				return true;
			}

			// 2차: 중심이 프레임 내부에 있으면 포함
			if (frameBounds.Contains(entityBounds.Center))
				return true;

			// 3차: 표/프레임 경계선 같은 아주 얇은 객체 보정
			if (!IsBoundsOverlapped(frameBounds, entityBounds))
				return false;

			double entityMinSize =
				System.Math.Min(entityBounds.Width, entityBounds.Height);

			double entityMaxSize =
				System.Math.Max(entityBounds.Width, entityBounds.Height);

			bool isThinLineLike =
				entityMinSize <= tol * 2.0 &&
				entityMaxSize <= System.Math.Max(frameBounds.Width, frameBounds.Height) * 1.05;

			if (!isThinLineLike)
				return false;

			double overlapRatio =
				GetBoundsOverlapRatio(entityBounds, frameBounds);

			if (overlapRatio >= 0.50)
				return true;

			return false;
		}

		private static double GetBoundsOverlapRatio(
			Bounds2D entityBounds,
			Bounds2D frameBounds)
		{
			double minX = System.Math.Max(entityBounds.MinX, frameBounds.MinX);
			double minY = System.Math.Max(entityBounds.MinY, frameBounds.MinY);
			double maxX = System.Math.Min(entityBounds.MaxX, frameBounds.MaxX);
			double maxY = System.Math.Min(entityBounds.MaxY, frameBounds.MaxY);

			double w = maxX - minX;
			double h = maxY - minY;

			if (w < 0.0)
				w = 0.0;

			if (h < 0.0)
				h = 0.0;

			double overlapArea = w * h;
			double entityArea = entityBounds.Width * entityBounds.Height;

			// 선 객체는 면적이 0일 수 있으므로 길이 기준 보정
			if (entityArea <= 0.000001)
			{
				double ew = entityBounds.Width;
				double eh = entityBounds.Height;

				if (ew >= eh)
				{
					if (ew <= 0.000001)
						return 0.0;

					return w / ew;
				}
				else
				{
					if (eh <= 0.000001)
						return 0.0;

					return h / eh;
				}
			}

			return overlapArea / entityArea;
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


		private static Bounds2D GetEntityWorldBounds(
			Transaction tr,
			Entity ent)
		{
			if (ent == null)
				return null;

			BlockReference br = ent as BlockReference;

			if (br != null)
				return GetBlockReferenceWorldBounds(tr, br);

			try
			{
				Extents3d ext = ent.GeometricExtents;

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

		private static Bounds2D GetBlockReferenceWorldBounds(
			Transaction tr,
			BlockReference br)
		{
			if (br == null)
				return null;

			try
			{
				BlockTableRecord btr =
					tr.GetObject(
						br.BlockTableRecord,
						OpenMode.ForRead) as BlockTableRecord;

				if (btr == null)
					return null;

				Bounds2D result = null;

				foreach (ObjectId childId in btr)
				{
					Entity child =
						tr.GetObject(childId, OpenMode.ForRead) as Entity;

					if (child == null)
						continue;

					Bounds2D childBounds =
						GetTransformedChildBounds(child, br.BlockTransform);

					if (childBounds == null || !childBounds.IsValid)
						continue;

					result = UnionBounds(result, childBounds);
				}

				return result;
			}
			catch
			{
				return null;
			}
		}

		private static Bounds2D GetTransformedChildBounds(
			Entity child,
			Matrix3d transform)
		{
			try
			{
				Extents3d ext = child.GeometricExtents;

				Point3d p1 = new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0).TransformBy(transform);
				Point3d p2 = new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0).TransformBy(transform);
				Point3d p3 = new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0).TransformBy(transform);
				Point3d p4 = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0).TransformBy(transform);

				double minX = System.Math.Min(System.Math.Min(p1.X, p2.X), System.Math.Min(p3.X, p4.X));
				double minY = System.Math.Min(System.Math.Min(p1.Y, p2.Y), System.Math.Min(p3.Y, p4.Y));
				double maxX = System.Math.Max(System.Math.Max(p1.X, p2.X), System.Math.Max(p3.X, p4.X));
				double maxY = System.Math.Max(System.Math.Max(p1.Y, p2.Y), System.Math.Max(p3.Y, p4.Y));

				return new Bounds2D(minX, minY, maxX, maxY);
			}
			catch
			{
				return null;
			}
		}

		private static Bounds2D UnionBounds(
			Bounds2D a,
			Bounds2D b)
		{
			if (a == null || !a.IsValid)
				return b;

			if (b == null || !b.IsValid)
				return a;

			return new Bounds2D(
				System.Math.Min(a.MinX, b.MinX),
				System.Math.Min(a.MinY, b.MinY),
				System.Math.Max(a.MaxX, b.MaxX),
				System.Math.Max(a.MaxY, b.MaxY));
		}

		private static List<SheetPlacement> CreateRowPreservingPlacements(
			List<SheetRegion> sheets,
			Bounds2D drawingBounds,
			double targetTopY,
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
			double currentTopY = targetTopY;

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