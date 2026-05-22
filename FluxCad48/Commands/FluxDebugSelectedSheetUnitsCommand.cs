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
	public class FluxDebugSelectedSheetUnitsCommand
	{
		[CommandMethod("FLUX_DEBUG_SELECTED_SHEET_UNITS")]
		public void FluxDebugSelectedSheetUnits()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions opt = new PromptSelectionOptions();
			opt.MessageForAdding = "\n분석할 쉬트 영역 객체들을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(opt);
			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[SelectedSheet] 선택이 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				ObjectId[] ids = psr.Value.GetObjectIds();

				List<SelectedSheetUnitInfo> units =
					SelectedSheetUnitInfoBuilder.BuildFromEntities(tr, ids);

				int dimCount = units.Count(x => x.IsDimensionLike);
				int centerCount = units.Count(x => x.IsCenterLineLike);
				int hiddenCount = units.Count(x => x.IsHiddenLineLike);
				int textCount = units.Count(x => x.IsTextLike);
				int blockCount = units.Count(x => x.IsBlockReference);
				int lineCount = units.Count(x => x.IsLineLike);

				ed.WriteMessage($"\n[SelectedSheet] SelectedIds={ids.Length}");
				ed.WriteMessage($"\n[SelectedSheet] Units={units.Count}");
				ed.WriteMessage($"\n[SelectedSheet] Dimensions={dimCount}");
				ed.WriteMessage($"\n[SelectedSheet] CenterLines={centerCount}");
				ed.WriteMessage($"\n[SelectedSheet] HiddenLines={hiddenCount}");
				ed.WriteMessage($"\n[SelectedSheet] Texts={textCount}");
				ed.WriteMessage($"\n[SelectedSheet] Blocks={blockCount}");
				ed.WriteMessage($"\n[SelectedSheet] Lines={lineCount}");

				foreach (SelectedSheetUnitInfo u in units.Take(30))
				{
					ed.WriteMessage(
						$"\n[Unit] H={u.HandleText}, Type={u.TypeName}, Layer={u.Layer}, " +
						$"B={u.BoundsWcs}, C={u.CenterWcs}, " +
						$"Dim={u.IsDimensionLike}, Center={u.IsCenterLineLike}, Hidden={u.IsHiddenLineLike}, " +
						$"Text={u.IsTextLike}, Block={u.IsBlockReference}, Txt='{u.NormalizedText}'");
				}

				tr.Commit();
			}
		}
	}
}
