using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using FluxCad48.ShapeViewAnalysis.Loops;
using FluxCad48.Sheets;
using FluxCad48.CopiedSheets;
using System;
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


		[CommandMethod("FLUX_COPY_DETECTED_SHEETS_TO_RIGHT_FAST")]
		public void FluxCopyDetectedSheetsToRightFast()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptPointResult ppr1 =
				ed.GetPoint(new PromptPointOptions(
					"\n[FAST] 복사할 쉬트 영역의 첫 번째 구석점을 지정하세요: "));

			if (ppr1.Status != PromptStatus.OK)
				return;

			PromptCornerOptions pco =
				new PromptCornerOptions(
					"\n[FAST] 반대 구석점을 지정하세요: ",
					ppr1.Value);

			PromptPointResult ppr2 = ed.GetCorner(pco);

			if (ppr2.Status != PromptStatus.OK)
				return;

			PromptSelectionResult psr =
				ed.SelectCrossingWindow(ppr1.Value, ppr2.Value);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[FAST] 선택된 객체가 없습니다.");
				return;
			}

			ObjectId[] selectedIds = psr.Value.GetObjectIds();

			ed.SetImpliedSelection(selectedIds);

			PromptKeywordOptions confirmOptions =
				new PromptKeywordOptions(
					"\n[FAST] 선택 상태를 확인하세요. 계속 진행하시겠습니까? [Yes/No] <Yes>: ");

			confirmOptions.Keywords.Add("Yes");
			confirmOptions.Keywords.Add("No");
			confirmOptions.Keywords.Default = "Yes";
			confirmOptions.AllowNone = true;

			PromptResult confirmResult = ed.GetKeywords(confirmOptions);

			if (confirmResult.Status != PromptStatus.OK ||
				string.Equals(confirmResult.StringResult, "No", StringComparison.OrdinalIgnoreCase))
			{
				ed.SetImpliedSelection(new ObjectId[0]);
				ed.WriteMessage("\n[FAST] 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				List<Entity> selectedEntities = new List<Entity>();

				foreach (ObjectId id in selectedIds)
				{
					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

					if (ent != null)
						selectedEntities.Add(ent);
				}

				List<SheetFrameCandidate> frames =
					SheetFrameDetector.Detect(selectedEntities, null);

				Bounds2D selectionBounds = new Bounds2D(
					System.Math.Min(ppr1.Value.X, ppr2.Value.X),
					System.Math.Min(ppr1.Value.Y, ppr2.Value.Y),
					System.Math.Max(ppr1.Value.X, ppr2.Value.X),
					System.Math.Max(ppr1.Value.Y, ppr2.Value.Y));

				frames = frames
					.Where(f => IsFrameFullyInsideSelection(f.Bounds, selectionBounds))
					.ToList();

				frames = FilterDetachedSmallAuxiliaryFrames(
					frames,
					selectedEntities,
					null);

				if (frames.Count == 0)
				{
					ed.WriteMessage("\n[FAST] 탐지된 쉬트 프레임이 없습니다.");
					return;
				}

				int nextSheetIndex =
					GetNextSheetIndexFromDrawingFast(tr, db);

				List<SheetRegion> sheets =
					BuildSheetRegionsFromDetectedFramesForPureCopyFast(
						tr,
						db,
						frames,
						selectedIds,
						nextSheetIndex);

				if (sheets.Count == 0)
				{
					ed.WriteMessage("\n[FAST] 복사할 쉬트 내부 객체가 없습니다.");
					return;
				}

				Bounds2D drawingBounds = GetOriginalDrawingBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n[FAST] 원본 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				SheetArrangeOptions options = new SheetArrangeOptions();

				Bounds2D existingCopiedBounds =
					GetExistingCopiedSheetBounds(tr, db);

				List<SheetPlacement> placements =
					CreateWorkspacePlacementsFast(
						sheets,
						drawingBounds,
						existingCopiedBounds,
						options);

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

					ObjectIdCollection idsToClone = new ObjectIdCollection();
					HashSet<ObjectId> sourceTopLevelIds = new HashSet<ObjectId>();

					CopiedSheets.CopiedSheetInfo copiedInfo = new CopiedSheets.CopiedSheetInfo();

					copiedInfo.SheetCode = sheetCode;
					copiedInfo.SourceBounds = placement.SourceBounds;
					copiedInfo.MoveX = placement.MoveX;
					copiedInfo.MoveY = placement.MoveY;

					foreach (ObjectId id in placement.SourceSheet.EntityIds)
					{
						idsToClone.Add(id);
						sourceTopLevelIds.Add(id);
					}

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

						if (!sourceTopLevelIds.Contains(pair.Key))
							continue;

						Entity clonedEntity =
							tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;

						if (clonedEntity == null)
							continue;

						clonedEntity.TransformBy(
							Matrix3d.Displacement(displacement));

						SetSheetCodeXData(tr, db, clonedEntity, sheetCode);

						copiedInfo.AddCopiedEntity(pair.Key, pair.Value);

						clonedCount++;
					}

					totalCloned += clonedCount;

					Bounds2D copiedFrameBounds = new Bounds2D(
						placement.SourceBounds.MinX + placement.MoveX,
						placement.SourceBounds.MinY + placement.MoveY,
						placement.SourceBounds.MaxX + placement.MoveX,
						placement.SourceBounds.MaxY + placement.MoveY);

					copiedInfo.CopiedBounds = copiedFrameBounds;

					Polyline copiedFrame =
						BricscadEntityTools.CreateRectanglePolyline(copiedFrameBounds);

					copiedFrame.Layer = CopiedLayerName;
					copiedFrame.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
					copiedFrame.LineWeight = LineWeight.LineWeight050;

					modelSpace.AppendEntity(copiedFrame);
					tr.AddNewlyCreatedDBObject(copiedFrame, true);
					SetSheetCodeXData(tr, db, copiedFrame, sheetCode);

					copiedInfo.CopiedFrameObjectId = copiedFrame.ObjectId;

					Polyline sourceMarker =
						BricscadEntityTools.CreateRectanglePolyline(
							placement.SourceBounds);

					sourceMarker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
					sourceMarker.LineWeight = LineWeight.LineWeight050;
					sourceMarker.Layer = MarkerLayerName;

					modelSpace.AppendEntity(sourceMarker);
					tr.AddNewlyCreatedDBObject(sourceMarker, true);

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

					copiedInfo.CopiedLabelObjectId = copiedLabel.ObjectId;

					CopiedSheetRegistry.Register(copiedInfo);
				}

				tr.Commit();

				ed.SetImpliedSelection(new ObjectId[0]);

				ed.WriteMessage(
					"\nFLUX_COPY_DETECTED_SHEETS_TO_RIGHT_FAST 완료: " +
					"DetectedSheets=" + sheets.Count +
					", TotalCloned=" + totalCloned);
			}
		}


		private static int GetNextSheetIndexFromDrawingFast(
	Transaction tr,
	Database db)
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

			return maxNumber;
		}

		private static List<SheetRegion> BuildSheetRegionsFromDetectedFramesForPureCopyFast(
	Transaction tr,
	Database db,
	List<SheetFrameCandidate> frames,
	ObjectId[] selectedIds,
	int startIndex)
		{
			frames = SortFramesTopToBottomLeftToRight(frames);

			List<SheetRegion> allSheets = new List<SheetRegion>();

			for (int i = 0; i < frames.Count; i++)
			{
				SheetRegion sheet = new SheetRegion();
				sheet.Bounds = frames[i].Bounds;
				sheet.Index = startIndex + i;
				allSheets.Add(sheet);
			}

			foreach (ObjectId id in selectedIds)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				if (IsCopyCommandGeneratedMarker(ent))
					continue;

				int ownerIndex = -1;

				int forcedFrameIndex =
					FindFrameIndexByFrameOwnerBlock(
						frames,
						id,
						ent.Handle.ToString());

				if (forcedFrameIndex >= 0)
					ownerIndex = forcedFrameIndex;

				Bounds2D entityBounds = null;

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
					entityBounds =
						GetEntityWorldBoundsRecursive(
							tr,
							ent,
							Matrix3d.Identity,
							0);

					if (IsUsableBoundsForOwnership(entityBounds))
					{
						ownerIndex =
							FindBestOwningSheetIndex(
								frames,
								entityBounds);
					}
				}

				if (ownerIndex < 0 && IsUsableBoundsForOwnership(entityBounds))
				{
					ownerIndex =
						FindBestOwningSheetIndexForBoundsEvenIfFlat(
							frames,
							entityBounds);
				}

				if (ownerIndex < 0)
				{
					BlockReference br = ent as BlockReference;

					if (br != null)
					{
						ownerIndex =
							FindBestOwningSheetIndexForPoint(
								frames,
								br.Position);
					}
				}

				if (ownerIndex < 0)
					continue;

				if (ownerIndex >= allSheets.Count)
					continue;

				if (!allSheets[ownerIndex].EntityIds.Contains(id))
					allSheets[ownerIndex].EntityIds.Add(id);
			}

			List<SheetRegion> result = new List<SheetRegion>();

			for (int i = 0; i < allSheets.Count; i++)
			{
				SheetRegion sheet = allSheets[i];

				if (sheet.EntityIds.Count == 0)
					continue;

				sheet.Index = startIndex + result.Count;
				result.Add(sheet);
			}

			return result;
		}

		private static List<SheetPlacement> CreateWorkspacePlacementsFast(
	List<SheetRegion> sheets,
	Bounds2D drawingBounds,
	Bounds2D existingCopiedBounds,
	SheetArrangeOptions options)
		{
			List<SheetPlacement> result = new List<SheetPlacement>();

			if (sheets == null || sheets.Count == 0)
				return result;

			if (drawingBounds == null || !drawingBounds.IsValid)
				return result;

			double workspaceOffsetX = 3000.0;
			double workspaceWidth = 20000.0;

			double columnGap = options.ColumnGap;
			double rowGap = options.RowGap;

			double startX = drawingBounds.MaxX + workspaceOffsetX;
			double limitX = startX + workspaceWidth;

			double cursorX = startX;
			double cursorTopY = drawingBounds.MaxY;

			if (existingCopiedBounds != null && existingCopiedBounds.IsValid)
				cursorTopY = existingCopiedBounds.MinY - 300.0;

			double currentRowBottomY = double.MaxValue;
			int rowIndex = 0;
			int columnIndex = 0;

			List<SheetRegion> ordered = new List<SheetRegion>(sheets);

			for (int i = 0; i < ordered.Count; i++)
			{
				SheetRegion sheet = ordered[i];

				double w = sheet.Bounds.Width;
				double h = sheet.Bounds.Height;

				if (cursorX > startX && cursorX + w > limitX)
				{
					cursorX = startX;
					cursorTopY = currentRowBottomY - rowGap;
					currentRowBottomY = double.MaxValue;

					rowIndex++;
					columnIndex = 0;
				}

				double targetMinX = cursorX;
				double targetMaxY = cursorTopY;

				double moveX = targetMinX - sheet.Bounds.MinX;
				double moveY = targetMaxY - sheet.Bounds.MaxY;

				SheetPlacement placement = new SheetPlacement();
				placement.SourceSheet = sheet;
				placement.SourceBounds = sheet.Bounds;
				placement.TargetBottomLeft =
					new CadPoint2D(targetMinX, targetMaxY - h);
				placement.MoveX = moveX;
				placement.MoveY = moveY;
				placement.RowIndex = rowIndex;
				placement.ColumnIndex = columnIndex;

				result.Add(placement);

				double placedBottomY = sheet.Bounds.MinY + moveY;

				if (placedBottomY < currentRowBottomY)
					currentRowBottomY = placedBottomY;

				cursorX += w + columnGap;
				columnIndex++;
			}

			return result;
		}



		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}

		[CommandMethod("FLUX_DEBUG_BLOCK_VISUAL_BOUNDS")]
		public void FluxDebugBlockVisualBounds()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\nVisual Bounds를 확인할 BlockReference를 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

				int count = 0;

				foreach (ObjectId id in psr.Value.GetObjectIds())
				{
					Entity ent =
						tr.GetObject(id, OpenMode.ForRead) as Entity;

					BlockReference br = ent as BlockReference;

					if (br == null)
						continue;

					Bounds2D geometricBounds =
						GetBlockReferenceGeometricExtentsBounds(br);

					Bounds2D visualBounds =
						GetBlockReferenceVisualGeometryBounds(
							tr,
							br,
							br.BlockTransform,
							0,
							ed);

					ed.WriteMessage(
						"\n[BlockVisualBounds] Handle=" + br.Handle +
						", BlockName=" + GetBlockName(tr, br) +
						", GeometricBounds=" + geometricBounds +
						", VisualBounds=" + visualBounds);

					if (geometricBounds != null && geometricBounds.IsValid)
					{
						Polyline red =
							BricscadEntityTools.CreateRectanglePolyline(
								geometricBounds);

						red.Layer = MarkerLayerName;
						red.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
						red.LineWeight = LineWeight.LineWeight050;

						modelSpace.AppendEntity(red);
						tr.AddNewlyCreatedDBObject(red, true);
					}

					if (visualBounds != null && visualBounds.IsValid)
					{
						Polyline green =
							BricscadEntityTools.CreateRectanglePolyline(
								visualBounds);

						green.Layer = MarkerLayerName;
						green.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
						green.LineWeight = LineWeight.LineWeight070;

						modelSpace.AppendEntity(green);
						tr.AddNewlyCreatedDBObject(green, true);

						DBText label =
							CreateDebugLabel(
								visualBounds.MinX,
								visualBounds.MaxY,
								"VISUAL " + br.Handle,
								MarkerLayerName);

						label.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);

						modelSpace.AppendEntity(label);
						tr.AddNewlyCreatedDBObject(label, true);
					}

					count++;
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_DEBUG_BLOCK_VISUAL_BOUNDS 완료. BlockReference Count=" +
					count);
			}
		}

		private static Bounds2D GetBlockReferenceVisualGeometryBounds(
	Transaction tr,
	BlockReference br,
	Matrix3d transform,
	int depth,
	Editor ed)
		{
			if (tr == null || br == null)
				return null;

			if (depth > 20)
				return null;

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

				BlockReference childBr = child as BlockReference;

				if (childBr != null)
				{
					Matrix3d childTransform =
						transform * childBr.BlockTransform;

					Bounds2D childBlockBounds =
						GetBlockReferenceVisualGeometryBounds(
							tr,
							childBr,
							childTransform,
							depth + 1,
							ed);

					result = UnionBounds(result, childBlockBounds);
					continue;
				}

				if (!IsVisualGeometryEntityForBounds(child))
					continue;

				Bounds2D childBounds =
					GetPrimitiveEntityTransformedBounds(
						child,
						transform);

				if (childBounds == null || !childBounds.IsValid)
					continue;

				if (!IsReasonableChildBoundsForBlockVisual(br, childBounds))
				{
					ed?.WriteMessage(
						"\n[SkipAbnormalVisualChild] Parent=" +
						br.Handle +
						", Child=" +
						child.Handle +
						", Type=" +
						child.GetType().Name +
						", Bounds=" +
						childBounds);

					continue;
				}

				result = UnionBounds(result, childBounds);
			}

			return result;
		}

		private static bool IsVisualGeometryEntityForBounds(Entity ent)
		{
			if (ent == null)
				return false;

			if (ent is Line)
				return true;

			if (ent is Polyline)
				return true;

			if (ent is Polyline2d)
				return true;

			if (ent is Polyline3d)
				return true;

			if (ent is Circle)
				return true;

			if (ent is Arc)
				return true;

			if (ent is Ellipse)
				return true;

			return false;
		}

		private static bool IsReasonableChildBoundsForBlockVisual(
	BlockReference owner,
	Bounds2D childBounds)
		{
			if (owner == null || childBounds == null || !childBounds.IsValid)
				return false;

			Bounds2D ownerBounds = GetBlockReferenceGeometricExtentsBounds(owner);

			if (ownerBounds == null || !ownerBounds.IsValid)
				return true;

			double margin =
				Math.Max(ownerBounds.Width, ownerBounds.Height) * 3.0;

			if (margin < 1000.0)
				margin = 1000.0;

			if (childBounds.MaxX < ownerBounds.MinX - margin)
				return false;

			if (childBounds.MinX > ownerBounds.MaxX + margin)
				return false;

			if (childBounds.MaxY < ownerBounds.MinY - margin)
				return false;

			if (childBounds.MinY > ownerBounds.MaxY + margin)
				return false;

			return true;
		}



		private static Bounds2D GetBlockReferenceGeometricExtentsBounds(
	BlockReference br)
		{
			if (br == null)
				return null;

			try
			{
				Extents3d ext = br.GeometricExtents;

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


		[CommandMethod("FLUX_DEBUG_MARK_ENTITY_BY_HANDLE")]
		public void FluxDebugMarkEntityByHandle()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptStringOptions pso =
				new PromptStringOptions("\n표시할 Entity Handle을 입력하세요: ");

			pso.AllowSpaces = false;

			PromptResult pr = ed.GetString(pso);

			if (pr.Status != PromptStatus.OK)
				return;

			string handleText = pr.StringResult.Trim();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				ObjectId id;

				try
				{
					Handle handle =
						new Handle(
							System.Convert.ToInt64(handleText, 16));

					id = db.GetObjectId(false, handle, 0);
				}
				catch
				{
					ed.WriteMessage("\nHandle을 ObjectId로 변환하지 못했습니다.");
					return;
				}

				if (id.IsNull)
				{
					ed.WriteMessage(
						"\n해당 Handle을 현재 도면에서 찾지 못했습니다. Handle=" +
						handleText);
					return;
				}

				Entity ent = null;

				try
				{
					ent =
						tr.GetObject(id, OpenMode.ForRead) as Entity;
				}
				catch
				{
					ed.WriteMessage(
						"\nObjectId는 찾았지만 Entity를 열지 못했습니다. Handle=" +
						handleText);
					return;
				}

				if (ent == null)
				{
					ed.WriteMessage("\n해당 Handle은 Entity가 아닙니다.");
					return;
				}

				Bounds2D bounds =
					GetEntityWorldBounds(tr, ent);

				BlockReference br = ent as BlockReference;

				ed.WriteMessage(
					"\n[MarkEntity] Handle=" + handleText +
					", Type=" + ent.GetType().Name +
					", Layer=" + ent.Layer +
					", Bounds=" + bounds);

				if (br != null)
				{
					ed.WriteMessage(
						"\n[MarkEntity] BlockName=" + GetBlockName(tr, br) +
						", Position=(" +
						br.Position.X.ToString("0.###") + "," +
						br.Position.Y.ToString("0.###") + "," +
						br.Position.Z.ToString("0.###") + ")");
				}

				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

				if (bounds != null && IsUsableBoundsForOwnership(bounds))
				{
					Polyline rect =
						BricscadEntityTools.CreateRectanglePolyline(bounds);

					rect.Layer = MarkerLayerName;
					rect.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
					rect.LineWeight = LineWeight.LineWeight070;

					modelSpace.AppendEntity(rect);
					tr.AddNewlyCreatedDBObject(rect, true);

					DBText label =
						CreateDebugLabel(
							bounds.MinX,
							bounds.MaxY,
							"Handle " + handleText,
							MarkerLayerName);

					modelSpace.AppendEntity(label);
					tr.AddNewlyCreatedDBObject(label, true);
				}
				else if (br != null)
				{
					double size = 200.0;

					Bounds2D markerBounds =
						new Bounds2D(
							br.Position.X - size,
							br.Position.Y - size,
							br.Position.X + size,
							br.Position.Y + size);

					Polyline rect =
						BricscadEntityTools.CreateRectanglePolyline(markerBounds);

					rect.Layer = MarkerLayerName;
					rect.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
					rect.LineWeight = LineWeight.LineWeight070;

					modelSpace.AppendEntity(rect);
					tr.AddNewlyCreatedDBObject(rect, true);

					DBText label =
						CreateDebugLabel(
							br.Position.X,
							br.Position.Y + size,
							"Handle " + handleText + " POS",
							MarkerLayerName);

					modelSpace.AppendEntity(label);
					tr.AddNewlyCreatedDBObject(label, true);

					DBPoint pt = new DBPoint(br.Position);
					pt.Layer = MarkerLayerName;
					pt.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);

					modelSpace.AppendEntity(pt);
					tr.AddNewlyCreatedDBObject(pt, true);
				}

				tr.Commit();

				ed.WriteMessage("\nFLUX_DEBUG_MARK_ENTITY_BY_HANDLE 완료.");
			}
		}

		private static string GetBlockName(
	Transaction tr,
	BlockReference br)
		{
			if (br == null)
				return "";

			try
			{
				BlockTableRecord btr =
					tr.GetObject(
						br.BlockTableRecord,
						OpenMode.ForRead) as BlockTableRecord;

				if (btr == null)
					return "";

				return btr.Name;
			}
			catch
			{
				return "";
			}
		}

		private static DBText CreateDebugLabel(
			double x,
			double y,
			string text,
			string layerName)
		{
			DBText label = new DBText();

			label.TextString = text;
			label.Position = new Point3d(x, y, 0);
			label.Height = 80.0;
			label.Layer = layerName;
			label.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);

			return label;
		}


		[CommandMethod("FLUX_DEBUG_ANALYZE_COPIED_SHEET_V2")]
		public void FluxDebugAnalyzeCopiedSheet()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptEntityOptions peo =
				new PromptEntityOptions(
					"\n분석할 복사된 쉬트 내부 개체 하나를 선택하세요: ");

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Entity picked =
					tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;

				if (picked == null)
				{
					ed.WriteMessage("\n선택한 객체가 Entity가 아닙니다.");
					return;
				}

				string sheetCode = TryReadSheetCodeFromEntity(picked);

				if (string.IsNullOrWhiteSpace(sheetCode))
				{
					ed.WriteMessage(
						"\n선택한 객체에는 FLUX_SHEET XData가 없습니다.");
					return;
				}

				List<Entity> sheetEntities =
					CollectEntitiesBySheetCode(tr, db, sheetCode);

				Bounds2D sheetBounds =
					GetBoundsFromEntities(tr, sheetEntities);

				ed.WriteMessage(
					"\n[CopiedSheetAnalyze] SheetCode=" + sheetCode +
					", EntityCount=" + sheetEntities.Count +
					", Bounds=" + sheetBounds);

				if (sheetBounds == null || !sheetBounds.IsValid)
				{
					ed.WriteMessage("\n쉬트 Bounds 계산 실패.");
					return;
				}

				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

				Polyline rect =
					BricscadEntityTools.CreateRectanglePolyline(sheetBounds);

				rect.Layer = MarkerLayerName;
				rect.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
				rect.LineWeight = LineWeight.LineWeight070;

				modelSpace.AppendEntity(rect);
				tr.AddNewlyCreatedDBObject(rect, true);

				DBText label =
					CreateSheetCodeText(
						sheetBounds,
						0,
						0,
						"DEBUG-" + sheetCode,
						MarkerLayerName,
						1);

				modelSpace.AppendEntity(label);
				tr.AddNewlyCreatedDBObject(label, true);

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_DEBUG_ANALYZE_COPIED_SHEET_V2 완료.");
			}
		}

		private static string TryReadSheetCodeFromEntity(Entity ent)
		{
			if (ent == null)
				return null;

			ResultBuffer rb =
				ent.GetXDataForApplication(SheetCodeAppName);

			if (rb == null)
				return null;

			TypedValue[] values = rb.AsArray();

			foreach (TypedValue value in values)
			{
				if (value.TypeCode != (int)DxfCode.ExtendedDataAsciiString)
					continue;

				string text = value.Value as string;

				if (!string.IsNullOrWhiteSpace(text))
					return text.Trim();
			}

			return null;
		}

		private static List<Entity> CollectEntitiesBySheetCode(
			Transaction tr,
			Database db,
			string sheetCode)
		{
			var result = new List<Entity>();

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

				string code = TryReadSheetCodeFromEntity(ent);

				if (code == sheetCode)
					result.Add(ent);
			}

			return result;
		}

		private static Bounds2D GetBoundsFromEntities(
			Transaction tr,
			List<Entity> entities)
		{
			Bounds2D result = null;

			foreach (Entity ent in entities)
			{
				Bounds2D b = GetEntityWorldBounds(tr, ent);

				if (b == null || !b.IsValid)
					continue;

				result = UnionBounds(result, b);
			}

			return result;
		}



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

			ed.SetImpliedSelection(selectedIds);

			ed.WriteMessage(
				"\n[DetectedSheetCopy] 드래그 범위로 선택된 객체를 화면에 표시했습니다.");

			PromptKeywordOptions confirmOptions =
				new PromptKeywordOptions(
					"\n선택 상태를 확인하세요. 계속 진행하시겠습니까? [Yes/No] <Yes>: ");

			confirmOptions.Keywords.Add("Yes");
			confirmOptions.Keywords.Add("No");
			confirmOptions.Keywords.Default = "Yes";
			confirmOptions.AllowNone = true;

			PromptResult confirmResult =
				ed.GetKeywords(confirmOptions);

			if (confirmResult.Status != PromptStatus.OK)
			{
				ed.SetImpliedSelection(new ObjectId[0]);
				ed.WriteMessage("\n[DetectedSheetCopy] 선택 확인이 취소되었습니다.");
				return;
			}

			if (string.Equals(
				confirmResult.StringResult,
				"No",
				StringComparison.OrdinalIgnoreCase))
			{
				ed.SetImpliedSelection(new ObjectId[0]);
				ed.WriteMessage("\n[DetectedSheetCopy] 사용자가 선택 결과를 취소했습니다.");
				return;
			}

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

				// 좌표 검증용: 알고리즘이 사용하는 Bounds를 CAD 화면에 직접 표시
				/*
				SheetFrameDetector.DebugDrawBoundsForCoordinateCheck(
					tr,
					db,
					ed,
					selectedEntities);
				*/

				List<SheetRegion> sheets =
					BuildSheetRegionsFromDetectedFrames(
						tr,
						db,
						frames,
						selectedIds,
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
					GetOriginalDrawingBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n원본 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				SheetArrangeOptions options = new SheetArrangeOptions();

				Bounds2D existingCopiedBounds =
					GetExistingCopiedSheetBounds(tr, db);

				List<SheetPlacement> placements =
					CreateWorkspacePlacements(
						sheets,
						drawingBounds,
						existingCopiedBounds,
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

					Bounds2D newCopiedFrameBounds = new Bounds2D(
						placement.SourceBounds.MinX + placement.MoveX,
						placement.SourceBounds.MinY + placement.MoveY,
						placement.SourceBounds.MaxX + placement.MoveX,
						placement.SourceBounds.MaxY + placement.MoveY);

					Polyline copiedFrame =
						BricscadEntityTools.CreateRectanglePolyline(newCopiedFrameBounds);

					copiedFrame.Layer = CopiedLayerName;
					copiedFrame.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
					copiedFrame.LineWeight = LineWeight.LineWeight050;

					modelSpace.AppendEntity(copiedFrame);
					tr.AddNewlyCreatedDBObject(copiedFrame, true);
					SetSheetCodeXData(tr, db, copiedFrame, sheetCode);


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



		[CommandMethod("FLUX_COPY_DETECTED_SHEETS_TO_RIGHT_V2")]
		public void FluxCopyDetectedSheetsToRightV2()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptPointOptions ppo1 = new PromptPointOptions(
				"\n[V2] 복사할 쉬트 영역의 첫 번째 구석점을 지정하세요: ");

			PromptPointResult ppr1 = ed.GetPoint(ppo1);

			if (ppr1.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[V2] 첫 번째 점 선택이 취소되었습니다.");
				return;
			}

			PromptCornerOptions pco = new PromptCornerOptions(
				"\n[V2] 반대 구석점을 지정하세요: ",
				ppr1.Value);

			PromptPointResult ppr2 = ed.GetCorner(pco);

			if (ppr2.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[V2] 반대 구석점 선택이 취소되었습니다.");
				return;
			}

			PromptSelectionResult psr =
				ed.SelectCrossingWindow(
					ppr1.Value,
					ppr2.Value);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[V2] 선택된 객체가 없습니다.");
				return;
			}

			ObjectId[] selectedIds = psr.Value.GetObjectIds();

			ed.SetImpliedSelection(selectedIds);

			ed.WriteMessage(
				"\n[V2] 드래그 범위로 선택된 객체를 화면에 표시했습니다.");

			PromptKeywordOptions confirmOptions =
				new PromptKeywordOptions(
					"\n[V2] 선택 상태를 확인하세요. 계속 진행하시겠습니까? [Yes/No] <Yes>: ");

			confirmOptions.Keywords.Add("Yes");
			confirmOptions.Keywords.Add("No");
			confirmOptions.Keywords.Default = "Yes";
			confirmOptions.AllowNone = true;

			PromptResult confirmResult =
				ed.GetKeywords(confirmOptions);

			if (confirmResult.Status != PromptStatus.OK)
			{
				ed.SetImpliedSelection(new ObjectId[0]);
				ed.WriteMessage("\n[V2] 선택 확인이 취소되었습니다.");
				return;
			}

			if (string.Equals(
				confirmResult.StringResult,
				"No",
				StringComparison.OrdinalIgnoreCase))
			{
				ed.SetImpliedSelection(new ObjectId[0]);
				ed.WriteMessage("\n[V2] 사용자가 선택 결과를 취소했습니다.");
				return;
			}

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
					"\n[V2] SelectedEntities=" +
					selectedEntities.Count);

				List<SheetFrameCandidate> frames =
					SheetFrameDetector.Detect(selectedEntities, ed);

				Bounds2D selectionBounds = new Bounds2D(
					System.Math.Min(ppr1.Value.X, ppr2.Value.X),
					System.Math.Min(ppr1.Value.Y, ppr2.Value.Y),
					System.Math.Max(ppr1.Value.X, ppr2.Value.X),
					System.Math.Max(ppr1.Value.Y, ppr2.Value.Y));

				frames = frames
					.Where(f => IsFrameFullyInsideSelection(f.Bounds, selectionBounds))
					.ToList();

				frames = FilterDetachedSmallAuxiliaryFrames(
					frames,
					selectedEntities,
					ed);

				ed.WriteMessage(
					"\n[V2] DetectedFrames=" +
					frames.Count);

				if (frames.Count == 0)
				{
					ed.WriteMessage(
						"\n[V2] 탐지된 쉬트 프레임이 없습니다.");
					return;
				}

				int nextSheetIndex = GetNextSheetIndexFromDrawing(tr, db, ed);

				List<SheetRegion> sheets =
					BuildSheetRegionsFromDetectedFramesForPureCopyV2(
						tr,
						db,
						frames,
						selectedIds,
						nextSheetIndex,
						ed);

				ed.WriteMessage(
					"\n[V2] BuiltSheets=" +
					sheets.Count);

				if (sheets.Count == 0)
				{
					ed.WriteMessage(
						"\n[V2] 복사할 쉬트 내부 객체가 없습니다.");
					return;
				}

				Bounds2D drawingBounds =
					GetOriginalDrawingBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n[V2] 원본 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				SheetArrangeOptions options = new SheetArrangeOptions();

				Bounds2D existingCopiedBounds =
					GetExistingCopiedSheetBounds(tr, db);

				List<SheetPlacement> placements =
					CreateWorkspacePlacements(
						sheets,
						drawingBounds,
						existingCopiedBounds,
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

					ObjectIdCollection idsToClone = new ObjectIdCollection();

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


					HashSet<ObjectId> sourceTopLevelIds = new HashSet<ObjectId>();

					foreach (ObjectId id in placement.SourceSheet.EntityIds)
					{
						idsToClone.Add(id);
						sourceTopLevelIds.Add(id);
					}


					int clonedCount = 0;
					int dependencySkippedCount = 0;

					foreach (IdPair pair in mapping)
					{
						if (!pair.IsCloned)
							continue;

						if (!sourceTopLevelIds.Contains(pair.Key))
						{
							dependencySkippedCount++;
							continue;
						}

						Entity clonedEntity =
							tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;

						if (clonedEntity == null)
							continue;

						clonedEntity.TransformBy(
							Matrix3d.Displacement(displacement));

						SetSheetCodeXData(tr, db, clonedEntity, sheetCode);

						clonedCount++;
					}

					totalCloned += clonedCount;

					Bounds2D copiedFrameBounds = new Bounds2D(
						placement.SourceBounds.MinX + placement.MoveX,
						placement.SourceBounds.MinY + placement.MoveY,
						placement.SourceBounds.MaxX + placement.MoveX,
						placement.SourceBounds.MaxY + placement.MoveY);

					Polyline copiedFrame =
						BricscadEntityTools.CreateRectanglePolyline(copiedFrameBounds);

					copiedFrame.Layer = CopiedLayerName;
					copiedFrame.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
					copiedFrame.LineWeight = LineWeight.LineWeight050;

					modelSpace.AppendEntity(copiedFrame);
					tr.AddNewlyCreatedDBObject(copiedFrame, true);
					SetSheetCodeXData(tr, db, copiedFrame, sheetCode);

					Polyline sourceMarker =
						BricscadEntityTools.CreateRectanglePolyline(
							placement.SourceBounds);

					sourceMarker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
					sourceMarker.LineWeight = LineWeight.LineWeight050;
					sourceMarker.Layer = MarkerLayerName;

					modelSpace.AppendEntity(sourceMarker);
					tr.AddNewlyCreatedDBObject(sourceMarker, true);

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
						"\n[V2] Sheet=" +
						placement.SourceSheet.Index +
						", Code=" + sheetCode +
						", SourceEntities=" +
						placement.SourceSheet.EntityIds.Count +
						", Cloned=" +
						clonedCount +
						", DependencySkipped=" +
						dependencySkippedCount +
						", Bounds=" +
						placement.SourceBounds);
				}

				tr.Commit();

				ed.SetImpliedSelection(new ObjectId[0]);

				ed.WriteMessage(
					"\nFLUX_COPY_DETECTED_SHEETS_TO_RIGHT_V2 완료: " +
					"DetectedSheets=" + sheets.Count +
					", TotalCloned=" + totalCloned);
			}
		}

		private static List<SheetFrameCandidate> FilterDetachedSmallAuxiliaryFrames(
	List<SheetFrameCandidate> frames,
	IReadOnlyList<Entity> selectedEntities,
	Editor ed)
		{
			var result = new List<SheetFrameCandidate>();

			foreach (SheetFrameCandidate frame in frames)
			{
				int visualCount = CountMeaningfulEntitiesInsideFrame(
					selectedEntities,
					frame.Bounds);

				bool keep = visualCount >= 8;

				if (!keep)
				{
					ed?.WriteMessage(
						"\n[V2 SkipAuxSmallFrame] Handle=" +
						frame.Handle +
						", Type=" +
						frame.EntityType +
						", VisualCount=" +
						visualCount +
						", Bounds=" +
						frame.Bounds);
					continue;
				}

				result.Add(frame);
			}

			return result;
		}


		private static int CountMeaningfulEntitiesInsideFrame(
	IReadOnlyList<Entity> entities,
	Bounds2D frameBounds)
		{
			if (entities == null || frameBounds == null || !frameBounds.IsValid)
				return 0;

			int count = 0;

			foreach (Entity ent in entities)
			{
				if (ent == null)
					continue;

				if (IsCopyCommandGeneratedMarker(ent))
					continue;

				if (!IsMeaningfulEntityForSheetFrame(ent))
					continue;

				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				if (IsEntityInsideSheetFrameForCopy(frameBounds, b))
					count++;
			}

			return count;
		}

		private static bool IsMeaningfulEntityForSheetFrame(Entity ent)
		{
			if (ent == null)
				return false;

			if (ent is Line)
				return true;

			if (ent is Polyline)
				return true;

			if (ent is Polyline2d)
				return true;

			if (ent is Polyline3d)
				return true;

			if (ent is Circle)
				return true;

			if (ent is Arc)
				return true;

			if (ent is Ellipse)
				return true;

			if (ent is BlockReference)
				return true;

			if (ent is DBText)
				return true;

			if (ent is MText)
				return true;

			if (ent is Dimension)
				return true;

			return false;
		}

		private static Bounds2D GetOriginalDrawingBounds(
	Transaction tr,
	Database db)
		{
			Bounds2D result = null;

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
				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;

				if (ent == null)
					continue;

				if (IsFluxGeneratedEntity(ent))
					continue;

				Bounds2D b = GetEntityWorldBounds(tr, ent);

				if (b == null || !b.IsValid)
					continue;

				result = UnionBounds(result, b);
			}

			return result;
		}


		private static List<SheetPlacement> CreateWorkspacePlacements(
			List<SheetRegion> sheets,
			Bounds2D drawingBounds,
			Bounds2D existingCopiedBounds,
			SheetArrangeOptions options,
			Editor ed)
		{
			var result = new List<SheetPlacement>();

			if (sheets == null || sheets.Count == 0)
				return result;

			if (drawingBounds == null || !drawingBounds.IsValid)
				return result;

			double workspaceOffsetX = 3000.0;
			double workspaceWidth = 20000.0;

			double columnGap = options.ColumnGap;
			double rowGap = options.RowGap;

			double startX = drawingBounds.MaxX + workspaceOffsetX;
			double limitX = startX + workspaceWidth;

			double cursorX = startX;
			double cursorTopY = drawingBounds.MaxY;

			if (existingCopiedBounds != null && existingCopiedBounds.IsValid)
			{
				cursorTopY = existingCopiedBounds.MinY - 300.0;
			}

			double currentRowBottomY = double.MaxValue;
			int rowIndex = 0;
			int columnIndex = 0;

			// 원래 BuildSheetRegionsFromDetectedFrames에서 넘어온 순서 유지
			List<SheetRegion> ordered = new List<SheetRegion>(sheets);

			for (int i = 0; i < ordered.Count; i++)
			{
				SheetRegion sheet = ordered[i];

				double w = sheet.Bounds.Width;
				double h = sheet.Bounds.Height;

				if (cursorX > startX && cursorX + w > limitX)
				{
					cursorX = startX;
					cursorTopY = currentRowBottomY - rowGap;
					currentRowBottomY = double.MaxValue;

					rowIndex++;
					columnIndex = 0;
				}

				double targetMinX = cursorX;
				double targetMaxY = cursorTopY;

				double moveX = targetMinX - sheet.Bounds.MinX;
				double moveY = targetMaxY - sheet.Bounds.MaxY;

				SheetPlacement placement = new SheetPlacement();
				placement.SourceSheet = sheet;
				placement.SourceBounds = sheet.Bounds;
				placement.TargetBottomLeft =
					new CadPoint2D(targetMinX, targetMaxY - h);
				placement.MoveX = moveX;
				placement.MoveY = moveY;
				placement.RowIndex = rowIndex;
				placement.ColumnIndex = columnIndex;

				result.Add(placement);

				double placedBottomY = sheet.Bounds.MinY + moveY;

				if (placedBottomY < currentRowBottomY)
					currentRowBottomY = placedBottomY;

				ed.WriteMessage(
					"\n[WorkspaceArrange] Row=" + rowIndex +
					", Col=" + columnIndex +
					", Sheet=" + GetSheetCode(sheet) +
					", W=" + w.ToString("0.###") +
					", H=" + h.ToString("0.###") +
					", TargetMinX=" + targetMinX.ToString("0.###") +
					", TargetMaxY=" + targetMaxY.ToString("0.###") +
					", MoveX=" + moveX.ToString("0.###") +
					", MoveY=" + moveY.ToString("0.###"));

				cursorX += w + columnGap;
				columnIndex++;
			}

			return result;
		}

		private static Bounds2D GetExistingCopiedSheetBounds(
			Transaction tr,
			Database db)
		{
			Bounds2D result = null;

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
					tr.GetObject(id, OpenMode.ForRead, false) as Entity;

				if (ent == null)
					continue;

				bool isCopied =
					ent.Layer == CopiedLayerName ||
					TryReadSheetCodeFromEntity(ent) != null;

				if (!isCopied)
					continue;

				Bounds2D b = GetEntityWorldBounds(tr, ent);

				if (b == null || !b.IsValid)
					continue;

				result = UnionBounds(result, b);
			}

			return result;
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
			ObjectId[] selectedIds,
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

			Bounds2D unownedBounds = null;
			int unownedCount = 0;

			foreach (ObjectId id in selectedIds)
			{
				Entity ent =
					tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				if (IsCopyCommandGeneratedMarker(ent))
				{
					ed.WriteMessage(
						"\n[SkipFluxGenerated] Handle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name +
						", Layer=" +
						ent.Layer);

					continue;
				}

				int ownerIndex = -1;
				Bounds2D entityBounds = null;

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
					entityBounds = GetEntityWorldBoundsRecursive(
						tr,
						ent,
						Matrix3d.Identity,
						0);

					if (IsUsableBoundsForOwnership(entityBounds))
					{
						ownerIndex =
							FindBestOwningSheetIndex(
								frames,
								entityBounds);
					}
				}

				if (ownerIndex < 0 && IsUsableBoundsForOwnership(entityBounds))
				{
					ownerIndex =
						FindBestOwningSheetIndexForBoundsEvenIfFlat(
							frames,
							entityBounds);
				}

				if (ownerIndex < 0)
				{
					BlockReference br = ent as BlockReference;

					if (br != null)
					{
						ownerIndex =
							FindBestOwningSheetIndexForPoint(
								frames,
								br.Position);
					}
				}

				if (ownerIndex < 0)
				{
					DebugOwnershipFailure(
						frames,
						ent,
						entityBounds,
						ed);

					if (entityBounds != null && entityBounds.IsValid)
					{
						unownedBounds = UnionBounds(unownedBounds, entityBounds);
						unownedCount++;
					}


					ed.WriteMessage(
						"\n[SkipUnownedEntity] Handle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name +
						", Layer=" +
						ent.Layer +
						", Bounds=" +
						entityBounds);

					continue;
				}

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

				Bounds2D ownerFrameBounds = frames[ownerIndex].Bounds;

				bool isFrameOwnerOfThisSheet =
					frames[ownerIndex].ObjectId == id;

				if (!isFrameOwnerOfThisSheet &&
					!IsEntitySafeToCloneAsWhole(tr, ent, ownerFrameBounds, ed))
				{
					ed.WriteMessage(
						"\n[SkipUnsafeWholeBlockClone] Handle=" +
						ent.Handle +
						", Type=" + ent.GetType().Name +
						", Bounds=" + entityBounds +
						", OwnerFrame=" + ownerFrameBounds);

					continue;
				}

				if (!allSheets[ownerIndex].EntityIds.Contains(id))
					allSheets[ownerIndex].EntityIds.Add(id);
			}

			ed.WriteMessage(
				"\n[UnownedSummary] Count=" +
				unownedCount +
				", Bounds=" +
				unownedBounds);

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



		private static List<SheetRegion> BuildSheetRegionsFromDetectedFramesForPureCopyV2(
			Transaction tr,
			Database db,
			List<SheetFrameCandidate> frames,
			ObjectId[] selectedIds,
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

			Bounds2D unownedBounds = null;
			int unownedCount = 0;
			int assignedCount = 0;

			foreach (ObjectId id in selectedIds)
			{
				Entity ent =
					tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				if (IsCopyCommandGeneratedMarker(ent))
				{
					ed.WriteMessage(
						"\n[V2 SkipFluxGenerated] Handle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name +
						", Layer=" +
						ent.Layer);

					continue;
				}

				int ownerIndex = -1;

				int forcedFrameIndex =
					FindFrameIndexByFrameOwnerBlock(
						frames,
						id,
						ent.Handle.ToString());

				if (forcedFrameIndex >= 0)
				{
					ownerIndex = forcedFrameIndex;

					ed.WriteMessage(
						"\n[V2 ForceFrameOwner] Handle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name +
						", ForcedSheet=" +
						(startIndex + ownerIndex + 1).ToString("000") +
						", FrameHandle=" +
						frames[ownerIndex].Handle);
				}

				Bounds2D entityBounds = null;

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
					entityBounds = GetEntityWorldBoundsRecursive(
						tr,
						ent,
						Matrix3d.Identity,
						0);

					if (IsUsableBoundsForOwnership(entityBounds))
					{
						ownerIndex =
							FindBestOwningSheetIndex(
								frames,
								entityBounds);
					}
				}

				if (ownerIndex < 0 && IsUsableBoundsForOwnership(entityBounds))
				{
					ownerIndex =
						FindBestOwningSheetIndexForBoundsEvenIfFlat(
							frames,
							entityBounds);
				}

				if (ownerIndex < 0)
				{
					BlockReference br = ent as BlockReference;

					if (br != null)
					{
						ownerIndex =
							FindBestOwningSheetIndexForPoint(
								frames,
								br.Position);
					}
				}

				if (ownerIndex < 0)
				{
					DebugOwnershipFailure(
						frames,
						ent,
						entityBounds,
						ed);

					if (entityBounds != null && entityBounds.IsValid)
					{
						unownedBounds = UnionBounds(unownedBounds, entityBounds);
						unownedCount++;
					}

					ed.WriteMessage(
						"\n[V2 SkipUnownedEntity] Handle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name +
						", Layer=" +
						ent.Layer +
						", Bounds=" +
						entityBounds);

					continue;
				}

				if (ownerIndex >= allSheets.Count)
				{
					ed.WriteMessage(
						"\n[V2 OwnerIndexError] OwnerIndex=" +
						ownerIndex +
						", SheetCount=" +
						allSheets.Count +
						", EntityHandle=" +
						ent.Handle +
						", Type=" +
						ent.GetType().Name);

					continue;
				}

				// V2 핵심 정책:
				// 소속만 판정하고, BlockReference/Bounds 안전성 검증으로 탈락시키지 않는다.
				// 다만 BlockInnerFourLineRectangle은 "가상 프레임"이므로,
				// 그 ObjectId가 부모 BlockReference를 가리키는 경우 부모 블록 전체를 복사하면 안 된다.
				/*
				SheetFrameCandidate ownerFrame = frames[ownerIndex];

				bool isVirtualInnerFrame =
					ownerFrame != null &&
					string.Equals(
						ownerFrame.EntityType,
						"BlockInnerFourLineRectangle",
						StringComparison.OrdinalIgnoreCase);

				bool isSameObjectId =
					isVirtualInnerFrame &&
					ownerFrame.ObjectId == id;

				bool isSameBlockHandle =
					isVirtualInnerFrame &&
					ownerFrame.Handle != null &&
					ownerFrame.Handle.StartsWith(
						"BR:" + ent.Handle.ToString(),
						StringComparison.OrdinalIgnoreCase);

				bool isVirtualInnerFrameOwner =
					isSameObjectId || isSameBlockHandle;
				
				if (isVirtualInnerFrameOwner)
				{
					ed.WriteMessage(
						"\n[V2 SkipVirtualFrameOwnerBlock] Sheet=" +
						(startIndex + ownerIndex + 1).ToString("000") +
						", Handle=" + ent.Handle +
						", Type=" + ent.GetType().Name +
						", FrameHandle=" + ownerFrame.Handle +
						", MatchByObjectId=" + isSameObjectId +
						", MatchByHandle=" + isSameBlockHandle);

					continue;
				}
				*/

				if (ownerIndex == 1)
				{
					ed.WriteMessage(
						"\n[V2 Sheet002Candidate] Handle=" + ent.Handle +
						", Type=" + ent.GetType().Name +
						", Layer=" + ent.Layer +
						", Bounds=" + entityBounds);
				}
				/*
				if (ownerIndex == 1 && ent is BlockReference &&
					(entityBounds == null || !entityBounds.IsValid))
				{
					ed.WriteMessage(
						"\n[V2 SkipSheet002NullBoundsBlock] Handle=" +
						ent.Handle +
						", Layer=" + ent.Layer);

					continue;
				}
				*/

				if (!allSheets[ownerIndex].EntityIds.Contains(id))
				{
					allSheets[ownerIndex].EntityIds.Add(id);
					assignedCount++;
				}
			}

			ed.WriteMessage(
				"\n[V2 OwnershipSummary] Assigned=" +
				assignedCount +
				", Unowned=" +
				unownedCount +
				", UnownedBounds=" +
				unownedBounds);

			var result = new List<SheetRegion>();

			for (int i = 0; i < allSheets.Count; i++)
			{
				SheetRegion sheet = allSheets[i];
				SheetFrameCandidate frame = frames[i];

				if (sheet.EntityIds.Count == 0)
				{
					ed.WriteMessage(
						"\n[V2] SkipEmptySheet FrameHandle=" +
						frame.Handle +
						", Bounds=" +
						sheet.Bounds);

					continue;
				}

				sheet.Index = startIndex + result.Count;
				result.Add(sheet);

				ed.WriteMessage(
					"\n[V2] BuildSheet Code=" +
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

		private static int FindFrameIndexByFrameOwnerBlock(
	List<SheetFrameCandidate> frames,
	ObjectId id,
	string handle)
		{
			if (frames == null)
				return -1;

			for (int i = 0; i < frames.Count; i++)
			{
				SheetFrameCandidate f = frames[i];

				if (f == null)
					continue;

				if (!string.Equals(
					f.EntityType,
					"BlockInnerFourLineRectangle",
					StringComparison.OrdinalIgnoreCase))
					continue;

				if (f.ObjectId == id)
					return i;

				if (!string.IsNullOrEmpty(f.Handle) &&
					!string.IsNullOrEmpty(handle) &&
					f.Handle.StartsWith(
						"BR:" + handle + "/",
						StringComparison.OrdinalIgnoreCase))
					return i;
			}

			return -1;
		}

		private static bool IsEntitySafeToCloneAsWhole(
			Transaction tr,
			Entity ent,
			Bounds2D frameBounds,
			Editor ed)
		{
			if (ent == null)
				return false;

			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			Line line = ent as Line;

			if (line != null)
				return IsLineInsideSheetFrameForCopy(frameBounds, line);

			DBText dbText = ent as DBText;

			if (dbText != null)
			{
				Bounds2D textBounds = GetEntityWorldBounds(tr, ent);

				if (textBounds == null || !textBounds.IsValid)
					return IsPointInsideBounds(frameBounds, dbText.Position, 5.0);

				return IsEntityInsideSheetFrameForCopy(frameBounds, textBounds);
			}

			MText mText = ent as MText;

			if (mText != null)
			{
				Bounds2D textBounds = GetEntityWorldBounds(tr, ent);

				if (textBounds == null || !textBounds.IsValid)
					return IsPointInsideBounds(frameBounds, mText.Location, 5.0);

				return IsEntityInsideSheetFrameForCopy(frameBounds, textBounds);
			}

			BlockReference br = ent as BlockReference;

			if (br != null)
				return IsSafeSmallBlockReferenceForCopy(tr, br, frameBounds, ed);

			Bounds2D b = GetEntityWorldBounds(tr, ent);

			if (b == null || !b.IsValid)
				return false;

			return IsEntityInsideSheetFrameForCopy(frameBounds, b);
		}



		private static bool IsSafeSmallBlockReferenceForCopy(
	Transaction tr,
	BlockReference br,
	Bounds2D frameBounds,
	Editor ed)
		{
			Bounds2D b = GetEntityWorldBounds(tr, br);

			if (b == null || !b.IsValid)
				return false;

			if (!IsEntityInsideSheetFrameForCopy(frameBounds, b))
				return false;

			double frameArea = frameBounds.Area;
			double blockArea = b.Area;

			if (frameArea <= 0.0)
				return false;

			double areaRatio = blockArea / frameArea;

			// 표 안의 작은 심볼/텍스트 블록은 허용
			if (areaRatio <= 0.20)
				return true;

			ed?.WriteMessage(
				"\n[UnsafeBlockReferenceWholeClone] Handle=" +
				br.Handle +
				", Bounds=" + b +
				", Frame=" + frameBounds);

			return false;
		}

		private static Bounds2D GetEntityWorldBoundsRecursive(
	Transaction tr,
	Entity ent,
	Matrix3d parentTransform,
	int depth)
		{
			if (ent == null)
				return null;

			if (depth > 20)
				return null;

			BlockReference br = ent as BlockReference;

			if (br != null)
			{
				Matrix3d nextTransform = br.BlockTransform * parentTransform;

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

					if (ShouldIgnoreForOwnershipBounds(child))
						continue;

					Bounds2D childBounds =
						GetEntityWorldBoundsRecursive(
							tr,
							child,
							nextTransform,
							depth + 1);

					result = UnionBounds(result, childBounds);
				}

				return result;
			}

			return GetPrimitiveEntityTransformedBounds(ent, parentTransform);
		}

		private static Bounds2D GetPrimitiveEntityTransformedBounds(
	Entity ent,
	Matrix3d transform)
		{
			try
			{
				Extents3d ext = ent.GeometricExtents;

				Point3d p1 = new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0).TransformBy(transform);
				Point3d p2 = new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0).TransformBy(transform);
				Point3d p3 = new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0).TransformBy(transform);
				Point3d p4 = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0).TransformBy(transform);

				double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
				double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
				double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
				double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

				return new Bounds2D(minX, minY, maxX, maxY);
			}
			catch
			{
				return null;
			}
		}

		private static bool ShouldIgnoreForOwnershipBounds(Entity ent)
		{
			if (ent == null)
				return true;

			if (ent is AttributeReference)
				return true;

			if (ent is DBText)
				return true;

			if (ent is MText)
				return true;

			if (ent is Dimension)
				return true;

			if (ent is DBPoint)
				return true;

			return false;
		}



		private static bool BoundsIntersects(
	Bounds2D a,
	Bounds2D b)
		{
			if (a == null || !a.IsValid)
				return false;

			if (b == null || !b.IsValid)
				return false;

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

		private static void DebugOwnershipFailure(
	List<SheetFrameCandidate> frames,
	Entity ent,
	Bounds2D entityBounds,
	Editor ed)
		{
			if (ed == null)
				return;

			ed.WriteMessage(
				"\n[OwnershipFailDetail] Handle=" +
				ent.Handle +
				", Type=" +
				ent.GetType().Name +
				", Layer=" +
				ent.Layer +
				", Bounds=" +
				entityBounds);

			if (entityBounds == null || !entityBounds.IsValid)
			{
				BlockReference br = ent as BlockReference;
				if (br != null)
				{
					ed.WriteMessage(
						", BlockPosition=(" +
						br.Position.X.ToString("0.###") + "," +
						br.Position.Y.ToString("0.###") + ")");
				}

				ed.WriteMessage(", Reason=InvalidBounds");
				return;
			}

			ed.WriteMessage(
				", Center=(" +
				entityBounds.CenterX.ToString("0.###") + "," +
				entityBounds.CenterY.ToString("0.###") + ")" +
				", W=" + entityBounds.Width.ToString("0.###") +
				", H=" + entityBounds.Height.ToString("0.###"));

			for (int i = 0; i < frames.Count; i++)
			{
				SheetFrameCandidate frame = frames[i];

				if (frame == null || frame.Bounds == null || !frame.Bounds.IsValid)
					continue;

				bool centerInside =
					entityBounds.CenterX >= frame.Bounds.MinX &&
					entityBounds.CenterX <= frame.Bounds.MaxX &&
					entityBounds.CenterY >= frame.Bounds.MinY &&
					entityBounds.CenterY <= frame.Bounds.MaxY;

				bool intersects =
					BoundsIntersects(entityBounds, frame.Bounds);

				double containedRatio =
					entityBounds.ContainedRatioIn(frame.Bounds);

				double dx =
					entityBounds.CenterX - frame.Bounds.CenterX;

				double dy =
					entityBounds.CenterY - frame.Bounds.CenterY;

				double dist =
					Math.Sqrt(dx * dx + dy * dy);

				ed.WriteMessage(
					"\n  [FrameTest " + i + "] Handle=" +
					frame.Handle +
					", Type=" + frame.EntityType +
					", CenterInside=" + centerInside +
					", Intersects=" + intersects +
					", ContainedRatio=" + containedRatio.ToString("0.###") +
					", Dist=" + dist.ToString("0.###") +
					", FrameBounds=" + frame.Bounds);
			}
		}


		private static int FindBestOwningSheetIndexForBoundsEvenIfFlat(
			List<SheetFrameCandidate> frames,
			Bounds2D entityBounds)
		{
			if (frames == null || entityBounds == null)
				return -1;

			for (int i = 0; i < frames.Count; i++)
			{
				Bounds2D frameBounds = frames[i].Bounds;

				if (frameBounds == null || !frameBounds.IsValid)
					continue;

				double tol = 2.0;

				bool inside =
					entityBounds.MinX >= frameBounds.MinX - tol &&
					entityBounds.MaxX <= frameBounds.MaxX + tol &&
					entityBounds.MinY >= frameBounds.MinY - tol &&
					entityBounds.MaxY <= frameBounds.MaxY + tol;

				if (inside)
					return i;
			}

			return -1;
		}

		private static bool IsUsableBoundsForOwnership(Bounds2D b)
		{
			if (b == null)
				return false;

			if (b.Width < 0.0)
				return false;

			if (b.Height < 0.0)
				return false;

			return true;
		}


		private static int FindBestOwningSheetIndexForPoint(
	List<SheetFrameCandidate> frames,
	Point3d point)
		{
			if (frames == null || frames.Count == 0)
				return -1;

			for (int i = 0; i < frames.Count; i++)
			{
				Bounds2D b = frames[i].Bounds;

				if (b == null || !b.IsValid)
					continue;

				if (point.X >= b.MinX &&
					point.X <= b.MaxX &&
					point.Y >= b.MinY &&
					point.Y <= b.MaxY)
				{
					return i;
				}
			}

			return -1;
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

		// = 분석 대상에서 Flux 흔적 전체 제외
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

		// = 복사할 때 방해되는 마커 레이어만 제외
		private static bool IsCopyCommandGeneratedMarker(Entity ent)
		{
			if (ent == null)
				return false;

			if (ent.Layer == CopiedLayerName)
				return true;

			if (ent.Layer == MarkerLayerName)
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
			const double height = 120.0;
			const double margin = 40.0;

			DBText text = new DBText();
			text.TextString = sheetCode;

			text.Position =
				new Point3d(
					bounds.MinX + offsetX,
					bounds.MaxY + margin + offsetY,
					0);

			text.Height = height;
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