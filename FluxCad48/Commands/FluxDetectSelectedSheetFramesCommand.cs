using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxDetectSelectedSheetFramesCommand
	{
		[CommandMethod("FLUX_DETECT_SELECTED_SHEET_FRAMES")]
		public void FluxDetectSelectedSheetFrames()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionResult psr = ed.GetSelection();

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[MultiFrame] 선택된 객체가 없습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				var selectedEntities = new List<Entity>();
				var selectedIds = new List<ObjectId>();

				foreach (SelectedObject so in psr.Value)
				{
					if (so == null || so.ObjectId.IsNull)
						continue;

					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					selectedEntities.Add(ent);
					selectedIds.Add(so.ObjectId);
				}

				ed.WriteMessage($"\n[MultiFrame] SelectedEntities={selectedEntities.Count}");

				var units = SelectedSheetUnitInfoBuilder.BuildFromEntities(tr, selectedIds);

				ed.WriteMessage(
					$"\n[MultiFrame] Units={units.Count}, " +
					$"Dim={units.Count(u => u.IsDimensionLike)}, " +
					$"Center={units.Count(u => u.IsCenterLineLike)}, " +
					$"Hidden={units.Count(u => u.IsHiddenLineLike)}");

				var frames = SheetFrameDetector.Detect(selectedEntities, ed);

				ed.WriteMessage($"\n[MultiFrame] RawDetectedFrames={frames.Count}");

				int index = 1;

				foreach (var frame in frames)
				{
					int dimInside = units.Count(u =>
						u.IsDimensionLike &&
						frame.Bounds.Contains(u.CenterWcs));

					int centerInside = units.Count(u =>
						u.IsCenterLineLike &&
						frame.Bounds.Contains(u.CenterWcs));

					int hiddenInside = units.Count(u =>
						u.IsHiddenLineLike &&
						frame.Bounds.Contains(u.CenterWcs));

					bool hasEssentialFeatures =
						dimInside > 0 ||
						centerInside > 0 ||
						hiddenInside > 0;

					ed.WriteMessage(
						$"\n[Frame {index}] " +
						$"Handle={frame.Handle}, " +
						$"Type={frame.EntityType}, " +
						$"Layer={frame.Layer}, " +
						$"Bounds={frame.Bounds}, " +
						$"Inside={frame.InsideEntityCount}, " +
						$"Dim={dimInside}, " +
						$"Center={centerInside}, " +
						$"Hidden={hiddenInside}, " +
						$"Essential={hasEssentialFeatures}, " +
						$"Score={frame.Score:0.00}");

					index++;
				}

				tr.Commit();
			}
		}
	}
}
