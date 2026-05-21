using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using FluxCad48.Bricscad;
using FluxCad48.Geometry;
using FluxCad48.Sheets;

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
				List<ObjectId> contentIds = new List<ObjectId>();

				BlockReference frameBlock = frameEntity as BlockReference;

				if (frameBlock != null)
				{
					contentIds.Add(frameId);

					ed.WriteMessage(
						"\n[FramePick] BlockReference 선택됨: BlockReference 자체만 복사합니다.");
				}
				else
				{
					contentIds =
						BricscadSheetContentFinder.FindEntitiesInsideBounds(
							tr,
							db,
							frameBounds);

					if (!contentIds.Contains(frameId))
						contentIds.Add(frameId);

					ed.WriteMessage(
						"\n[FramePick] 일반 Entity 선택됨: Bounds 내부 객체를 수집합니다.");
				}

				ed.WriteMessage(
					"\n프레임 내부 수집 객체 수: " + contentIds.Count);

				ed.WriteMessage(
					"\n[FrameBounds] Width=" + frameBounds.Width +
					", Height=" + frameBounds.Height);

				ed.WriteMessage(
					"\n[FrameEntity] Type=" + frameEntity.GetType().Name +
					", Layer=" + frameEntity.Layer +
					", Bounds=" + frameBounds.ToString());



				if (!contentIds.Contains(frameId))
					contentIds.Add(frameId);

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
	}
}
