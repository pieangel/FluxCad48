using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Colors;
using Teigha.Runtime;
using FluxCad48.Bricscad;
using FluxCad48.Geometry;
using FluxCad48.Sheets;

namespace FluxCad48.Commands
{
	public class FluxCopySelectedSheetsToRightCommand
	{
		[CommandMethod("FLUX_COPY_SELECTED_SHEETS_TO_RIGHT")]
		public void Execute()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionResult psr = ed.GetSelection();

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택된 객체가 없습니다.");
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

				double gap = 100.0;
				double moveX = sourceBounds.Width + gap;
				Vector3d displacement = new Vector3d(moveX, 0, 0);

				ObjectIdCollection idsToClone = new ObjectIdCollection();

				foreach (ObjectId id in selectedIds)
					idsToClone.Add(id);

				BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

				IdMapping mapping = new IdMapping();

				db.DeepCloneObjects(
					idsToClone,
					modelSpace.ObjectId,
					mapping,
					false);

				foreach (IdPair pair in mapping)
				{
					if (!pair.IsCloned)
						continue;

					Entity clonedEntity = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
					if (clonedEntity == null)
						continue;

					clonedEntity.TransformBy(Matrix3d.Displacement(displacement));
				}

				Polyline marker = BricscadEntityTools.CreateRectanglePolyline(sourceBounds);
				marker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2); // yellow
				marker.LineWeight = LineWeight.LineWeight050;

				modelSpace.AppendEntity(marker);
				tr.AddNewlyCreatedDBObject(marker, true);

				tr.Commit();

				ed.WriteMessage("\n선택 영역을 오른쪽으로 복사하고 원본에 노란 테두리를 표시했습니다.");
			}
		}
	}
}