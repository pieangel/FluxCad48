using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;

namespace FluxCad48.Commands
{
	public class FluxCopySelectedSheetsToRightCommand
	{
		[CommandMethod("FLUX_COPY_SELECTED_SHEETS_TO_RIGHT")]
		public void FluxCopySelectedSheetsToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding = "\n오른쪽으로 복사할 쉬트 객체들을 선택하세요. 선택 후 Enter를 누르세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 완료되지 않았습니다. 객체 선택 후 Enter를 눌러 주세요."); 
				return;
			}

			ObjectId[] selectedIds = psr.Value.GetObjectIds();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Bounds2D sourceBounds = BricscadEntityTools.GetEntitiesBounds(tr, selectedIds);

				if (sourceBounds == null || !sourceBounds.IsValid)
				{
					ed.WriteMessage("\n선택 영역의 Bounds를 계산하지 못했습니다.");
					return;
				}

				Bounds2D drawingBounds = BricscadEntityTools.GetModelSpaceBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n전체 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				List<SheetRegion> sheets = new List<SheetRegion>();

				SheetRegion sheet = new SheetRegion();
				sheet.Index = 0;
				sheet.Bounds = sourceBounds;

				foreach (ObjectId id in selectedIds)
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

				// 여기에 추가
				BricscadEntityTools.EnsureLayer(tr, db, "FLUX_COPIED");
				BricscadEntityTools.EnsureLayer(tr, db, "FLUX_MARKER");


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

						BricscadEntityTools.EnsureLayer(tr, db, "FLUX_COPIED");

						clonedEntity.TransformBy(Matrix3d.Displacement(displacement));

						clonedEntity.Layer = "FLUX_COPIED";
					}

					if (options.DrawYellowMarkerOnSource)
					{
						Polyline marker =
							BricscadEntityTools.CreateRectanglePolyline(placement.SourceBounds);

						marker.Layer = "FLUX_MARKER";

						marker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2); // Yellow
						marker.LineWeight = LineWeight.LineWeight050;

						modelSpace.AppendEntity(marker);
						tr.AddNewlyCreatedDBObject(marker, true);
					}
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_COPY_SELECTED_SHEETS_TO_RIGHT 완료: 선택 영역을 오른쪽 공간에 정렬 복사하고 원본에 노란 테두리를 표시했습니다.");
			}
		}
	}
}