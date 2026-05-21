using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxPickFrameCopySheetToRightCommand
	{
		[CommandMethod("FLUX_PICK_FRAME_COPY_SHEET_TO_RIGHT")]
		public void FluxPickFrameCopySheetToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptEntityOptions peo = new PromptEntityOptions(
				"\n오른쪽으로 복사할 쉬트의 프레임을 클릭하세요: ");

			peo.AllowNone = false;

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n프레임 선택이 완료되지 않았습니다.");
				return;
			}

			ObjectId frameId = per.ObjectId;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Entity frameEntity = tr.GetObject(frameId, OpenMode.ForRead) as Entity;

				if (frameEntity == null)
				{
					ed.WriteMessage("\n선택한 객체가 Entity가 아닙니다.");
					return;
				}

				Bounds2D frameBounds = BricscadEntityTools.GetEntityBounds(tr, frameId);

				if (frameBounds == null || !frameBounds.IsValid)
				{
					ed.WriteMessage("\n선택한 프레임의 Bounds를 계산하지 못했습니다.");
					return;
				}

				Bounds2D drawingBounds = BricscadEntityTools.GetModelSpaceBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n전체 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				// TODO:
				// 다음 단계에서 BricscadSheetContentFinder로 교체
				// 현재는 우선 프레임만 복사 대상으로 넣어둡니다.
				ed.WriteMessage(
					"\n[FramePick] 선택 객체 Type=" + frameEntity.GetType().Name +
					": Bounds를 기준으로 ModelSpace 객체를 수집합니다.");

				List<ObjectId> contentIds =
					BricscadSheetContentFinder.FindEntitiesInsideBounds(
						tr,
						db,
						frameBounds,
						ed);

				if (!contentIds.Contains(frameId))
					contentIds.Add(frameId);

				ed.WriteMessage(
					"\n프레임 내부 수집 객체 수: " + contentIds.Count);

				ed.WriteMessage(
					"\n[FrameBounds] Width=" + frameBounds.Width +
					", Height=" + frameBounds.Height);

				ed.WriteMessage(
					"\n[FrameEntity] Type=" + frameEntity.GetType().Name +
					", Layer=" + frameEntity.Layer +
					", Bounds=" + frameBounds.ToString());


				List<SheetRegion> sheets = new List<SheetRegion>();

				SheetRegion sheet = new SheetRegion();
				sheet.Index = 0;
				sheet.Bounds = frameBounds;

				foreach (ObjectId id in contentIds)
					sheet.EntityIds.Add(id);

				sheets.Add(sheet);

				SheetArrangeOptions options = new SheetArrangeOptions();
				SheetArranger arranger = new SheetArranger();

				List<SheetPlacement> placements =
					arranger.CreateHorizontalPlacements(
						sheets,
						drawingBounds,
						options);

				BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

				foreach (SheetPlacement placement in placements)
				{
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
						new Vector3d(placement.MoveX, placement.MoveY, 0);

					foreach (IdPair pair in mapping)
					{
						if (!pair.IsCloned)
							continue;

						Entity clonedEntity = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
						if (clonedEntity == null)
							continue;

						clonedEntity.TransformBy(Matrix3d.Displacement(displacement));
					}

					if (options.DrawYellowMarkerOnSource)
					{
						Polyline marker =
							BricscadEntityTools.CreateRectanglePolyline(placement.SourceBounds);

						marker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
						marker.LineWeight = LineWeight.LineWeight050;

						modelSpace.AppendEntity(marker);
						tr.AddNewlyCreatedDBObject(marker, true);
					}
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_PICK_FRAME_COPY_SHEET_TO_RIGHT 완료: 선택한 프레임 기준 쉬트를 오른쪽 공간에 복사했습니다.");
			}
		}

		[CommandMethod("FLUX_DEBUG_WORLD_ENTITIES")]
		public void FluxDebugWorldEntities()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				List<WorldEntityInfo> infos =
					BricscadEntityTools.CollectWorldEntitiesDeep(tr, db);

				ed.WriteMessage($"\n[WorldEntities] Count={infos.Count}");

				int invalid = 0;
				int insideBlock = 0;

				foreach (WorldEntityInfo info in infos)
				{
					if (info.WorldBounds == null || !info.WorldBounds.IsValid)
						invalid++;

					if (info.IsInsideBlock)
						insideBlock++;
				}

				ed.WriteMessage($"\n[WorldEntities] InsideBlock={insideBlock}, InvalidBounds={invalid}");

				for (int i = 0; i < Math.Min(50, infos.Count); i++)
				{
					WorldEntityInfo info = infos[i];

					ed.WriteMessage(
						$"\n[{i}] Type={info.EntityType}, Layer={info.Layer}, Depth={info.BlockDepth}, World={FormatBoundsForDebug(info.WorldBounds)}");
				}

				tr.Commit();
			}
		}

		private static string FormatBoundsForDebug(Bounds2D b)
		{
			if (b == null || !b.IsValid)
				return "(invalid)";

			return $"Min=({b.MinX:0.###},{b.MinY:0.###}) Max=({b.MaxX:0.###},{b.MaxY:0.###}) W={b.Width:0.###} H={b.Height:0.###}";
		}
	}
}
