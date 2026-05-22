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

				foreach (SelectedObject so in psr.Value)
				{
					if (so == null || so.ObjectId.IsNull)
						continue;

					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					selectedEntities.Add(ent);
				}

				ed.WriteMessage($"\n[MultiFrame] SelectedEntities={selectedEntities.Count}");

				var frames = SheetFrameDetector.Detect(selectedEntities, ed);

				ed.WriteMessage($"\n[MultiFrame] DetectedFrames={frames.Count}");

				int index = 1;

				foreach (var frame in frames)
				{
					ed.WriteMessage(
						$"\n[Frame {index}] " +
						$"Handle={frame.Handle}, " +
						$"Type={frame.EntityType}, " +
						$"Layer={frame.Layer}, " +
						$"Bounds={frame.Bounds}, " +
						$"Inside={frame.InsideEntityCount}, " +
						$"Score={frame.Score:0.00}");

					index++;
				}

				tr.Commit();
			}
		}
	}
}
