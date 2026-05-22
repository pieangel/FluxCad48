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
		private const string CopiedLayerName = "FLUX_COPIED";
		private const string MarkerLayerName = "FLUX_MARKER";

		[CommandMethod("FLUX_COPY_SELECTED_SHEETS_TO_RIGHT")]
		public void FluxCopySelectedSheetsToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding = "\n오른쪽으로 복사할 객체들을 드래그 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 완료되지 않았습니다.");
				return;
			}

			ObjectId[] selectedIds = psr.Value.GetObjectIds();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Bounds2D selectedBounds =
					BricscadEntityTools.GetEntitiesBounds(tr, selectedIds);

				if (selectedBounds == null || !selectedBounds.IsValid)
				{
					ed.WriteMessage("\n선택 객체 Bounds를 계산하지 못했습니다.");
					return;
				}

				Bounds2D drawingBounds = BricscadEntityTools.GetModelSpaceBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n전체 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				SheetRegion sheet = new SheetRegion();
				sheet.Index = 0;
				sheet.Bounds = selectedBounds;

				foreach (ObjectId id in selectedIds)
					sheet.EntityIds.Add(id);

				List<SheetRegion> sheets = new List<SheetRegion>();
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

				BricscadEntityTools.EnsureLayer(tr, db, CopiedLayerName);
				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

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

					int clonedCount = 0;

					foreach (IdPair pair in mapping)
					{
						if (!pair.IsCloned)
							continue;

						Entity clonedEntity = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
						if (clonedEntity == null)
							continue;

						clonedEntity.TransformBy(Matrix3d.Displacement(displacement));
						clonedEntity.Layer = CopiedLayerName;
						clonedCount++;
					}

					Polyline marker =
						BricscadEntityTools.CreateRectanglePolyline(placement.SourceBounds);

					marker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
					marker.LineWeight = LineWeight.LineWeight050;
					marker.Layer = MarkerLayerName;

					modelSpace.AppendEntity(marker);
					tr.AddNewlyCreatedDBObject(marker, true);

					ed.WriteMessage(
						"\n[SelectionCopy] Selected=" + selectedIds.Length +
						", Cloned=" + clonedCount +
						", Bounds=" + selectedBounds);
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_COPY_SELECTED_SHEETS_TO_RIGHT 완료: 드래그 선택 객체를 오른쪽 공간에 복사했습니다.");
			}
		}
	}
}