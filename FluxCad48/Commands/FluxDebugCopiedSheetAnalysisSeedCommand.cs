using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.CopiedSheetAnalysis;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxDebugCopiedSheetAnalysisSeedCommand
	{
		[CommandMethod("FLUX_DEBUG_COPIED_SHEET_ANALYSIS_SEED")]
		public void FluxDebugCopiedSheetAnalysisSeed()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\n분석할 복사된 단일 쉬트 내부 개체들을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[CopiedSheetAnalysisSeed] 선택이 취소되었습니다.");
				return;
			}

			SelectionSet ss = psr.Value;
			ObjectId[] ids = ss.GetObjectIds();

			CopiedSheetAnalysisContext context = new CopiedSheetAnalysisContext();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Extents3d? totalBounds = null;

				for (int i = 0; i < ids.Length; i++)
				{
					ObjectId id = ids[i];

					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					ClassifyCopiedSheetEntity(context, ent, id);

					Extents3d? b = TryGetEntityBounds(ent);
					if (b.HasValue)
					{
						if (totalBounds.HasValue)
						{
							Extents3d merged = totalBounds.Value;
							merged.AddExtents(b.Value);
							totalBounds = merged;
						}
						else
						{
							totalBounds = b.Value;
						}
					}
				}

				if (totalBounds.HasValue)
					context.Bounds = totalBounds.Value;

				tr.Commit();
			}

			AppendLog(ed, "========== CopiedSheetAnalysis Seed ==========");
			AppendLog(ed, "Geometry Entities   : " + context.GeometryIds.Count);
			AppendLog(ed, "Dimension Entities  : " + context.DimensionIds.Count);
			AppendLog(ed, "Text Entities       : " + context.TextIds.Count);
			AppendLog(ed, "BlockReferences     : " + context.BlockReferenceIds.Count);
			AppendLog(ed, "Other Entities      : " + context.OtherIds.Count);

			AppendLog(ed, "[CopiedSheetAnalysisSeed] 완료.");
		}

		private static void ClassifyCopiedSheetEntity(
			CopiedSheetAnalysisContext context,
			Entity ent,
			ObjectId id)
		{
			if (ent is Line ||
				ent is Polyline ||
				ent is Circle ||
				ent is Arc ||
				ent is Ellipse ||
				ent is Spline)
			{
				context.GeometryIds.Add(id);
				return;
			}

			if (ent is Dimension)
			{
				context.DimensionIds.Add(id);
				return;
			}

			if (ent is DBText || ent is MText)
			{
				context.TextIds.Add(id);
				return;
			}

			if (ent is BlockReference)
			{
				context.BlockReferenceIds.Add(id);
				return;
			}

			context.OtherIds.Add(id);
		}

		private static Extents3d? TryGetEntityBounds(Entity ent)
		{
			try
			{
				return ent.GeometricExtents;
			}
			catch
			{
				return null;
			}
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}